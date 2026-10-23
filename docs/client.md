# Client: Dienst und Injector auf dem Notebook

Dienst und Injector sind **ein Programm** (`InputFusion.Client.exe`) in zwei Prozessen. Ohne Argument startet der Dienst, mit `--injector` der Injector; per Doppelklick außerhalb des Installationsordners installiert sich die exe selbst, siehe [Installation](installation.md). Das spart ein zweites Projekt und ein zweites Programm im Installer. Weil der Dienst Session 0 nicht verlassen kann, bleiben es trotzdem zwei Prozesse.

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
- **Konfiguration** in `%ProgramData%\InputFusion\client.json` (Rechnername des PCs, gemeinsamer Schlüssel, optional feste IP), ACL nur für SYSTEM und Administratoren. Der Dienst ist ihr einziger Besitzer; die [Einstellungen](einstellungen.md) ändern sie später nur über IPC. Bis dahin schreibt sie das Installationsskript, siehe [Installation](installation.md).
- **`Strg+Alt+Entf`** optional über `SendSAS` (sas.dll). Setzt die Richtlinie *SoftwareSASGeneration* voraus, die Diensten das erlauben muss.

## Injector

- **Desktop-Wechsel nach VNC-Muster:** Vor dem Abspielen prüft der Thread mit `OpenInputDesktop` den aktiven Desktop (Default, Winlogon, UAC) und wechselt bei Bedarf mit `SetThreadDesktop` dorthin. Ein Prozess deckt so Desktop, Sperrbildschirm und UAC-Abfrage ab. Als SYSTEM gibt es kein UIPI-Problem mit erhöhten Fenstern.
- **Misst die Monitore**, weil nur er in der Sitzung die echten Werte sieht: `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, Geräte-IDs. Bei `WM_DISPLAYCHANGE` (Andocken, Auflösung) meldet er neu. Was mit den Werten geschieht, steht unter [Layout](layout.md).
- **Führt gedrückte Tasten nach** und lässt beim Wechsel oder Verbindungsabbruch alle los.
- **Absichtlich dumm gehalten:** kein Netzwerk, keine Konfiguration. Wenig Code im SYSTEM-Kontext heißt wenig Angriffsfläche. Der Code liegt zwar in derselben exe wie der Dienst, der Injector-Pfad öffnet aber weder Sockets noch Konfiguration.

## Stand Phase 1 (Durchstich)

Im Durchstich läuft der Client als einfaches Konsolenprogramm: `InputFusion.Client.exe --connect <host> --key <schlüssel>`. Es verbindet sich zum Host, empfängt die Eingaben und spielt sie direkt mit `SendInput` ab. Damit lässt sich das Mausgefühl früh testen, ohne Dienst, Sitzungswechsel und Desktop-Wechsel.

## Stand Phase 2 (Schritt 1: IPC und Injector)

Die Aufteilung in Dienst und Injector ist begonnen. Umgesetzt und über Unit-Test und einen lokalen Durchlauf verifiziert ist die **IPC-Kette** Host → TCP → Dienst-Rolle → Named Pipe → Injector → `SendInput`:

- **Injector** (`--injector`): verbindet sich als Pipe-Client, spielt die Frames über denselben [`InputPlayer`](#injector) ab wie der Durchstich-Client und lässt bei Pipe-Abbruch alle gedrückten Tasten los. Er kennt weder Netz noch Konfiguration.
- **Dienst-Rolle / Relay:** hostet die Pipe (siehe [IPC](ipc.md)) und reicht Frames in beide Richtungen unverändert weiter: die Eingaben vom Host zum Injector, dessen `SwitchBackRequest` zurück zum Host. Der Injector darf zwischendurch wegfallen und sich neu verbinden (Sitzungswechsel), Eingaben in der Lücke verfallen. Endet die Host-Verbindung, endet das Relay, und der Injector lässt beim Pipe-Abbruch alle Tasten los.
- Die Nachricht-zu-Eingabe-Umsetzung liegt geteilt in `InputDispatcher`, genutzt von Durchstich-Client und Injector.
- **Austritt über die Rückkehrkanten:** Ebenfalls im `InputDispatcher`. Der Host schickt mit `ReturnEdges` die Kantenabschnitte des Clients, die laut [Layout](layout.md) an einem Host-Monitor anliegen, beim Verbinden und nach jeder Änderung der Anordnung. Nach jeder Bewegung prüft der Dispatcher per `GetCursorPos` mit `ReturnEdge.Find`, ob der Cursor genau auf einer dieser Kanten steht und nach außen drückt, und meldet dann einmal `SwitchBackRequest` mit Kante und Position. Der Host rechnet daraus die Stelle auf seiner Seite aus. Der Durchstich-Client sendet die Meldung direkt über TCP, der Injector über die Pipe. Bei absoluten Bewegungen ergibt sich die Richtung aus der Cursorposition davor und danach.
- **Eintritt:** `SwitchToClient` trägt den Eintrittspunkt in Bildschirmkoordinaten des Clients; der Dispatcher setzt den Cursor mit `SetCursorPos` genau dorthin. Absolut per `SendInput` wäre er auf 0…65535 normiert und um einen Pixel ungenau, womöglich schon auf dem Nachbarmonitor.
- **Monitore melden** (`MonitorReporter`): Der Injector misst nach dem Verbinden mit der Pipe die Monitore seiner Sitzung (`MonitorEnumerator` in Core) und schickt sie als `Monitors`; der Dienst reicht sie zum Host weiter. Danach misst er alle zwei Sekunden neu und meldet nur Änderungen (Andocken, Auflösung). Er hat kein Fenster für `WM_DISPLAYCHANGE`, und das Messen kostet kaum etwas. Der Durchstich-Client meldet seine Monitore ebenso. Weil der Host die Rückkehrkanten nur bei einer Änderung schickt, merkt sich das Relay die zuletzt empfangenen und gibt sie einem neu verbundenen Injector sofort.

## Stand Phase 2 (Schritt 2: Windows-Dienst)

Der Client läuft als Windows-Dienst `InputFusionClient`, eingerichtet per Skript, siehe [Installation](installation.md).

- **Ein Ablauf für Dienst und Entwicklung:** `ClientService` hält den Injector am Leben (`InjectorSupervisor`) und die Verbindung zum Host, mit Neuverbindung. Im Dienst kommt die Host-Adresse aus `client.json`, und der Injector startet per `CreateProcessAsUser` als SYSTEM in der aktiven Sitzung (`SessionLauncher`), wie oben unter [Dienst](#dienst) beschrieben. Mit `--relay <host> --key <schlüssel>` läuft derselbe Ablauf als Konsole, mit Host und Schlüssel aus den Argumenten und dem Injector als Kindprozess. Ohne Argument aus einer Konsole im Installationsordner gestartet, läuft der Dienst-Ablauf sichtbar mit (anderswo fragt die exe stattdessen, ob sie sich installieren soll), aber ebenfalls mit Kindprozess, weil nur SYSTEM die Sitzung eines Tokens umsetzen darf.
- **Verifiziert auf dem PC** mit einem Test-Host: Dienst per Skript installiert, Injector läuft in Sitzung 1, Dienst in Sitzung 0; Verbindung nach Start des Hosts innerhalb des Wiederholungsabstands; Austritt über Dienst und Injector kommt beim Host an; nach Verbindungsabbruch neu verbunden; ein beendeter Injector wird binnen einer Sekunde neu gestartet und übernimmt die Pipe; Deinstallation ohne Reste.
- **Noch offen:** der Test des Sitzungswechsels (Abmelden, Benutzerwechsel) am Notebook; die Prüfung, dass der SYSTEM-Injector nur mit dem eigenen Dienst spricht. Bisher könnte ein Prozess, der die Pipe vor dem Dienst anlegt, dem Injector Eingaben unterschieben.

## Updates

Der Dienst aktualisiert sich selbst aus den GitHub-Releases, ohne Velopack; Ablauf, Prüfsumme und Rückfall auf die alte Version stehen unter [Updates](updates.md#client). Weicht die Protokollversion des Hosts ab, sucht er sofort.

## Stand: Desktop-Wechsel

Umgesetzt wie unter [Injector](#injector) geplant, weil am gesperrten Notebook Maus und Tastatur nichts taten: Sperrbildschirm und Anmeldung liegen auf dem Desktop „Winlogon“, `SendInput` wirkt aber nur auf den Desktop des aufrufenden Threads.

- **Eigener Abspiel-Thread** (`PlaybackThread`): Der Injector empfängt weiter asynchron, spielt aber alles auf einem festen Thread ab (Eingaben, Cursor setzen, `GetCursorPos` für die Rückkehrkanten, Tasten lösen). `SetThreadDesktop` wirkt pro Thread, und asynchroner Code springt zwischen Threadpool-Threads. Der Thread hat weder Fenster noch Hooks, sonst verweigert Windows den Wechsel. Eine Ausnahme beim Abspielen eines Ereignisses beendet ihn nicht.
- **Folgen** (`InputDesktop.Follow`): vor jedem Ereignis, höchstens alle 200 ms, `OpenInputDesktop` mit vollen Rechten; heißt der Eingabe-Desktop anders als der des Threads, `SetThreadDesktop`. Den vorher geöffneten Desktop schließt er erst danach. Der Austritt über eine Rückkehrkante geht vom Abspiel-Thread aus an die Pipe, ohne auf das Senden zu warten.
- **Nur als SYSTEM:** Als Benutzer verweigert Windows den Zugriff auf „Winlogon“. Mit `--relay` oder `--connect` als Benutzer bleibt der Sperrbildschirm deshalb weiter unerreichbar; der installierte Dienst startet den Injector als SYSTEM.
- **`Strg+Alt+Entf`** lässt sich so nicht einspielen. Verlangt das Notebook es zum Entsperren (Richtlinie „Interaktive Anmeldung: Kein STRG+ALT+ENTF erforderlich“ aus, auf Firmengeräten üblich), braucht es weiter `SendSAS`, siehe [Dienst](#dienst).
- **Noch nicht am Notebook geprüft.**

## Selbstinstallation

`ClientSetup`: Startet die exe ohne Argument, nicht als Dienst und nicht aus `%ProgramFiles%\InputFusion\Client`, gilt das als Doppelklick auf ein Setup.

1. Nachfrage im Konsolenfenster „als Dienst installieren? [J/n]“. Nein beendet ohne Änderung.
2. Neustart per UAC mit `--setup` (ist der Prozess schon erhöht, entfällt das). Im erhöhten Fenster fragt sie PC-Name, Schlüssel (mit denselben Regeln wie der Host, `HostConfig.ValidateKey`) und optional eine feste IP ab. Gibt es schon eine `client.json`, stehen deren Werte als Vorschlag in Klammern; Enter übernimmt sie.
3. Danach dasselbe wie `install-client.ps1`: vorhandenen Dienst stoppen, übrige Client-Prozesse beenden, **nur die exe selbst** als `InputFusion.Client.exe` nach `%ProgramFiles%\InputFusion\Client` kopieren (als Setup liegt sie umbenannt im Download-Ordner, neben fremden Dateien), `client.json` schreiben, Dienst anlegen bzw. umkonfigurieren, Neustart nach Absturz einstellen, starten. Das Fenster bleibt bis zu einem Tastendruck offen.
4. `client.json` entsteht gleich mit ihrer ACL (nur SYSTEM und Administratoren, ohne Vererbung); nachträglich gesetzt, wäre sie mit dem Schlüssel kurz für alle Benutzer lesbar, weil `%ProgramData%` Lesen vererbt.

Möglich ist das, weil der Release den Client als **einzelne Datei** veröffentlicht (`PublishSingleFile`, framework-abhängig, rund 3 MB). Die Log-Stufen für das Ereignisprotokoll stehen deshalb im Code statt in `appsettings.json`, das der einzelnen exe fehlen würde. Deinstalliert wird weiter mit `uninstall-client.ps1` aus dem Client-Paket.

Geprüft: Veröffentlichen als einzelne Datei, Start, Nachfrage, Abbruch ohne Änderung. Der erhöhte Teil (Dienst anlegen) ist noch nicht am Notebook gelaufen.

## Später

**Zwischenablage** braucht auf dem Client zusätzlich einen Prozess im Benutzerkontext: Dateien müssen mit den Rechten des Benutzers in dessen Profil landen, nicht als SYSTEM.
