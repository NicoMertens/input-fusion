namespace InputFusion.Core.Layout;

/// <summary>
/// Ein Kantenabschnitt, an dem die Maus vom Host auf den Client wechselt. <paramref name="Side"/> ist die
/// Kante des Host-Monitors, <paramref name="From"/> bis <paramref name="To"/> (exklusiv) der gemeinsame
/// Abschnitt entlang dieser Kante in Host-Koordinaten. <paramref name="ClientMonitor"/> steht in den
/// eigenen Koordinaten des Clients, <paramref name="PlacedClientMonitor"/> verschoben auf die gemeinsame Fläche.
/// </summary>
public readonly record struct EdgeTransition(
    EdgeSide Side, MonitorRect HostMonitor, MonitorRect ClientMonitor, MonitorRect PlacedClientMonitor, int From, int To);

/// <summary>
/// Ein Kantenabschnitt eines Client-Monitors, über den die Maus zurück zum Host geht, in den Koordinaten des
/// Clients. <paramref name="Edge"/> ist die Pixelzeile bzw. -spalte der Kante, die der Cursor erreichen kann
/// (bei rechts und unten also die letzte innerhalb des Monitors).
/// </summary>
public readonly record struct ReturnEdge(EdgeSide Side, int Edge, int From, int To)
{
    /// <summary>
    /// True, wenn der Cursor nach einer Bewegung um (<paramref name="dx"/>, <paramref name="dy"/>) auf
    /// (<paramref name="x"/>, <paramref name="y"/>) an dieser Kante ansteht und nach außen drückt. Windows hält
    /// den Cursor an der Monitorkante fest, deshalb genügt der Vergleich auf genau die Kante.
    /// </summary>
    public bool IsHit(int x, int y, int dx, int dy) => Side switch
    {
        EdgeSide.Left => dx < 0 && x == Edge && y >= From && y < To,
        EdgeSide.Right => dx > 0 && x == Edge && y >= From && y < To,
        EdgeSide.Top => dy < 0 && y == Edge && x >= From && x < To,
        _ => dy > 0 && y == Edge && x >= From && x < To,
    };

    public static ReturnEdge? Find(IReadOnlyList<ReturnEdge> edges, int x, int y, int dx, int dy)
    {
        foreach (var edge in edges)
            if (edge.IsHit(x, y, dx, dy))
                return edge;
        return null;
    }
}
