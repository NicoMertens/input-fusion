# Win32-Fallstricke

Fallstricke, die beide Seiten betreffen. Was nur eine Seite betrifft, steht bei [Host](host.md) bzw. [Client](client.md).

- **DPI:** Alle Prozesse mit Desktop-Zugriff laufen Per-Monitor-V2-DPI-aware, sonst rechnet Windows die Koordinaten um. Gemischte Skalierung (PC 100 %, Notebook 150 %) ist der Normalfall. Gesetzt wird das für alle drei Programme über das gemeinsame `source/app.manifest`. Im Dienst wirkt es nicht, dort schadet es aber auch nicht.
- **Relativ oder absolut senden** bestimmt maßgeblich, wie sich die Maus anfühlt. Früh im Durchstich ausprobieren.
- **Hängende Tasten:** Beim Wechsel auf der alten Seite Key-Up für alle gedrückten Tasten nachsenden.
- **Nicht abfangbar:** `Strg+Alt+Entf`. `Win+L` sperrt immer den lokalen Rechner.
- **RDP:** In einer Remotedesktop-Sitzung lässt sich die Maus nicht kapern. Gemessen (`--diag`, siehe [Host](host.md)): `SetCursorPos` greift zwar, der Zeiger steht direkt danach zu 100 % an der Zielstelle. Aber RDP liefert die Mausbewegung als **absolute Position**; das nächste Maus-Ereignis trägt wieder die echte Cursorposition des Clients, bis zu eine ganze Monitorbreite entfernt. Das Parken in der Mitte ist damit wirkungslos und erzeugt beim Deltarechnen Sprünge. Deshalb erkennt der Host RDP (`SM_REMOTESESSION`) und beobachtet dort nur. Dass RDP absolute Positionen liefert, hat auch sein Gutes: Die Differenz zweier Ereignisse ergibt direkt die echte Bewegung, der Beobachtungsmodus läuft über RDP deshalb flüssig. Das Kapern setzt eine echte Konsolensitzung voraus, dort kommt die Maus relativ und lässt sich einfrieren.
- **EDR und Virenscanner:** Globale Tastatur-Hooks plus Eingabe-Injection sehen aus wie ein Keylogger. Auf Firmengeräten vorher mit der IT klären.
- **Interop über CsWin32** statt handgeschriebener P/Invoke-Signaturen. Die Liste der benötigten APIs steht in `source/InputFusion.Core/NativeMethods.txt`.
