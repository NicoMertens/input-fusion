# Vorbild: Mouse Without Borders

So arbeitet Mouse Without Borders aus den PowerToys:

- Low-Level-Hooks (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) fangen Eingaben ab. Im Remote-Modus gibt der Hook einen Wert ungleich 0 zurück und verschluckt die Eingabe damit lokal.
- Übertragung als kleine Pakete fester Größe über TCP, Peer-to-Peer ohne Server. Standard-Port 15100 für Zwischenablage und Dateien, 15101 für Nachrichten, Maus und Tastatur.
- AES-256, Schlüssel per PBKDF2 (SHA-512, 100 000 Iterationen) aus dem gemeinsamen Sicherheitsschlüssel. Salt und IV werden je Verbindung neu erzeugt und vorab im Klartext übertragen. Eine 24-Bit-Hash-„Magic Number“ und eine Prüfsumme im Kopf jedes Pakets filtern Absender mit falschem Schlüssel grob vor; der eigentliche Abgleich ist ein Handshake mit bitweise invertierter Antwort.
- Der Ziel-PC spielt die Eingaben mit `SendInput` ab; Mauspositionen sind auf 0…65535 über den virtuellen Desktop normiert.
- Ein optionaler SYSTEM-Dienst startet einen Hilfsprozess auf dem Winlogon-Desktop, damit Sperrbildschirm und UAC-Abfrage steuerbar sind.

## Kopplung und Serversuche

Diese Angaben sind gegen den PowerToys-Quellcode (Stand 2026-06) geprüft:

- **Kopplung über einen Schlüssel.** Auf einem Rechner erzeugt „New key“ einen zufälligen Schlüssel (16 Zeichen, verwechselbare Zeichen ausgelassen). Auf dem anderen trägt man diesen Schlüssel und den Gerätenamen des ersten ein und klickt „Connect“.
- **Namensauflösung nur über DNS** (`Dns.GetHostEntry`), dazu der Windows-Resolver. Es gibt keinen eigenen Namensdienst.
- **Feste Zuordnung als Ergänzung:** Die Einstellung „IP address mapping“ (Name → IP) wird zusätzlich zum DNS-Ergebnis verbunden, nicht als reiner Rückfall.
- **Keine automatische Suche.** Im ganzen Modul gibt es kein UDP, keinen Broadcast und kein Multicast. Neue Rechner werden nur über Heartbeats auf bereits bestehenden TCP-Verbindungen gelernt.
- **Beide Seiten verbinden sich.** Jeder Rechner lauscht auf 15100/15101 und baut zu allen Namen in seiner Matrix selbst Verbindungen auf.

InputFusion übernimmt davon die Kopplung per Schlüssel und die Namensauflösung über DNS mit optionaler fester IP-Zuordnung, verzichtet aber auf die Matrix mehrerer Rechner. Siehe [Netzwerk](netzwerk.md).

**Was InputFusion anders macht:**

- Hooks und Abspielen übernimmt InputFusion im Prinzip, siehe [Host](host.md) und [Client](client.md).
- Statt eines gemeinsamen Schlüssels gibt es TLS mit Zertifikats-Pinning nach einer Kopplung, siehe [Netzwerk](netzwerk.md).
- Statt eines Rasters ganzer Rechner gibt es ein Layout je Monitor, siehe [Layout](layout.md).
