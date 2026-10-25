# Zwischenablage

Text in der Zwischenablage wandert beim Wechsel mit: Wer auf dem PC kopiert und die Maus aufs Notebook schiebt, fügt dort denselben Text ein, und umgekehrt. Bilder und Dateien gehen bisher nicht mit, siehe [Offene Aufgaben](todo.md).

## Ablauf

- **PC → Notebook:** Beim Wechsel aufs Notebook (`RemoteController.EnterRemote`) schickt der [Host](host.md) nach `SwitchToClient` seinen Text als `ClipboardText`. Das läuft nebenher, damit Lesen und Senden den Wechsel nicht aufhalten; die Stücke reihen sich zwischen die Eingaben. Der [Dienst](client.md#dienst) reicht sie nicht an den Injector weiter, sondern an den [Tray](client.md#tray), der sie in die Zwischenablage des Benutzers schreibt.
- **Notebook → PC:** Sieht der Dienst, dass die Maus zurückgeht, schickt er dem Tray `ClipboardRequest`: bei `SwitchToHost` vom Host (Strg+Alt+F12, Win+L) und bei `SwitchBackRequest` vom Injector (Rückkehrkante). Der Tray antwortet mit seinem Text, der Dienst reicht ihn zum Host, und der schreibt ihn in seine Zwischenablage. Der Text kommt so einige Millisekunden nach dem Wechsel an.
- **Nur Änderungen** (`ClipboardSync`): Jede Seite merkt sich den Stand der Zwischenablage (`GetClipboardSequenceNumber`) beim letzten Abgleich und sendet nur, wenn er sich seitdem geändert hat. Nach dem Schreiben eines empfangenen Textes merkt sie sich den neuen Stand, sonst ginge der Text beim nächsten Wechsel zurück. Der Host fängt je Verbindung neu an, beim ersten Wechsel geht sein vorhandener Text also mit; der Tray behält seinen Stand über Neuverbindungen hinweg, bis er neu startet.
- **Ohne Text nichts:** Enthält die Zwischenablage keinen Text (etwa nur ein Bild) oder ist sie leer, wird nichts gesendet; die Gegenseite behält ihren Text, statt geleert zu werden.
- **Bricht die Verbindung** ab, während die Maus auf dem Notebook ist, kommt dessen Text nicht zum PC: Ohne Verbindung gibt es kein `SwitchToHost`, und den Text nachzureichen lohnt nicht.

## Übertragung

- **In Stücken** (`ClipboardChunks`): Ein Frame fasst verschlüsselt knapp 64 KB (`FrameCipher.MaxPayload`). Der Text geht deshalb als UTF-8 in Stücken von 32 KB, jedes mit einem Flag-Byte davor (erstes, letztes). Ein erstes Stück beginnt immer neu, eine abgebrochene Übertragung verdirbt die nächste nicht.
- **Höchstens 1 MB** UTF-8. Längeren Text überträgt der Absender nicht und schreibt das ins Protokoll; der Empfänger verwirft ebenfalls alles darüber. Ein Megabyte geht im WLAN in Bruchteilen einer Sekunde durch, mehr hielte die Eingaben spürbar auf.
- **Keine neue Protokollversion:** Ältere Gegenseiten ignorieren die neuen Frame-Typen, siehe [Netzwerk](netzwerk.md). Ein alter Dienst reicht `ClipboardText` an den Injector, der es nicht kennt und überspringt.
- **Frame-Typen:** `ClipboardText` (17) in beiden Richtungen, im Netz und auf der Pipe zwischen Dienst und Tray; `ClipboardRequest` (18) vom Dienst zum Tray; `ClipboardReceiver` (19) vom Tray zum Dienst, siehe [IPC](ipc.md#stand-tray-und-einstellungen--dienst).

## Wer liest und schreibt

- **Auf dem PC** der Agent selbst; er läuft im Benutzerkontext.
- **Auf dem Notebook** der Tray, weil die Zwischenablage dem angemeldeten Benutzer gehört. Der Dienst in Session 0 hat keinen Zugriff auf sie, und der Injector soll als SYSTEM möglichst wenig Code ausführen; er folgt außerdem dem Eingabe-Desktop bis auf den Sperrbildschirm. Der Dienst vermittelt nur (`ClipboardBridge`) und liest den Text nicht.
- **Nur die aktive Sitzung:** Die Pipe des Dienstes steht allen interaktiven Benutzern offen. Der Dienst gibt Text deshalb nur an Trays weiter, die sich mit `ClipboardReceiver` angemeldet haben und in der aktiven Konsolensitzung laufen (`GetNamedPipeClientSessionId`), und nimmt nur von solchen Text für den Host an. Geprüft wird bei jeder Übergabe, nicht nur bei der Anmeldung: Nach einem Benutzerwechsel kann der alte Tray noch kurz verbunden sein. Beim Entwickeln mit `--relay` gilt statt der Konsolensitzung die des Dienst-Ablaufs selbst, auch per RDP. Die Einstellungen hängen an derselben Pipe und bekommen nie Text.
- **Win32 direkt** (`ClipboardText`): `OpenClipboard`, `GetClipboardData` und `SetClipboardData` mit `CF_UNICODETEXT`, ohne WinForms und WPF, damit Host und Client AOT-fähig bleiben. Ein Fenster braucht es nicht: Laut Dokumentation scheitert `SetClipboardData` nach `OpenClipboard(NULL)`, unter Windows 11 klappt es trotzdem (geprüft). Hält eine andere Anwendung die Zwischenablage gerade offen, versucht `ClipboardText` es bis zu zehnmal im Abstand von 20 ms.
- **Durchstich-Client und Simulator** gleichen nichts ab; der Simulator überspringt `ClipboardText` still.

## Stand

- **Geprüft** mit Unit-Tests (Stückelung, Umlaute an der Grenze zweier Stücke, Obergrenze, abgebrochene Übertragung) und einmal gegen die echte Zwischenablage des PCs: 50 000 Umlaute kommen in vier Stücken an, und der empfangene Text geht nicht zurück.
- **Noch nicht** über Host, Dienst und Tray hinweg und nicht am Notebook, siehe [Offene Aufgaben](todo.md).
