using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Host;

/// <summary>
/// Durchstich-Logik für Phase 1: An der rechten Kante des gesamten Monitorverbunds wird in den
/// Remote-Modus gewechselt. Danach parkt der Cursor in der Mitte des Hauptmonitors und wird nach
/// jeder Bewegung dorthin zurückgesetzt, damit er nicht am Desktop-Rand ansteht und weiter Deltas
/// liefert. Zurück nur per Strg+Alt+F12 oder Verbindungsabbruch; das layoutabhängige Austreten an
/// der Kante kommt mit Phase 3. Kein TLS; das ist Phase 4.
/// </summary>
internal sealed class RemoteController(MessageConnection connection, bool absolute)
{
    private const ushort VkF12 = 0x7B;

    private int _edgeX, _desktopWidth, _desktopHeight, _desktopLeft, _desktopTop;
    private int _parkX, _parkY, _lastX, _lastY, _margin;
    private double _virtualX, _virtualY;
    private bool _remote;
    private bool _ctrlDown, _altDown;

    public async Task RunAsync(CancellationToken ct)
    {
        _desktopLeft = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        _desktopTop = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        _desktopWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        _desktopHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
        _edgeX = _desktopLeft + _desktopWidth - 1;
        _margin = Math.Min(_desktopWidth, _desktopHeight) / 4;
        Console.WriteLine($"Rechte Kante bei x={_edgeX} (Desktop {_desktopWidth}x{_desktopHeight} ab {_desktopLeft},{_desktopTop}).");

        await foreach (var e in LowLevelHooks.Events.ReadAllAsync(ct))
        {
            switch (e)
            {
                case MouseMoveEvent move:
                    await HandleMove(move, ct);
                    break;
                case MouseButtonEvent button when _remote:
                    await connection.SendAsync(MessageType.MouseButton,
                        new MouseButton(button.Button, button.Down ? ButtonState.Down : ButtonState.Up).ToPayload(), ct);
                    break;
                case MouseWheelEvent wheel when _remote:
                    await connection.SendAsync(MessageType.MouseWheel,
                        new MouseWheel(wheel.Delta, wheel.Horizontal).ToPayload(), ct);
                    break;
                case KeyInputEvent key:
                    await HandleKey(key, ct);
                    break;
            }
        }
    }

    private async Task HandleMove(MouseMoveEvent move, CancellationToken ct)
    {
        if (!_remote)
        {
            if (move.X >= _edgeX)
                await EnterRemote(move.Y, ct);
            return;
        }

        // Delta zum letzten Punkt, nicht zur Mitte: Während die Eingabe verschluckt wird, friert der Cursor
        // in der Mitte ein, aber die gemeldete Position summiert alle Bewegungen auf. Gegen die Mitte zu
        // rechnen würde deshalb doppelt zählen.
        int dx = move.X - _lastX;
        int dy = move.Y - _lastY;
        _lastX = move.X;
        _lastY = move.Y;
        if (dx == 0 && dy == 0)
            return;

        if (absolute)
        {
            _virtualX = Math.Clamp(_virtualX + dx, 0, _desktopWidth);
            _virtualY = Math.Clamp(_virtualY + dy, 0, _desktopHeight);
            ushort nx = (ushort)(_virtualX / _desktopWidth * ushort.MaxValue);
            ushort ny = (ushort)(_virtualY / _desktopHeight * ushort.MaxValue);
            await connection.SendAsync(MessageType.MouseMoveAbsolute, new MouseMoveAbsolute(nx, ny).ToPayload(), ct);
        }
        else
        {
            await connection.SendAsync(MessageType.MouseMoveRelative,
                new MouseMoveRelative((short)Math.Clamp(dx, short.MinValue, short.MaxValue),
                                      (short)Math.Clamp(dy, short.MinValue, short.MaxValue)).ToPayload(), ct);
        }

        // Nur zurücksetzen, wenn der Cursor zu weit aus der Mitte läuft, sonst stünde er bald am Rand an.
        if (Math.Abs(move.X - _parkX) > _margin || Math.Abs(move.Y - _parkY) > _margin)
        {
            PInvoke.SetCursorPos(_parkX, _parkY);
            _lastX = _parkX;
            _lastY = _parkY;
        }
    }

    private async Task HandleKey(KeyInputEvent key, CancellationToken ct)
    {
        TrackModifier(key);

        if (_remote && key.Down && key.VirtualKey == VkF12 && _ctrlDown && _altDown)
        {
            await ExitRemote(ct);
            return;
        }

        if (_remote)
            await connection.SendAsync(MessageType.Key,
                new KeyEvent(key.VirtualKey, key.ScanCode, key.Down ? ButtonState.Down : ButtonState.Up, key.Extended).ToPayload(), ct);
    }

    private void TrackModifier(KeyInputEvent key)
    {
        bool isCtrl = key.VirtualKey is 0x11 or 0xA2 or 0xA3;
        bool isAlt = key.VirtualKey is 0x12 or 0xA4 or 0xA5;
        if (isCtrl) _ctrlDown = key.Down;
        if (isAlt) _altDown = key.Down;
    }

    private async Task EnterRemote(int y, CancellationToken ct)
    {
        _parkX = _desktopLeft + _desktopWidth / 2;
        _parkY = _desktopTop + _desktopHeight / 2;
        _lastX = _parkX;
        _lastY = _parkY;
        _virtualX = 0;
        _virtualY = Math.Clamp(y - _desktopTop, 0, _desktopHeight);
        _remote = true;
        LowLevelHooks.Swallow = true;
        PInvoke.SetCursorPos(_parkX, _parkY);
        Console.WriteLine("Remote-Modus an (Strg+Alt+F12 zurueck)");

        ushort entryY = (ushort)(_virtualY / _desktopHeight * ushort.MaxValue);
        await connection.SendAsync(MessageType.SwitchToClient, new SwitchToClient(entryY).ToPayload(), ct);
    }

    private async Task ExitRemote(CancellationToken ct)
    {
        _remote = false;
        LowLevelHooks.Swallow = false;
        PInvoke.SetCursorPos(_edgeX - 5, _parkY);
        Console.WriteLine("Remote-Modus aus");
        await connection.SendAsync(MessageType.SwitchToHost, ReadOnlyMemory<byte>.Empty, ct);
    }
}
