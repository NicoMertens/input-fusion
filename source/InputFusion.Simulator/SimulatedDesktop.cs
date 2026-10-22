using InputFusion.Core.Layout;
using KeyInterop = System.Windows.Input.KeyInterop;
using InputFusion.Core.Protocol;

namespace InputFusion.Simulator;

/// <summary>
/// Der simulierte Client: wendet empfangene Nachrichten auf einen virtuellen Cursor und eine Liste
/// gedrückter Tasten an, statt sie per SendInput abzuspielen. Wird vom Netzwerk-Thread geschrieben
/// und vom UI-Thread je Frame gelesen, daher alles unter einem Lock.
/// </summary>
internal sealed class SimulatedDesktop(IReadOnlyList<MonitorRect> monitors)
{
    private static readonly string[] ButtonNames = ["Links", "Rechts", "Mitte", "X1", "X2"];

    private readonly Lock _lock = new();
    private readonly SortedSet<string> _pressed = [];
    private int _x, _y;

    public IReadOnlyList<MonitorRect> Monitors => monitors;

    public bool Active { get; private set; }

    public long MoveCount { get; private set; }

    public event Action<string>? Logged;

    public (int X, int Y, bool Active, string[] Pressed, long Moves) Snapshot()
    {
        lock (_lock)
            return (_x, _y, Active, [.. _pressed], MoveCount);
    }

    public void Apply(MessageType type, byte[] payload)
    {
        string? log = null;
        lock (_lock)
        {
            switch (type)
            {
                case MessageType.SwitchToClient:
                    Active = true;
                    var leftmost = monitors.MinBy(m => m.Left);
                    var (_, vy) = LayoutGeometry.DenormalizeFromVirtualDesktop(monitors, 0, SwitchToClient.FromPayload(payload).Y);
                    _x = leftmost.Left;
                    _y = Math.Clamp(vy, leftmost.Top, leftmost.Bottom - 1);
                    log = $"Steuerung übernommen, Eintritt bei ({_x}, {_y})";
                    break;

                case MessageType.SwitchToHost:
                    Active = false;
                    log = _pressed.Count == 0
                        ? "Steuerung an den Host zurückgegeben"
                        : $"Steuerung zurück, aber noch gedrückt: {string.Join(", ", _pressed)}";
                    break;

                case MessageType.MouseMoveRelative:
                    var rel = MouseMoveRelative.FromPayload(payload);
                    MoveTo(_x + rel.Dx, _y + rel.Dy);
                    MoveCount++;
                    break;

                case MessageType.MouseMoveAbsolute:
                    var abs = MouseMoveAbsolute.FromPayload(payload);
                    var (ax, ay) = LayoutGeometry.DenormalizeFromVirtualDesktop(monitors, abs.X, abs.Y);
                    MoveTo(ax, ay);
                    MoveCount++;
                    break;

                case MessageType.MouseButton:
                    var button = MouseButton.FromPayload(payload);
                    string name = $"Maus {(button.Button < ButtonNames.Length ? ButtonNames[button.Button] : button.Button.ToString())}";
                    Toggle(name, button.State == ButtonState.Down);
                    log = $"{name} {(button.State == ButtonState.Down ? "gedrückt" : "losgelassen")} bei ({_x}, {_y})";
                    break;

                case MessageType.MouseWheel:
                    var wheel = MouseWheel.FromPayload(payload);
                    log = $"{(wheel.Horizontal ? "Querrad" : "Rad")} {wheel.Delta:+#;-#;0}";
                    break;

                case MessageType.Key:
                    var key = KeyEvent.FromPayload(payload);
                    string keyName = KeyInterop.KeyFromVirtualKey(key.VirtualKey).ToString();
                    Toggle(keyName, key.State == ButtonState.Down);
                    log = $"Taste {keyName} {(key.State == ButtonState.Down ? "unten" : "oben")}"
                        + $" (VK 0x{key.VirtualKey:X2}, Scan 0x{key.ScanCode:X2}{(key.Extended ? ", erweitert" : "")})";
                    break;

                default:
                    log = $"Unbekannte Nachricht {type} ({payload.Length} Bytes)";
                    break;
            }
        }

        if (log is not null)
            Logged?.Invoke(log);
    }

    /// <summary>Wie Windows: Ein Punkt außerhalb aller Monitore wird je Achse an der letzten gültigen Position gehalten.</summary>
    private void MoveTo(int x, int y)
    {
        if (LayoutGeometry.MonitorAt(monitors, x, y) is not null)
            (_x, _y) = (x, y);
        else if (LayoutGeometry.MonitorAt(monitors, x, _y) is not null)
            _x = x;
        else if (LayoutGeometry.MonitorAt(monitors, _x, y) is not null)
            _y = y;
    }

    private void Toggle(string name, bool down)
    {
        if (down)
            _pressed.Add(name);
        else
            _pressed.Remove(name);
    }
}
