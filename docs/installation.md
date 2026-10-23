# Installation für die Entwicklung

Bis zum WiX-Installer richten PowerShell-Skripte unter `build/` die Rechner ein. Sie laufen auch unter Windows PowerShell 5.1 und brauchen Administratorrechte.

## Client auf dem Notebook

```
powershell -ExecutionPolicy Bypass -File .\install-client.ps1 -HostName PC-NAME -Key <Schlüssel> [-Ip 192.168.1.10] [-Source <Ordner>]
```

Das Skript

1. stoppt einen vorhandenen Dienst `InputFusionClient` und beendet laufende Client-Prozesse,
2. kopiert die Dateien nach `%ProgramFiles%\InputFusion\Client`. Dort darf nur ein Administrator schreiben; das ist wichtig, weil der Dienst die exe als SYSTEM ausführt.
3. schreibt `%ProgramData%\InputFusion\client.json` mit `host`, `key` und optional `ip`, ACL nur SYSTEM und Administratoren, ohne Vererbung. Den Schlüssel zeigen die [Einstellungen](einstellungen.md) des Hosts im Reiter Verbindung; er muss mindestens 16 Zeichen haben.
4. legt den Dienst an (LocalSystem, Start automatisch, Neustart nach Absturz nach 5 s, 5 s, 30 s) und startet ihn.

`-Source` ist der Ordner mit `InputFusion.Client.exe`. Ohne Angabe nimmt das Skript seinen eigenen Ordner, falls die exe dort liegt, sonst die Build-Ausgabe im Repo (`_out/InputFusion.Client`). Zum Übertragen aufs Notebook genügt es also, die Skripte in den Ordner mit den Client-Dateien zu legen.

Die Konfiguration liest der Dienst vor jedem Verbindungsversuch neu, eine Änderung greift ohne Neustart beim nächsten Versuch.

```json
{
  "host": "PC-NAME",
  "key": "<Schlüssel aus den Einstellungen des Hosts>",
  "ip": "192.168.1.10"
}
```

**Entfernen:** `uninstall-client.ps1` stoppt und löscht den Dienst und die Dateien. Die Konfiguration bleibt, außer mit `-RemoveConfig`.

**Protokoll:** Ereignisanzeige, *Windows-Protokolle → Anwendung*, Quelle `InputFusion.Client`. Ab Stufe Information, damit Verbindungsaufbau, Abbrüche und Injector-Starts sichtbar sind. Dieselbe Störung, etwa ein ausgeschalteter PC, steht nur einmal drin, nicht bei jedem Versuch.

**Kodierung:** Die Skripte tragen ein UTF-8-BOM. Ohne liest Windows PowerShell 5.1 sie als ANSI, und Umlaute in Zeichenketten gehen kaputt.

## Host auf dem PC

Noch ohne Skript: `InputFusion.Host.exe` von Hand starten. Für eingehende Verbindungen braucht der PC eine Firewall-Regel, einmalig in einer Admin-PowerShell:

```powershell
New-NetFirewallRule -DisplayName "InputFusion Host" -Direction Inbound -Protocol TCP -LocalPort 15111 -Action Allow -Profile Any
```

Ohne Regel blockiert Windows eingehend still, wenn die Richtlinie die Rückfrage beim Lauschen abschaltet (auf Firmengeräten üblich). Später übernimmt das der Installer, zusammen mit der Aufgabe „mit höchsten Rechten" für den Start bei Anmeldung.
