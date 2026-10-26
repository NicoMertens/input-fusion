using System.IO;
using System.Text;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Layout;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;
using InputFusion.Core.Shell;
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
    private const ushort VkL = 0x4C;
    private const ushort VkLeftWin = 0x5B;
    private const ushort VkRightWin = 0x5C;

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
    // Dasselbe für Maustasten, unter derselben Sperre wie _clientDown.
    private readonly HashSet<byte> _clientButtonsDown = new();
    // Maustasten, die der Hook gerade als gedrückt sieht. Solange eine gehalten wird, wechselt die Maus nicht auf den
    // Client: Das Loslassen käme dort an, und auf dem PC bliebe die Taste gedrückt.
    private readonly HashSet<byte> _physicalButtonsDown = new();

    private readonly ClipboardSync _clipboard = new(Log.Write);

    private int _recenters, _held, _maxDrift;
    private readonly string _diagPath = Path.Combine(Path.GetTempPath(), "inputfusion-diag.log");

    private string? _endReason;

    /// <summary>Warum die Verbindung endete, als Satz für die Benachrichtigung; null, wenn der Grund nicht bekannt ist.</summary>
    public string? EndReason => Volatile.Read(ref _endReason);

    /// <summary>Die Maus war beim Ende der Verbindung auf dem Notebook und ist zurück auf den PC gesprungen.</summary>
    public bool MouseReturned { get; private set; }

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
        Action<MonitorLayout?> layoutChanged = layout => _ = SendLayoutAsync(layout, token);
        state.LayoutChanged += layoutChanged;
        Action<string> keyTransfer = key => _ = SendKeyAsync(key, token);
        state.KeyTransferRequested += keyTransfer;
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
                    case MouseButtonEvent button:
                        await HandleButton(button, token);
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
            state.KeyTransferRequested -= keyTransfer;
            LowLevelHooks.Swallow = false;
            // Brach die Verbindung im Remote-Modus ab, die Maus dort zurückgeben, wo sie den Host verlassen hat.
            if (state.IsRemote)
            {
                state.IsRemote = false;
                PInvoke.SetCursorPos(_returnX, _returnY);
                MouseReturned = true;
            }
            linked.Cancel();
            await incoming;
            await monitor;
        }
    }

    /// <summary>
    /// Schickt dem Client die Kanten, über die er zurückkehrt, und danach die Anordnung für seine Einstellungen. Ohne
    /// Layout keine Kanten, dann bleibt nur Strg+Alt+F12. Ältere Clients übergehen die Anordnung.
    /// </summary>
    private async Task SendLayoutAsync(MonitorLayout? layout, CancellationToken ct)
    {
        try
        {
            await connection.SendAsync(MessageType.ReturnEdges, ReturnEdgeList.ToPayload(layout?.ReturnEdges ?? []), ct);
            await connection.SendAsync(MessageType.Arrangement, ClientArrangement.From(layout).ToPayload(), ct);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Schickt dem Client den neuen Schlüssel; verschlüsselt mit dem alten, unter dem die Verbindung steht.</summary>
    private async Task SendKeyAsync(string key, CancellationToken ct)
    {
        try
        {
            await connection.SendAsync(MessageType.KeyChange, Encoding.UTF8.GetBytes(key), ct);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Die Verbindung ist weg; HostState speichert den Schlüssel beim Abbruch trotzdem.
        }
    }

    /// <summary>
    /// Schickt dem Client den Text der Zwischenablage, wenn er sich seit dem letzten Abgleich geändert hat. Nebenher,
    /// damit Lesen und Senden den Wechsel nicht aufhalten; die Stücke reihen sich zwischen die Eingaben.
    /// </summary>
    private async Task SendClipboardAsync(CancellationToken ct)
    {
        try
        {
            foreach (byte[] chunk in _clipboard.TakeChanged())
            {
                await connection.SendAsync(MessageType.ClipboardText, chunk, ct);
            }
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
            SetEndReason($"Das Notebook antwortet seit {MessageConnection.HeartbeatTimeout.TotalSeconds:0} s nicht mehr (Standby oder Netz weg).");
            LowLevelHooks.Swallow = false;
            linked.Cancel();
        }
    }

    /// <summary>Liest die Gegenrichtung: Monitore und Angaben des Clients, den Austritt über eine Rückkehrkante und den Text seiner Zwischenablage.</summary>
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
                else if (type == MessageType.ClipboardText)
                {
                    _clipboard.Receive(payload);
                }
                else if (type == MessageType.ClientInfo && ClientInfo.FromPayload(payload) is { } info)
                {
                    state.ClientInfoReported(info);
                }
                else if (type == MessageType.KeyChangeResult)
                {
                    state.KeyTransferred(payload.Length == 0 ? null : Encoding.UTF8.GetString(payload));
                }
                else if (type == MessageType.SwitchBackRequest && state.IsRemote)
                {
                    state.IsRemote = false;
                    LowLevelHooks.Swallow = false;
                    await ReleaseClientInputAsync(linked.Token);
                    SwitchBackRequest request = SwitchBackRequest.FromPayload(payload);
                    (int x, int y) = state.Layout is { } layout && layout.TryReturn(request.Side, request.X, request.Y, out (int X, int Y) point)
                        ? point
                        : (_returnX, _returnY);
                    PInvoke.SetCursorPos(x, y);
                    Log.Write($"Remote-Modus aus (Client an der Kante {request.Side}), zurück bei ({x}, {y})");
                }
            }

            if (!linked.IsCancellationRequested)
            {
                SetEndReason("Das Notebook hat die Verbindung getrennt.");
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            SetEndReason($"Verbindungsfehler: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            LowLevelHooks.Swallow = false;
            linked.Cancel();
        }
    }

    /// <summary>Der erste Grund zählt; was danach scheitert, ist nur die Folge.</summary>
    private void SetEndReason(string reason) => Interlocked.CompareExchange(ref _endReason, reason, null);

    private async Task HandleMove(MouseMoveEvent move, CancellationToken ct)
    {
        if (!state.IsRemote)
        {
            // Ohne gemeldete Client-Monitore gibt es kein Layout und damit keinen Übergang. Mit gehaltener Maustaste,
            // etwa beim Ziehen eines Fensters an den Rand, bleibt die Maus auf dem PC.
            if (_physicalButtonsDown.Count == 0 && state.Layout is { } layout && layout.TryLeaveHost(move.X, move.Y, move.Dx, move.Dy, out _, out (int X, int Y) entry))
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

    private async Task HandleButton(MouseButtonEvent button, CancellationToken ct)
    {
        if (button.Down)
        {
            _physicalButtonsDown.Add(button.Button);
        }
        else
        {
            _physicalButtonsDown.Remove(button.Button);
        }

        if (!state.IsRemote)
        {
            return;
        }

        lock (_clientDown)
        {
            if (button.Down)
            {
                _clientButtonsDown.Add(button.Button);
            }
            else
            {
                _clientButtonsDown.Remove(button.Button);
            }
        }
        await connection.SendAsync(MessageType.MouseButton,
            new MouseButton(button.Button, button.Down ? ButtonState.Down : ButtonState.Up).ToPayload(), ct);
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

        // Win+L sperrt den PC immer; ein Low-Level-Hook sieht die Tasten, kann es aber nicht verhindern. Das Notebook
        // sperrt sich mit, auch wenn die Maus gerade auf dem PC ist.
        if (key.Down && key.VirtualKey == VkL && (_physicalDown.ContainsKey(VkLeftWin) || _physicalDown.ContainsKey(VkRightWin)))
        {
            await LockBothAsync(ct);
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

    /// <summary>
    /// Lässt auf dem Client alle Tasten und Maustasten los, die noch als gedrückt gelten. Wird beim Verlassen des
    /// Remote-Modus aufgerufen, damit Modifier (etwa das Strg+Alt des Austritts-Hotkeys) nicht kleben bleiben und ein
    /// Ziehen auf dem Notebook nicht weiterläuft, wenn die Maus schon wieder auf dem PC ist.
    /// </summary>
    private async Task ReleaseClientInputAsync(CancellationToken ct)
    {
        KeyEvent[] ups;
        byte[] buttons;
        lock (_clientDown)
        {
            ups = _clientDown.Values.Select(ev => ev with { State = ButtonState.Up }).ToArray();
            buttons = _clientButtonsDown.ToArray();
            _clientDown.Clear();
            _clientButtonsDown.Clear();
        }
        foreach (KeyEvent up in ups)
        {
            await connection.SendAsync(MessageType.Key, up.ToPayload(), ct);
        }

        foreach (byte button in buttons)
        {
            await connection.SendAsync(MessageType.MouseButton, new MouseButton(button, ButtonState.Up).ToPayload(), ct);
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
        _ = Task.Run(() => SendClipboardAsync(ct), CancellationToken.None);

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

    /// <summary>
    /// Verlässt den Remote-Modus, damit die Windows-Taste auf dem Notebook nicht gedrückt hängen bleibt, und lässt das
    /// Notebook sich sperren. Auf dem Sperrbildschirm des PCs sieht der Hook die Tasten nicht mehr los gehen; deshalb
    /// hier vergessen, sonst sperrte nach dem Entsperren schon das nächste L.
    /// </summary>
    private async Task LockBothAsync(CancellationToken ct)
    {
        if (state.IsRemote)
        {
            await ExitRemote(ct);
        }

        _physicalDown.Remove(VkLeftWin);
        _physicalDown.Remove(VkRightWin);
        _physicalDown.Remove(VkL);
        Log.Write("Win+L: sperre auch das Notebook.");
        await connection.SendAsync(MessageType.LockWorkstation, ReadOnlyMemory<byte>.Empty, ct);
    }

    private async Task ExitRemote(CancellationToken ct)
    {
        state.IsRemote = false;
        LowLevelHooks.Swallow = false;
        await ReleaseClientInputAsync(ct);
        PInvoke.SetCursorPos(_returnX, _returnY);
        Log.Write("Remote-Modus aus");
        await connection.SendAsync(MessageType.SwitchToHost, ReadOnlyMemory<byte>.Empty, ct);
    }
}
