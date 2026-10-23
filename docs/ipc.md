# IPC

Named Pipes zwischen den Prozessen eines Rechners. Die Verbindung zwischen PC und Notebook steht unter [Netzwerk](netzwerk.md).

| Verbindung | Weg | ACL der Pipe |
|---|---|---|
| [Einstellungen](einstellungen.md) → [Agent](host.md) | Named Pipe, JSON-Nachrichten | nur der angemeldete Benutzer |
| [Einstellungen](einstellungen.md) → [Dienst](client.md#dienst) | Named Pipe, JSON-Nachrichten | interaktive Benutzer |
| [Dienst](client.md#dienst) ↔ [Injector](client.md#injector) | Named Pipe, binär | nur SYSTEM |

Der Wechsel läuft über `SwitchToClient` und `SwitchToHost` (Host → Client) sowie `SwitchBackRequest` (Client → Host, wenn der Cursor die Kante des Clients erreicht).

- Die Nachrichten liegen in `InputFusion.Core`.
- JSON läuft über System.Text.Json mit Source-Generator, damit Agent und Dienst AOT-fähig bleiben, siehe [AOT und Speicher](aot-und-speicher.md).
- Gerahmt wird wie im Netzwerkprotokoll mit Länge und Typ.
- **Kein StreamJsonRpc:** Ob es mit AOT funktioniert, ist nicht geprüft, und für eine Handvoll Nachrichten lohnt die Abhängigkeit nicht.

## Stand Dienst ↔ Injector (Phase 2, Schritt 1)

Umgesetzt in `InputFusion.Core.Ipc.InputPipe` und über Unit-Test und einen lokalen Durchlauf verifiziert:

- **Dieselbe Rahmung wie im Netz:** Die Pipe trägt genau die Eingabe-Frames, die der Host schickt. Der Dienst (Pipe-Server) reicht sie unverändert an den Injector (Pipe-Client) weiter und in der Gegenrichtung dessen `SwitchBackRequest` an den Host; beide nutzen `MessageConnection` über dem Pipe-Strom. Für diese binäre Strecke gibt es deshalb kein eigenes JSON — das bleibt der Einstellungs-IPC vorbehalten.
- **ACL:** Der Server erlaubt immer SYSTEM mit Vollzugriff. Im Produktivfall laufen Dienst und Injector beide als SYSTEM (der Dienst startet den Injector per `CreateProcessAsUser` mit dupliziertem SYSTEM-Token), also bleibt es bei SYSTEM. Zum Entwickeln laufen beide als der angemeldete Benutzer; dann wird dessen SID zusätzlich erlaubt, sonst käme der Injector nicht an die Pipe.
- **Pipe-Name:** `\\.\pipe\InputFusion.Input`, ein Server-Exemplar. Ein kleiner Puffer sorgt dafür, dass das Weiterreichen der winzigen Frames nicht auf die Lese-Kadenz des Injectors wartet.
- **Noch offen:** Der sitzungsabhängige Start des Injectors (`CreateProcessAsUser`, Desktop-Wechsel) kommt in Schritt 2 und ist nur an echter Hardware testbar, siehe [Client](client.md#stand-phase-2-schritt-1-ipc-und-injector).
