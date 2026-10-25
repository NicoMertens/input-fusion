# Client: Dienst, Injector und Tray auf dem Notebook

Dienst, Injector und Tray sind **ein Programm** (`InputFusion.Client.exe`) in drei Prozessen. Ohne Argument startet der Dienst, mit `--injector` der Injector, mit `--tray` das Tray-Symbol; per Doppelklick außerhalb des Installationsordners installiert sich die exe selbst, siehe [Installation](installation.md). Das spart ein zweites Projekt und ein zweites Programm im Installer. Weil der Dienst Session 0 nicht verlassen kann und der Injector als SYSTEM keine Oberfläche zeigen soll, bleiben es trotzdem drei Prozesse.

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
- **Meldet Angaben zum Notebook** (`ClientInfoReporter`): Name, Windows-Version und angemeldeten Benutzer als `ClientInfo` (20), JSON mit optionalen Feldern, damit spätere Versionen weitere ergänzen können. Der Host zeigt sie nur an, siehe [Einstellungen](einstellungen.md#stand-einstellungen-des-pcs). Gesendet wird direkt nach der Anmeldung, danach alle zwei Sekunden geprüft und nur bei Änderung: Das Anmelden nach dem Hochfahren geschieht in derselben Sitzung, ohne dass der Injector neu startet. Der Dienst schickt sie selbst, nicht der Injector, der als SYSTEM nur abspielen soll. Die Windows-Version stammt aus `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`; dort steht unter `ProductName` auch auf Windows 11 noch „Windows 10“, deshalb entscheidet die Build-Nummer (ab 22000). Den Benutzer der aktiven Sitzung (`ActiveSession`, dieselbe wie für die [Zwischenablage](zwischenablage.md)) liefert `WTSQuerySessionInformation`, leer, solange niemand angemeldet ist. Der Simulator meldet sich als „<PC-Name> (Simulator)“.
- **Konfiguration** in `%ProgramData%\InputFusion\client.json` (Rechnername des PCs, gemeinsamer Schlüssel, optional feste IP und Port, Standard 15111), ACL nur für SYSTEM und Administratoren. Der Dienst ist ihr einziger Besitzer; Setup oder Installationsskript legen sie an (siehe [Installation](installation.md)), danach ändert sie ein Administrator in den [Einstellungen](einstellungen.md#stand-einstellungen-des-notebooks), die über IPC den Dienst schreiben lassen.
- **`Strg+Alt+Entf`** optional über `SendSAS` (sas.dll). Setzt die Richtlinie *SoftwareSASGeneration* voraus, die Diensten das erlauben muss.

## Injector

- **Desktop-Wechsel nach VNC-Muster:** Vor dem Abspielen prüft der Thread mit `OpenInputDesktop` den aktiven Desktop (Default, Winlogon, UAC) und wechselt bei Bedarf mit `SetThreadDesktop` dorthin. Ein Prozess deckt so Desktop, Sperrbildschirm und UAC-Abfrage ab. Als SYSTEM gibt es kein UIPI-Problem mit erhöhten Fenstern.
- **Misst die Monitore**, weil nur er in der Sitzung die echten Werte sieht: `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, Geräte-IDs. Bei `WM_DISPLAYCHANGE` (Andocken, Auflösung) meldet er neu. Was mit den Werten geschieht, steht unter [Layout](layout.md).
- **Führt gedrückte Tasten nach** und lässt beim Wechsel oder Verbindungsabbruch alle los.
- **Absichtlich dumm gehalten:** kein Netzwerk, keine Konfiguration. Wenig Code im SYSTEM-Kontext heißt wenig Angriffsfläche. Der Code liegt zwar in derselben exe wie der Dienst, der Injector-Pfad öffnet aber weder Sockets noch Konfiguration.

## Tray

Das Tray-Symbol auf dem Notebook zeigt im Tooltip den Zustand des Dienstes: verbunden mit welchem Host, sonst die letzte Störung („Host … nicht erreichbar“, „stimmt der Schlüssel?“) oder „Dienst nicht erreichbar“. Doppelklick oder „Einstellungen …“ im Kontextmenü öffnet die [Einstellungen](einstellungen.md#stand-einstellungen-des-notebooks) als angemeldeter Benutzer; darüber steht ausgegraut die Version und darunter „Nach Updates suchen“, das der Dienst ausführt (siehe [Updates](updates.md#client)).

- **Eigener Prozess als angemeldeter Benutzer** (`ClientTray`, `--tray`): Der Dienst in Session 0 kann kein Symbol zeigen, und der Injector soll als SYSTEM keine Oberfläche haben. Als Benutzer gestartet, liest und schreibt er auch die [Zwischenablage](zwischenablage.md), die dem Benutzer gehört; später landen so auch Dateien mit dessen Rechten in dessen Profil.
- **Start durch den Dienst**, wie der Injector über einen `SessionSupervisor`, aber mit dem Token des Benutzers: `WTSQueryUserToken` für die aktive Konsolensitzung, `CreateEnvironmentBlock` für dessen Umgebung (sonst erbte der Prozess TEMP und APPDATA von SYSTEM), dann `CreateProcessAsUser` mit `CREATE_NO_WINDOW`. Solange niemand angemeldet ist, schlägt `WTSQueryUserToken` fehl; der Supervisor meldet das einmal und versucht es jede Sekunde wieder. Beendet ein Update oder das Setup alle Client-Prozesse, startet der Dienst das Symbol danach neu. Mit `--relay` läuft es als Kindprozess.
- **Kein „Beenden“:** Der Dienst liefe ohnehin weiter, und der Supervisor startete das Symbol binnen einer Sekunde neu.
- **Endet mit dem Dienst**, wie der Injector: Der Dienst gibt seine PID mit (`--tray <pid>`, `--injector <pid>`), und beide prüfen jede Sekunde in der Prozessliste, ob es ihn noch gibt (`OwnerProcess`). Nach einem Absturz des Dienstes kennt der neu gestartete die alten Prozesse nicht, es gäbe sonst zwei Symbole und zwei Injectoren. Warten auf das Prozess-Handle geht nicht, ein Benutzer bekommt es für einen SYSTEM-Prozess nicht; der Name in der Prozessliste schützt vor einer neu vergebenen PID. Geprüft mit `--relay`: Nach dessen Ende sind Injector und Tray binnen einer Sekunde weg.
- **Zustand über die [Pipe des Dienstes](ipc.md#stand-tray-und-einstellungen--dienst):** Der Dienst schickt ihn beim Verbinden und bei jeder Änderung. Bricht die Pipe ab, verbindet das Symbol jede Sekunde neu.
- **Symbol:** `InputFusion.Client.ico` (Notebook blau, Monitor grau) als `ApplicationIcon` der Client-exe, dieselbe Tray-Klasse wie beim [Host](host.md#stand-tray-agent) (`TrayIcon` in Core).
- **Geprüft** auf dem PC mit `--relay` und als installierter Dienst gegen einen Host auf demselben Rechner: Der Dienst startet das Symbol in Sitzung 1 als angemeldeter Benutzer, nicht erhöht; der Zustand des Dienstes zeigt nicht erreichbaren Host, abweichende Protokollversion, Verbindung mit Adresse und Version und nach dem Beenden des Hosts wieder „nicht verbunden“. Der Tooltip selbst zeigt ihn erst, seit das Symbol den Besitzer der Pipe prüft statt das Konto des Dienst-Prozesses, das ein Benutzer nicht lesen darf (siehe [IPC](ipc.md#stand-tray-und-einstellungen--dienst)). Nach einer Neuinstallation gibt es nur ein Symbol; beendet man `--relay`, endet das Symbol mit. Noch nicht am Notebook (Anmelden, Benutzerwechsel).

## Stand Phase 1 (Durchstich)

Im Durchstich läuft der Client als einfaches Konsolenprogramm: `InputFusion.Client.exe --connect <host> --key <schlüssel> [--port <nummer>]`; `--relay` nimmt dieselben Argumente. Es verbindet sich zum Host, empfängt die Eingaben und spielt sie direkt mit `SendInput` ab. Damit lässt sich das Mausgefühl früh testen, ohne Dienst, Sitzungswechsel und Desktop-Wechsel.

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

- **Ein Ablauf für Dienst und Entwicklung:** `ClientService` hält Injector und [Tray](#tray) am Leben (je ein `SessionSupervisor`) und die Verbindung zum Host, mit Neuverbindung. Im Dienst kommt die Host-Adresse aus `client.json`, und der Injector startet per `CreateProcessAsUser` als SYSTEM in der aktiven Sitzung (`SessionLauncher`), wie oben unter [Dienst](#dienst) beschrieben. Mit `--relay <host> --key <schlüssel>` läuft derselbe Ablauf als Konsole, mit Host und Schlüssel aus den Argumenten und dem Injector als Kindprozess. Ohne Argument aus einer Konsole im Installationsordner gestartet, läuft der Dienst-Ablauf sichtbar mit (anderswo fragt die exe stattdessen, ob sie sich installieren soll), aber ebenfalls mit Kindprozess, weil nur SYSTEM die Sitzung eines Tokens umsetzen darf.
- **Verifiziert auf dem PC** mit einem Test-Host: Dienst per Skript installiert, Injector läuft in Sitzung 1, Dienst in Sitzung 0; Verbindung nach Start des Hosts innerhalb des Wiederholungsabstands; Austritt über Dienst und Injector kommt beim Host an; nach Verbindungsabbruch neu verbunden; ein beendeter Injector wird binnen einer Sekunde neu gestartet und übernimmt die Pipe; Deinstallation ohne Reste.
- **Nur der eigene Dienst:** Der Injector spielt nur ab, wenn der Server der Pipe unter demselben Konto läuft wie er, im Dienst also SYSTEM; siehe [IPC](ipc.md#absicherung).
- **Noch offen:** der Test des Sitzungswechsels (Abmelden, Benutzerwechsel) am Notebook.

## Updates

Der Dienst aktualisiert sich selbst aus den GitHub-Releases, ohne Velopack; Ablauf, Prüfsumme und Rückfall auf die alte Version stehen unter [Updates](updates.md#client). Weicht die Protokollversion des Hosts ab, sucht er sofort.

## Stand: Desktop-Wechsel

Umgesetzt wie unter [Injector](#injector) geplant, weil am gesperrten Notebook Maus und Tastatur nichts taten: Sperrbildschirm und Anmeldung liegen auf dem Desktop „Winlogon“, `SendInput` wirkt aber nur auf den Desktop des aufrufenden Threads.

- **Eigener Abspiel-Thread** (`PlaybackThread`): Der Injector empfängt weiter asynchron, spielt aber alles auf einem festen Thread ab (Eingaben, Cursor setzen, `GetCursorPos` für die Rückkehrkanten, Tasten lösen). `SetThreadDesktop` wirkt pro Thread, und asynchroner Code springt zwischen Threadpool-Threads. Der Thread hat weder Fenster noch Hooks, sonst verweigert Windows den Wechsel. Eine Ausnahme beim Abspielen eines Ereignisses beendet ihn nicht.
- **Folgen** (`InputDesktop.Follow`): vor jedem Ereignis, höchstens alle 200 ms, `OpenInputDesktop` mit vollen Rechten; heißt der Eingabe-Desktop anders als der des Threads, `SetThreadDesktop`. Den vorher geöffneten Desktop schließt er erst danach. Der Austritt über eine Rückkehrkante geht vom Abspiel-Thread aus an die Pipe, ohne auf das Senden zu warten.
- **Nur als SYSTEM:** Als Benutzer verweigert Windows den Zugriff auf „Winlogon“. Mit `--relay` oder `--connect` als Benutzer bleibt der Sperrbildschirm deshalb weiter unerreichbar; der installierte Dienst startet den Injector als SYSTEM.
- **Sperren:** Auf `LockWorkstation` vom Host (Win+L auf dem PC, siehe [Host](host.md)) lässt der Dispatcher alle Tasten los und ruft `LockWorkStation` auf. Eingespieltes Win+L sperrt nicht, Windows ignoriert es. Danach führt der Desktop-Wechsel die weiteren Eingaben auf den Sperrbildschirm.
- **`Strg+Alt+Entf`** lässt sich so nicht einspielen. Verlangt das Notebook es zum Entsperren (Richtlinie „Interaktive Anmeldung: Kein STRG+ALT+ENTF erforderlich“ aus, auf Firmengeräten üblich), braucht es weiter `SendSAS`, siehe [Dienst](#dienst).
- **Noch nicht am Notebook geprüft.**

## Selbstinstallation

`ClientSetup`: Startet die exe ohne Argument, nicht als Dienst und nicht aus `%ProgramFiles%\InputFusion\Client`, gilt das als Doppelklick auf ein Setup.

1. Nachfrage im Konsolenfenster „als Dienst installieren? [J/n]“. Nein beendet ohne Änderung.
2. Neustart per UAC mit `--setup` (ist der Prozess schon erhöht, entfällt das). Im erhöhten Fenster fragt sie PC-Name, Schlüssel (mit denselben Regeln wie der Host, `HostConfig.ValidateKey`), optional eine feste IP und den Port (Vorschlag 15111) ab. Gibt es schon eine `client.json`, stehen deren Werte als Vorschlag in Klammern; Enter übernimmt sie.
3. Danach dasselbe wie `install-client.ps1`: vorhandenen Dienst stoppen, übrige Client-Prozesse beenden, **nur die exe selbst** als `InputFusion.Client.exe` nach `%ProgramFiles%\InputFusion\Client` kopieren (als Setup liegt sie umbenannt im Download-Ordner, neben fremden Dateien), `client.json` schreiben, Dienst anlegen bzw. umkonfigurieren, Neustart nach Absturz einstellen, starten. Das Fenster bleibt bis zu einem Tastendruck offen.
4. `client.json` entsteht gleich mit ihrer ACL (nur SYSTEM und Administratoren, ohne Vererbung); nachträglich gesetzt, wäre sie mit dem Schlüssel kurz für alle Benutzer lesbar, weil `%ProgramData%` Lesen vererbt.

Möglich ist das, weil der Release den Client als **einzelne Datei** veröffentlicht (`PublishSingleFile`, framework-abhängig, rund 3 MB). Die Log-Stufen für das Ereignisprotokoll stehen deshalb im Code statt in `appsettings.json`, das der einzelnen exe fehlen würde. Deinstalliert wird weiter mit `uninstall-client.ps1` aus dem Client-Paket.

Geprüft: Veröffentlichen als einzelne Datei, Start, Nachfrage, Abbruch ohne Änderung. Der erhöhte Teil (Dienst anlegen) ist noch nicht am Notebook gelaufen.

## Einstellungen ausliefern

Der Release veröffentlicht die Einstellungen als einzelne Datei (rund 700 KB, framework-abhängig) und bettet sie in die Client-exe ein (`-p:EmbeddedSettings=…`, Ressource `InputFusion.Settings.exe`). Der installierte Dienst packt sie bei jedem Start neben sich aus (`SettingsExtractor`), wenn sie fehlt oder sich unterscheidet: erst als `.new` daneben, dann getauscht. So bringen Setup per Doppelklick, Installationsskript und Selbst-Update sie mit, ohne dass sie einzeln verteilt werden. Sind sie gerade offen, klappt das Auspacken nicht; der Dienst meldet das und versucht es beim nächsten Start. Setup und Update-Helfer beenden vorher offene Einstellungen aus dem Installationsordner, sonst sperrten sie den Tausch (`ClientProcesses`). Builds ohne die Eigenschaft bringen keine mit; beim Entwickeln findet das Tray sie unter `_out\InputFusion.Settings`.

Geprüft: lokal wie im Release veröffentlicht (Client-exe rund 700 KB größer), installiert, vom Dienst ausgepackt und aus dem Tray gestartet.
