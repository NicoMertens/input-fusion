# Multimonitor-Layout

Alle Monitore beider Rechner liegen frei angeordnet auf einer Fläche, wie in den Windows-Anzeigeeinstellungen.

1. **Beide Seiten melden ihre Monitore:** Rechteck in physischen Pixeln, DPI, Geräte-ID. Auf dem Notebook misst der [Injector](client.md#injector), weil nur er in der Sitzung die echten Werte sieht.
2. **Der Layout-Editor** in den [Einstellungen](einstellungen.md) ist ein WPF-`Canvas` mit allen Monitoren beider Rechner als Rechtecke. Sie werden per Drag & Drop angeordnet und rasten an den Kanten ein.
3. **Die Layout-Engine** erzeugt daraus Kantenabschnitte, etwa „PC-Monitor 2 rechts, y 200–900 → Notebook-Monitor 1 links“. Der Austrittspunkt wird proportional oder in Millimetern auf die Zielkante umgerechnet, damit die Maus bei unterschiedlich großen Monitoren nicht springt. Der [Host](host.md) wertet die Kantenabschnitte bei jeder Mausbewegung aus.
4. **Gespeichert wird je Monitor-Kombination** mit den Geräte-IDs als Schlüssel: Angedockt und unterwegs sind zwei verschiedene Layouts.

## Layout-Engine

Die Engine liegt in `InputFusion.Core` unter dem Namespace `InputFusion.Core.Layout`. Dieser Namespace bleibt bewusst frei von Win32-Aufrufen. Die Engine ist reine Logik (Rechtecke rein, Übergänge raus) und damit voll unit-testbar. Das ist wichtig, weil sie der fehleranfälligste Teil ist.

Wie sich DPI und gemischte Skalierung auf die Koordinaten auswirken, steht unter [Fallstricke](fallstricke.md).
