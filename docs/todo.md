# Offene Aufgaben

Arbeitsliste über mehrere Sessions. Erledigtes wird gelöscht, nicht abgehakt: Was umgesetzt ist, steht in der zuständigen Doku-Datei. Neue Punkte kommen in den passenden Abschnitt, mit Verweis auf die Stelle, die sie beschreibt.

## Bauen

- [ ] **Maustasten beim Wechsel lösen:** Beim Wechsel zwischen den Rechnern werden bisher nur Tasten der Tastatur und Modifier losgelassen, gehaltene Maustasten nicht. Siehe [Host](host.md).
- [ ] **Port in den Einstellungen des PCs:** Der Port lässt sich bisher nur von Hand in `config.json` bei beendetem Host ändern. Siehe [Einstellungen](einstellungen.md#auf-dem-pc), [Netzwerk](netzwerk.md).
- [ ] **Tastenkürzel in den Einstellungen des PCs:** Der Austritt liegt fest auf Strg+Alt+F11 und lässt sich nicht einstellen. Siehe [Einstellungen](einstellungen.md#auf-dem-pc).
- [ ] **Einstellungen für das Notebook:** Server-Adresse, Port, Schlüssel, Status, Vorschau der Monitore; das Erkennen der Seite an der erreichbaren Pipe; `setClientConfig` über die [Pipe des Dienstes](ipc.md#stand-tray-und-einstellungen--dienst), nur für Administratoren (UAC-Neustart, Konto im Dienst prüfen, Schlüssel nur für sie); Settings.exe in die Client-exe einbetten, beim Setup auspacken, im Zip mitliefern; Menüeintrag „Einstellungen …“ im Tray. Die WPF-Oberfläche braucht auf dem Notebook die .NET Desktop Runtime, die das Setup bisher nicht prüft. Siehe [Einstellungen](einstellungen.md#auf-dem-notebook).
- [ ] **Injector endet nicht mit dem Dienst:** Nach einem Absturz des Dienstes (oder dem Beenden von `--relay`) läuft der alte Injector weiter, der neue Dienst startet einen zweiten. Wie beim Tray die PID des Dienstes mitgeben. Siehe [Client](client.md#tray).
- [ ] **Verbindung über den Rechnernamen scheitert als Dienst:** Beim Test auf dem Entwicklungs-PC löste dessen Name zuerst in IPv6-Adressen auf, der Host lauscht nur auf IPv4 (`0.0.0.0`). Als Benutzer klappte `TcpClient.ConnectAsync` über den Namen in 29 ms, der Dienst als SYSTEM bekam binnen 5 s keine Antwort; mit fester IP ging es. Ursache als SYSTEM nachmessen; Abhilfe etwa: Host auch auf IPv6 lauschen lassen oder IPv4-Adressen zuerst versuchen. Siehe [Netzwerk](netzwerk.md).
- [ ] **`install-client.ps1` zeigt den Schlüssel:** Am Ende gibt das Skript `client.json` samt Schlüssel im Klartext aus. Den Schlüssel dort ausblenden. Siehe [Installation](installation.md).
- [ ] **`Strg+Alt+Entf` über `SendSAS`:** Nötig, wenn das Notebook zum Entsperren Strg+Alt+Entf verlangt; eingespielt wirkt die Kombination nicht. Setzt die Richtlinie *SoftwareSASGeneration* voraus. Siehe [Client](client.md#dienst).
- [ ] **Zwischenablage:** braucht auf dem Client einen Prozess im Benutzerkontext, damit Dateien mit dessen Rechten im Profil landen; dafür bietet sich der Tray an. Siehe [Client](client.md#später).

## Entscheiden

- [ ] **„Kopplung“ und „Kopplungscode“ in den Einstellungen:** stammen aus dem verworfenen TLS-Plan. Mit dem gemeinsamen Schlüssel klären, ob es noch eine Kopplung gibt oder nur das Anzeigen und Eingeben des Schlüssels. Siehe [Netzwerk](netzwerk.md), [Einstellungen](einstellungen.md).

## Am echten Notebook prüfen

Umgesetzt, aber bisher nur am PC, mit Unit-Tests oder im [Simulator](simulator.md) geprüft.

- [ ] **Desktop-Wechsel:** Sperrbildschirm, Anmeldung, UAC-Abfrage. Siehe [Client](client.md#stand-desktop-wechsel).
- [ ] **Sitzungswechsel:** Abmelden, Benutzerwechsel, Anmelden nach dem Hochfahren; Injector und Tray müssen in der neuen Sitzung wieder starten. Siehe [Client](client.md#stand-phase-2-schritt-2-windows-dienst).
- [ ] **Win+L sperrt beide Rechner.** Siehe [Host](host.md).
- [ ] **Kanten mit der echten Maus überqueren,** auch an einer anderen als der rechten Kante; Mausgefühl. Siehe [Host](host.md), [Layout](layout.md).
- [ ] **Selbstinstallation:** der erhöhte Teil, der den Dienst anlegt. Siehe [Client](client.md#selbstinstallation).
- [ ] **Selbst-Update des Client-Dienstes:** der Tausch der exe mit installiertem Dienst und veröffentlichtem Release. Siehe [Updates](updates.md#client).
