using System.Text.Json.Serialization;

namespace NC2.InputFusion.Core.Layout;

/// <summary>
/// Ein Monitor als Rechteck in physischen Pixeln auf der gemeinsamen Fläche, plus DPI und Geräte-ID.
/// Reine Daten ohne Win32; der Injector füllt sie aus den echten Monitorwerten.
/// </summary>
public readonly record struct MonitorRect(Int32 X, Int32 Y, Int32 Width, Int32 Height, Int32 Dpi, String DeviceId)
{
    [JsonIgnore]
    public Int32 Left => X;
    [JsonIgnore]
    public Int32 Top => Y;
    [JsonIgnore]
    public Int32 Right => X + Width;
    [JsonIgnore]
    public Int32 Bottom => Y + Height;

    public Boolean Contains(Int32 px, Int32 py) => px >= Left && px < Right && py >= Top && py < Bottom;
}
