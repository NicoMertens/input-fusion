using System.IO;
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
internal sealed class RemoteController(MessageConnection connection, bool absolute, bool observe, bool diag)
{
    private const ushort VkF11 = 0x7A;
    private const ushort VkF12 = 0x7B;

    private int _edgeX, _desktopWidth, _desktopHeight, _desktopLeft, _desktopTop;
    private int _parkX, _parkY, _lastX, _lastY, _margin;
    private double _virtualX, _virtualY;
    private volatile bool _remote;
    private bool _ctrlDown, _altDown;

    // Alle Tasten, die der Hook gerade als gedrückt sieht (down ohne up). Damit lässt sich beim Wechsel
    // der Modifier-Zustand des Hosts an den Client mitschicken. Wird nur auf dem Hook-Thread berührt.
    private readonly Dictionary<ushort, KeyInputEvent> _physicalDown = new();
    // Tasten, die als "down" an den Client gingen und dort noch gedrückt sind. Beim Verlassen werden sie
    // losgelassen, sonst bleiben sie auf der Gegenseite kleben. Hook-Thread und PumpIncoming greifen zu.
    private readonly Dictionary<ushort, KeyEvent> _clientDown = new();

    private int _recenters, _held, _maxDrift;
    private readonly string _diagPath = Path.Combine(Path.GetTempPath(), "inputfusion-diag.log");

    public async Task RunAsync(CancellationToken ct)
    {
        _desktopLeft = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        _desktopTop = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        _desktopWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        _desktopHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
        _edgeX = _desktopLeft + _desktopWidth - 1;
        _margin = Math.Min(_desktopWidth, _desktopHeight) / 4;
        Console.WriteLine($"Rechte Kante bei x={_edgeX} (Desktop {_desktopWidth}x{_desktopHeight} ab {_desktopLeft},{_desktopTop}).");
        if (diag)
            Console.WriteLine($"Diagnose aktiv, Protokoll in {_diagPath}. Maus an die rechte Kante bewegen.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var incoming = Task.Run(() => PumpIncoming(linked));

        try
        {
            await foreach (var e in LowLevelHooks.Events.ReadAllAsync(linked.Token))
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
                    await HandleKey(key, linked.Token);
                    break;
                }
            }
        }
        finally
        {
            LowLevelHooks.Swallow = false;
            linked.Cancel();
            await incoming;
        }
    }

    /// <summary>Liest die Gegenrichtung: Der Client meldet mit <see cref="MessageType.SwitchBackRequest"/>, dass er die Kante erreicht hat.</summary>
    private async Task PumpIncoming(CancellationTokenSource linked)
    {
        try
        {
            await foreach (var (type, _) in connection.ReceiveAsync(linked.Token))
            {
                if (type == MessageType.SwitchBackRequest && _remote)
                {
                    _remote = false;
                    LowLevelHooks.Swallow = false;
                    await ReleaseClientKeysAsync(linked.Token);
                    PInvoke.SetCursorPos(_edgeX - 5, _parkY);
                    Console.WriteLine("Remote-Modus aus (Client an der Kante)");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
        }
        finally
        {
            LowLevelHooks.Swallow = false;
            linked.Cancel();
        }
    }

    private async Task HandleMove(MouseMoveEvent move, CancellationToken ct)
    {
        if (!_remote)
        {
            if (move.X >= _edgeX)
                await EnterRemote(move.X, move.Y, ct);
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

        if (diag)
        {
            Measure(move);
            return;
        }

        // Im Beobachtungsmodus (z. B. über RDP) läuft die echte Maus frei; nicht zurücksetzen, sonst ruckelt es.
        // Sonst nur zurücksetzen, wenn der Cursor zu weit aus der Mitte läuft, damit er nicht am Rand ansteht.
        if (!observe && (Math.Abs(move.X - _parkX) > _margin || Math.Abs(move.Y - _parkY) > _margin))
        {
            PInvoke.SetCursorPos(_parkX, _parkY);
            _lastX = _parkX;
            _lastY = _parkY;
        }
    }

    /// <summary>
    /// Misst, wie oft SetCursorPos wirklich hält: wie weit der Cursor vor dem Zurücksetzen aus der Mitte
    /// gelaufen ist (Drift) und ob er direkt nach dem Zurücksetzen tatsächlich in der Mitte steht.
    /// Bei eingefrorener Maus bleibt die Drift klein; zieht RDP den Cursor weg, wird sie groß.
    /// </summary>
    private void Measure(MouseMoveEvent move)
    {
        int drift = Math.Max(Math.Abs(move.X - _parkX), Math.Abs(move.Y - _parkY));
        _maxDrift = Math.Max(_maxDrift, drift);

        PInvoke.SetCursorPos(_parkX, _parkY);
        PInvoke.GetCursorPos(out var after);
        bool held = Math.Abs(after.X - _parkX) <= 2 && Math.Abs(after.Y - _parkY) <= 2;
        if (held)
            _held++;
        _recenters++;
        _lastX = _parkX;
        _lastY = _parkY;

        if (_recenters % 60 == 0)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  SetCursorPos direkt gehalten: {_held}/{_recenters} "
                + $"({100 * _held / _recenters} %), groesste Drift aus der Mitte: {_maxDrift}px";
            Console.WriteLine(line);
            try { File.AppendAllText(_diagPath, line + Environment.NewLine); } catch (IOException) { }
            _maxDrift = 0;
        }
    }

    private async Task HandleKey(KeyInputEvent key, CancellationToken ct)
    {
        TrackModifier(key);
        if (key.Down)
            _physicalDown[key.VirtualKey] = key;
        else
            _physicalDown.Remove(key.VirtualKey);

        if (key.Down && _ctrlDown && _altDown && key.VirtualKey == VkF12 && _remote)
        {
            await ExitRemote(ct);
            return;
        }

        // Strg+Alt+F11: Remote an der aktuellen Cursorposition ein-/ausschalten. Zum Testen der Bewegung,
        // wenn das Parken nicht greift (RDP): Maus vorher an den linken Rand setzen, dann bleibt Platz nach rechts.
        if (key.Down && _ctrlDown && _altDown && key.VirtualKey == VkF11)
        {
            if (_remote)
            {
                await ExitRemote(ct);
            }
            else
            {
                PInvoke.GetCursorPos(out var p);
                await EnterRemote(p.X, p.Y, ct);
            }
            return;
        }

        if (_remote)
        {
            var ev = new KeyEvent(key.VirtualKey, key.ScanCode, key.Down ? ButtonState.Down : ButtonState.Up, key.Extended);
            lock (_clientDown)
            {
                if (key.Down)
                    _clientDown[key.VirtualKey] = ev;
                else
                    _clientDown.Remove(key.VirtualKey);
            }
            await connection.SendAsync(MessageType.Key, ev.ToPayload(), ct);
        }
    }

    /// <summary>Lässt auf dem Client alle Tasten los, die noch als gedrückt gelten. Wird beim Verlassen des
    /// Remote-Modus aufgerufen, damit Modifier (etwa das Strg+Alt des Austritts-Hotkeys) nicht kleben bleiben.</summary>
    private async Task ReleaseClientKeysAsync(CancellationToken ct)
    {
        KeyEvent[] ups;
        lock (_clientDown)
        {
            if (_clientDown.Count == 0)
                return;
            ups = new KeyEvent[_clientDown.Count];
            int i = 0;
            foreach (var ev in _clientDown.Values)
                ups[i++] = ev with { State = ButtonState.Up };
            _clientDown.Clear();
        }
        foreach (var up in ups)
            await connection.SendAsync(MessageType.Key, up.ToPayload(), ct);
    }

    private static bool IsModifier(ushort vk) => vk is
        0x10 or 0xA0 or 0xA1 or   // Umschalt (Shift)
        0x11 or 0xA2 or 0xA3 or   // Strg (Control)
        0x12 or 0xA4 or 0xA5 or   // Alt (Menu)
        0x5B or 0x5C;             // Windows

    private void TrackModifier(KeyInputEvent key)
    {
        bool isCtrl = key.VirtualKey is 0x11 or 0xA2 or 0xA3;
        bool isAlt = key.VirtualKey is 0x12 or 0xA4 or 0xA5;
        if (isCtrl) _ctrlDown = key.Down;
        if (isAlt) _altDown = key.Down;
    }

    private async Task EnterRemote(int x, int y, CancellationToken ct)
    {
        _parkX = _desktopLeft + _desktopWidth / 2;
        _parkY = _desktopTop + _desktopHeight / 2;
        _virtualX = 0;
        _virtualY = Math.Clamp(y - _desktopTop, 0, _desktopHeight);
        _remote = true;

        if (observe)
        {
            // Maus nicht kapern, nur die Deltas verfolgen. Zum Testen an den linken Rand setzen, damit
            // nach rechts die volle Breite bleibt. Über RDP hält das evtl. nicht, dann hilft Strg+Alt+F11.
            int startX = _desktopLeft + 2;
            PInvoke.SetCursorPos(startX, y);
            _lastX = startX;
            _lastY = y;
        }
        else
        {
            _lastX = _parkX;
            _lastY = _parkY;
            LowLevelHooks.Swallow = true;
            PInvoke.SetCursorPos(_parkX, _parkY);
        }
        Console.WriteLine($"Remote-Modus an{(observe ? " (Beobachtung)" : "")} (Strg+Alt+F12 zurueck)");

        ushort entryY = (ushort)(_virtualY / _desktopHeight * ushort.MaxValue);
        await connection.SendAsync(MessageType.SwitchToClient, new SwitchToClient(entryY).ToPayload(), ct);

        // Modifier, die beim Übertreten gehalten werden (z. B. Umschalt beim Ziehen über die Kante, oder das
        // Strg+Alt des F11-Hotkeys), an den Client mitschicken, damit der Zustand drüben stimmt.
        foreach (var (vk, k) in _physicalDown)
        {
            if (!IsModifier(vk))
                continue;
            var down = new KeyEvent(k.VirtualKey, k.ScanCode, ButtonState.Down, k.Extended);
            lock (_clientDown)
                _clientDown[vk] = down;
            await connection.SendAsync(MessageType.Key, down.ToPayload(), ct);
        }
    }

    private async Task ExitRemote(CancellationToken ct)
    {
        _remote = false;
        LowLevelHooks.Swallow = false;
        await ReleaseClientKeysAsync(ct);
        PInvoke.SetCursorPos(_edgeX - 5, _parkY);
        Console.WriteLine("Remote-Modus aus");
        await connection.SendAsync(MessageType.SwitchToHost, ReadOnlyMemory<byte>.Empty, ct);
    }
}
