# InputFusion

Maus und Tastatur eines PCs steuern zusätzlich ein Notebook, wie Mouse Without Borders aus den PowerToys, aber schlanker und mit einem Layout je Monitor statt je Rechner. C# mit .NET 10, Einstellungen in WPF, nur Windows.

**Stand:** Projektgerüst, noch keine Funktion.

## Aufbau

| Projekt | Läuft als | Aufgabe |
|---|---|---|
| `InputFusion.Host` | Agent auf dem PC, dauerhaft | Hooks, Layout-Engine, TCP-Listener, Tray |
| `InputFusion.Client` | Dienst auf dem Notebook (SYSTEM) | Verbindung, Kopplung, Konfiguration, startet sich selbst mit `--injector` in der Benutzersitzung |
| | Injector (`--injector`) | `SendInput`, Desktop-Wechsel für Sperrbildschirm und UAC, Monitore messen |
| `InputFusion.Settings` | WPF, nur bei Bedarf, auf beiden Seiten | Layout-Editor auf dem PC, Server-Adresse und Kopplung auf dem Notebook |
| `InputFusion.Core` | Bibliothek | Layout-Engine, Netzwerk- und IPC-Nachrichten, Win32-Interop über CsWin32 |
| `InputFusion.Tests` | xUnit | Layout-Engine und Protokoll |

Veröffentlicht wird framework-abhängig, beide Rechner brauchen die .NET 10 Desktop Runtime. Host und Client bleiben trotzdem AOT-fähig: keine reflection-lastigen Bibliotheken, JSON nur mit Source-Generator, kein WinForms oder WPF. Die AOT-Analyzer melden Verstöße schon beim Build. Der Installer (WiX, je ein MSI für Server und Client) kommt später dazu.

## Bauen

```
dotnet build source/InputFusion.slnx
dotnet test source/InputFusion.slnx
dotnet publish source/InputFusion.Host -c Release
dotnet publish source/InputFusion.Client -c Release
dotnet publish source/InputFusion.Settings -c Release
```

Solution, Projekte, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` und das gemeinsame `app.manifest` liegen in `source/`. Die Testprojekte liegen als `*.Tests` direkt daneben. Dokumentation gehört nach `docs/`.

Für einen späteren Umstieg auf NativeAOT genügt `<PublishAot>true</PublishAot>` in Host und Client, dazu die Visual-Studio-Workload „Desktopentwicklung mit C++“ auf dem Build-Rechner. Das SDK ist über `global.json` im Wurzelverzeichnis auf .NET 10 festgelegt, die Paketversionen stehen zentral in `source/Directory.Packages.props`.
