# Simulator

`InputFusion.Simulator.exe` ist ein reines Entwicklungswerkzeug und kommt nicht in den Installer. Er verhält sich gegenüber dem [Host](host.md) wie ein Client, spielt die Eingaben aber nicht ab, sondern zeigt sie in einem Fenster: die simulierten Monitore als Rechtecke, einen virtuellen Cursor, die gerade gedrückten Tasten und Knöpfe und ein Ereignisprotokoll.

Damit lässt sich der ganze Ablauf auf einem einzigen Rechner testen. Host und Simulator laufen nebeneinander und verbinden sich über `localhost`. Weil der Simulator nie `SendInput` aufruft, gibt es keine Rückkopplung mit den Hooks des Hosts.

## Start

```
InputFusion.Host.exe
InputFusion.Simulator.exe [--host <name>] [--monitors "x,y,b,h[,dpi];…"] [--delay <ms>]
```

| Parameter | Standard | Bedeutung |
|---|---|---|
| `--host` | `localhost` | Rechnername oder IP des Hosts |
| `--monitors` | `0,0,1920,1200,144;1920,-240,2560,1440,96` | simulierte Monitore in physischen Pixeln, je Monitor `x,y,breite,höhe` und optional die DPI |
| `--delay` | `0` | künstliche Latenz je Nachricht in Millisekunden, etwa zum Nachstellen von WLAN |

Der Simulator verbindet sich neu, sobald der Host wieder erreichbar ist.

## Was er zeigt

- **Eintritt:** Bei `SwitchToClient` erscheint der Cursor an der linken Kante des linken Monitors, auf der vom Host gemeldeten Höhe.
- **Bewegung:** Relative Bewegungen werden wie unter Windows an den Monitorgrenzen festgehalten. Absolute Bewegungen werden über die [Normierung](layout.md) zurückgerechnet.
- **Gedrückte Tasten:** Bleibt beim `SwitchToHost` etwas gedrückt, steht das als Warnung im Protokoll. So fallen hängende Tasten sofort auf, siehe [Fallstricke](fallstricke.md).

## Was er nicht testet

Das Mausgefühl auf dem echten Notebook (Zeigerbeschleunigung und DPI beim Abspielen), alles aus dem Dienst und dem Injector des [Clients](client.md), EDR-Verhalten und echte WLAN-Latenz. Dafür bleibt der Test mit zwei Rechnern nötig.

**Über RDP** erkennt der Host die Sitzung und geht in den Beobachtungsmodus (siehe [Host](host.md)): Die Übertragung und die Darstellung im Simulator lassen sich so flüssig prüfen, das Kapern und Parken der echten Maus aber nicht. Dafür muss der Test an der physischen Konsole laufen.

**Vorsicht beim Test auf einem Rechner an der Konsole:** Im echten Modus verschluckt der Host die lokale Eingabe. Zurück kommt man durch Bewegen nach links über die Kante oder mit `Strg+Alt+F12`. Bricht die Verbindung ab, etwa weil der Simulator geschlossen wird, verlässt der Host den Remote-Modus von selbst.
