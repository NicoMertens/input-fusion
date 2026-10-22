# Repo-Aufbau

## Ordner

- **Im Wurzelverzeichnis** liegen nur `source/`, `docs/`, `README.md`, `global.json`, `.gitignore` und `.gitattributes`. Build- und Veröffentlichungsskripte kommen nach `build/` parallel zu `source/`, sobald es welche gibt.
- **`source/`** enthält flach nebeneinander alle Projekte einschließlich der Testprojekte (`*.Tests`, kein eigener Unterordner), dazu `InputFusion.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` und das gemeinsame `app.manifest`.
- **`docs/`** enthält diese Doku, Einstieg und Regeln in [README.md](README.md).

## Build-Einstellungen

- `global.json` liegt im Wurzelverzeichnis und legt das SDK auf .NET 10 fest. Das SDK sucht die Datei vom Arbeitsverzeichnis aufwärts, deshalb greift sie auch für `source/`.
- `Directory.Build.props` setzt Nullable, ImplicitUsings und Nullable-Warnungen als Fehler.
- `Directory.Packages.props` verwaltet die Paketversionen zentral.
- `nuget.config` kennt nur nuget.org, damit der Restore nicht von rechnerweit eingetragenen Feeds abhängt.

## Projekte

| Projekt | Ziel | Inhalt |
|---|---|---|
| `InputFusion.Core` | Bibliothek, `net10.0-windows`, `IsAotCompatible` | Layout-Engine samt Monitor-Modell, Netzwerk-Nachrichten und Framing, IPC-Nachrichten, CsWin32-Interop |
| `InputFusion.Host` | WinExe, AOT-Analyzer an | [Agent](host.md) |
| `InputFusion.Client` | Worker Service (Konsole), AOT-Analyzer an | [Dienst und Injector](client.md) |
| `InputFusion.Settings` | WPF | [Einstellungen](einstellungen.md) für beide Seiten |
| `InputFusion.Simulator` | WPF | [Simulator](simulator.md) für Tests auf einem Rechner; reines Entwicklungswerkzeug, nicht im Installer |
| `InputFusion.Tests` | xUnit, `net10.0-windows` | Layout-Engine, Kantenumrechnung, Protokoll |

Später kommt der Installer dazu: WiX mit zwei MSIs. Das Server-MSI enthält Host, Settings, die Aufgabe und die Firewall-Regel, das Client-MSI enthält Client, Settings und den Dienst.

**Warum nur so wenige Projekte:**

- **Die Zahl der Projekte kostet zur Laufzeit kaum etwas.** Mit JIT lädt .NET Assemblies erst bei Bedarf. Bei einem AOT-Publish würden Host und Client samt aller Bibliotheken zu je einer getrimmten Binärdatei.
- **Gewicht kosten die Dauerprozesse und ihre Laufzeit.** Deshalb trennt das Repo nur dort, wo eine Prozessgrenze technisch erzwungen ist: Session 0 kann nicht abspielen, und WPF soll nur bei Bedarf geladen werden.
- **Alle Bibliothekslogik liegt in `InputFusion.Core`.**
