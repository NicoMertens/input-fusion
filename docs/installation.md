# Installation für die Entwicklung

Den Host installiert ein Velopack-Setup, den Client ein PowerShell-Skript unter `build/`, das auch im Client-Paket jedes Releases liegt. Die Skripte laufen auch unter Windows PowerShell 5.1 und brauchen Administratorrechte.

## Client auf dem Notebook

```
powershell -ExecutionPolicy Bypass -File .\install-client.ps1 -HostName PC-NAME -Key <Schlüssel> [-Ip 192.168.1.10] [-Port 15111] [-Source <Ordner>]
```

Das Skript trägt einen PC ein. Für weitere, etwa zu Hause und im Büro, ruft man es erneut auf oder trägt sie in den [Einstellungen](einstellungen.md#stand-einstellungen-des-notebooks) ein; das Notebook verbindet sich mit dem, der gerade erreichbar ist, siehe [Mehrere PCs](mehrere-pcs.md).

Das Skript

1. stoppt einen vorhandenen Dienst `InputFusionClient` und beendet laufende Client-Prozesse und offene Einstellungen aus dem Installationsordner,
2. kopiert die Dateien nach `%ProgramFiles%\InputFusion\Client`. Dort darf nur ein Administrator schreiben; das ist wichtig, weil der Dienst die exe als SYSTEM ausführt.
3. schreibt `%ProgramData%\InputFusion\client.json`: unter `hosts` je PC `host`, `key`, optional `ip` und `port` (Standard 15111, muss zum Host passen). Ein vorhandener Eintrag mit demselben Namen wird ersetzt, die übrigen bleiben, mit `-Replace` nicht. Ist die vorhandene Datei unlesbar, bricht das Skript ab, bevor es etwas ändert. ACL nur SYSTEM und Administratoren, ohne Vererbung. Die Datei entsteht gleich mit der ACL, sonst wäre sie kurz für alle lesbar; ausgegeben wird sie ohne Schlüssel. Den Schlüssel zeigen die [Einstellungen](einstellungen.md) des Hosts im Reiter Verbindung; er muss mindestens 16 Zeichen haben.
4. legt den Dienst an (LocalSystem, Start automatisch, Neustart nach Absturz nach 5 s, 5 s, 30 s) und startet ihn.

**Per Setup:** `InputFusion.Client-Setup.exe` aus dem [GitHub-Release](https://github.com/NicoMertens/input-fusion/releases/latest) per Doppelklick starten. Sie fragt nach, holt per UAC Adminrechte, fragt je PC Name, Schlüssel, optional eine IP und den Port ab, bei Bedarf für weitere PCs, und tut dann dasselbe wie das Skript unten, siehe [Client](client.md#selbstinstallation).

**Per Skript aus dem Release:** `InputFusion.Client-win-x64.zip` entpacken; die Skripte liegen mit im Paket. Danach aktualisiert sich der Dienst selbst, siehe [Updates](updates.md#client).

`-Source` ist der Ordner mit `InputFusion.Client.exe`. Ohne Angabe nimmt das Skript seinen eigenen Ordner, falls die exe dort liegt, sonst die Build-Ausgabe im Repo (`_out/InputFusion.Client`). Zum Übertragen aufs Notebook genügt es also, die Skripte in den Ordner mit den Client-Dateien zu legen.

Danach ändert ein Administrator die Verbindung in den Einstellungen (Doppelklick aufs Tray-Symbol, dann „Als Administrator bearbeiten …“), siehe [Einstellungen](einstellungen.md#stand-einstellungen-des-notebooks). Sie brauchen die .NET 10 Desktop Runtime, die weder Setup noch Skript bisher prüfen. Die Konfiguration liest der Dienst vor jedem Verbindungsversuch neu, eine Änderung von Hand greift ohne Neustart beim nächsten Versuch.

```json
{
  "hosts": [
    {
      "host": "PC-ZUHAUSE",
      "key": "<Schlüssel aus den Einstellungen dieses Hosts>",
      "ip": "192.168.1.10",
      "port": 15111
    },
    {
      "host": "PC-BUERO",
      "key": "<Schlüssel aus den Einstellungen dieses Hosts>",
      "port": 15111
    }
  ]
}
```

Bis 0.11 stand dort ein einzelner PC ohne `hosts`, mit `host`, `key`, `ip` und `port` direkt im Objekt. Das liest der Dienst weiter.

**Entfernen:** `uninstall-client.ps1` stoppt und löscht den Dienst und die Dateien. Die Konfiguration bleibt, außer mit `-RemoveConfig`.

**Protokoll:** Ereignisanzeige, *Windows-Protokolle → Anwendung*, Quelle `InputFusion.Client`. Ab Stufe Information, damit Verbindungsaufbau, Abbrüche und Injector-Starts sichtbar sind. Dieselbe Störung, etwa ein ausgeschalteter PC, steht nur einmal drin, nicht bei jedem Versuch.

**Kodierung:** Die Skripte tragen ein UTF-8-BOM. Ohne liest Windows PowerShell 5.1 sie als ANSI, und Umlaute in Zeichenketten gehen kaputt.

## Host auf dem PC

**Installiert:** `InputFusion.Host-win-Setup.exe` aus dem [GitHub-Release](https://github.com/NicoMertens/input-fusion/releases/latest) ausführen. Es installiert ohne Adminrechte nach `%LOCALAPPDATA%\InputFusion.Host\`, bringt bei Bedarf die .NET 10 Desktop Runtime mit und startet den Host. Beim ersten Start fragt der Host einmal per UAC nach Adminrechten, um die geplante Aufgabe für den erhöhten Start bei Anmeldung und die Firewall-Regel anzulegen; danach aktualisiert er sich selbst. Einzelheiten unter [Updates](updates.md#host).

**Aus dem Build:** `InputFusion.Host.exe` von Hand starten, für die Hooks am besten erhöht. Für eingehende Verbindungen braucht der PC dann eine Firewall-Regel, einmalig in einer Admin-PowerShell:

```powershell
New-NetFirewallRule -DisplayName "InputFusion Host" -Direction Inbound -Protocol TCP -LocalPort 15111 -Action Allow -Profile Any
```

Mit anderem Port in den Einstellungen des Hosts diesen statt 15111 eintragen.

Ohne Regel blockiert Windows eingehend still, wenn die Richtlinie die Rückfrage beim Lauschen abschaltet (auf Firmengeräten üblich). Die installierte Fassung legt dieselbe Regel selbst an und stellt sie bei jedem Start auf den konfigurierten Port.
