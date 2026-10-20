# Host: der Agent auf dem PC

`InputFusion.Host.exe` läuft dauerhaft im Benutzerkontext des PCs. Er fängt Maus und Tastatur ab, entscheidet anhand des [Layouts](layout.md), wann die Maus auf das Notebook wechselt, und schickt die Eingaben über das [Netzwerk](netzwerk.md) an den [Client](client.md).

- **Hooks** laufen auf einem eigenen Thread mit eigener Nachrichtenschleife, nie auf einem UI-Thread. Der Callback schreibt nur in einen `Channel<T>`. Braucht er länger als `LowLevelHooksTimeout`, entfernt Windows den Hook stillschweigend.
- **Erhöht starten** (Aufgabe „mit höchsten Rechten“): Ein nicht erhöhter Low-Level-Hook bekommt wegen UIPI keine Eingaben, solange ein erhöhtes Fenster im Vordergrund ist.
- **Remote-Modus:** Cursor parken, Deltas über Raw Input (`WM_INPUT`) lesen, Eingaben verschlucken. Die eigenen Tastenkürzel werden vorher ausgewertet.
- **Tray** direkt über `Shell_NotifyIcon` mit einem eigenen Nachrichtenfenster, ohne WinForms und ohne WPF. WinForms ist nicht AOT-fähig, siehe [AOT und Speicher](aot-und-speicher.md). Aus dem Tray starten die [Einstellungen](einstellungen.md).
- **Konfiguration** in `%LOCALAPPDATA%\InputFusion\config.json`. Der Agent ist ihr einziger Besitzer; die Einstellungen ändern sie nur über [IPC](ipc.md).

**Bewusst nicht vorgesehen:** Steuerung bei gesperrtem PC. Der Sperrbildschirm liegt auf dem Secure Desktop, dort laufen die Hooks nicht. Ein Dienst auf dem Server würde das lösen, lohnt sich aber kaum.

Fallstricke, die auch den Host betreffen, etwa hängende Tasten und nicht abfangbare Tastenkombinationen, stehen unter [Fallstricke](fallstricke.md).
