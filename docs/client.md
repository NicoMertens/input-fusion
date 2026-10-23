# Client: Dienst und Injector auf dem Notebook

Dienst und Injector sind **ein Programm** (`InputFusion.Client.exe`) in zwei Prozessen. Ohne Argument startet der Dienst, mit `--injector` der Injector. Das spart ein zweites Projekt und ein zweites Programm im Installer. Weil der Dienst Session 0 nicht verlassen kann, bleiben es trotzdem zwei Prozesse.

## Dienst

Worker Service mit `AddWindowsService()`, LocalSystem, Starttyp automatisch.

- **Spielt nicht selbst ab:** Session 0 ist isoliert, `SendInput` aus dem Dienst erreicht keinen Desktop.
- **Hält die Verbindung zum Host**, siehe [Netzwerk](netzwerk.md), und reicht die Eingaben über eine Pipe an den Injector weiter, siehe [IPC](ipc.md).
- **Sitzungswechsel:** Reagiert auf `OnSessionChange` (Anmelden, Abmelden, Sperren, Benutzerwechsel, RDP). Wechselt die aktive Konsolensitzung (`WTSGetActiveConsoleSessionId`), startet er den Injector dort neu.
- **Injector-Start:**
  1. Eigenes SYSTEM-Token mit `DuplicateTokenEx` kopieren.
  2. Mit `SetTokenInformation(TokenSessionId)` auf die Zielsitzung setzen.
  3. `CreateProcessAsUser` auf die eigene exe mit `--injector` und `lpDesktop = "winsta0\\default"`.

  Die exe ist eine Konsolenanwendung, damit der Dienst beim Entwickeln ins Terminal loggt. Deshalb braucht der Start `CREATE_NO_WINDOW`, sonst öffnet sich in der Benutzersitzung ein Konsolenfenster.
- **Konfiguration** in `%ProgramData%\InputFusion\`, ACL nur für SYSTEM und Administratoren. Der Dienst ist ihr einziger Besitzer; die [Einstellungen](einstellungen.md) ändern sie nur über IPC.
- **`Strg+Alt+Entf`** optional über `SendSAS` (sas.dll). Setzt die Richtlinie *SoftwareSASGeneration* voraus, die Diensten das erlauben muss.

## Injector

- **Desktop-Wechsel nach VNC-Muster:** Vor dem Abspielen prüft der Thread mit `OpenInputDesktop` den aktiven Desktop (Default, Winlogon, UAC) und wechselt bei Bedarf mit `SetThreadDesktop` dorthin. Ein Prozess deckt so Desktop, Sperrbildschirm und UAC-Abfrage ab. Als SYSTEM gibt es kein UIPI-Problem mit erhöhten Fenstern.
- **Misst die Monitore**, weil nur er in der Sitzung die echten Werte sieht: `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, Geräte-IDs. Bei `WM_DISPLAYCHANGE` (Andocken, Auflösung) meldet er neu. Was mit den Werten geschieht, steht unter [Layout](layout.md).
- **Führt gedrückte Tasten nach** und lässt beim Wechsel oder Verbindungsabbruch alle los.
- **Absichtlich dumm gehalten:** kein Netzwerk, keine Konfiguration. Wenig Code im SYSTEM-Kontext heißt wenig Angriffsfläche. Der Code liegt zwar in derselben exe wie der Dienst, der Injector-Pfad öffnet aber weder Sockets noch Konfiguration.

## Stand Phase 1 (Durchstich)

Im Durchstich läuft der Client als einfaches Konsolenprogramm: `InputFusion.Client.exe --connect <host>`. Es verbindet sich zum Host, empfängt die Eingaben und spielt sie direkt mit `SendInput` ab. Damit lässt sich das Mausgefühl früh testen, ohne Dienst, Sitzungswechsel und Desktop-Wechsel.

## Stand Phase 2 (Schritt 1: IPC und Injector)

Die Aufteilung in Dienst und Injector ist begonnen. Umgesetzt und über Unit-Test und einen lokalen Durchlauf verifiziert ist die **IPC-Kette** Host → TCP → Dienst-Rolle → Named Pipe → Injector → `SendInput`:

- **Injector** (`--injector`): verbindet sich als Pipe-Client, spielt die Frames über denselben [`InputPlayer`](#injector) ab wie der Durchstich-Client und lässt bei Pipe-Abbruch alle gedrückten Tasten los. Er kennt weder Netz noch Konfiguration.
- **Dienst-Rolle / Relay:** hostet die Pipe (siehe [IPC](ipc.md)) und reicht die vom Host empfangenen Frames unverändert weiter. Zum Entwickeln als Konsole über `InputFusion.Client.exe --relay <host>`, das den Injector als Kindprozess in derselben Sitzung startet.
- Die Nachricht-zu-Eingabe-Umsetzung liegt geteilt in `InputDispatcher`, genutzt von Durchstich-Client und Injector.

**Noch offen (Schritt 2, nur an echter Hardware testbar):** Im echten Dienst ersetzt `CreateProcessAsUser` mit dupliziertem SYSTEM-Token den schlichten Kindprozess-Start, damit der Injector in der aktiven Konsolensitzung und auf dem richtigen Desktop landet. Dazu `OnSessionChange`, der Desktop-Wechsel (Default, Winlogon, UAC) und das Neustarten des Injectors beim Sitzungswechsel — siehe die Abschnitte [Dienst](#dienst) und [Injector](#injector) oben als Zielbild. Ebenso offen: die Host-Adresse kommt noch aus dem Argument, nicht aus der Konfiguration (Phase 5).

## Später

**Zwischenablage** braucht auf dem Client zusätzlich einen Prozess im Benutzerkontext: Dateien müssen mit den Rechten des Benutzers in dessen Profil landen, nicht als SYSTEM.
