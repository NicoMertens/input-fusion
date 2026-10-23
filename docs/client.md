# Client: Dienst und Injector auf dem Notebook

Dienst und Injector sind **ein Programm** (`InputFusion.Client.exe`) in zwei Prozessen. Ohne Argument startet der Dienst, mit `--injector` der Injector. Das spart ein zweites Projekt und ein zweites Programm im Installer. Weil der Dienst Session 0 nicht verlassen kann, bleiben es trotzdem zwei Prozesse.

## Dienst

Worker Service mit `AddWindowsService()`, LocalSystem, Starttyp automatisch.

- **Spielt nicht selbst ab:** Session 0 ist isoliert, `SendInput` aus dem Dienst erreicht keinen Desktop.
- **Hält die Verbindung zum Host**, siehe [Netzwerk](netzwerk.md), und reicht die Eingaben über eine Pipe an den Injector weiter, siehe [IPC](ipc.md).
- **Sitzungswechsel:** Prüft jede Sekunde die aktive Konsolensitzung (`WTSGetActiveConsoleSessionId`) und ob der Injector noch lebt. Wechselt die Sitzung oder ist der Injector weg, startet er ihn dort neu. Bewusst eine Abfrage statt `OnSessionChange`: Sie deckt Anmelden, Abmelden, Benutzerwechsel und Abstürze gleichermaßen ab und braucht keine Kopplung an `ServiceBase`. Zwischen zwei Sitzungen meldet Windows kurz keine aktive, dann wartet er.
- **Verbindet sich neu:** Ist der Host nicht erreichbar oder bricht die Verbindung ab, versucht er es mit wachsendem Abstand bis höchstens 15 s erneut. Vor jedem Versuch liest er die Konfiguration neu. TCP-Keepalive sorgt dafür, dass eine tote Verbindung nach Standby oder Netzwechsel auffällt, siehe [Netzwerk](netzwerk.md).
- **Injector-Start:**
  1. Eigenes SYSTEM-Token mit `DuplicateTokenEx` kopieren.
  2. Mit `SetTokenInformation(TokenSessionId)` auf die Zielsitzung setzen.
  3. `CreateProcessAsUser` auf die eigene exe mit `--injector` und `lpDesktop = "winsta0\\default"`.

  Die exe ist eine Konsolenanwendung, damit der Dienst beim Entwickeln ins Terminal loggt. Deshalb braucht der Start `CREATE_NO_WINDOW`, sonst öffnet sich in der Benutzersitzung ein Konsolenfenster.
- **Konfiguration** in `%ProgramData%\InputFusion\client.json` (Rechnername des PCs, optional feste IP), ACL nur für SYSTEM und Administratoren. Der Dienst ist ihr einziger Besitzer; die [Einstellungen](einstellungen.md) ändern sie später nur über IPC. Bis dahin schreibt sie das Installationsskript, siehe [Installation](installation.md).
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
- **Dienst-Rolle / Relay:** hostet die Pipe (siehe [IPC](ipc.md)) und reicht Frames in beide Richtungen unverändert weiter: die Eingaben vom Host zum Injector, dessen `SwitchBackRequest` zurück zum Host. Der Injector darf zwischendurch wegfallen und sich neu verbinden (Sitzungswechsel), Eingaben in der Lücke verfallen. Endet die Host-Verbindung, endet das Relay, und der Injector lässt beim Pipe-Abbruch alle Tasten los.
- Die Nachricht-zu-Eingabe-Umsetzung liegt geteilt in `InputDispatcher`, genutzt von Durchstich-Client und Injector.
- **Austritt an der linken Kante:** Ebenfalls im `InputDispatcher`. Nach einer Bewegung nach links prüft er per `GetCursorPos`, ob der Cursor am linken Rand des virtuellen Desktops steht, und meldet dann einmal `SwitchBackRequest`. Der Durchstich-Client sendet die Meldung direkt über TCP, der Injector über die Pipe. Geprüft wird nur bei Bewegung nach links, weil der Eintritt den Cursor genau auf den Rand setzt; anders als im [Simulator](simulator.md) braucht es deshalb kein Einrücken um einen Pixel. Lokal mit einem Test-Host verifiziert: Eine Bewegung nach rechts löst nichts aus, nach links genau eine Meldung, sowohl bei `--connect` als auch bei `--relay`.

## Stand Phase 2 (Schritt 2: Windows-Dienst)

Der Client läuft als Windows-Dienst `InputFusionClient`, eingerichtet per Skript, siehe [Installation](installation.md).

- **Ein Ablauf für Dienst und Entwicklung:** `ClientService` hält den Injector am Leben (`InjectorSupervisor`) und die Verbindung zum Host, mit Neuverbindung. Im Dienst kommt die Host-Adresse aus `client.json`, und der Injector startet per `CreateProcessAsUser` als SYSTEM in der aktiven Sitzung (`SessionLauncher`), wie oben unter [Dienst](#dienst) beschrieben. Mit `--relay <host>` läuft derselbe Ablauf als Konsole, mit dem Host aus dem Argument und dem Injector als Kindprozess. Ohne Argument aus einer Konsole gestartet, läuft der Dienst-Ablauf sichtbar mit, aber ebenfalls mit Kindprozess, weil nur SYSTEM die Sitzung eines Tokens umsetzen darf.
- **Verifiziert auf dem PC** mit einem Test-Host: Dienst per Skript installiert, Injector läuft in Sitzung 1, Dienst in Sitzung 0; Verbindung nach Start des Hosts innerhalb des Wiederholungsabstands; Austritt über Dienst und Injector kommt beim Host an; nach Verbindungsabbruch neu verbunden; ein beendeter Injector wird binnen einer Sekunde neu gestartet und übernimmt die Pipe; Deinstallation ohne Reste.
- **Noch offen:** der Desktop-Wechsel für Sperrbildschirm und UAC-Abfrage (siehe [Injector](#injector)); der Test des Sitzungswechsels (Abmelden, Benutzerwechsel) am Notebook; die Prüfung, dass der SYSTEM-Injector nur mit dem eigenen Dienst spricht. Bisher könnte ein Prozess, der die Pipe vor dem Dienst anlegt, dem Injector Eingaben unterschieben.

## Später

**Zwischenablage** braucht auf dem Client zusätzlich einen Prozess im Benutzerkontext: Dateien müssen mit den Rechten des Benutzers in dessen Profil landen, nicht als SYSTEM.
