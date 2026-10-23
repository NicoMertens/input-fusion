# IPC

Named Pipes zwischen den Prozessen eines Rechners. Die Verbindung zwischen PC und Notebook steht unter [Netzwerk](netzwerk.md).

| Verbindung | Weg | ACL der Pipe |
|---|---|---|
| [Einstellungen](einstellungen.md) → [Agent](host.md) | Named Pipe, JSON-Nachrichten | nur der angemeldete Benutzer |
| [Einstellungen](einstellungen.md) → [Dienst](client.md#dienst) | Named Pipe, JSON-Nachrichten | interaktive Benutzer |
| [Dienst](client.md#dienst) ↔ [Injector](client.md#injector) | Named Pipe, binär | nur SYSTEM |

Der Wechsel läuft über `SwitchToClient`, `SwitchToHost` und `ReturnEdges` (Host → Client) sowie `Monitors` und `SwitchBackRequest` (Client → Host, wenn der Cursor eine Rückkehrkante erreicht). Bedeutung siehe [Layout](layout.md).

- Die Nachrichten liegen in `InputFusion.Core`.
- JSON läuft über System.Text.Json mit Source-Generator, damit Agent und Dienst AOT-fähig bleiben, siehe [AOT und Speicher](aot-und-speicher.md).
- Gerahmt wird wie im Netzwerkprotokoll mit Länge und Typ.
- **Kein StreamJsonRpc:** Ob es mit AOT funktioniert, ist nicht geprüft, und für eine Handvoll Nachrichten lohnt die Abhängigkeit nicht.

## Stand Einstellungen ↔ Agent

Umgesetzt in `InputFusion.Core.Ipc.SettingsPipe` (Pipe), `SettingsMessages` (Nachrichten) und auf Seiten des Agents in `SettingsServer`:

- **Pipe-Name:** `\\.\pipe\InputFusion.Settings.<SID des Benutzers>`. Die SID im Namen sorgt dafür, dass bei mehreren angemeldeten Benutzern jeder seinen eigenen Agent erreicht. Der Agent nimmt beliebig viele Verbindungen gleichzeitig an.
- **ACL** als SDDL `D:P(A;;GA;;;<SID>)(A;;GA;;;SY)S:(ML;;NW;;;ME)`: nur der Benutzer selbst und SYSTEM, ohne Vererbung, dazu ausdrücklich die Integritätsstufe **Mittel**. Der Agent läuft erhöht, die Einstellungen aus dem Startmenü nicht; ohne die Stufe Mittel trüge die Pipe die hohe Stufe des Agents, und die Einstellungen kämen nicht heran.
- **Nachrichten:** JSON im Frame-Typ `Settings` (32), polymorph über das Feld `type`, mit Source-Generator (AOT-fähig):

  | `type` | Richtung | Inhalt |
  |---|---|---|
  | `getState` | Einstellungen → Agent | fordert den Zustand an |
  | `setKey` | Einstellungen → Agent | neuer Schlüssel; der Agent prüft ihn (mindestens 16 Zeichen, keine Leerzeichen am Rand, keine Steuerzeichen) und speichert ihn |
  | `setClientOffset` | Einstellungen → Agent | neue Lage der Client-Gruppe; der Agent rastet sie selbst noch einmal ein (`MonitorLayout.Snap`) und speichert sie für die Monitor-Kombination |
  | `hostState` | Agent → Einstellungen | Rechnername, Version des Agents, IPv4-Adressen, Port, Schlüssel, eigene Monitore, verbundener Client mit Adresse, Version, Monitoren und Lage |
  | `error` | Agent → Einstellungen | eine Änderung wurde abgelehnt, mit Grund |

- **Ablauf:** Der Agent schickt jeder neuen Verbindung sofort `hostState`, danach bei jeder Änderung von selbst (Client verbunden oder getrennt, Monitore, Schlüssel, Layout) und nach jeder Anfrage. Bei einer Ablehnung kommt erst `error`, dann der unveränderte Zustand, der die Anzeige zurücksetzt.
- **Angelegt per `CreateNamedPipe`** mit einem Security-Descriptor aus SDDL, nicht über `PipeSecurity`: Mit dem Label in dessen SACL scheitert das Anlegen an „A required privilege is not held by the client“ (SeSecurityPrivilege), gemessen mit nicht erhöhtem Agent. Mit `PIPE_REJECT_REMOTE_CLIENTS`, die Pipe ist nur lokal erreichbar.
- **Verifiziert** mit nicht erhöhtem Agent über ein Testskript: Zustand bei Verbindung, Ablehnung eines zu kurzen Schlüssels und einer Lage ohne Client, Schlüsselwechsel samt Speichern. Der Zugriff nicht erhöhter Einstellungen auf den erhöhten Agent ist noch am echten Aufbau zu prüfen.

## Stand Dienst ↔ Injector (Phase 2, Schritt 1)

Umgesetzt in `InputFusion.Core.Ipc.InputPipe` und über Unit-Test und einen lokalen Durchlauf verifiziert:

- **Dieselbe Rahmung wie im Netz:** Die Pipe trägt genau die Eingabe-Frames, die der Host schickt. Der Dienst (Pipe-Server) reicht sie unverändert an den Injector (Pipe-Client) weiter und in der Gegenrichtung dessen `SwitchBackRequest` an den Host; beide nutzen `MessageConnection` über dem Pipe-Strom. Für diese binäre Strecke gibt es deshalb kein eigenes JSON — das bleibt der Einstellungs-IPC vorbehalten.
- **ACL:** Der Server erlaubt immer SYSTEM mit Vollzugriff. Im Produktivfall laufen Dienst und Injector beide als SYSTEM (der Dienst startet den Injector per `CreateProcessAsUser` mit dupliziertem SYSTEM-Token), also bleibt es bei SYSTEM. Zum Entwickeln laufen beide als der angemeldete Benutzer; dann wird dessen SID zusätzlich erlaubt, sonst käme der Injector nicht an die Pipe.
- **Pipe-Name:** `\\.\pipe\InputFusion.Input`, ein Server-Exemplar. Ein kleiner Puffer sorgt dafür, dass das Weiterreichen der winzigen Frames nicht auf die Lese-Kadenz des Injectors wartet.
- **Noch offen:** Der sitzungsabhängige Start des Injectors (`CreateProcessAsUser`, Desktop-Wechsel) kommt in Schritt 2 und ist nur an echter Hardware testbar, siehe [Client](client.md#stand-phase-2-schritt-1-ipc-und-injector).
