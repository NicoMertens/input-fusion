using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Host;

/// <summary>
/// Durchstich-Logik für Phase 1: An der festen rechten Bildschirmkante wird in den Remote-Modus
/// gewechselt, danach parkt der Cursor und die Bewegung geht als Delta an den Client. Zurück geht
/// es durch Bewegen nach links über die Kante oder per Strg+Alt+F12.
/// Noch kein Multimonitor-Layout, kein TLS; das sind spätere Phasen.
/// </summary>
internal sealed class RemoteController(MessageConnection connection, bool absolute)
{
    private const ushort VkF12 = 0x7B;

    private int _edgeX, _screenWidth, _screenHeight;
    private int _parkX, _parkY;
    private double _virtualX, _virtualY;
    private bool _remote;
    private bool _ctrlDown, _altDown;

    public async Task RunAsync(CancellationToken ct)
    {
        _screenWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
        _screenHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
        _edgeX = _screenWidth - 1;

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

        int dx = move.X - _parkX;
        int dy = move.Y - _parkY;
        if (dx == 0 && dy == 0)
            return;

        _virtualX += dx;
        _virtualY += dy;
        if (_virtualX <= 0)
        {
            await ExitRemote(ct);
            return;
        }
        _virtualY = Math.Clamp(_virtualY, 0, _screenHeight);

        if (absolute)
        {
            ushort nx = (ushort)(Math.Clamp(_virtualX / _screenWidth, 0, 1) * ushort.MaxValue);
            ushort ny = (ushort)(Math.Clamp(_virtualY / _screenHeight, 0, 1) * ushort.MaxValue);
            await connection.SendAsync(MessageType.MouseMoveAbsolute, new MouseMoveAbsolute(nx, ny).ToPayload(), ct);
        }
        else
        {
            await connection.SendAsync(MessageType.MouseMoveRelative,
                new MouseMoveRelative((short)Math.Clamp(dx, short.MinValue, short.MaxValue),
                                      (short)Math.Clamp(dy, short.MinValue, short.MaxValue)).ToPayload(), ct);
        }

        PInvoke.SetCursorPos(_parkX, _parkY);
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
        _parkX = _edgeX - 2;
        _parkY = y;
        _virtualX = 100;
        _virtualY = y;
        _remote = true;
        LowLevelHooks.Swallow = true;
        PInvoke.SetCursorPos(_parkX, _parkY);
        Console.WriteLine("Remote-Modus an");

        ushort entryY = (ushort)(Math.Clamp((double)y / _screenHeight, 0, 1) * ushort.MaxValue);
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
