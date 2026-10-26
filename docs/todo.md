# Offene Aufgaben

Arbeitsliste über mehrere Sessions. Erledigtes wird gelöscht, nicht abgehakt: Was umgesetzt ist, steht in der zuständigen Doku-Datei. Neue Punkte kommen in den passenden Abschnitt, mit Verweis auf die Stelle, die sie beschreibt.

## Bauen

- [ ] **Desktop Runtime auf dem Notebook prüfen:** Die Einstellungen brauchen die .NET 10 Desktop Runtime; Setup und `install-client.ps1` prüfen sie nicht. Fehlt sie, meldet Windows das erst beim Öffnen. Siehe [Einstellungen](einstellungen.md).
- [ ] **`Strg+Alt+Entf` über `SendSAS`:** Nötig, wenn das Notebook zum Entsperren Strg+Alt+Entf verlangt; eingespielt wirkt die Kombination nicht. Setzt die Richtlinie *SoftwareSASGeneration* voraus. Siehe [Client](client.md#dienst).
- [ ] **Bilder in der Zwischenablage:** bisher geht nur Text mit. Siehe [Zwischenablage](zwischenablage.md).
- [ ] **Dateien in der Zwischenablage:** sollen mit den Rechten des Benutzers in dessen Profil landen, deshalb über den Tray. Siehe [Zwischenablage](zwischenablage.md), [Client](client.md#tray).

## Entscheiden

- [ ] **„Kopplung“ und „Kopplungscode“ in den Einstellungen:** stammen aus dem verworfenen TLS-Plan. Mit dem gemeinsamen Schlüssel klären, ob es noch eine Kopplung gibt oder nur das Anzeigen und Eingeben des Schlüssels. Siehe [Netzwerk](netzwerk.md), [Einstellungen](einstellungen.md).

## Am echten Notebook prüfen

Umgesetzt, aber bisher nur am PC, mit Unit-Tests oder im [Simulator](simulator.md) geprüft.

- [ ] **Desktop-Wechsel:** Sperrbildschirm, Anmeldung, UAC-Abfrage. Siehe [Client](client.md#stand-desktop-wechsel).
- [ ] **Sitzungswechsel:** Abmelden, Benutzerwechsel, Anmelden nach dem Hochfahren; Injector und Tray müssen in der neuen Sitzung wieder starten. Siehe [Client](client.md#stand-phase-2-schritt-2-windows-dienst).
- [ ] **Win+L sperrt beide Rechner.** Siehe [Host](host.md).
- [ ] **Kanten mit der echten Maus überqueren,** auch an einer anderen als der rechten Kante; Mausgefühl; mit gehaltener Maustaste kein Wechsel vom PC, beim Rückweg mit gehaltener Taste wird sie auf dem Notebook losgelassen. Siehe [Host](host.md), [Layout](layout.md).
- [ ] **Selbstinstallation:** der erhöhte Teil, der den Dienst anlegt. Siehe [Client](client.md#selbstinstallation).
- [ ] **Tray und Einstellungen:** Tooltip mit dem Zustand, Einstellungen per Doppelklick, Ändern nach UAC-Neustart; der Reiter Monitore zeigt bei bestehender Verbindung dieselbe Anordnung wie der Reiter Anordnung des PCs und folgt deren Änderungen, ohne Verbindung die eigenen Monitore, auch mit externem Monitor und gemischter Skalierung. Siehe [Client](client.md#tray), [Einstellungen](einstellungen.md#stand-einstellungen-des-notebooks).
- [ ] **Verbindung über den Rechnernamen** ohne feste IP in `client.json`. Siehe [Netzwerk](netzwerk.md).
- [ ] **Desktop Runtime:** ob sie auf dem Notebook vorhanden ist bzw. was Windows meldet, wenn sie fehlt. Siehe [Einstellungen](einstellungen.md).
- [ ] **Zwischenablage:** Text in beiden Richtungen über Host, Dienst und Tray, auch mit Umlauten und über 32 KB; nach einem Benutzerwechsel bekommt nur der Tray der aktiven Sitzung Text. Siehe [Zwischenablage](zwischenablage.md).
- [ ] **Angaben zum Notebook:** Name, Windows-Version und Benutzer erscheinen in den Einstellungen des PCs und im Tooltip; der Benutzer wechselt nach Anmelden, Abmelden und Benutzerwechsel binnen zwei Sekunden. Siehe [Client](client.md#dienst).
- [ ] **Port in den Einstellungen des PCs:** Listener zieht um, Firewall-Regel folgt, belegter Port wird abgelehnt; danach verbindet sich das Notebook mit dem neuen Port. Siehe [Einstellungen](einstellungen.md#stand-einstellungen-des-pcs).
- [ ] **„Nach Updates suchen“ im Tray:** am installierten Host (Velopack) und am installierten Dienst: Meldung „ist aktuell“; mit neuerem Release wird es geladen und eingespielt, auf dem Notebook kommt dabei „Verbindung abgebrochen“ oder das Ergebnis. Siehe [Updates](updates.md).
- [ ] **Schlüssel übertragen:** neuen Schlüssel auf dem PC übernehmen, während das Notebook verbunden ist; es speichert ihn, die Verbindung bleibt, und nach einem Neustart des Dienstes meldet es sich mit dem neuen an. Siehe [Netzwerk](netzwerk.md).
- [ ] **Abbruch melden:** Notebook in den Standby, WLAN aus, Dienst neu starten; auf PC und Notebook erscheint der Abbruch mit Grund und danach die Wiederverbindung, bei einem kurzen Neuaufbau nichts, mit der Maus auf dem Notebook sofort. Siehe [Netzwerk](netzwerk.md).
- [ ] **Reiter Diagnose:** auf PC und Notebook Laufzeit und Nachrichten pro Sekunde bei laufender Verbindung, letzte Störung nach einem Abbruch, mitlaufende Meldungen; „Protokoll öffnen“ öffnet `host.log`, auf dem Notebook die Ereignisanzeige. Bisher nur ohne Verbindung angesehen. Siehe [Einstellungen](einstellungen.md#reiter-diagnose).
- [ ] **Selbst-Update des Client-Dienstes:** der Tausch der exe mit installiertem Dienst und veröffentlichtem Release. Siehe [Updates](updates.md#client).
