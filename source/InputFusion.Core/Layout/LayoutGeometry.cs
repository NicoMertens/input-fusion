namespace InputFusion.Core.Layout;

/// <summary>
/// Reine Geometrie für das Multimonitor-Layout: welcher Monitor einen Punkt enthält und wie ein
/// Austrittspunkt proportional auf die Zielkante umgerechnet wird, damit die Maus bei
/// unterschiedlich großen Monitoren am Übergang nicht springt. Voll unit-testbar, kein Win32.
/// </summary>
public static class LayoutGeometry
{
    /// <summary>Der Monitor, der den Punkt enthält, oder null.</summary>
    public static MonitorRect? MonitorAt(IReadOnlyList<MonitorRect> monitors, Int32 x, Int32 y)
    {
        foreach (MonitorRect m in monitors)
        {
            if (m.Contains(x, y))
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>
    /// Bildet einen Wert aus dem Quellbereich [sourceStart, sourceEnd] proportional auf den
    /// Zielbereich [targetStart, targetEnd] ab. Außerhalb liegende Werte werden auf den Zielbereich
    /// geklemmt. Ein Quellbereich der Länge 0 liefert die Mitte des Ziels.
    /// </summary>
    public static Int32 MapProportional(Int32 value, Int32 sourceStart, Int32 sourceEnd, Int32 targetStart, Int32 targetEnd)
    {
        Int32 sourceSpan = sourceEnd - sourceStart;
        if (sourceSpan == 0)
        {
            return targetStart + (targetEnd - targetStart) / 2;
        }

        Double t = (Double)(value - sourceStart) / sourceSpan;
        t = Math.Clamp(t, 0.0, 1.0);
        return targetStart + (Int32)Math.Round(t * (targetEnd - targetStart));
    }

    /// <summary>
    /// Rechnet die y-Koordinate eines Austritts an der rechten Kante des Quellmonitors auf die
    /// y-Koordinate am Eintritt an der linken Kante des Zielmonitors um. Der Eintritt liegt einen
    /// Pixel innerhalb der Zielkante.
    /// </summary>
    public static (Int32 X, Int32 Y) MapAcrossRightEdge(MonitorRect source, MonitorRect target, Int32 sourceY)
    {
        Int32 targetY = MapProportional(sourceY, source.Top, source.Bottom, target.Top, target.Bottom);
        return (target.Left, targetY);
    }

    /// <summary>Normiert einen Punkt auf 0…65535 über die Bounding-Box aller Monitore (wie das Vorbild).</summary>
    public static (UInt16 X, UInt16 Y) NormalizeToVirtualDesktop(IReadOnlyList<MonitorRect> monitors, Int32 x, Int32 y)
    {
        if (monitors.Count == 0)
        {
            return (0, 0);
        }

        Int32 minX = monitors.Min(m => m.Left);
        Int32 minY = monitors.Min(m => m.Top);
        Int32 maxX = monitors.Max(m => m.Right);
        Int32 maxY = monitors.Max(m => m.Bottom);

        UInt16 nx = Normalize(x, minX, maxX);
        UInt16 ny = Normalize(y, minY, maxY);
        return (nx, ny);
    }

    /// <summary>Umkehrung von <see cref="NormalizeToVirtualDesktop"/>: 0…65535 zurück auf die Bounding-Box.</summary>
    public static (Int32 X, Int32 Y) DenormalizeFromVirtualDesktop(IReadOnlyList<MonitorRect> monitors, UInt16 x, UInt16 y)
    {
        if (monitors.Count == 0)
        {
            return (0, 0);
        }

        Int32 minX = monitors.Min(m => m.Left);
        Int32 minY = monitors.Min(m => m.Top);
        Int32 maxX = monitors.Max(m => m.Right);
        Int32 maxY = monitors.Max(m => m.Bottom);

        return (Denormalize(x, minX, maxX), Denormalize(y, minY, maxY));
    }

    private static Int32 Denormalize(UInt16 value, Int32 min, Int32 max) =>
        min + (Int32)Math.Round((Double)value / UInt16.MaxValue * (max - min));

    private static UInt16 Normalize(Int32 value, Int32 min, Int32 max)
    {
        if (max <= min)
        {
            return 0;
        }

        Double t = Math.Clamp((Double)(value - min) / (max - min), 0.0, 1.0);
        return (UInt16)Math.Round(t * UInt16.MaxValue);
    }
}
