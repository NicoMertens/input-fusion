# InputFusion – Dokumentation

InputFusion ist ein schlanker Ersatz für Mouse Without Borders aus den PowerToys: Maus und Tastatur des PCs steuern zusätzlich ein Notebook. Drei Dinge sollen besser werden als beim [Vorbild](vorbild.md):

- **Layout je Monitor statt je Rechner.** Das Vorbild kennt nur ganze Rechner im 1×4- oder 2×2-Raster. Hier liegen alle Monitore beider Rechner frei angeordnet auf einer Fläche, wie in den Windows-Anzeigeeinstellungen.
- **Oberflächen nur bei Bedarf.** Dauerhaft laufen nur schlanke Hintergrundprozesse. WPF wird nur geladen, wenn jemand die Einstellungen öffnet.
- **Leichtgewichtige Dauerprozesse.** Auf dem PC läuft dauerhaft nur der Agent, auf dem Notebook nur Dienst und Injector. Veröffentlicht wird framework-abhängig, der Code bleibt aber AOT-fähig, siehe [AOT und Speicher](aot-und-speicher.md).

## Prozesse

```
PC (Server)                                    Notebook (Client)
┌───────────────────────────────┐              ┌──────────────────────────────────────┐
│ Einstellungen (WPF)           │              │ Einstellungen (WPF)                  │
│  Settings.exe, bei Bedarf     │              │  Settings.exe, bei Bedarf            │
│      │ Named Pipe             │              │      │ Named Pipe                    │
│ Agent  Host.exe               │ TCP, AES-GCM │ Dienst  Client.exe                   │
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
| [Agent](host.md) | `InputFusion.Host.exe` | PC | Benutzer, erhöht | dauerhaft, Aufgabenplanung bei Anmeldung | Hooks, Layout-Engine, TCP-Listener, Tray, Konfiguration |
| [Dienst](client.md#dienst) | `InputFusion.Client.exe` | Notebook | SYSTEM, Session 0 | dauerhaft, Windows-Dienst | Verbindung, Kopplung, Konfiguration, startet den Injector |
| [Injector](client.md#injector) | `InputFusion.Client.exe --injector` | Notebook | SYSTEM, Benutzersitzung | solange eine Konsolensitzung aktiv ist | `SendInput`, Desktop-Wechsel, Monitore messen |
| [Einstellungen](einstellungen.md) | `InputFusion.Settings.exe` | beide | Benutzer | bei Bedarf, aus Tray oder Startmenü | WPF: auf dem PC Layout-Editor, Kopplung, Tastenkürzel; auf dem Notebook Server-Adresse, Kopplung, Status |

**Grundsatz:** Die Konfiguration hat je Seite genau einen Besitzer, den Agent bzw. den Dienst. Die Oberflächen lesen und schreiben nur per [IPC](ipc.md). Ob sie laufen, ist für den Betrieb egal.

## Inhalt

| Datei | Thema |
|---|---|
| [vorbild.md](vorbild.md) | Wie Mouse Without Borders arbeitet und was davon übernommen wird |
| [host.md](host.md) | Agent auf dem PC: Hooks, Remote-Modus, Tray, Rechte |
| [client.md](client.md) | Dienst und Injector auf dem Notebook: Sitzungen, Desktop-Wechsel, Abspielen |
| [einstellungen.md](einstellungen.md) | Die WPF-App für beide Seiten |
| [netzwerk.md](netzwerk.md) | Verbindung zwischen PC und Notebook, Anmeldung und Verschlüsselung |
| [ipc.md](ipc.md) | Named Pipes zwischen den Prozessen eines Rechners |
| [layout.md](layout.md) | Multimonitor-Layout und Layout-Engine |
| [fallstricke.md](fallstricke.md) | Win32-Fallstricke, die beide Seiten betreffen |
| [aot-und-speicher.md](aot-und-speicher.md) | AOT-Fähigkeit, Voraussetzungen für NativeAOT, gemessener Speicher |
| [repo-aufbau.md](repo-aufbau.md) | Ordner, Projekte, Build-Einstellungen und warum es so wenige Projekte sind |
| [installation.md](installation.md) | Einrichtung für die Entwicklung: Client-Dienst per Skript, Firewall-Regel für den Host |
| [updates.md](updates.md) | Selbst-Update von Host (Velopack, geplante Aufgabe) und Client, Releases |
| [simulator.md](simulator.md) | Entwicklungswerkzeug, das einen Client mit mehreren Monitoren in einem Fenster simuliert |
| [todo.md](todo.md) | Offene Aufgaben: was zu bauen, zu entscheiden und am Notebook zu prüfen ist |

## Regeln für diese Doku

- **Eine Datei je fachlichem Thema**, etwa Host, Client oder Layout. Wird eine Datei zu groß (Richtwert: rund 200 Zeilen oder 15 KB), wird sie entlang fachlicher Teilthemen aufgeteilt, nicht nach Zeilenzahl.
- **Verlinken statt wiederholen.** Querbezüge sind relative Links auf die zuständige Datei, bei Bedarf mit Anker (`client.md#injector`). Jede neue Datei kommt in die Tabelle oben.
- **Mit dem Code ändern.** Die Doku wird im selben Commit nachgezogen wie der Code, den sie beschreibt.
- Dateinamen in Kleinbuchstaben mit Bindestrich, ohne Umlaute.
