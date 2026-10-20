# IPC

Named Pipes zwischen den Prozessen eines Rechners. Die Verbindung zwischen PC und Notebook steht unter [Netzwerk](netzwerk.md).

| Verbindung | Weg | ACL der Pipe |
|---|---|---|
| [Einstellungen](einstellungen.md) → [Agent](host.md) | Named Pipe, JSON-Nachrichten | nur der angemeldete Benutzer |
| [Einstellungen](einstellungen.md) → [Dienst](client.md#dienst) | Named Pipe, JSON-Nachrichten | interaktive Benutzer |
| [Dienst](client.md#dienst) ↔ [Injector](client.md#injector) | Named Pipe, binär | nur SYSTEM |

- Die Nachrichten liegen in `InputFusion.Core`.
- JSON läuft über System.Text.Json mit Source-Generator, damit Agent und Dienst AOT-fähig bleiben, siehe [AOT und Speicher](aot-und-speicher.md).
- Gerahmt wird wie im Netzwerkprotokoll mit Länge und Typ.
- **Kein StreamJsonRpc:** Ob es mit AOT funktioniert, ist nicht geprüft, und für eine Handvoll Nachrichten lohnt die Abhängigkeit nicht.
