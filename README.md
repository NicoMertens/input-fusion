# InputFusion

Maus und Tastatur eines PCs steuern zusätzlich ein Notebook, wie Mouse Without Borders aus den PowerToys, aber schlanker und mit einem Layout je Monitor statt je Rechner. C# mit .NET 10, Einstellungen in WPF, nur Windows.

**Stand:** Durchstich: Host als Tray-Agent mit Einstellungen und Monitor-Layout, Client als Windows-Dienst, Anmeldung mit gemeinsamem Schlüssel. Installation und Selbst-Update über GitHub-Releases. Noch ohne Verschlüsselung.

## Aufbau

| Projekt | Läuft als | Aufgabe |
|---|---|---|
| `InputFusion.Host` | Agent auf dem PC, dauerhaft | Hooks, Layout-Engine, TCP-Listener, Tray |
| `InputFusion.Client` | Dienst auf dem Notebook (SYSTEM) | Verbindung, Kopplung, Konfiguration, startet sich selbst mit `--injector` in der Benutzersitzung |
| | Injector (`--injector`) | `SendInput`, Desktop-Wechsel für Sperrbildschirm und UAC, Monitore messen |
| `InputFusion.Settings` | WPF, nur bei Bedarf, auf beiden Seiten | Layout-Editor auf dem PC, Server-Adresse und Kopplung auf dem Notebook |
| `InputFusion.Core` | Bibliothek | Layout-Engine, Netzwerk- und IPC-Nachrichten, Win32-Interop über CsWin32 |
| `InputFusion.Tests` | xUnit | Layout-Engine und Protokoll |

Veröffentlicht wird framework-abhängig, beide Rechner brauchen die .NET 10 Desktop Runtime. Host und Client bleiben trotzdem AOT-fähig: keine reflection-lastigen Bibliotheken, JSON nur mit Source-Generator, kein WinForms oder WPF. Die AOT-Analyzer melden Verstöße schon beim Build. Installiert wird der Host per Velopack-Setup, der Client per Skript aus dem Release-Paket, siehe [Updates](docs/updates.md).

## Installieren

Aus dem [neuesten Release](https://github.com/NicoMertens/input-fusion/releases/latest):

- **PC:** `InputFusion.Host-win-Setup.exe` ausführen. Beim ersten Start fragt der Host einmal nach Adminrechten, um sich bei der Anmeldung erhöht zu starten und die Firewall für Port 15111 zu öffnen.
- **Notebook:** `InputFusion.Client-Setup.exe` per Doppelklick starten. Sie fragt nach, holt per UAC Adminrechte, fragt PC-Name und Schlüssel ab (beides zeigen die Einstellungen des Hosts) und richtet den Dienst ein. Alternativ `InputFusion.Client-win-x64.zip` entpacken und `install-client.ps1 -HostName <PC-Name> -Key <Schlüssel>` in einer Admin-PowerShell ausführen.

Einzelheiten unter [`docs/installation.md`](docs/installation.md).

## Updates

Host und Client aktualisieren sich selbst aus den GitHub-Releases: beim Start, danach alle sechs Stunden und sofort, wenn sich Host und Client mit verschiedenen Protokollversionen treffen. Der Host spielt ein Update ein, sobald die Maus nicht auf dem Notebook ist, und startet neu; der Dienst auf dem Notebook tauscht sich selbst aus und startet ebenfalls neu. Builds aus dem Quellcode suchen nicht. Siehe [`docs/updates.md`](docs/updates.md).

## Branches und Releases

Entwickelt wird auf `develop`, `release` trägt immer den zuletzt veröffentlichten Stand. Jeder Push auf `release` veröffentlicht die `<Version>` aus `source/Directory.Build.props`: GitHub Actions baut Setup und Update-Pakete des Hosts sowie das Client-Paket, legt den Tag `vX.Y.Z` an und veröffentlicht das Release. Gibt es die Version schon, bricht der Workflow ab.

- **Release:** `develop` per Merge-Commit nach `release` mergen und pushen, danach auf `develop` die Minor-Version erhöhen (0.1.0 → 0.2.0).
- **Hotfix:** direkt auf `release` korrigieren, Patch-Version erhöhen (0.2.0 → 0.2.1), danach `release` nach `develop` mergen und dort die Version von `develop` behalten.

## Bauen

```
dotnet build source/InputFusion.slnx
dotnet test source/InputFusion.slnx
dotnet publish source/InputFusion.Host -c Release
dotnet publish source/InputFusion.Client -c Release
dotnet publish source/InputFusion.Settings -c Release
```

## Durchstich ausprobieren (Phase 1)

Auf dem PC den Host starten, auf dem Notebook den Client verbinden:

```
# PC
InputFusion.Host.exe            # oder --absolute zum Vergleich des Mausgefühls

# Notebook
InputFusion.Client.exe --connect <pc-name-oder-ip> --key <schlüssel>
```

Der Host läuft als Tray-Symbol; seine Einstellungen (Doppelklick auf das Symbol) zeigen PC-Name, IP und Schlüssel und im Reiter Anordnung die Monitore beider Rechner. Dort wird das Notebook an eine Kante der eigenen Monitore gezogen, anfangs liegt es rechts. An den anliegenden Kanten wechselt die Maus hin und zurück, außerdem zurück mit `Strg+Alt+F12`. Noch ohne Verschlüsselung.

Ohne zweiten Rechner: `InputFusion.Simulator.exe` statt des Clients starten. Er zeigt die simulierten Monitore, den Cursor und die gedrückten Tasten in einem Fenster, siehe [`docs/simulator.md`](docs/simulator.md).

Solution, Projekte, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` und das gemeinsame `app.manifest` liegen in `source/`. Die Testprojekte liegen als `*.Tests` direkt daneben. Build- und Veröffentlichungsskripte kommen nach `build/` parallel zu `source/`, sobald es welche gibt.

## Dokumentation

Die Doku liegt in `docs/`, aufgeteilt nach fachlichen Themen wie Host, Client und Layout. Einstieg mit Überblick, Inhaltsverzeichnis und Regeln ist [`docs/README.md`](docs/README.md). Die Doku wird im selben Commit nachgezogen wie der Code, den sie beschreibt.

Für einen späteren Umstieg auf NativeAOT genügt `<PublishAot>true</PublishAot>` in Host und Client, dazu die Visual-Studio-Workload „Desktopentwicklung mit C++“ auf dem Build-Rechner. Das SDK ist über `global.json` im Wurzelverzeichnis auf .NET 10 festgelegt, die Paketversionen stehen zentral in `source/Directory.Packages.props`.
