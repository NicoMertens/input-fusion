namespace InputFusion.Core.Layout;

/// <summary>
/// Anordnung der Client-Monitore um die Host-Monitore. Die Host-Monitore stehen fest auf ihren
/// Windows-Koordinaten; die Client-Monitore werden als Gruppe um (<see cref="OffsetX"/>, <see cref="OffsetY"/>)
/// verschoben. Einzeln verschieben lassen sie sich nicht: Ihre Lage zueinander bestimmt Windows auf dem
/// Client, und darin bewegt sich der Cursor dort. Aus der Anordnung folgen die Übergangskanten in beide
/// Richtungen. Reine Logik ohne Win32.
/// </summary>
public sealed class MonitorLayout
{
    /// <summary>So viele Pixel muss ein Client-Monitor mindestens an einem Host-Monitor anliegen.</summary>
    public const int MinOverlap = 50;

    /// <summary>Innerhalb dieses Abstands rasten Kanten beim Verschieben bündig ein.</summary>
    public const int SnapDistance = 40;

    /// <summary>So weit innerhalb der Host-Kante landet der Cursor bei der Rückkehr, damit eine kleine
    /// Bewegung zurück nicht sofort wieder wechselt.</summary>
    public const int ReturnInset = 2;

    public MonitorLayout(IReadOnlyList<MonitorRect> hostMonitors, IReadOnlyList<MonitorRect> clientMonitors, int offsetX, int offsetY)
    {
        HostMonitors = hostMonitors;
        ClientMonitors = clientMonitors;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Transitions = ComputeTransitions();
        ReturnEdges = Transitions.Select(ToReturnEdge).ToArray();
    }

    public IReadOnlyList<MonitorRect> HostMonitors { get; }

    /// <summary>In den eigenen Koordinaten des Clients.</summary>
    public IReadOnlyList<MonitorRect> ClientMonitors { get; }

    public int OffsetX { get; }

    public int OffsetY { get; }

    public IReadOnlyList<EdgeTransition> Transitions { get; }

    /// <summary>Die Rückwege in Client-Koordinaten; der Host schickt sie dem Client.</summary>
    public IReadOnlyList<ReturnEdge> ReturnEdges { get; }

    /// <summary>Schlüssel der Monitor-Kombination aus den Geräte-IDs: angedockt und unterwegs sind zwei Layouts.</summary>
    public static string CombinationKey(IReadOnlyList<MonitorRect> hostMonitors, IReadOnlyList<MonitorRect> clientMonitors) =>
        string.Join("|", hostMonitors.Select(m => m.DeviceId).Order(StringComparer.Ordinal))
        + "#" + string.Join("|", clientMonitors.Select(m => m.DeviceId).Order(StringComparer.Ordinal));

    public MonitorRect Place(MonitorRect client) => client with { X = client.X + OffsetX, Y = client.Y + OffsetY };

    /// <summary>
    /// Gültig ist eine Anordnung, wenn kein Client-Monitor einen Host-Monitor überdeckt und es mindestens
    /// einen Übergang gibt. Eine gespeicherte Anordnung kann ungültig werden, wenn sich eine Auflösung ändert.
    /// </summary>
    public bool IsValid => Transitions.Count > 0 && !Overlaps(HostMonitors, ClientMonitors, OffsetX, OffsetY);

    /// <summary>Standard für eine neue Kombination: der Client rechts neben dem rechtesten Host-Monitor, oben bündig.</summary>
    public static (int X, int Y) DefaultOffset(IReadOnlyList<MonitorRect> hostMonitors, IReadOnlyList<MonitorRect> clientMonitors)
    {
        MonitorRect host = hostMonitors.MaxBy(m => m.Right);
        MonitorRect client = clientMonitors.MinBy(m => m.Left);
        return Snap(hostMonitors, clientMonitors, host.Right - client.Left, host.Top - client.Top);
    }

    /// <summary>
    /// Die gültige Lage der Client-Gruppe, die dem Wunsch (<paramref name="x"/>, <paramref name="y"/>) am nächsten
    /// liegt: Ein Client-Monitor liegt mit mindestens <see cref="MinOverlap"/> Pixeln an einem Host-Monitor an,
    /// ohne einen zu überdecken. Bündige Kanten rasten innerhalb von <see cref="SnapDistance"/> ein.
    /// </summary>
    public static (int X, int Y) Snap(IReadOnlyList<MonitorRect> hostMonitors, IReadOnlyList<MonitorRect> clientMonitors, int x, int y)
    {
        (int X, int Y)? best = null;
        long bestDistance = long.MaxValue;
        foreach (MonitorRect h in hostMonitors)
        {
            foreach (MonitorRect c in clientMonitors)
            {
                foreach ((int X, int Y) candidate in Candidates(h, c, x, y))
                {
                    if (Overlaps(hostMonitors, clientMonitors, candidate.X, candidate.Y))
                    {
                        continue;
                    }

                    long dx = candidate.X - x, dy = candidate.Y - y;
                    long distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }
        }
        return best ?? (x, y);
    }

    /// <summary>
    /// Prüft, ob der Cursor die Host-Fläche über einen Übergang verlässt. (<paramref name="x"/>, <paramref name="y"/>)
    /// ist das Ziel der Bewegung, wie der Hook es meldet, (<paramref name="dx"/>, <paramref name="dy"/>) die
    /// Bewegung; der Cursor stand also vorher auf (x − dx, y − dy). Liefert den Eintrittspunkt in Client-Koordinaten.
    /// </summary>
    public bool TryLeaveHost(int x, int y, int dx, int dy, out EdgeTransition transition, out (int X, int Y) clientPoint)
    {
        int px = x - dx, py = y - dy;
        // Klemmt Windows die Zielposition schon auf den Rand, kommt beim Drücken gegen die Kante ein Ereignis ganz
        // ohne Bewegung an. Das zählt auf dem Randpixel als Drücken nach außen, eine Bewegung entlang der Kante nicht.
        bool still = dx == 0 && dy == 0;
        foreach (EdgeTransition t in Transitions)
        {
            MonitorRect h = t.HostMonitor;
            if (!h.Contains(px, py))
            {
                continue;
            }

            bool crossing = t.Side switch
            {
                EdgeSide.Right => (dx > 0 || still) && x >= h.Right - 1 && py >= t.From && py < t.To,
                EdgeSide.Left => (dx < 0 || still) && x <= h.Left && py >= t.From && py < t.To,
                EdgeSide.Bottom => (dy > 0 || still) && y >= h.Bottom - 1 && px >= t.From && px < t.To,
                _ => (dy < 0 || still) && y <= h.Top && px >= t.From && px < t.To,
            };
            if (!crossing)
            {
                continue;
            }

            transition = t;
            clientPoint = EntryPoint(t, t.Side.IsVertical() ? py : px);
            return true;
        }

        transition = default;
        clientPoint = default;
        return false;
    }

    /// <summary>Der Eintrittspunkt auf dem Client, wenn die Maus an der Stelle <paramref name="along"/> (Host-Koordinate
    /// entlang der Kante) über den Übergang geht.</summary>
    public (int X, int Y) EntryPoint(EdgeTransition t, int along)
    {
        int a = Math.Clamp(along, t.From, t.To - 1);
        MonitorRect p = t.PlacedClientMonitor;
        (int lx, int ly) = t.Side switch
        {
            EdgeSide.Right => (p.Left, a),
            EdgeSide.Left => (p.Right - 1, a),
            EdgeSide.Bottom => (a, p.Top),
            _ => (a, p.Bottom - 1),
        };
        return (lx - OffsetX, ly - OffsetY);
    }

    /// <summary>
    /// Rechnet den Austritt des Clients an seiner Kante <paramref name="clientSide"/> bei (<paramref name="clientX"/>,
    /// <paramref name="clientY"/>) in Client-Koordinaten auf den Punkt um, an dem der Host-Cursor erscheint.
    /// </summary>
    public bool TryReturn(EdgeSide clientSide, int clientX, int clientY, out (int X, int Y) hostPoint)
    {
        int lx = clientX + OffsetX, ly = clientY + OffsetY;
        foreach (EdgeTransition t in Transitions)
        {
            if (t.Side.Opposite() != clientSide)
            {
                continue;
            }

            int along = t.Side.IsVertical() ? ly : lx;
            if (along < t.From || along >= t.To)
            {
                continue;
            }

            MonitorRect h = t.HostMonitor;
            hostPoint = t.Side switch
            {
                EdgeSide.Right => (h.Right - 1 - ReturnInset, ly),
                EdgeSide.Left => (h.Left + ReturnInset, ly),
                EdgeSide.Bottom => (lx, h.Bottom - 1 - ReturnInset),
                _ => (lx, h.Top + ReturnInset),
            };
            return true;
        }

        hostPoint = default;
        return false;
    }

    private List<EdgeTransition> ComputeTransitions()
    {
        List<EdgeTransition> result = new List<EdgeTransition>();
        foreach (MonitorRect h in HostMonitors)
        {
            foreach (MonitorRect c in ClientMonitors)
            {
                MonitorRect p = Place(c);
                if (p.Left == h.Right)
                {
                    AddIfOverlapping(result, EdgeSide.Right, h, c, p, h.Top, h.Bottom, p.Top, p.Bottom);
                }

                if (p.Right == h.Left)
                {
                    AddIfOverlapping(result, EdgeSide.Left, h, c, p, h.Top, h.Bottom, p.Top, p.Bottom);
                }

                if (p.Top == h.Bottom)
                {
                    AddIfOverlapping(result, EdgeSide.Bottom, h, c, p, h.Left, h.Right, p.Left, p.Right);
                }

                if (p.Bottom == h.Top)
                {
                    AddIfOverlapping(result, EdgeSide.Top, h, c, p, h.Left, h.Right, p.Left, p.Right);
                }
            }
        }
        return result;
    }

    private static void AddIfOverlapping(List<EdgeTransition> result, EdgeSide side, MonitorRect h, MonitorRect c, MonitorRect p,
        int hostStart, int hostEnd, int clientStart, int clientEnd)
    {
        int from = Math.Max(hostStart, clientStart);
        int to = Math.Min(hostEnd, clientEnd);
        if (to > from)
        {
            result.Add(new EdgeTransition(side, h, c, p, from, to));
        }
    }

    private ReturnEdge ToReturnEdge(EdgeTransition t)
    {
        MonitorRect c = t.ClientMonitor;
        return t.Side switch
        {
            EdgeSide.Right => new ReturnEdge(EdgeSide.Left, c.Left, t.From - OffsetY, t.To - OffsetY),
            EdgeSide.Left => new ReturnEdge(EdgeSide.Right, c.Right - 1, t.From - OffsetY, t.To - OffsetY),
            EdgeSide.Bottom => new ReturnEdge(EdgeSide.Top, c.Top, t.From - OffsetX, t.To - OffsetX),
            _ => new ReturnEdge(EdgeSide.Bottom, c.Bottom - 1, t.From - OffsetX, t.To - OffsetX),
        };
    }

    /// <summary>Die möglichen Lagen, in denen <paramref name="c"/> an einer der vier Kanten von <paramref name="h"/> anliegt.</summary>
    private static IEnumerable<(int X, int Y)> Candidates(MonitorRect h, MonitorRect c, int x, int y)
    {
        // Entlang der Kante so weit verschieben wie gewünscht, solange mindestens MinOverlap anliegen.
        int minY = h.Top + MinOverlap - c.Bottom, maxY = h.Bottom - MinOverlap - c.Top;
        int minX = h.Left + MinOverlap - c.Right, maxX = h.Right - MinOverlap - c.Left;
        if (minY <= maxY)
        {
            int alongY = AlignEdges(Math.Clamp(y, minY, maxY), h.Top, h.Bottom, c.Top, c.Bottom, minY, maxY);
            yield return (h.Right - c.Left, alongY);
            yield return (h.Left - c.Right, alongY);
        }
        if (minX <= maxX)
        {
            int alongX = AlignEdges(Math.Clamp(x, minX, maxX), h.Left, h.Right, c.Left, c.Right, minX, maxX);
            yield return (alongX, h.Bottom - c.Top);
            yield return (alongX, h.Top - c.Bottom);
        }
    }

    /// <summary>Lässt Anfang oder Ende des Client-Monitors bündig mit dem Host-Monitor einrasten, wenn sie nah dran sind.</summary>
    private static int AlignEdges(int offset, int hostStart, int hostEnd, int clientStart, int clientEnd, int min, int max)
    {
        foreach (int aligned in new[] { hostStart - clientStart, hostEnd - clientEnd })
        {
            if (Math.Abs(offset - aligned) <= SnapDistance && aligned >= min && aligned <= max)
            {
                return aligned;
            }
        }

        return offset;
    }

    private static bool Overlaps(IReadOnlyList<MonitorRect> hostMonitors, IReadOnlyList<MonitorRect> clientMonitors, int offsetX, int offsetY)
    {
        foreach (MonitorRect h in hostMonitors)
        {
            foreach (MonitorRect c in clientMonitors)
            {
                int left = c.Left + offsetX, top = c.Top + offsetY;
                if (left < h.Right && left + c.Width > h.Left && top < h.Bottom && top + c.Height > h.Top)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
