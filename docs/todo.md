# Offene Aufgaben

Arbeitsliste über mehrere Sessions. Erledigtes wird gelöscht, nicht abgehakt: Was umgesetzt ist, steht in der zuständigen Doku-Datei. Neue Punkte kommen in den passenden Abschnitt, mit Verweis auf die Stelle, die sie beschreibt.

## Bauen

- [ ] **Tastenkürzel in den Einstellungen des PCs:** Die Kürzel liegen fest (Strg+Alt+F11 schaltet um, Strg+Alt+F12 führt zurück) und lassen sich nicht einstellen. Siehe [Einstellungen](einstellungen.md#auf-dem-pc).
- [ ] **Monitor-Vorschau in den Einstellungen des Notebooks:** die eigenen Monitore wie im Reiter Anordnung des PCs, gemessen von der Oberfläche selbst (`MonitorEnumerator`). Siehe [Einstellungen](einstellungen.md#auf-dem-notebook).
- [ ] **Desktop Runtime auf dem Notebook prüfen:** Die Einstellungen brauchen die .NET 10 Desktop Runtime; Setup und `install-client.ps1` prüfen sie nicht. Fehlt sie, meldet Windows das erst beim Öffnen. Siehe [Einstellungen](einstellungen.md).
- [ ] **`Strg+Alt+Entf` über `SendSAS`:** Nötig, wenn das Notebook zum Entsperren Strg+Alt+Entf verlangt; eingespielt wirkt die Kombination nicht. Setzt die Richtlinie *SoftwareSASGeneration* voraus. Siehe [Client](client.md#dienst).
- [ ] **Zwischenablage:** braucht auf dem Client einen Prozess im Benutzerkontext, damit Dateien mit dessen Rechten im Profil landen; dafür bietet sich der Tray an. Siehe [Client](client.md#später).

## Entscheiden

- [ ] **„Kopplung“ und „Kopplungscode“ in den Einstellungen:** stammen aus dem verworfenen TLS-Plan. Mit dem gemeinsamen Schlüssel klären, ob es noch eine Kopplung gibt oder nur das Anzeigen und Eingeben des Schlüssels. Siehe [Netzwerk](netzwerk.md), [Einstellungen](einstellungen.md).

## Am echten Notebook prüfen

Umgesetzt, aber bisher nur am PC, mit Unit-Tests oder im [Simulator](simulator.md) geprüft.

- [ ] **Desktop-Wechsel:** Sperrbildschirm, Anmeldung, UAC-Abfrage. Siehe [Client](client.md#stand-desktop-wechsel).
- [ ] **Sitzungswechsel:** Abmelden, Benutzerwechsel, Anmelden nach dem Hochfahren; Injector und Tray müssen in der neuen Sitzung wieder starten. Siehe [Client](client.md#stand-phase-2-schritt-2-windows-dienst).
- [ ] **Win+L sperrt beide Rechner.** Siehe [Host](host.md).
- [ ] **Kanten mit der echten Maus überqueren,** auch an einer anderen als der rechten Kante; Mausgefühl; mit gehaltener Maustaste kein Wechsel vom PC, beim Rückweg mit gehaltener Taste wird sie auf dem Notebook losgelassen. Siehe [Host](host.md), [Layout](layout.md).
- [ ] **Selbstinstallation:** der erhöhte Teil, der den Dienst anlegt. Siehe [Client](client.md#selbstinstallation).
- [ ] **Tray und Einstellungen:** Tooltip mit dem Zustand, Einstellungen per Doppelklick, Ändern nach UAC-Neustart. Siehe [Client](client.md#tray), [Einstellungen](einstellungen.md#stand-einstellungen-des-notebooks).
- [ ] **Verbindung über den Rechnernamen** ohne feste IP in `client.json`. Siehe [Netzwerk](netzwerk.md).
- [ ] **Desktop Runtime:** ob sie auf dem Notebook vorhanden ist bzw. was Windows meldet, wenn sie fehlt. Siehe [Einstellungen](einstellungen.md).
- [ ] **Port in den Einstellungen des PCs:** Listener zieht um, Firewall-Regel folgt, belegter Port wird abgelehnt; danach verbindet sich das Notebook mit dem neuen Port. Siehe [Einstellungen](einstellungen.md#stand-einstellungen-des-pcs).
- [ ] **Selbst-Update des Client-Dienstes:** der Tausch der exe mit installiertem Dienst und veröffentlichtem Release. Siehe [Updates](updates.md#client).
