using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Spielt empfangene Eingaben lokal ab. Phase 1: direkt per <c>SendInput</c> aus dem Konsolenmodus.
/// Ab Phase 2 läuft derselbe Code im Injector in der Benutzersitzung.
/// </summary>
public sealed class InputPlayer
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // Was gerade gedrückt ist, damit sich beim Verbindungsabbruch alles lösen lässt (keine hängenden
    // Tasten). Einfädrig genutzt: ein Abspiel-Thread je Player.
    private readonly Dictionary<ushort, KeyEvent> _keysDown = new();
    private readonly HashSet<byte> _buttonsDown = new();

    public void MoveRelative(short dx, short dy) =>
        SendMouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE);

    /// <summary>Setzt den Cursor genau auf den Eintrittspunkt. Über <c>SendInput</c> absolut ginge das nur auf
    /// 0…65535 normiert und damit um einen Pixel ungenau, womöglich auf den Nachbarmonitor.</summary>
    public void SetCursor(int x, int y) => PInvoke.SetCursorPos(x, y);

    public void MoveAbsolute(ushort x, ushort y) =>
        SendMouse(x, y, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE
            | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE
            | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK);

    public void Button(MouseButton button)
    {
        bool down = button.State == ButtonState.Down;
        if (down)
            _buttonsDown.Add(button.Button);
        else
            _buttonsDown.Remove(button.Button);
        (MOUSE_EVENT_FLAGS flags, uint data) = button.Button switch
        {
            0 => (down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, 0u),
            1 => (down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, 0u),
            2 => (down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, 0u),
            3 => (down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 1u),
            _ => (down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 2u),
        };
        SendMouse(0, 0, data, flags);
    }

    public void Wheel(MouseWheel wheel) =>
        SendMouse(0, 0, (uint)wheel.Delta,
            wheel.Horizontal ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL : MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL);

    public void Key(KeyEvent key)
    {
        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE;
        if (key.State == ButtonState.Up)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        if (key.Extended)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;

        if (key.State == ButtonState.Up)
            _keysDown.Remove(key.VirtualKey);
        else
            _keysDown[key.VirtualKey] = key;

        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT
        {
            wVk = (VIRTUAL_KEY)key.VirtualKey,
            wScan = key.ScanCode,
            dwFlags = flags,
        };
        Send(input);
    }

    /// <summary>Lässt alle noch gedrückten Tasten und Maustasten los. Der Injector ruft das beim
    /// Verbindungsabbruch auf, damit auf dem Notebook nichts hängen bleibt.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _keysDown.Values.ToArray())
            Key(key with { State = ButtonState.Up });
        foreach (var button in _buttonsDown.ToArray())
            Button(new MouseButton(button, ButtonState.Up));
    }

    private void SendMouse(int dx, int dy, uint data, MOUSE_EVENT_FLAGS flags)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.Anonymous.mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags };
        Send(input);
    }

    private void Send(INPUT input)
    {
        Span<INPUT> inputs = [input];
        PInvoke.SendInput(inputs, InputSize);
    }
}
