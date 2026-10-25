# Repo-Aufbau

## Ordner

- **Im Wurzelverzeichnis** liegen nur `source/`, `docs/`, `build/`, `.github/workflows/` (Build und Release, siehe [Updates](updates.md#releases)), `README.md`, `global.json`, `.editorconfig`, `.gitignore` und `.gitattributes`. Build- und Installationsskripte liegen in `build/` parallel zu `source/`.
- **`source/`** enthält flach nebeneinander alle Projekte einschließlich der Testprojekte (`*.Tests`, kein eigener Unterordner), dazu `InputFusion.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, das gemeinsame `app.manifest` und die Programmsymbole `InputFusion.Host.ico` und `InputFusion.Client.ico`, die die Einstellungen je nach Seite mitbenutzen.
- **`docs/`** enthält diese Doku, Einstieg und Regeln in [README.md](README.md).
- **`_out/`** sammelt die Build-Ausgaben, parallel zu `source/`, je Projekt ein eigener Ordner (`_out/InputFusion.Host/` …) ohne Configuration- und TargetFramework-Unterordner. Wird erzeugt und ist per `.gitignore` ausgenommen; die Zwischendateien (`obj/`) bleiben beim jeweiligen Projekt.

## Build-Einstellungen

- `global.json` liegt im Wurzelverzeichnis und legt das SDK auf .NET 10 fest. Das SDK sucht die Datei vom Arbeitsverzeichnis aufwärts, deshalb greift sie auch für `source/`.
- `Directory.Build.props` setzt Nullable, ImplicitUsings und Nullable-Warnungen als Fehler und lenkt die Build-Ausgabe aller Projekte nach `_out/<Projektname>/` (siehe oben). Mit `EnforceCodeStyleInBuild` meldet jeder Build Verstöße gegen die Stilregeln.
- `.editorconfig` legt den Code-Stil fest und macht ihn zur Warnung: Zugriffsmodifizierer immer (IDE0040), explizite Typen statt `var` (IDE0008), geschweifte Klammern auch bei einzeiligen Blöcken (IDE0011), private Felder einschließlich `static readonly` als `_camelCase` und Konstanten in PascalCase (IDE1006). Bezeichner sind englisch, Kommentare deutsch. Bestehende Verstöße behebt `dotnet format style source --diagnostics <ID> --severity warn`; beim Typ-Umbau setzt es bei Dekonstruktionen und `out`-Variablen auch dort `?`, wo der Wert nie null ist, das ist danach von Hand zu bereinigen.
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

Ausgeliefert wird in zwei Paketen je Release: ein Velopack-Paket mit Host und Einstellungen (die geplante Aufgabe und die Firewall-Regel legt der Host selbst an) und ein Zip mit dem Client und den Installationsskripten. Ein WiX-Installer, wie anfangs geplant, ist damit überflüssig; siehe [Updates](updates.md).

**Warum nur so wenige Projekte:**

- **Die Zahl der Projekte kostet zur Laufzeit kaum etwas.** Mit JIT lädt .NET Assemblies erst bei Bedarf. Bei einem AOT-Publish würden Host und Client samt aller Bibliotheken zu je einer getrimmten Binärdatei.
- **Gewicht kosten die Dauerprozesse und ihre Laufzeit.** Deshalb trennt das Repo nur dort, wo eine Prozessgrenze technisch erzwungen ist: Session 0 kann nicht abspielen, und WPF soll nur bei Bedarf geladen werden.
- **Alle Bibliothekslogik liegt in `InputFusion.Core`.**
