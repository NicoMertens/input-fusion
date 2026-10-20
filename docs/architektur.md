# Architektur

InputFusion ist ein schlanker Ersatz für Mouse Without Borders aus den PowerToys: Maus und Tastatur des PCs steuern zusätzlich ein Notebook. Drei Dinge sollen besser werden als beim Vorbild:

- **Layout je Monitor statt je Rechner.** Das Vorbild kennt nur ganze Rechner im 1×4- oder 2×2-Raster. Hier liegen alle Monitore beider Rechner frei angeordnet auf einer Fläche, wie in den Windows-Anzeigeeinstellungen.
- **Oberflächen nur bei Bedarf.** Dauerhaft laufen nur schlanke Hintergrundprozesse. WPF wird nur geladen, wenn jemand die Einstellungen öffnet.
- **Leichtgewichtige Dauerprozesse.** Auf dem PC läuft dauerhaft nur der Agent, auf dem Notebook nur Dienst und Injector. Veröffentlicht wird framework-abhängig, ohne NativeAOT. Der Code bleibt aber AOT-fähig, sodass ein späterer Umstieg nur die Projektdateien ändert.

## Vorbild: so arbeitet Mouse Without Borders

- Low-Level-Hooks (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) fangen Eingaben ab. Im Remote-Modus gibt der Hook einen Wert ungleich 0 zurück und verschluckt die Eingabe damit lokal.
- Übertragung als kleine Pakete fester Größe über TCP, Peer-to-Peer ohne Server. Port 15100 für Eingaben, 15101 für Zwischenablage und Dateien.
- AES-Verschlüsselung, Schlüssel per PBKDF2 aus dem gemeinsamen Sicherheitsschlüssel. Eine daraus berechnete Magic Number in jedem Paket filtert Absender mit falschem Schlüssel aus.
- Der Ziel-PC spielt die Eingaben mit `SendInput` ab; Mauspositionen sind auf 0…65535 über den virtuellen Desktop normiert.
- Ein optionaler SYSTEM-Dienst startet einen Hilfsprozess auf dem Winlogon-Desktop, damit Sperrbildschirm und UAC-Abfrage steuerbar sind.

Diese Angaben stammen aus der Planung und sind nicht gegen den Quellcode von Mouse Without Borders geprüft.

## Prozesse

```
PC (Server)                                    Notebook (Client)
┌───────────────────────────────┐              ┌──────────────────────────────────────┐
│ Einstellungen (WPF)           │              │ Einstellungen (WPF)                  │
│  Settings.exe, bei Bedarf     │              │  Settings.exe, bei Bedarf            │
│      │ Named Pipe             │              │      │ Named Pipe                    │
│ Agent  Host.exe               │   TLS/TCP    │ Dienst  Client.exe                   │
│  Benutzerkontext, erhöht      │◄─────────────│  SYSTEM, Session 0                   │
│  Tray, Hooks, Layout-Engine,  │  Client baut │  Verbindung, Kopplung, Konfiguration │
│  TCP-Listener                 │  Verbindung  │      │ Named Pipe (nur SYSTEM)       │
└───────────────────────────────┘     auf      │ Injector  Client.exe --injector      │
                                               │  SYSTEM, Session N: SendInput,       │
                                               │  Desktop-Wechsel, Monitore/DPI       │
                                               └──────────────────────────────────────┘
```

| Prozess | Programm | Seite | Kontext | Läuft | Aufgabe |
|---|---|---|---|---|---|
| Agent | `InputFusion.Host.exe` | PC | Benutzer, erhöht | dauerhaft, Aufgabenplanung bei Anmeldung | Hooks, Layout-Engine, TCP-Listener, Tray, Konfiguration |
| Dienst | `InputFusion.Client.exe` | Notebook | SYSTEM, Session 0 | dauerhaft, Windows-Dienst | Verbindung, Kopplung, Konfiguration, startet den Injector |
| Injector | `InputFusion.Client.exe --injector` | Notebook | SYSTEM, Benutzersitzung | solange eine Konsolensitzung aktiv ist | `SendInput`, Desktop-Wechsel, Monitore messen |
| Einstellungen | `InputFusion.Settings.exe` | beide | Benutzer | bei Bedarf, aus Tray oder Startmenü | WPF: auf dem PC Layout-Editor, Kopplung, Tastenkürzel; auf dem Notebook Server-Adresse, Kopplung, Status |

**Grundsatz:** Die Konfiguration hat je Seite genau einen Besitzer, den Agent bzw. den Dienst. Die Oberflächen lesen und schreiben nur per IPC. Ob sie laufen, ist für den Betrieb egal.

## Server (PC)

**Agent (`InputFusion.Host`)**

- Hooks auf einem eigenen Thread mit eigener Nachrichtenschleife, nie auf einem UI-Thread. Der Callback schreibt nur in einen `Channel<T>`. Braucht er länger als `LowLevelHooksTimeout`, entfernt Windows den Hook stillschweigend.
- **Erhöht starten** (Aufgabe „mit höchsten Rechten“): Ein nicht erhöhter Low-Level-Hook bekommt wegen UIPI keine Eingaben, solange ein erhöhtes Fenster im Vordergrund ist.
- Remote-Modus: Cursor parken, Deltas über Raw Input (`WM_INPUT`), Eingaben verschlucken. Die eigenen Tastenkürzel werden vorher ausgewertet.
- Tray direkt über `Shell_NotifyIcon` mit einem eigenen Nachrichtenfenster, ohne WinForms und ohne WPF. WinForms ist nicht AOT-fähig.
- Konfiguration in `%LOCALAPPDATA%\InputFusion\config.json`.

**Einstellungen (`InputFusion.Settings`)**: eine WPF-App für beide Seiten, in beiden Installern enthalten.

- Erkennt die Seite daran, welche Pipe erreichbar ist: die des Agents oder die des Dienstes. Danach zeigt sie den Layout-Editor oder die Client-Einstellungen.
- Single-Instance über Mutex; ein zweiter Start holt das vorhandene Fenster nach vorn.
- Bezieht alles per IPC vom Agent bzw. Dienst. Auf dem PC gehören dazu auch die Monitore des Clients, live, solange er verbunden ist. Änderungen wirken ohne Neustart des Agents.
- Bleibt auch bei einem späteren AOT-Umstieg normales .NET, weil WPF kein AOT unterstützt. Deshalb braucht jeder der beiden Rechner ohnehin die .NET 10 Desktop Runtime, und ein AOT-Publish von Host und Client würde diese Abhängigkeit nicht beseitigen.

**Bewusst nicht vorgesehen:** Steuerung bei gesperrtem PC. Der Sperrbildschirm liegt auf dem Secure Desktop, dort laufen die Hooks nicht. Ein Dienst auf dem Server würde das lösen, lohnt sich aber kaum.

## Client (Notebook)

Dienst und Injector sind **ein Programm** (`InputFusion.Client.exe`) in zwei Prozessen. Ohne Argument startet der Dienst, mit `--injector` der Injector. Das spart ein zweites Projekt und ein zweites Programm im Installer. Weil der Dienst Session 0 nicht verlassen kann, bleiben es trotzdem zwei Prozesse.

**Dienst** (Worker Service mit `AddWindowsService()`, LocalSystem, Starttyp automatisch)

- Spielt nicht selbst ab: Session 0 ist isoliert, `SendInput` aus dem Dienst erreicht keinen Desktop.
- Reagiert auf `OnSessionChange` (Anmelden, Abmelden, Sperren, Benutzerwechsel, RDP). Wechselt die aktive Konsolensitzung (`WTSGetActiveConsoleSessionId`), startet er den Injector dort neu.
- Injector-Start: eigenes SYSTEM-Token mit `DuplicateTokenEx` kopieren, `SetTokenInformation(TokenSessionId)` auf die Zielsitzung setzen, dann `CreateProcessAsUser` auf die eigene exe mit `--injector` und `lpDesktop = "winsta0\\default"`. Die exe ist eine Konsolenanwendung, damit der Dienst beim Entwickeln ins Terminal loggt. Deshalb braucht der Start `CREATE_NO_WINDOW`, sonst öffnet sich in der Benutzersitzung ein Konsolenfenster.
- Konfiguration in `%ProgramData%\InputFusion\`, ACL nur für SYSTEM und Administratoren.
- Option `Strg+Alt+Entf` über `SendSAS` (sas.dll). Setzt die Richtlinie *SoftwareSASGeneration* voraus, die Diensten das erlauben muss.

**Injector**

- Desktop-Wechsel nach VNC-Muster: Vor dem Abspielen prüft der Thread mit `OpenInputDesktop` den aktiven Desktop (Default, Winlogon, UAC) und wechselt bei Bedarf mit `SetThreadDesktop` dorthin. Ein Prozess deckt so Desktop, Sperrbildschirm und UAC-Abfrage ab. Als SYSTEM gibt es kein UIPI-Problem mit erhöhten Fenstern.
- Misst die Monitore, weil nur er in der Sitzung die echten Werte sieht: `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, Geräte-IDs. Bei `WM_DISPLAYCHANGE` (Andocken, Auflösung) meldet er neu.
- Führt gedrückte Tasten nach und lässt beim Wechsel oder Verbindungsabbruch alle los.
- Absichtlich dumm gehalten: kein Netzwerk, keine Konfiguration. Wenig Code im SYSTEM-Kontext heißt wenig Angriffsfläche. Der Code liegt zwar in derselben exe wie der Dienst, der Injector-Pfad öffnet aber weder Sockets noch Konfiguration.

**Einstellungen auf dem Notebook:** Server-Adresse, Kopplungscode, Status, Vorschau der eigenen Monitore. Kein Layout-Editor, das Layout gehört dem Server.

**Zwischenablage (später)** braucht auf dem Client zusätzlich einen Prozess im Benutzerkontext: Dateien müssen mit den Rechten des Benutzers in dessen Profil landen, nicht als SYSTEM.

## Netzwerk und Sicherheit

- **Der Client verbindet sich zum Server.** Das Notebook wechselt die Netze, der PC steht fest. So braucht nur der PC eine Firewall-Regel für eingehende Verbindungen.
- TCP mit `NoDelay`, Frames aus Länge, Typ und Nutzlast. Bei Stau, etwa im WLAN, werden aufeinanderfolgende Mausbewegungen zusammengefasst.
- `SslStream` mit selbstsigniertem Zertifikat je Seite. Kopplung einmalig per Code, danach ist der Zertifikats-Fingerprint fest hinterlegt (Pinning). Das ist sauberer als der gemeinsame Schlüssel des Vorbilds.
- Heartbeat und automatische Neuverbindung nach Standby oder Netzwechsel.
- Server finden: zunächst Hostname oder IP, später zusätzlich UDP-Broadcast.

## IPC

| Verbindung | Weg | ACL der Pipe |
|---|---|---|
| Einstellungen → Agent | Named Pipe, JSON-Nachrichten | nur der angemeldete Benutzer |
| Einstellungen → Dienst | Named Pipe, JSON-Nachrichten | interaktive Benutzer |
| Dienst ↔ Injector | Named Pipe, binär | nur SYSTEM |

Die Nachrichten liegen in `InputFusion.Core`. JSON läuft über System.Text.Json mit Source-Generator, damit Agent und Dienst AOT-fähig bleiben. Gerahmt wird wie im Netzwerkprotokoll mit Länge und Typ. Kein StreamJsonRpc: Ob es mit AOT funktioniert, ist nicht geprüft, und für eine Handvoll Nachrichten lohnt die Abhängigkeit nicht.

## Multimonitor-Layout

1. Beide Seiten melden ihre Monitore: Rechteck in physischen Pixeln, DPI, Geräte-ID.
2. Der Layout-Editor in den Einstellungen ist ein WPF-`Canvas` mit allen Monitoren beider Rechner als Rechtecke. Sie werden per Drag & Drop angeordnet und rasten an den Kanten ein.
3. Die Layout-Engine erzeugt daraus Kantenabschnitte, etwa „PC-Monitor 2 rechts, y 200–900 → Notebook-Monitor 1 links“. Der Austrittspunkt wird proportional oder in Millimetern auf die Zielkante umgerechnet, damit die Maus bei unterschiedlich großen Monitoren nicht springt.
4. Gespeichert wird je Monitor-Kombination mit den Geräte-IDs als Schlüssel: Angedockt und unterwegs sind zwei verschiedene Layouts.

Die Engine liegt in `InputFusion.Core` unter dem Namespace `InputFusion.Core.Layout`. Dieser Namespace bleibt bewusst frei von Win32-Aufrufen. Die Engine ist reine Logik (Rechtecke rein, Übergänge raus) und damit voll unit-testbar. Das ist wichtig, weil sie der fehleranfälligste Teil ist.

## Win32-Fallstricke

- **DPI:** Alle Prozesse mit Desktop-Zugriff laufen Per-Monitor-V2-DPI-aware, sonst rechnet Windows die Koordinaten um. Gemischte Skalierung (PC 100 %, Notebook 150 %) ist der Normalfall. Gesetzt wird das für alle drei Programme über das gemeinsame `source/app.manifest`. Im Dienst wirkt es nicht, dort schadet es aber auch nicht.
- **Relativ oder absolut senden** bestimmt maßgeblich, wie sich die Maus anfühlt. Früh im Durchstich ausprobieren.
- **Hängende Tasten:** Beim Wechsel auf der alten Seite Key-Up für alle gedrückten Tasten nachsenden.
- **Nicht abfangbar:** `Strg+Alt+Entf`. `Win+L` sperrt immer den lokalen Rechner.
- **EDR und Virenscanner:** Globale Tastatur-Hooks plus Eingabe-Injection sehen aus wie ein Keylogger. Auf Firmengeräten vorher mit der IT klären.
- **Interop über CsWin32** statt handgeschriebener P/Invoke-Signaturen.

## AOT-Fähigkeit und Speicher

- **AOT-fähig halten, ohne AOT zu veröffentlichen.** Host und Client setzen `EnableAotAnalyzer` und `EnableTrimAnalyzer`, Core setzt `IsAotCompatible`. Damit meldet schon der normale Build alles, was einem späteren AOT-Publish im Weg stünde. Für Host und Client gilt: keine reflection-lastigen Bibliotheken, kein WinForms und kein WPF, JSON nur mit Source-Generator.
- **Was AOT bräuchte:** `<PublishAot>true</PublishAot>` in Host und Client sowie den MSVC-Linker aus der Visual-Studio-Workload „Desktopentwicklung mit C++“ auf dem Build-Rechner. NativeAOT kompiliert zu einer `.obj`, die erst der Plattform-Linker zur exe bindet, zusammen mit den Windows-Importbibliotheken und der C-Laufzeit. Ohne Linker bricht der Publish schon vor der AOT-Analyse ab.
- **Prüfung ohne Linker:** Ein getrimmter Publish (`-p:PublishTrimmed=true --self-contained`) meldet die Trim-Warnungen. Am 2026-10-20 gab es dabei weder für den Client mit Hosting und WindowsServices noch für den Host eine einzige.
- **Speicher ohne AOT**, gemessen am 2026-10-20 nach vier Sekunden Leerlauf:
  - Ein minimaler Dauerprozess hat 22 MB Arbeitsspeicher, davon 6 MB privat, und ist framework-abhängig 186 KB groß.
  - Der Client-Dienst hat 34 MB Arbeitsspeicher, davon 9 MB privat, und ist 2,6 MB groß.
  - Self-contained getrimmt ist der Speicher etwa gleich, die Ausgabe aber rund 20 MB groß.
  - Ohne Concurrent GC und ohne PGO sinkt der Bedarf um weniger als 1 MB.
  - Der Arbeitsspeicher enthält die DLLs der .NET-Laufzeit, die sich alle .NET-Prozesse teilen; aussagekräftig ist der private Anteil.

## Repo-Aufbau

- Im Wurzelverzeichnis liegen nur `source/`, `docs/`, `README.md`, `global.json`, `.gitignore` und `.gitattributes`. Build- und Veröffentlichungsskripte kommen nach `build/` parallel zu `source/`, sobald es welche gibt.
- `source/` enthält flach nebeneinander alle Projekte einschließlich der Testprojekte (`*.Tests`, kein eigener Unterordner), dazu `InputFusion.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` und das gemeinsame `app.manifest`.
- `global.json` liegt im Wurzelverzeichnis und legt das SDK auf .NET 10 fest. Das SDK sucht die Datei vom Arbeitsverzeichnis aufwärts, deshalb greift sie auch für `source/`.
- `Directory.Build.props` setzt Nullable, ImplicitUsings und Nullable-Warnungen als Fehler. `Directory.Packages.props` verwaltet die Paketversionen zentral.
- `nuget.config` kennt nur nuget.org, damit der Restore nicht von rechnerweit eingetragenen Feeds abhängt.

| Projekt | Ziel | Inhalt |
|---|---|---|
| `InputFusion.Core` | Bibliothek, `net10.0-windows`, `IsAotCompatible` | Layout-Engine samt Monitor-Modell, Netzwerk-Nachrichten und Framing, IPC-Nachrichten, CsWin32-Interop |
| `InputFusion.Host` | WinExe, AOT-Analyzer an | Agent, siehe Server |
| `InputFusion.Client` | Worker Service (Konsole), AOT-Analyzer an | Dienst und Injector, siehe Client |
| `InputFusion.Settings` | WPF | Einstellungen für beide Seiten |
| `InputFusion.Tests` | xUnit, `net10.0-windows` | Layout-Engine, Kantenumrechnung, Protokoll |

Später kommt der Installer dazu: WiX mit zwei MSIs. Das Server-MSI enthält Host, Settings, die Aufgabe und die Firewall-Regel, das Client-MSI enthält Client, Settings und den Dienst.

**Warum nur so wenige Projekte:** Die Zahl der Projekte macht die App zur Laufzeit kaum schwerer. Mit JIT lädt .NET Assemblies erst bei Bedarf. Bei einem AOT-Publish würden Host und Client samt aller Bibliotheken zu je einer getrimmten Binärdatei. Gewicht kosten die Dauerprozesse und ihre Laufzeit. Deshalb trennt das Repo nur dort, wo eine Prozessgrenze technisch erzwungen ist: Session 0 kann nicht abspielen, und WPF soll nur bei Bedarf geladen werden. Alle Bibliothekslogik liegt in `InputFusion.Core`.
