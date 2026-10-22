namespace InputFusion.Core.Layout;

/// <summary>
/// Reine Geometrie für das Multimonitor-Layout: welcher Monitor einen Punkt enthält und wie ein
/// Austrittspunkt proportional auf die Zielkante umgerechnet wird, damit die Maus bei
/// unterschiedlich großen Monitoren am Übergang nicht springt. Voll unit-testbar, kein Win32.
/// </summary>
public static class LayoutGeometry
{
    /// <summary>Der Monitor, der den Punkt enthält, oder null.</summary>
    public static MonitorRect? MonitorAt(IReadOnlyList<MonitorRect> monitors, int x, int y)
    {
        foreach (var m in monitors)
            if (m.Contains(x, y))
                return m;
        return null;
    }

    /// <summary>
    /// Bildet einen Wert aus dem Quellbereich [sourceStart, sourceEnd] proportional auf den
    /// Zielbereich [targetStart, targetEnd] ab. Außerhalb liegende Werte werden auf den Zielbereich
    /// geklemmt. Ein Quellbereich der Länge 0 liefert die Mitte des Ziels.
    /// </summary>
    public static int MapProportional(int value, int sourceStart, int sourceEnd, int targetStart, int targetEnd)
    {
        int sourceSpan = sourceEnd - sourceStart;
        if (sourceSpan == 0)
            return targetStart + (targetEnd - targetStart) / 2;

        double t = (double)(value - sourceStart) / sourceSpan;
        t = Math.Clamp(t, 0.0, 1.0);
        return targetStart + (int)Math.Round(t * (targetEnd - targetStart));
    }

    /// <summary>
    /// Rechnet die y-Koordinate eines Austritts an der rechten Kante des Quellmonitors auf die
    /// y-Koordinate am Eintritt an der linken Kante des Zielmonitors um. Der Eintritt liegt einen
    /// Pixel innerhalb der Zielkante.
    /// </summary>
    public static (int X, int Y) MapAcrossRightEdge(MonitorRect source, MonitorRect target, int sourceY)
    {
        int targetY = MapProportional(sourceY, source.Top, source.Bottom, target.Top, target.Bottom);
        return (target.Left, targetY);
    }

    /// <summary>Normiert einen Punkt auf 0…65535 über die Bounding-Box aller Monitore (wie das Vorbild).</summary>
    public static (ushort X, ushort Y) NormalizeToVirtualDesktop(IReadOnlyList<MonitorRect> monitors, int x, int y)
    {
        if (monitors.Count == 0)
            return (0, 0);

        int minX = monitors.Min(m => m.Left);
        int minY = monitors.Min(m => m.Top);
        int maxX = monitors.Max(m => m.Right);
        int maxY = monitors.Max(m => m.Bottom);

        ushort nx = Normalize(x, minX, maxX);
        ushort ny = Normalize(y, minY, maxY);
        return (nx, ny);
    }

    /// <summary>Umkehrung von <see cref="NormalizeToVirtualDesktop"/>: 0…65535 zurück auf die Bounding-Box.</summary>
    public static (int X, int Y) DenormalizeFromVirtualDesktop(IReadOnlyList<MonitorRect> monitors, ushort x, ushort y)
    {
        if (monitors.Count == 0)
            return (0, 0);

        int minX = monitors.Min(m => m.Left);
        int minY = monitors.Min(m => m.Top);
        int maxX = monitors.Max(m => m.Right);
        int maxY = monitors.Max(m => m.Bottom);

        return (Denormalize(x, minX, maxX), Denormalize(y, minY, maxY));
    }

    private static int Denormalize(ushort value, int min, int max) =>
        min + (int)Math.Round((double)value / ushort.MaxValue * (max - min));

    private static ushort Normalize(int value, int min, int max)
    {
        if (max <= min)
            return 0;
        double t = Math.Clamp((double)(value - min) / (max - min), 0.0, 1.0);
        return (ushort)Math.Round(t * ushort.MaxValue);
    }
}
