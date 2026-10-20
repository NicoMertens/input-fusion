# InputFusion

Maus und Tastatur eines PCs steuern zusätzlich ein Notebook, wie Mouse Without Borders aus den PowerToys, aber schlanker und mit einem Layout je Monitor statt je Rechner. C# mit .NET 10, Einstellungen in WPF, nur Windows.

**Stand:** Projektgerüst, noch keine Funktion.

## Aufbau

| Projekt | Seite | Aufgabe |
|---|---|---|
| `InputFusion.Host.Agent` | PC | Dauerhaft im Benutzerkontext (erhöht): Hooks, Layout-Engine, TCP-Listener, Tray |
| `InputFusion.Host.Settings` | PC | WPF, nur bei Bedarf: Layout-Editor, Kopplung, Tastenkürzel |
| `InputFusion.Client.Service` | Notebook | Windows-Dienst (SYSTEM): Verbindung, Kopplung, Konfiguration, startet den Injector |
| `InputFusion.Client.Injector` | Notebook | SYSTEM in der Benutzersitzung: `SendInput`, Desktop-Wechsel für Sperrbildschirm und UAC, Monitore messen |
| `InputFusion.Client.Settings` | Notebook | WPF, nur bei Bedarf: Server-Adresse, Kopplung, Status |
| `InputFusion.Layout` | beide | Monitor-Modell und Layout-Engine, reine Logik ohne Win32 |
| `InputFusion.Protocol` | beide | Netzwerk-Nachrichten und Framing |
| `InputFusion.Ipc` | beide | RPC-Verträge zwischen Prozessen einer Seite |
| `InputFusion.Platform` | beide | Win32-Interop über CsWin32 |
| `InputFusion.Ui.Shared` | beide | Gemeinsame WPF-Controls, etwa die Monitor-Vorschau |
| `InputFusion.Tests` | | xUnit für Layout-Engine und Protokoll |

Abhängigkeiten zeigen immer zu `InputFusion.Layout` hin, nie zurück. Der Installer (WiX, je ein MSI für Server und Client) kommt später dazu.

## Bauen

```
dotnet build InputFusion.slnx
dotnet test InputFusion.slnx
```

Das SDK ist über `global.json` auf .NET 10 festgelegt, Paketversionen stehen zentral in `Directory.Packages.props`.
