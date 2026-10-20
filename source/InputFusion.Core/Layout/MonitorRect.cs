namespace InputFusion.Core.Layout;

/// <summary>
/// Ein Monitor als Rechteck in physischen Pixeln auf der gemeinsamen Fläche, plus DPI und Geräte-ID.
/// Reine Daten ohne Win32; der Injector füllt sie aus den echten Monitorwerten.
/// </summary>
public readonly record struct MonitorRect(int X, int Y, int Width, int Height, int Dpi, string DeviceId)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int px, int py) => px >= Left && px < Right && py >= Top && py < Bottom;
}
