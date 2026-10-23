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
    private ReturnEdge[] _returnEdges = [];
    private SwitchBackRequest? _exitRequest;

    public IReadOnlyList<MonitorRect> Monitors => monitors;

    public bool Active { get; private set; }

    public long MoveCount { get; private set; }

    public event Action<string>? Logged;

    /// <summary>Liefert einmal den Austritt, nachdem der Cursor eine Rückkehrkante erreicht hat: der Host soll zurück übernehmen.</summary>
    public SwitchBackRequest? TryTakeExitRequest()
    {
        lock (_lock)
        {
            SwitchBackRequest? request = _exitRequest;
            _exitRequest = null;
            return request;
        }
    }

    public (int X, int Y, bool Active, string[] Pressed, long Moves) Snapshot()
    {
        lock (_lock)
        {
            return (_x, _y, Active, [.. _pressed], MoveCount);
        }
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
                    SwitchToClient entry = SwitchToClient.FromPayload(payload);
                    MoveTo(entry.X, entry.Y);
                    log = $"Steuerung übernommen, Eintritt bei ({_x}, {_y})";
                    break;

                case MessageType.ReturnEdges:
                    _returnEdges = ReturnEdgeList.FromPayload(payload);
                    log = _returnEdges.Length == 0
                        ? "Keine Rückkehrkanten"
                        : "Rückkehrkanten: " + string.Join(", ", _returnEdges.Select(e => $"{e.Side} {e.Edge} [{e.From}, {e.To})"));
                    break;

                case MessageType.Heartbeat:
                    break;

                case MessageType.SwitchToHost:
                    Active = false;
                    log = _pressed.Count == 0
                        ? "Steuerung an den Host zurückgegeben"
                        : $"Steuerung zurück, aber noch gedrückt: {string.Join(", ", _pressed)}";
                    break;

                case MessageType.MouseMoveRelative:
                    MouseMoveRelative rel = MouseMoveRelative.FromPayload(payload);
                    MoveTo(_x + rel.Dx, _y + rel.Dy);
                    MoveCount++;
                    log = CheckExit(rel.Dx, rel.Dy);
                    break;

                case MessageType.MouseMoveAbsolute:
                    MouseMoveAbsolute abs = MouseMoveAbsolute.FromPayload(payload);
                    (int ax, int ay) = LayoutGeometry.DenormalizeFromVirtualDesktop(monitors, abs.X, abs.Y);
                    (int bx, int by) = (_x, _y);
                    MoveTo(ax, ay);
                    MoveCount++;
                    log = CheckExit(_x - bx, _y - by);
                    break;

                case MessageType.MouseButton:
                    MouseButton button = MouseButton.FromPayload(payload);
                    string name = $"Maus {(button.Button < ButtonNames.Length ? ButtonNames[button.Button] : button.Button.ToString())}";
                    Toggle(name, button.State == ButtonState.Down);
                    log = $"{name} {(button.State == ButtonState.Down ? "gedrückt" : "losgelassen")} bei ({_x}, {_y})";
                    break;

                case MessageType.MouseWheel:
                    MouseWheel wheel = MouseWheel.FromPayload(payload);
                    log = $"{(wheel.Horizontal ? "Querrad" : "Rad")} {wheel.Delta:+#;-#;0}";
                    break;

                case MessageType.Key:
                    KeyEvent key = KeyEvent.FromPayload(payload);
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
        {
            Logged?.Invoke(log);
        }
    }

    /// <summary>Verbindung weg: Steuerung und gedrückte Tasten verwerfen, wie es der echte Injector tut.</summary>
    public void Disconnected()
    {
        lock (_lock)
        {
            Active = false;
            _exitRequest = null;
            _returnEdges = [];
            _pressed.Clear();
        }
    }

    private string? CheckExit(int dx, int dy)
    {
        if (!Active || ReturnEdge.Find(_returnEdges, _x, _y, dx, dy) is not { } edge)
        {
            return null;
        }

        Active = false;
        _exitRequest = new SwitchBackRequest(edge.Side, _x, _y);
        return $"An der Rückkehrkante ({edge.Side}) angekommen, zurück an den Host";
    }

    /// <summary>
    /// Liegt das Ziel auf einem Monitor, dorthin. Sonst an die Kante des aktuellen Monitors klemmen,
    /// damit der Cursor die Kante wirklich erreicht (sonst löst der Austritt links nie aus).
    /// </summary>
    private void MoveTo(int x, int y)
    {
        if (LayoutGeometry.MonitorAt(monitors, x, y) is not null)
        {
            (_x, _y) = (x, y);
            return;
        }

        MonitorRect current = LayoutGeometry.MonitorAt(monitors, _x, _y) ?? monitors[0];
        _x = Math.Clamp(x, current.Left, current.Right - 1);
        _y = Math.Clamp(y, current.Top, current.Bottom - 1);
    }

    private void Toggle(string name, bool down)
    {
        if (down)
        {
            _pressed.Add(name);
        }
        else
        {
            _pressed.Remove(name);
        }
    }
}
