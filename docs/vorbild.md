# Vorbild: Mouse Without Borders

So arbeitet Mouse Without Borders aus den PowerToys:

- Low-Level-Hooks (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) fangen Eingaben ab. Im Remote-Modus gibt der Hook einen Wert ungleich 0 zurück und verschluckt die Eingabe damit lokal.
- Übertragung als kleine Pakete fester Größe über TCP, Peer-to-Peer ohne Server. Port 15100 für Eingaben, 15101 für Zwischenablage und Dateien.
- AES-Verschlüsselung, Schlüssel per PBKDF2 aus dem gemeinsamen Sicherheitsschlüssel. Eine daraus berechnete Magic Number in jedem Paket filtert Absender mit falschem Schlüssel aus.
- Der Ziel-PC spielt die Eingaben mit `SendInput` ab; Mauspositionen sind auf 0…65535 über den virtuellen Desktop normiert.
- Ein optionaler SYSTEM-Dienst startet einen Hilfsprozess auf dem Winlogon-Desktop, damit Sperrbildschirm und UAC-Abfrage steuerbar sind.

Diese Angaben stammen aus der Planung und sind nicht gegen den Quellcode von Mouse Without Borders geprüft.

**Was InputFusion anders macht:**

- Hooks und Abspielen übernimmt InputFusion im Prinzip, siehe [Host](host.md) und [Client](client.md).
- Statt eines gemeinsamen Schlüssels gibt es TLS mit Zertifikats-Pinning nach einer Kopplung, siehe [Netzwerk](netzwerk.md).
- Statt eines Rasters ganzer Rechner gibt es ein Layout je Monitor, siehe [Layout](layout.md).
