# Einstellungen

`InputFusion.Settings.exe` ist eine WPF-App für beide Seiten; sie liegt im Velopack-Paket des Hosts, für das Notebook gibt es sie noch nicht. Sie läuft nur bei Bedarf, aus dem Tray des [Hosts](host.md) oder aus dem Startmenü.

- **Erkennt die Seite** daran, welche Pipe erreichbar ist: die des Agents oder die des Dienstes. Danach zeigt sie den Layout-Editor oder die Client-Einstellungen.
- **Single-Instance** über Mutex; ein zweiter Start holt das vorhandene Fenster nach vorn.
- **Besitzt keine Konfiguration.** Sie bezieht alles per [IPC](ipc.md) vom Agent bzw. Dienst. Auf dem PC gehören dazu auch die Monitore des Clients, live, solange er verbunden ist. Änderungen wirken ohne Neustart des Agents.
- **Bleibt normales .NET**, auch bei einem späteren AOT-Umstieg von Host und Client, weil WPF kein AOT unterstützt. Deshalb braucht jeder der beiden Rechner ohnehin die .NET 10 Desktop Runtime, siehe [AOT und Speicher](aot-und-speicher.md).

## Auf dem PC

Layout-Editor, Kopplung, Tastenkürzel, Port (später; bis dahin von Hand in der Konfiguration, siehe [Netzwerk](netzwerk.md)). Wie der Editor funktioniert, steht unter [Layout](layout.md).

## Auf dem Notebook

Server-Adresse und Port, Kopplungscode, Status, Vorschau der eigenen Monitore. Kein Layout-Editor, das Layout gehört dem Server. Ob der Client ein eigenes Tray-Icon bekommt, ist noch offen; bis dahin steht der Status hier.

## Stand: Einstellungen des PCs

Umgesetzt ist die Seite des PCs. Die Notebook-Seite und das Erkennen der Seite fehlen noch; ohne erreichbaren Agent zeigt das Fenster nur „Der InputFusion-Agent läuft nicht“ und verbindet sich, sobald er da ist.

- **Verbindung zum Agent** (`SettingsConnection`): über die [Pipe der Einstellungen](ipc.md), mit Neuverbindung jede Sekunde, solange das Fenster offen ist. Ohne Verbindung sind die Reiter gesperrt. Den Zustand schickt der Agent von selbst, bei jeder Änderung.
- **Eine Instanz:** Mutex `Local\InputFusion.Settings`; ein zweiter Start sucht das Fenster der ersten Instanz, stellt es wieder her und holt es nach vorn.
- **Reiter Verbindung:** PC-Name, die IPv4-Adressen aller aktiven Netzwerkkarten und der Port, zum Abschreiben für das Notebook. Darunter der **Schlüssel**: vorbelegt mit dem Zufallswert, den der Agent beim ersten Start erzeugt hat, frei änderbar. „Neu erzeugen“ setzt einen neuen Zufallswert ein, „Kopieren“ legt ihn in die Zwischenablage, „Übernehmen“ schickt ihn an den Agent. Geprüft wird schon bei der Eingabe mit denselben Regeln wie im Agent (`HostConfig.ValidateKey`: mindestens 16 Zeichen, keine Leerzeichen am Rand, keine Steuerzeichen); „Übernehmen“ ist nur mit gültigem, geändertem Schlüssel aktiv. Eine Ablehnung des Agents erscheint unter dem Feld. Ein neuer Schlüssel gilt ab der nächsten Verbindung, das Notebook braucht ihn in `client.json`, siehe [Installation](installation.md). Unten steht, ob und mit welcher Adresse ein Notebook verbunden ist.
- **Reiter Anordnung:** die eigenen Monitore grau auf ihren Windows-Koordinaten, von links nach rechts nummeriert, der Hauptmonitor markiert. Ist ein Notebook verbunden und hat seine Monitore gemeldet, erscheinen sie blau und lassen sich **als Gruppe** mit der Maus verschieben. Schon beim Ziehen rastet die Gruppe über `MonitorLayout.Snap` an der nächsten Kante ein, also immer rundum anliegend an den eigenen Monitoren; orange Linien zeigen die Übergänge, die daraus folgen. Beim Loslassen geht die Lage an den Agent, der sie noch einmal einrastet und für die Monitor-Kombination speichert. Regeln und Begründung stehen unter [Layout](layout.md).
- **Zeichenfläche:** ein `Canvas` in Layout-Pixeln in einer `Viewbox`. Um die eigenen Monitore bleibt so viel Rand, wie das Notebook groß ist, damit es an jede Seite passt. Der Ursprung bleibt beim Ziehen stehen, sonst wanderte die Ansicht mit.
- **Symbol:** dasselbe `InputFusion.ico` wie der Host, als `ApplicationIcon` und als WPF-Ressource für das Fenster.
