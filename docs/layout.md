# Multimonitor-Layout

Alle Monitore beider Rechner liegen frei angeordnet auf einer Fläche, wie in den Windows-Anzeigeeinstellungen.

1. **Beide Seiten melden ihre Monitore:** Rechteck in physischen Pixeln, DPI, Geräte-ID. Auf dem Notebook misst der [Injector](client.md#injector), weil nur er in der Sitzung die echten Werte sieht.
2. **Der Layout-Editor** in den [Einstellungen](einstellungen.md) ist ein WPF-`Canvas` mit allen Monitoren beider Rechner als Rechtecke. Sie werden per Drag & Drop angeordnet und rasten an den Kanten ein.
3. **Die Layout-Engine** erzeugt daraus Kantenabschnitte, etwa „PC-Monitor 2 rechts, y 200–900 → Notebook-Monitor 1 links“. Der Austrittspunkt wird proportional oder in Millimetern auf die Zielkante umgerechnet, damit die Maus bei unterschiedlich großen Monitoren nicht springt. Der [Host](host.md) wertet die Kantenabschnitte bei jeder Mausbewegung aus.
4. **Gespeichert wird je Monitor-Kombination** mit den Geräte-IDs als Schlüssel: Angedockt und unterwegs sind zwei verschiedene Layouts.

## Layout-Engine

Die Engine liegt in `InputFusion.Core` unter dem Namespace `InputFusion.Core.Layout`. Dieser Namespace bleibt bewusst frei von Win32-Aufrufen. Die Engine ist reine Logik (Rechtecke rein, Übergänge raus) und damit voll unit-testbar. Das ist wichtig, weil sie der fehleranfälligste Teil ist.

`MonitorLayout` hält die Host-Monitore, die Client-Monitore und den Versatz der Client-Gruppe:

- **Die Host-Monitore stehen fest** auf ihren Windows-Koordinaten in physischen Pixeln. **Die Client-Monitore werden nur als Gruppe verschoben**, um (`OffsetX`, `OffsetY`). Ihre Lage zueinander bestimmt Windows auf dem Notebook, und darin bewegt sich der Cursor dort; einzeln verschoben passte das Layout nicht mehr zur Wirklichkeit des Clients. Damit weicht die Umsetzung bewusst vom Punkt 2 oben ab, wo alle Monitore frei angeordnet werden.
- **`Snap`** sucht zu einer gewünschten Lage die nächste gültige: Ein Client-Monitor liegt mit mindestens `MinOverlap` (50) Pixeln an einer Kante eines Host-Monitors an, kein Client-Monitor überdeckt einen Host-Monitor. Ober- und Unterkante bzw. linke und rechte Kante rasten innerhalb von `SnapDistance` (40) Pixeln bündig ein. `DefaultOffset` legt eine neue Kombination rechts neben den rechtesten Host-Monitor, oben bündig.
- **`Transitions`:** Jeder Abschnitt, an dem ein verschobener Client-Monitor genau an einer Host-Kante anliegt, ist ein `EdgeTransition` mit Seite, Host-Monitor, Client-Monitor und dem gemeinsamen Bereich `From`…`To` entlang der Kante.
- **`TryLeaveHost`** prüft eine Mausbewegung des Hosts gegen die Übergänge: Der Cursor stand vorher auf dem Host-Monitor, im Bereich des Übergangs, und drückt über dessen Kante hinaus. Bewegungen entlang der Kante wechseln nicht. Der Eintrittspunkt auf dem Client liegt auf derselben Höhe bzw. Breite der gemeinsamen Fläche, nicht proportional umgerechnet: So verhält es sich wie zwischen zwei Monitoren unter Windows, und an jedem Punkt des Abschnitts liegen beide Monitore ja wirklich nebeneinander.
- **`ReturnEdges`** sind dieselben Abschnitte aus Sicht des Clients und in dessen Koordinaten (`ReturnEdge`: Seite des Client-Monitors, Pixelspalte bzw. -zeile der Kante, Bereich). `ReturnEdge.IsHit` gilt, wenn der Cursor genau auf der Kante steht und nach außen drückt; Windows hält ihn dort fest. **`TryReturn`** rechnet den Austrittspunkt des Clients zurück auf den Host, `ReturnInset` (2) Pixel innerhalb der Host-Kante.
- **`CombinationKey`** bildet aus den Geräte-IDs beider Seiten den Schlüssel, unter dem eine Anordnung gespeichert wird. `IsValid` ist falsch, wenn eine gespeicherte Anordnung nicht mehr passt, etwa nach einer Auflösungsänderung.

Wie sich DPI und gemischte Skalierung auf die Koordinaten auswirken, steht unter [Fallstricke](fallstricke.md).
