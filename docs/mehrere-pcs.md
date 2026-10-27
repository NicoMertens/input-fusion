# Mehrere PCs

Wie sich der Dienst auf dem Notebook zwischen mehreren eingetragenen PCs entscheidet. Verbindung, Anmeldung und Verschlüsselung je PC stehen unter [Netzwerk](netzwerk.md).

Ein Notebook, das zu Hause und im Büro jeweils an einem anderen PC hängt, trägt beide ein, ohne die Konfiguration umzuschreiben. `client.json` führt dafür eine Liste `hosts`, jeder Eintrag mit eigenem Rechnernamen, fester IP, Port und Schlüssel, denn jeder Host erzeugt seinen eigenen (Beispiel unter [Installation](installation.md#client-auf-dem-notebook)).

- **Alle zugleich, der erste gewinnt:** Ohne Verbindung versucht der Dienst alle PCs parallel (`AttemptRace` in Core): auflösen, verbinden, anmelden. Der erste mit gelungener Anmeldung gewinnt, die übrigen Versuche werden abgebrochen. Danach bleibt der Dienst bei diesem PC, bis die Verbindung endet, und fragt die anderen nicht ab.
- **Warum nicht nacheinander:** Ein PC aus dem anderen Netz kostet bis zu 5 s fürs Auflösen und 3 s je Adresse. Nacheinander käme der erreichbare erst danach dran; parallel dauert der Aufbau so lange wie beim erreichbaren, im eigenen Netz Millisekunden. Bei bestehender Verbindung kostet die Liste nichts.
- **Warum nicht mit allen verbinden:** Es gibt einen Injector, eine Anordnung und eine Zwischenablage. Zwei PCs schickten Eingaben an denselben Cursor.
- **Zwei PCs zugleich erreichbar,** etwa über VPN: Es gewinnt der schnellere, nicht der erste der Liste. Wird die Anmeldung beim zweiten im selben Moment fertig, trennt der Client ihn sofort; dieser PC meldet dann nach 5 s einen Abbruch.
- **Störungen** aller PCs stehen in einer Meldung, in der Reihenfolge der Liste, damit dieselbe Lage nur einmal ins Protokoll kommt. Spricht ein PC eine andere Protokollversion und nimmt keiner an, sucht der Dienst nach einem Update.
- **Netzwechsel:** Ohne Verbindung wächst der Abstand zwischen den Versuchen bis 15 s. Meldet Windows eine geänderte Adresse (`NetworkChange.NetworkAddressChanged`: WLAN verbunden, LAN gesteckt, VPN), versucht der Dienst sofort neu. Eine bestehende Verbindung bleibt davon unberührt; ob sie noch lebt, entscheidet der Heartbeat.
- **Format bis 0.11:** Dort stand genau ein PC flach in `client.json` (`host`, `key`, `ip`, `port`). Der Dienst liest das weiter als Liste mit einem Eintrag und schreibt beim nächsten Speichern das neue Format.
- **Geprüft** mit Unit-Tests (Rennen, Format, Regeln) und `install-client.ps1` unter Windows PowerShell 5.1. Noch nicht mit zwei echten Hosts, siehe [Offene Aufgaben](todo.md).
