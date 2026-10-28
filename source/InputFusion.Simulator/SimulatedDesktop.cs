using NC2.InputFusion.Core.Layout;
using KeyInterop = System.Windows.Input.KeyInterop;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Simulator;

/// <summary>
/// Der simulierte Client: wendet empfangene Nachrichten auf einen virtuellen Cursor und eine Liste
/// gedrückter Tasten an, statt sie per SendInput abzuspielen. Wird vom Netzwerk-Thread geschrieben
/// und vom UI-Thread je Frame gelesen, daher alles unter einem Lock.
/// </summary>
internal sealed class SimulatedDesktop(IReadOnlyList<MonitorRect> monitors)
{
    private static readonly String[] _buttonNames = ["Links", "Rechts", "Mitte", "X1", "X2"];

    private readonly Lock _lock = new();
    private readonly SortedSet<String> _pressed = [];
    private Int32 _x, _y;
    private ReturnEdge[] _returnEdges = [];
    private SwitchBackRequest? _exitRequest;

    public IReadOnlyList<MonitorRect> Monitors => monitors;

    public Boolean Active { get; private set; }

    public Int64 MoveCount { get; private set; }

    public event Action<String>? Logged;

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

    public (Int32 X, Int32 Y, Boolean Active, String[] Pressed, Int64 Moves) Snapshot()
    {
        lock (_lock)
        {
            return (_x, _y, Active, [.. _pressed], MoveCount);
        }
    }

    public void Apply(MessageType type, Byte[] payload)
    {
        String? log;
        lock (_lock)
        {
            log = Dispatch(type, payload);
        }

        if (log is not null)
        {
            Logged?.Invoke(log);
        }
    }

    private String? Dispatch(MessageType type, Byte[] payload) => type switch
    {
        MessageType.SwitchToClient => OnSwitchToClient(payload),
        MessageType.ReturnEdges => OnReturnEdges(payload),
        MessageType.LockWorkstation => OnLockWorkstation(),
        MessageType.SwitchToHost => OnSwitchToHost(),
        MessageType.MouseMoveRelative => OnMouseMoveRelative(payload),
        MessageType.MouseMoveAbsolute => OnMouseMoveAbsolute(payload),
        MessageType.MouseButton => OnMouseButton(payload),
        MessageType.MouseWheel => OnMouseWheel(payload),
        MessageType.Key => OnKey(payload),
        // Ohne Antwort speichert der Host den Schlüssel nach kurzer Wartezeit; ein neu gestarteter Simulator liest ihn aus dessen Konfiguration.
        MessageType.KeyChange => "Neuer Schlüssel vom Host; der Simulator übernimmt ihn erst nach einem Neustart",
        // Die Zwischenablage gleicht der Simulator nicht ab, je Stück eine Zeile füllte nur das Protokoll;
        // die Anordnung ist nur für die Einstellungen des Notebooks, die Rückkehrkanten kommen getrennt.
        MessageType.Heartbeat or MessageType.ClipboardText or MessageType.Arrangement => null,
        _ => $"Unbekannte Nachricht {type} ({payload.Length} Bytes)",
    };

    private String OnSwitchToClient(Byte[] payload)
    {
        Active = true;
        SwitchToClient entry = SwitchToClient.FromPayload(payload);
        MoveTo(entry.X, entry.Y);
        return $"Steuerung übernommen, Eintritt bei ({_x}, {_y})";
    }

    private String OnReturnEdges(Byte[] payload)
    {
        _returnEdges = ReturnEdgeList.FromPayload(payload);
        return _returnEdges.Length == 0
            ? "Keine Rückkehrkanten"
            : "Rückkehrkanten: " + String.Join(", ", _returnEdges.Select(e => $"{e.Side} {e.Edge} [{e.From}, {e.To})"));
    }

    private String OnLockWorkstation()
    {
        Active = false;
        _pressed.Clear();
        return "Win+L auf dem Host: Ein echtes Notebook würde sich jetzt sperren";
    }

    private String OnSwitchToHost()
    {
        Active = false;
        return _pressed.Count == 0
            ? "Steuerung an den Host zurückgegeben"
            : $"Steuerung zurück, aber noch gedrückt: {String.Join(", ", _pressed)}";
    }

    private String? OnMouseMoveRelative(Byte[] payload)
    {
        MouseMoveRelative rel = MouseMoveRelative.FromPayload(payload);
        MoveTo(_x + rel.Dx, _y + rel.Dy);
        MoveCount++;
        return CheckExit(rel.Dx, rel.Dy);
    }

    private String? OnMouseMoveAbsolute(Byte[] payload)
    {
        MouseMoveAbsolute abs = MouseMoveAbsolute.FromPayload(payload);
        (Int32 ax, Int32 ay) = LayoutGeometry.DenormalizeFromVirtualDesktop(monitors, abs.X, abs.Y);
        (Int32 bx, Int32 by) = (_x, _y);
        MoveTo(ax, ay);
        MoveCount++;
        return CheckExit(_x - bx, _y - by);
    }

    private String OnMouseButton(Byte[] payload)
    {
        MouseButton button = MouseButton.FromPayload(payload);
        String name = $"Maus {(button.Button < _buttonNames.Length ? _buttonNames[button.Button] : button.Button.ToString())}";
        Toggle(name, button.State == ButtonState.Down);
        return $"{name} {(button.State == ButtonState.Down ? "gedrückt" : "losgelassen")} bei ({_x}, {_y})";
    }

    private static String OnMouseWheel(Byte[] payload)
    {
        MouseWheel wheel = MouseWheel.FromPayload(payload);
        return $"{(wheel.Horizontal ? "Querrad" : "Rad")} {wheel.Delta:+#;-#;0}";
    }

    private String OnKey(Byte[] payload)
    {
        KeyEvent key = KeyEvent.FromPayload(payload);
        String keyName = KeyInterop.KeyFromVirtualKey(key.VirtualKey).ToString();
        Toggle(keyName, key.State == ButtonState.Down);
        return $"Taste {keyName} {(key.State == ButtonState.Down ? "unten" : "oben")}"
            + $" (VK 0x{key.VirtualKey:X2}, Scan 0x{key.ScanCode:X2}{(key.Extended ? ", erweitert" : "")})";
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

    private String? CheckExit(Int32 dx, Int32 dy)
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
    private void MoveTo(Int32 x, Int32 y)
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

    private void Toggle(String name, Boolean down)
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
