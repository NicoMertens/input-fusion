# Einstellungen

`InputFusion.Settings.exe` ist eine WPF-App für beide Seiten und in beiden Installern enthalten. Sie läuft nur bei Bedarf, aus dem Tray des [Hosts](host.md) oder aus dem Startmenü.

- **Erkennt die Seite** daran, welche Pipe erreichbar ist: die des Agents oder die des Dienstes. Danach zeigt sie den Layout-Editor oder die Client-Einstellungen.
- **Single-Instance** über Mutex; ein zweiter Start holt das vorhandene Fenster nach vorn.
- **Besitzt keine Konfiguration.** Sie bezieht alles per [IPC](ipc.md) vom Agent bzw. Dienst. Auf dem PC gehören dazu auch die Monitore des Clients, live, solange er verbunden ist. Änderungen wirken ohne Neustart des Agents.
- **Bleibt normales .NET**, auch bei einem späteren AOT-Umstieg von Host und Client, weil WPF kein AOT unterstützt. Deshalb braucht jeder der beiden Rechner ohnehin die .NET 10 Desktop Runtime, siehe [AOT und Speicher](aot-und-speicher.md).

## Auf dem PC

Layout-Editor, Kopplung, Tastenkürzel, Port (später; bis dahin fest, siehe [Netzwerk](netzwerk.md)). Wie der Editor funktioniert, steht unter [Layout](layout.md).

## Auf dem Notebook

Server-Adresse und Port, Kopplungscode, Status, Vorschau der eigenen Monitore. Kein Layout-Editor, das Layout gehört dem Server. Ob der Client ein eigenes Tray-Icon bekommt, ist noch offen; bis dahin steht der Status hier.
