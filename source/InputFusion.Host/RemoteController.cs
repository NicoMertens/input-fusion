using System.IO;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Layout;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;
using System.Drawing;

namespace InputFusion.Host;

/// <summary>
/// Steuert eine Verbindung: Überschreitet die Maus eine Übergangskante aus dem <see cref="MonitorLayout"/>, wird in
/// den Remote-Modus gewechselt. Danach parkt der Cursor in der Mitte des virtuellen Desktops und bleibt dort
/// eingefroren, weil der Hook die Eingabe verschluckt; die Bewegung kommt dann aus Raw Input. Zurück über eine
/// Rückkehrkante des Clients, per Strg+Alt+F12 oder bei Verbindungsabbruch. Kein TLS; das ist Phase 4.
/// </summary>
internal sealed class RemoteController(MessageConnection connection, HostState state, bool absolute, bool observe, bool diag)
{
    private const ushort VkF11 = 0x7A;
    private const ushort VkF12 = 0x7B;

    private int _desktopWidth, _desktopHeight, _desktopLeft, _desktopTop;
    private int _parkX, _parkY;
    // Wo der Host-Cursor beim Verlassen stand; dorthin kehrt er bei Strg+Alt+F12 oder Verbindungsabbruch zurück.
    private int _returnX, _returnY;
    private double _virtualX, _virtualY;
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
        if (diag)
        {
            Log.Write($"Diagnose aktiv, Protokoll in {_diagPath}. Maus über eine Übergangskante bewegen.");
        }

        // Alles läuft über ein gemeinsames Token: Endet die Verbindung oder schweigt der Client, bricht das
        // auch ein Senden ab, das im vollen Puffer hängt. Sonst blieben die Eingaben verschluckt.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = linked.Token;
        Action<MonitorLayout?> layoutChanged = layout => _ = SendReturnEdgesAsync(layout, token);
        state.LayoutChanged += layoutChanged;
        Task incoming = Task.Run(() => PumpIncoming(linked));
        Task monitor = WatchClient(linked);

        try
        {
            await foreach (InputEvent e in LowLevelHooks.Events.ReadAllAsync(token))
            {
                switch (e)
                {
                    case MouseMoveEvent move:
                        await HandleMove(move, token);
                        break;
                    case RawMouseMoveEvent raw when state.IsRemote && !observe:
                        await SendMove(raw.Dx, raw.Dy, token);
                        break;
                    case MouseButtonEvent button when state.IsRemote:
                        await connection.SendAsync(MessageType.MouseButton,
                            new MouseButton(button.Button, button.Down ? ButtonState.Down : ButtonState.Up).ToPayload(), token);
                        break;
                    case MouseWheelEvent wheel when state.IsRemote:
                        await connection.SendAsync(MessageType.MouseWheel,
                            new MouseWheel(wheel.Delta, wheel.Horizontal).ToPayload(), token);
                        break;
                    case KeyInputEvent key:
                        await HandleKey(key, token);
                        break;
                }
            }
        }
        finally
        {
            state.LayoutChanged -= layoutChanged;
            LowLevelHooks.Swallow = false;
            // Brach die Verbindung im Remote-Modus ab, die Maus dort zurückgeben, wo sie den Host verlassen hat.
            if (state.IsRemote)
            {
                state.IsRemote = false;
                PInvoke.SetCursorPos(_returnX, _returnY);
            }
            linked.Cancel();
            await incoming;
            await monitor;
        }
    }

    /// <summary>Schickt dem Client die Kanten, über die er zurückkehrt. Ohne Layout keine, dann bleibt nur Strg+Alt+F12.</summary>
    private async Task SendReturnEdgesAsync(MonitorLayout? layout, CancellationToken ct)
    {
        try
        {
            await connection.SendAsync(MessageType.ReturnEdges, ReturnEdgeList.ToPayload(layout?.ReturnEdges ?? []), ct);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Heartbeat zum Client; schweigt er länger als erlaubt (Standby, Netz weg), endet die Verbindung.</summary>
    private async Task WatchClient(CancellationTokenSource linked)
    {
        if (await connection.MonitorAsync(linked.Token))
        {
            Log.Write($"Client antwortet seit {MessageConnection.HeartbeatTimeout.TotalSeconds:0} s nicht mehr, trenne.");
            LowLevelHooks.Swallow = false;
            linked.Cancel();
        }
    }

    /// <summary>Liest die Gegenrichtung: die Monitore des Clients und den Austritt über eine Rückkehrkante.</summary>
    private async Task PumpIncoming(CancellationTokenSource linked)
    {
        try
        {
            await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(linked.Token))
            {
                if (type == MessageType.Monitors)
                {
                    state.ClientMonitorsReported(MonitorList.FromPayload(payload));
                }
                else if (type == MessageType.SwitchBackRequest && state.IsRemote)
                {
                    state.IsRemote = false;
                    LowLevelHooks.Swallow = false;
                    await ReleaseClientKeysAsync(linked.Token);
                    SwitchBackRequest request = SwitchBackRequest.FromPayload(payload);
                    (int x, int y) = state.Layout is { } layout && layout.TryReturn(request.Side, request.X, request.Y, out (int X, int Y) point)
                        ? point
                        : (_returnX, _returnY);
                    PInvoke.SetCursorPos(x, y);
                    Log.Write($"Remote-Modus aus (Client an der Kante {request.Side}), zurück bei ({x}, {y})");
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
        if (!state.IsRemote)
        {
            // Ohne gemeldete Client-Monitore gibt es kein Layout und damit keinen Übergang.
            if (state.Layout is { } layout && layout.TryLeaveHost(move.X, move.Y, move.Dx, move.Dy, out _, out (int X, int Y) entry))
            {
                await EnterRemote(entry, move.X - move.Dx, move.Y - move.Dy, ct);
            }

            return;
        }

        // Im Beobachtungsmodus läuft der Cursor frei, die Bewegung aus dem Hook stimmt.
        if (observe)
        {
            await SendMove(move.Dx, move.Dy, ct);
            return;
        }

        // Beim Kapern kommt die Bewegung aus Raw Input (siehe RawMouse), der Hook verschluckt nur.
        // Der Cursor steht dabei nicht sicher im Parkpunkt: Eine Bewegung, die der Hook kurz vor dem
        // Umschalten noch durchgelassen hat, kann Windows erst nach dem SetCursorPos umsetzen. Dann friert
        // er am Rand ein. Deshalb hier zurück in die Mitte holen.
        if (diag)
        {
            Measure(move);
        }
        else if (move.X - move.Dx != _parkX || move.Y - move.Dy != _parkY)
        {
            PInvoke.SetCursorPos(_parkX, _parkY);
        }
    }

    private async Task SendMove(int dx, int dy, CancellationToken ct)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

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
        PInvoke.GetCursorPos(out Point after);
        bool held = Math.Abs(after.X - _parkX) <= 2 && Math.Abs(after.Y - _parkY) <= 2;
        if (held)
        {
            _held++;
        }

        _recenters++;

        if (_recenters % 60 == 0)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  SetCursorPos direkt gehalten: {_held}/{_recenters} "
                + $"({100 * _held / _recenters} %), groesste Drift aus der Mitte: {_maxDrift}px";
            Log.Write(line);
            try { File.AppendAllText(_diagPath, line + Environment.NewLine); } catch (IOException) { }
            _maxDrift = 0;
        }
    }

    private async Task HandleKey(KeyInputEvent key, CancellationToken ct)
    {
        TrackModifier(key);
        if (key.Down)
        {
            _physicalDown[key.VirtualKey] = key;
        }
        else
        {
            _physicalDown.Remove(key.VirtualKey);
        }

        if (key.Down && _ctrlDown && _altDown && key.VirtualKey == VkF12 && state.IsRemote)
        {
            await ExitRemote(ct);
            return;
        }

        // Strg+Alt+F11: Remote ein-/ausschalten, ohne die Kante zu treffen. Zum Testen, wenn das Parken nicht
        // greift (RDP): Der Client-Cursor erscheint in der Mitte des ersten Übergangs.
        if (key.Down && _ctrlDown && _altDown && key.VirtualKey == VkF11)
        {
            if (state.IsRemote)
            {
                await ExitRemote(ct);
            }
            else if (state.Layout is { Transitions.Count: > 0 } layout)
            {
                EdgeTransition t = layout.Transitions[0];
                PInvoke.GetCursorPos(out Point p);
                await EnterRemote(layout.EntryPoint(t, (t.From + t.To) / 2), p.X, p.Y, ct);
            }
            else
            {
                Log.Write("Strg+Alt+F11: noch kein Layout, der Client hat seine Monitore nicht gemeldet.");
            }
            return;
        }

        if (state.IsRemote)
        {
            KeyEvent ev = new KeyEvent(key.VirtualKey, key.ScanCode, key.Down ? ButtonState.Down : ButtonState.Up, key.Extended);
            lock (_clientDown)
            {
                if (key.Down)
                {
                    _clientDown[key.VirtualKey] = ev;
                }
                else
                {
                    _clientDown.Remove(key.VirtualKey);
                }
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
            {
                return;
            }

            ups = new KeyEvent[_clientDown.Count];
            int i = 0;
            foreach (KeyEvent ev in _clientDown.Values)
            {
                ups[i++] = ev with { State = ButtonState.Up };
            }

            _clientDown.Clear();
        }
        foreach (KeyEvent up in ups)
        {
            await connection.SendAsync(MessageType.Key, up.ToPayload(), ct);
        }
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
        if (isCtrl)
        {
            _ctrlDown = key.Down;
        }

        if (isAlt)
        {
            _altDown = key.Down;
        }
    }

    /// <summary>Wechselt auf den Client. <paramref name="entry"/> ist der Eintrittspunkt in dessen Koordinaten,
    /// (<paramref name="hostX"/>, <paramref name="hostY"/>) die Stelle, an der der Host-Cursor zuletzt stand.</summary>
    private async Task EnterRemote((int X, int Y) entry, int hostX, int hostY, CancellationToken ct)
    {
        _parkX = _desktopLeft + _desktopWidth / 2;
        _parkY = _desktopTop + _desktopHeight / 2;
        (_returnX, _returnY) = (hostX, hostY);
        // Für den absoluten Vergleichsmodus: Startpunkt anteilig auf die Fläche des Hosts übertragen.
        if (state.Layout is { } layout)
        {
            (ushort nx, ushort ny) = LayoutGeometry.NormalizeToVirtualDesktop(layout.ClientMonitors, entry.X, entry.Y);
            _virtualX = (double)nx / ushort.MaxValue * _desktopWidth;
            _virtualY = (double)ny / ushort.MaxValue * _desktopHeight;
        }
        state.IsRemote = true;

        if (observe)
        {
            // Maus nicht kapern, nur die Deltas verfolgen. Zum Testen in die Mitte setzen, damit in jede Richtung
            // Platz bleibt. Über RDP hält das evtl. nicht, dann hilft Strg+Alt+F11.
            PInvoke.SetCursorPos(_parkX, _parkY);
        }
        else
        {
            LowLevelHooks.Swallow = true;
            PInvoke.SetCursorPos(_parkX, _parkY);
        }
        Log.Write($"Remote-Modus an{(observe ? " (Beobachtung)" : "")}, Eintritt beim Client bei ({entry.X}, {entry.Y})");

        await connection.SendAsync(MessageType.SwitchToClient, new SwitchToClient(entry.X, entry.Y).ToPayload(), ct);

        // Modifier, die beim Übertreten gehalten werden (z. B. Umschalt beim Ziehen über die Kante, oder das
        // Strg+Alt des F11-Hotkeys), an den Client mitschicken, damit der Zustand drüben stimmt.
        foreach ((ushort vk, KeyInputEvent k) in _physicalDown)
        {
            if (!IsModifier(vk))
            {
                continue;
            }

            KeyEvent down = new KeyEvent(k.VirtualKey, k.ScanCode, ButtonState.Down, k.Extended);
            lock (_clientDown)
            {
                _clientDown[vk] = down;
            }

            await connection.SendAsync(MessageType.Key, down.ToPayload(), ct);
        }
    }

    private async Task ExitRemote(CancellationToken ct)
    {
        state.IsRemote = false;
        LowLevelHooks.Swallow = false;
        await ReleaseClientKeysAsync(ct);
        PInvoke.SetCursorPos(_returnX, _returnY);
        Log.Write("Remote-Modus aus");
        await connection.SendAsync(MessageType.SwitchToHost, ReadOnlyMemory<byte>.Empty, ct);
    }
}
