# Offene Aufgaben

Arbeitsliste über mehrere Sessions. Erledigtes wird gelöscht, nicht abgehakt: Was umgesetzt ist, steht in der zuständigen Doku-Datei. Neue Punkte kommen in den passenden Abschnitt, mit Verweis auf die Stelle, die sie beschreibt.

## Bauen

- [ ] **Maustasten beim Wechsel lösen:** Beim Wechsel zwischen den Rechnern werden bisher nur Tasten der Tastatur und Modifier losgelassen, gehaltene Maustasten nicht. Siehe [Host](host.md).
- [ ] **Port in den Einstellungen des PCs:** Der Port lässt sich bisher nur von Hand in `config.json` bei beendetem Host ändern. Siehe [Einstellungen](einstellungen.md#auf-dem-pc), [Netzwerk](netzwerk.md).
- [ ] **Tastenkürzel in den Einstellungen des PCs:** Der Austritt liegt fest auf Strg+Alt+F11 und lässt sich nicht einstellen. Siehe [Einstellungen](einstellungen.md#auf-dem-pc).
- [ ] **Einstellungen für das Notebook:** Server-Adresse, Port, Schlüssel, Status, Vorschau der Monitore; dazu das Erkennen der Seite an der erreichbaren Pipe und die Einstellungs-IPC zum Dienst. Bis dahin schreibt das Setup bzw. `install-client.ps1` die `client.json`. Siehe [Einstellungen](einstellungen.md#auf-dem-notebook), [Client](client.md#dienst).
- [ ] **`Strg+Alt+Entf` über `SendSAS`:** Nötig, wenn das Notebook zum Entsperren Strg+Alt+Entf verlangt; eingespielt wirkt die Kombination nicht. Setzt die Richtlinie *SoftwareSASGeneration* voraus. Siehe [Client](client.md#dienst).
- [ ] **Zwischenablage:** braucht auf dem Client einen zusätzlichen Prozess im Benutzerkontext, damit Dateien mit dessen Rechten im Profil landen. Siehe [Client](client.md#später).

## Entscheiden

- [ ] **Tray-Icon für den Client:** ja oder nein; bis dahin zeigen die Einstellungen den Status. Siehe [Einstellungen](einstellungen.md#auf-dem-notebook).
- [ ] **„Kopplung“ und „Kopplungscode“ in den Einstellungen:** stammen aus dem verworfenen TLS-Plan. Mit dem gemeinsamen Schlüssel klären, ob es noch eine Kopplung gibt oder nur das Anzeigen und Eingeben des Schlüssels. Siehe [Netzwerk](netzwerk.md), [Einstellungen](einstellungen.md).

## Am echten Notebook prüfen

Umgesetzt, aber bisher nur am PC, mit Unit-Tests oder im [Simulator](simulator.md) geprüft.

- [ ] **Desktop-Wechsel:** Sperrbildschirm, Anmeldung, UAC-Abfrage. Siehe [Client](client.md#stand-desktop-wechsel).
- [ ] **Sitzungswechsel:** Abmelden, Benutzerwechsel; der Injector muss in der neuen Sitzung wieder starten. Siehe [Client](client.md#stand-phase-2-schritt-2-windows-dienst).
- [ ] **Win+L sperrt beide Rechner.** Siehe [Host](host.md).
- [ ] **Kanten mit der echten Maus überqueren,** auch an einer anderen als der rechten Kante; Mausgefühl. Siehe [Host](host.md), [Layout](layout.md).
- [ ] **Selbstinstallation:** der erhöhte Teil, der den Dienst anlegt. Siehe [Client](client.md#selbstinstallation).
- [ ] **Selbst-Update des Client-Dienstes:** der Tausch der exe mit installiertem Dienst und veröffentlichtem Release. Siehe [Updates](updates.md#client).
