# IPC

Named Pipes zwischen den Prozessen eines Rechners. Die Verbindung zwischen PC und Notebook steht unter [Netzwerk](netzwerk.md).

| Verbindung | Weg | ACL der Pipe |
|---|---|---|
| [Einstellungen](einstellungen.md) → [Agent](host.md) | Named Pipe, JSON-Nachrichten | nur der angemeldete Benutzer |
| [Tray](client.md#tray), [Einstellungen](einstellungen.md) → [Dienst](client.md#dienst) | Named Pipe, JSON-Nachrichten, binär für die Zwischenablage | SYSTEM, Administratoren, interaktive Benutzer |
| [Dienst](client.md#dienst) ↔ [Injector](client.md#injector) | Named Pipe, binär | nur SYSTEM |

Der Wechsel läuft über `SwitchToClient`, `SwitchToHost`, `ReturnEdges` und `LockWorkstation` (Host → Client) sowie `Monitors` und `SwitchBackRequest` (Client → Host, wenn der Cursor eine Rückkehrkante erreicht). Dazu schickt der Client mit `ClientInfo` Name, Windows-Version und angemeldeten Benutzer, nur zur Anzeige, siehe [Client](client.md#dienst); umgekehrt der Host mit `Arrangement` die Anordnung für die Einstellungen des Notebooks. Bedeutung siehe [Layout](layout.md).

- Die Nachrichten liegen in `InputFusion.Core`.
- JSON läuft über System.Text.Json mit Source-Generator, damit Agent und Dienst AOT-fähig bleiben, siehe [AOT und Speicher](aot-und-speicher.md).
- Gerahmt wird wie im Netzwerkprotokoll mit Länge und Typ.
- **Kein StreamJsonRpc:** Ob es mit AOT funktioniert, ist nicht geprüft, und für eine Handvoll Nachrichten lohnt die Abhängigkeit nicht.

## Stand Einstellungen ↔ Agent

Umgesetzt in `NC2.InputFusion.Core.Ipc.SettingsPipe` (Pipe), `SettingsMessages` (Nachrichten) und auf Seiten des Agents in `SettingsServer`:

- **Pipe-Name:** `\\.\pipe\InputFusion.Settings.<SID des Benutzers>`. Die SID im Namen sorgt dafür, dass bei mehreren angemeldeten Benutzern jeder seinen eigenen Agent erreicht. Der Agent nimmt beliebig viele Verbindungen gleichzeitig an.
- **ACL** als SDDL `D:P(A;;GA;;;<SID>)(A;;GA;;;SY)S:(ML;;NW;;;ME)`: nur der Benutzer selbst und SYSTEM, ohne Vererbung, dazu ausdrücklich die Integritätsstufe **Mittel**. Der Agent läuft erhöht, die Einstellungen aus dem Startmenü nicht; ohne die Stufe Mittel trüge die Pipe die hohe Stufe des Agents, und die Einstellungen kämen nicht heran.
- **Nachrichten:** JSON im Frame-Typ `Settings` (32), polymorph über das Feld `type`, mit Source-Generator (AOT-fähig):

  | `type` | Richtung | Inhalt |
  |---|---|---|
  | `getState` | Einstellungen → Agent | fordert den Zustand an |
  | `setKey` | Einstellungen → Agent | neuer Schlüssel; der Agent prüft ihn (mindestens 16 Zeichen, keine Leerzeichen am Rand, keine Steuerzeichen) und speichert ihn, mit verbundenem Notebook erst, wenn es ihn übernommen hat (siehe [Netzwerk](netzwerk.md)) |
  | `setPort` | Einstellungen → Agent | neuer Port; der Agent prüft ihn (1–65535, probeweise gebunden), speichert ihn und lauscht sofort darauf |
  | `setClientOffset` | Einstellungen → Agent | neue Lage der Client-Gruppe; der Agent rastet sie selbst noch einmal ein (`MonitorLayout.Snap`) und speichert sie für die Monitor-Kombination |
  | `hostState` | Agent → Einstellungen | Rechnername, Version des Agents, IPv4-Adressen, Port, Schlüssel, eigene Monitore, verbundener Client mit Adresse, Version, Monitoren und Lage; ein Schlüssel, der gerade an das Notebook geht (`pendingKey`), und wie die letzte Übertragung ausging (`keyStatus`) |
  | `error` | Agent → Einstellungen | eine Änderung wurde abgelehnt, mit Grund |
  | `getDiagnostics` | Einstellungen → Agent | fordert den Stand für den [Reiter Diagnose](einstellungen.md#reiter-diagnose) an; ohne `hostState` danach |
  | `diagnostics` | Agent → Einstellungen | nur an den Fragenden: ob verbunden und seit wann, Laufzeit, Zahl der Frames je Richtung, letzte Störung mit Zeit, letzte Zeilen des Protokolls, Pfad der Protokolldatei |

- **Ablauf:** Der Agent schickt jeder neuen Verbindung sofort `hostState`, danach bei jeder Änderung von selbst (Client verbunden oder getrennt, Monitore, Schlüssel, Layout) und nach jeder Anfrage. Bei einer Ablehnung kommt erst `error`, dann der unveränderte Zustand, der die Anzeige zurücksetzt.
- **Angelegt per `CreateNamedPipe`** mit einem Security-Descriptor aus SDDL, nicht über `PipeSecurity`: Mit dem Label in dessen SACL scheitert das Anlegen an „A required privilege is not held by the client“ (SeSecurityPrivilege), gemessen mit nicht erhöhtem Agent. Mit `PIPE_REJECT_REMOTE_CLIENTS`, die Pipe ist nur lokal erreichbar.
- **Verifiziert** mit nicht erhöhtem Agent über ein Testskript: Zustand bei Verbindung, Ablehnung eines zu kurzen Schlüssels und einer Lage ohne Client, Schlüsselwechsel samt Speichern. Der Zugriff nicht erhöhter Einstellungen auf den erhöhten Agent ist noch am echten Aufbau zu prüfen.

## Stand Tray und Einstellungen ↔ Dienst

Umgesetzt in `NC2.InputFusion.Core.Ipc.ClientPipe`, auf Seiten des Dienstes in `ClientSettingsServer` und `ClientStatus`. Genutzt vom [Tray](client.md#tray) und den [Einstellungen des Notebooks](einstellungen.md#stand-einstellungen-des-notebooks).

- **Pipe-Name:** `\\.\pipe\InputFusion.Client`, ohne SID: Es gibt einen Dienst je Rechner, nicht einen je Benutzer. Beliebig viele Verbindungen gleichzeitig.
- **ACL** als SDDL `O:<eigenes Konto>D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;<eigenes Konto>)(A;;0x120183;;;IU)S:(ML;;NW;;;ME)`: Besitzer ist ausdrücklich der Dienst. SYSTEM, Administratoren und das eigene Konto dürfen alles; beim Entwickeln legt der Benutzer so selbst weitere Exemplare an. Interaktive Benutzer dürfen lesen, schreiben und die Berechtigungen lesen, aber **keine Exemplare anlegen** (`FILE_CREATE_PIPE_INSTANCE`, das in `GENERIC_WRITE` steckt). Dazu die Integritätsstufe **Mittel**, damit auch nicht erhöhte Prozesse schreiben dürfen. Angelegt wie die Pipe des Agents über `CreateNamedPipe` (`SddlPipe`), nur lokal erreichbar. Die Clients öffnen sie deshalb mit genau diesen Rechten statt mit `PipeDirection.InOut`, das Exemplare anlegen will und abgewiesen würde.
- **Server prüfen:** Neben dem laufenden Dienst kann ein Benutzer kein Exemplar anlegen. Er könnte die Pipe aber anlegen, solange der Dienst nicht läuft; dann gehört sie ihm. Tray und Einstellungen prüfen deshalb nach dem Verbinden den **Besitzer der Pipe** (`ClientPipe.VerifyServer`): SYSTEM oder, beim Entwickeln, das eigene Konto. Auf SYSTEM setzen kann den Besitzer nur SYSTEM. Der Dienst bekommt die Pipe dann selbst nicht und meldet das im Ereignisprotokoll; Tray und Einstellungen zeigen „nicht erreichbar“. Das ist Sabotage, aber keine Rechteausweitung, wie beim [Injector](#absicherung). Ohne ausdrückliche Angabe gehörte eine von SYSTEM angelegte Pipe übrigens den Administratoren (gemessen). Das Konto des Server-Prozesses, wie beim Injector, lässt sich hier nicht prüfen: Ein normaler Benutzer darf den Dienst-Prozess nicht öffnen (`OpenProcess` scheitert mit Zugriff verweigert).
- **Zwischenablage:** Neben den JSON-Nachrichten trägt die Pipe binäre Frames für die [Zwischenablage](zwischenablage.md). Der Tray meldet sich nach dem Verbinden mit `ClipboardReceiver` an; der Dienst schickt ihm `ClipboardText` vom Host und `ClipboardRequest`, wenn die Maus zurückgeht, und nimmt von ihm `ClipboardText` für den Host an. Beides nur, solange der Tray in der aktiven Sitzung läuft.
- **Nachrichten:** dieselben wie beim Agent (`SettingsMessage`). Vom Dienst `clientState`: Version; die PCs aus `client.json` (`hosts`, je Name, IP, Port und Schlüssel, Schlüssel nur für Administratoren, sonst leer); `canEdit`; ob verbunden, mit welchem PC (`host`), Adresse und Version des Hosts, letzte Störung; mit Verbindung die Anordnung, die der Host zuletzt geschickt hat (`arrangement`, sonst null). Zum Dienst `getState` und `setClientConfig` (die ganze Liste der PCs, siehe [Mehrere PCs](mehrere-pcs.md)), dazu `getDiagnostics` wie beim Agent; die Antwort hat keinen Pfad, der Dienst schreibt nur ins Ereignisprotokoll. Aus dem Tray `checkForUpdates`, beantwortet nur an ihn mit `updateCheckResult` (Ergebnis als Satz), siehe [Updates](updates.md#client).
- **Ablauf:** wie beim Agent. Der Dienst schickt jeder neuen Verbindung sofort den Zustand und danach bei jeder Änderung.
- **Ändern nur für Administratoren:** Der Dienst ermittelt je Verbindung mit `GetNamedPipeClientProcessId` den Prozess der Gegenseite und prüft dessen Token (`PipeIdentity.IsClientAdministrator`): SYSTEM oder Mitglied der Administratoren mit aktiver Gruppe, also erhöht. Andere bekommen keinen Schlüssel, und `setClientConfig` lehnt er ab. Dann prüft er die Werte (`ClientConfigRules`), schreibt `client.json` mit derselben ACL wie das Setup (`ClientConfigFile.Save`), bricht den laufenden Verbindungsversuch samt Verbindung ab und verbindet sofort mit den neuen Werten. Mit `--relay` steht die Konfiguration fest; Änderungen lehnt er dann ab.

## Stand Dienst ↔ Injector (Phase 2, Schritt 1)

Umgesetzt in `NC2.InputFusion.Core.Ipc.InputPipe` und über Unit-Test und einen lokalen Durchlauf verifiziert:

- **Dieselbe Rahmung wie im Netz:** Die Pipe trägt genau die Eingabe-Frames, die der Host schickt. Der Dienst (Pipe-Server) reicht sie unverändert an den Injector (Pipe-Client) weiter und in der Gegenrichtung dessen `SwitchBackRequest` an den Host; beide nutzen `MessageConnection` über dem Pipe-Strom. Für diese binäre Strecke gibt es deshalb kein eigenes JSON — das bleibt der Einstellungs-IPC vorbehalten.
- **ACL:** Der Server erlaubt immer SYSTEM mit Vollzugriff. Im Produktivfall laufen Dienst und Injector beide als SYSTEM (der Dienst startet den Injector per `CreateProcessAsUser` mit dupliziertem SYSTEM-Token), also bleibt es bei SYSTEM. Zum Entwickeln laufen beide als der angemeldete Benutzer; dann wird dessen SID zusätzlich erlaubt, sonst käme der Injector nicht an die Pipe.
- **Pipe-Name:** `\\.\pipe\InputFusion.Input`, ein Server-Exemplar. Ein kleiner Puffer sorgt dafür, dass das Weiterreichen der winzigen Frames nicht auf die Lese-Kadenz des Injectors wartet.
- **Start des Injectors** je Sitzung per `CreateProcessAsUser` und der Desktop-Wechsel stehen unter [Client](client.md#stand-phase-2-schritt-2-windows-dienst).

### Absicherung

Die ACL schützt nur die Pipe des echten Dienstes. Legt ein anderer Prozess `\\.\pipe\InputFusion.Input` vorher an, bekommt der Dienst sie nicht (ein Server-Exemplar), und der Injector verbände sich mit dem fremden Server. Weil er als SYSTEM abspielt, erreichten fremde Eingaben dann auch Sperrbildschirm und UAC-Abfrage.

- **Prüfung im Injector** (`InputPipe.VerifyServer`, umgesetzt in `PipeIdentity`): Nach dem Verbinden ermittelt er mit `GetNamedPipeServerProcessId` den Server-Prozess und liest aus dessen Token das Konto. Nur wenn es sein eigenes ist, spielt er ab; sonst trennt er, schreibt den Grund auf die Konsole und versucht es nach 500 ms erneut. Ist Prozess oder Token nicht zu öffnen, gilt das als fremd.
- **Warum das Konto genügt:** Im Dienst ist es SYSTEM; wer als SYSTEM läuft, braucht den Umweg über den Injector nicht. Beim Entwickeln laufen beide als der angemeldete Benutzer, ein fremder Server unter demselben Konto gewinnt dann keine Rechte.
- **Nicht abgewehrt:** Ein fremder Server, der die Pipe besetzt hält, blockiert den Dienst. Das ist Sabotage, aber keine Rechteausweitung.
- **Geprüft:** Unit-Tests mit einem Server unter dem Testkonto: angenommen, wenn das eigene Konto erlaubt ist, abgelehnt, wenn nur SYSTEM erlaubt ist.
