# Host: der Agent auf dem PC

`InputFusion.Host.exe` läuft dauerhaft im Benutzerkontext des PCs. Er fängt Maus und Tastatur ab, entscheidet anhand des [Layouts](layout.md), wann die Maus auf das Notebook wechselt, und schickt die Eingaben über das [Netzwerk](netzwerk.md) an den [Client](client.md).

- **Hooks** laufen auf einem eigenen Thread mit eigener Nachrichtenschleife, nie auf einem UI-Thread. Der Callback schreibt nur in einen `Channel<T>`. Braucht er länger als `LowLevelHooksTimeout`, entfernt Windows den Hook stillschweigend.
- **Erhöht starten** (Aufgabe „mit höchsten Rechten“): Ein nicht erhöhter Low-Level-Hook bekommt wegen UIPI keine Eingaben, solange ein erhöhtes Fenster im Vordergrund ist.
- **Remote-Modus:** Cursor parken, Deltas über Raw Input (`WM_INPUT`) lesen, Eingaben verschlucken. Die eigenen Tastenkürzel werden vorher ausgewertet.
- **Tray** direkt über `Shell_NotifyIcon` mit einem eigenen Nachrichtenfenster, ohne WinForms und ohne WPF. WinForms ist nicht AOT-fähig, siehe [AOT und Speicher](aot-und-speicher.md). Aus dem Tray starten die [Einstellungen](einstellungen.md).
- **Konfiguration** in `%LOCALAPPDATA%\InputFusion\config.json`. Der Agent ist ihr einziger Besitzer; die Einstellungen ändern sie nur über [IPC](ipc.md).

**Bewusst nicht vorgesehen:** Steuerung bei gesperrtem PC. Der Sperrbildschirm liegt auf dem Secure Desktop, dort laufen die Hooks nicht. Ein Dienst auf dem Server würde das lösen, lohnt sich aber kaum.

Fallstricke, die auch den Host betreffen, etwa hängende Tasten und nicht abfangbare Tastenkombinationen, stehen unter [Fallstricke](fallstricke.md).

## Stand Phase 1 (Durchstich)

Der Host läuft im Durchstich als Konsolenprogramm (`AllocConsole`) und lauscht als TCP-Server, abweichend vom Zielbild, in dem der Client sich verbindet. Das vereinfacht den ersten Test.

- Start: `InputFusion.Host.exe`, optional `--absolute` für absolutes statt relatives Senden (zum Vergleich des Mausgefühls).
- **Beobachtungsmodus:** In einer RDP-Sitzung lässt sich die Maus nicht kapern (siehe [Fallstricke](fallstricke.md)). Der Host erkennt das über `SM_REMOTESESSION` und beobachtet dann nur: Er sendet die Bewegungen, verschluckt und parkt die Maus aber nicht. So lässt sich die Übertragung über RDP mit dem [Simulator](simulator.md) flüssig testen, während die echte Maus normal weiterläuft. `--observe` erzwingt das auch lokal, `--capture` erzwingt das Kapern trotz RDP.
- **Diagnose:** `--diag` kapert die Maus und misst nach jedem `SetCursorPos`, ob der Zeiger an der Parkposition bleibt, und wie weit er aus der Mitte läuft. Die Werte stehen in der Konsole und in `%TEMP%\inputfusion-diag.log`. Damit wurde belegt, dass `SetCursorPos` über RDP greift, die Bewegung aber absolut geliefert wird.
- Wechsel in den Remote-Modus an der rechten Kante des **gesamten Monitorverbunds** (virtueller Desktop), nicht der des Hauptmonitors. Dann parkt der Cursor in der Mitte und wird nach jeder Bewegung dorthin zurückgesetzt, damit er nicht am Rand ansteht; die Bewegung geht als Delta an den Client, lokale Eingaben werden verschluckt.
- Beim Wechsel sendet der Host `SwitchToClient` mit der Eintrittshöhe, beim Zurückwechseln `SwitchToHost`.
- Zurück: `Strg+Alt+F12`, oder der **Client meldet** mit `SwitchBackRequest`, dass sein Cursor die linke Kante erreicht hat. Der Austritt gehört zum Client, weil nur er seine Monitore kennt. Die volle layoutabhängige Zuordnung (welche Kante wohin führt) kommt mit Phase 3.
- Bricht die Verbindung ab, endet der Remote-Modus sofort, damit die lokale Eingabe nicht verschluckt bleibt.
- Ohne zweiten Rechner lässt sich das mit dem [Simulator](simulator.md) testen.
- Noch offen: kein Multimonitor-Layout, Austritt und Parken grob, hängende Modifier beim Wechsel werden noch nicht nachgesendet.
