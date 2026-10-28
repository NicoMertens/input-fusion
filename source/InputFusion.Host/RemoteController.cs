using System.IO;
using System.Text;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using NC2.InputFusion.Core.Layout;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;
using NC2.InputFusion.Core.Shell;
using System.Drawing;

namespace NC2.InputFusion.Host;

/// <summary>
/// Steuert eine Verbindung: Überschreitet die Maus eine Übergangskante aus dem <see cref="MonitorLayout"/>, wird in
/// den Remote-Modus gewechselt. Danach parkt der Cursor in der Mitte des virtuellen Desktops und bleibt dort
/// eingefroren, weil der Hook die Eingabe verschluckt; die Bewegung kommt dann aus Raw Input. Zurück über eine
/// Rückkehrkante des Clients, per Strg+Alt+F12 oder bei Verbindungsabbruch. Kein TLS; das ist Phase 4.
/// </summary>
internal sealed class RemoteController(MessageConnection connection, HostState state, HostOptions options)
{
    private const UInt16 VkF11 = 0x7A;
    private const UInt16 VkF12 = 0x7B;

    private Int32 _desktopWidth, _desktopHeight, _desktopLeft, _desktopTop;
    private Int32 _parkX, _parkY;
    // Wo der Host-Cursor beim Verlassen stand; dorthin kehrt er bei Strg+Alt+F12 oder Verbindungsabbruch zurück.
    private Int32 _returnX, _returnY;
    private Double _virtualX, _virtualY;

    private readonly PressedInput _pressed = new();
    private readonly ClipboardSync _clipboard = new(Log.Write);
    private readonly ParkDiagnostics? _parkDiagnostics = options.Diag ? new ParkDiagnostics() : null;

    private String? _endReason;

    /// <summary>Warum die Verbindung endete, als Satz für die Benachrichtigung; null, wenn der Grund nicht bekannt ist.</summary>
    public String? EndReason => Volatile.Read(ref _endReason);

    /// <summary>Die Maus war beim Ende der Verbindung auf dem Notebook und ist zurück auf den PC gesprungen.</summary>
    public Boolean MouseReturned { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        _desktopLeft = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        _desktopTop = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        _desktopWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        _desktopHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
        if (_parkDiagnostics is not null)
        {
            Log.Write($"Diagnose aktiv, Protokoll in {_parkDiagnostics.FilePath}. Maus über eine Übergangskante bewegen.");
        }

        // Alles läuft über ein gemeinsames Token: Endet die Verbindung oder schweigt der Client, bricht das
        // auch ein Senden ab, das im vollen Puffer hängt. Sonst blieben die Eingaben verschluckt.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = linked.Token;
        Action<MonitorLayout?> layoutChanged = layout => _ = SendLayoutAsync(layout, token);
        state.LayoutChanged += layoutChanged;
        Action<String> keyTransfer = key => _ = SendKeyAsync(key, token);
        state.KeyTransferRequested += keyTransfer;
        Task incoming = Task.Run(() => PumpIncoming(linked));
        Task monitor = WatchClient(linked);

        try
        {
            await foreach (InputEvent e in LowLevelHooks.Events.ReadAllAsync(token))
            {
                await HandleAsync(e, token);
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

    private async Task HandleAsync(InputEvent e, CancellationToken ct)
    {
        switch (e)
        {
            case MouseMoveEvent move:
                await HandleMove(move, ct);
                break;
            case RawMouseMoveEvent raw when state.IsRemote && !options.Observe:
                await SendMove(raw.Dx, raw.Dy, ct);
                break;
            case MouseButtonEvent button:
                _pressed.Track(button);
                if (state.IsRemote)
                {
                    await connection.SendAsync(MessageType.MouseButton, _pressed.ForwardButton(button).ToPayload(), ct);
                }
                break;
            case MouseWheelEvent wheel when state.IsRemote:
                await connection.SendAsync(MessageType.MouseWheel,
                    new MouseWheel(wheel.Delta, wheel.Horizontal).ToPayload(), ct);
                break;
            case KeyInputEvent key:
                await HandleKey(key, ct);
                break;
        }
    }

    /// <summary>
    /// Schickt dem Client die Kanten, über die er zurückkehrt, und danach die Anordnung für seine Einstellungen. Ohne
    /// Layout keine Kanten, dann bleibt nur Strg+Alt+F12. Ältere Clients übergehen die Anordnung.
    /// </summary>
    private Task SendLayoutAsync(MonitorLayout? layout, CancellationToken ct) => IgnoreConnectionLossAsync(async () =>
    {
        await connection.SendAsync(MessageType.ReturnEdges, ReturnEdgeList.ToPayload(layout?.ReturnEdges ?? []), ct);
        await connection.SendAsync(MessageType.Arrangement, ClientArrangement.From(layout).ToPayload(), ct);
    });

    /// <summary>
    /// Schickt dem Client den neuen Schlüssel; verschlüsselt mit dem alten, unter dem die Verbindung steht. Ist die
    /// Verbindung weg, speichert HostState den Schlüssel beim Abbruch trotzdem.
    /// </summary>
    private Task SendKeyAsync(String key, CancellationToken ct) => IgnoreConnectionLossAsync(
        async () => await connection.SendAsync(MessageType.KeyChange, Encoding.UTF8.GetBytes(key), ct));

    /// <summary>
    /// Schickt dem Client den Text der Zwischenablage, wenn er sich seit dem letzten Abgleich geändert hat. Nebenher,
    /// damit Lesen und Senden den Wechsel nicht aufhalten; die Stücke reihen sich zwischen die Eingaben.
    /// </summary>
    private Task SendClipboardAsync(CancellationToken ct) => IgnoreConnectionLossAsync(async () =>
    {
        foreach (Byte[] chunk in _clipboard.TakeChanged())
        {
            await connection.SendAsync(MessageType.ClipboardText, chunk, ct);
        }
    });

    /// <summary>Für Sendungen nebenher: Scheitern sie, weil die Verbindung weg ist, endet sie ohnehin.</summary>
    private static async Task IgnoreConnectionLossAsync(Func<Task> send)
    {
        try
        {
            await send();
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
            await foreach ((MessageType type, Byte[] payload) in connection.ReceiveAsync(linked.Token))
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
                    await SwitchBackAsync(SwitchBackRequest.FromPayload(payload), linked.Token);
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
    private void SetEndReason(String reason) => Interlocked.CompareExchange(ref _endReason, reason, null);

    /// <summary>Der Client hat die Maus über eine Rückkehrkante geschoben; sie erscheint an der passenden Stelle des PCs.</summary>
    private async Task SwitchBackAsync(SwitchBackRequest request, CancellationToken ct)
    {
        state.IsRemote = false;
        LowLevelHooks.Swallow = false;
        await ReleaseClientInputAsync(ct);
        (Int32 x, Int32 y) = state.Layout is { } layout && layout.TryReturn(request.Side, request.X, request.Y, out (Int32 X, Int32 Y) point)
            ? point
            : (_returnX, _returnY);
        PInvoke.SetCursorPos(x, y);
        Log.Write($"Remote-Modus aus (Client an der Kante {request.Side}), zurück bei ({x}, {y})");
    }

    private async Task HandleMove(MouseMoveEvent move, CancellationToken ct)
    {
        if (!state.IsRemote)
        {
            // Ohne gemeldete Client-Monitore gibt es kein Layout und damit keinen Übergang. Mit gehaltener Maustaste,
            // etwa beim Ziehen eines Fensters an den Rand, bleibt die Maus auf dem PC.
            if (!_pressed.AnyButtonDown && state.Layout is { } layout && layout.TryLeaveHost(move.X, move.Y, move.Dx, move.Dy, out _, out (Int32 X, Int32 Y) entry))
            {
                await EnterRemote(entry, move.X - move.Dx, move.Y - move.Dy, ct);
            }

            return;
        }

        // Im Beobachtungsmodus läuft der Cursor frei, die Bewegung aus dem Hook stimmt.
        if (options.Observe)
        {
            await SendMove(move.Dx, move.Dy, ct);
            return;
        }

        // Beim Kapern kommt die Bewegung aus Raw Input (siehe RawMouse), der Hook verschluckt nur.
        // Der Cursor steht dabei nicht sicher im Parkpunkt: Eine Bewegung, die der Hook kurz vor dem
        // Umschalten noch durchgelassen hat, kann Windows erst nach dem SetCursorPos umsetzen. Dann friert
        // er am Rand ein. Deshalb hier zurück in die Mitte holen.
        if (_parkDiagnostics is not null)
        {
            _parkDiagnostics.Recenter(move, _parkX, _parkY);
        }
        else if (move.X - move.Dx != _parkX || move.Y - move.Dy != _parkY)
        {
            PInvoke.SetCursorPos(_parkX, _parkY);
        }
    }

    private async Task SendMove(Int32 dx, Int32 dy, CancellationToken ct)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        if (options.Absolute)
        {
            _virtualX = Math.Clamp(_virtualX + dx, 0, _desktopWidth);
            _virtualY = Math.Clamp(_virtualY + dy, 0, _desktopHeight);
            UInt16 nx = (UInt16)(_virtualX / _desktopWidth * UInt16.MaxValue);
            UInt16 ny = (UInt16)(_virtualY / _desktopHeight * UInt16.MaxValue);
            await connection.SendAsync(MessageType.MouseMoveAbsolute, new MouseMoveAbsolute(nx, ny).ToPayload(), ct);
        }
        else
        {
            await connection.SendAsync(MessageType.MouseMoveRelative,
                new MouseMoveRelative((Int16)Math.Clamp(dx, Int16.MinValue, Int16.MaxValue),
                                      (Int16)Math.Clamp(dy, Int16.MinValue, Int16.MaxValue)).ToPayload(), ct);
        }
    }

    private async Task HandleKey(KeyInputEvent key, CancellationToken ct)
    {
        _pressed.Track(key);
        if (key.Down && await TryHandleHotkeyAsync(key.VirtualKey, ct))
        {
            return;
        }

        if (state.IsRemote)
        {
            await connection.SendAsync(MessageType.Key, _pressed.ForwardKey(key).ToPayload(), ct);
        }
    }

    private async Task<Boolean> TryHandleHotkeyAsync(UInt16 virtualKey, CancellationToken ct)
    {
        Boolean ctrlAlt = _pressed.CtrlAltDown;
        if (ctrlAlt && virtualKey == VkF12 && state.IsRemote)
        {
            await ExitRemote(ct);
            return true;
        }

        // Win+L sperrt den PC immer; ein Low-Level-Hook sieht die Tasten, kann es aber nicht verhindern. Das Notebook
        // sperrt sich mit, auch wenn die Maus gerade auf dem PC ist.
        if (_pressed.IsLockCombination(virtualKey))
        {
            await LockBothAsync(ct);
            return true;
        }

        if (ctrlAlt && virtualKey == VkF11)
        {
            await ToggleRemoteAsync(ct);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Strg+Alt+F11: Remote ein-/ausschalten, ohne die Kante zu treffen. Zum Testen, wenn das Parken nicht greift
    /// (RDP): Der Client-Cursor erscheint in der Mitte des ersten Übergangs.
    /// </summary>
    private async Task ToggleRemoteAsync(CancellationToken ct)
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
    }

    /// <summary>
    /// Lässt auf dem Client alle Tasten und Maustasten los, die noch als gedrückt gelten. Wird beim Verlassen des
    /// Remote-Modus aufgerufen, damit Modifier (etwa das Strg+Alt des Austritts-Hotkeys) nicht kleben bleiben und ein
    /// Ziehen auf dem Notebook nicht weiterläuft, wenn die Maus schon wieder auf dem PC ist.
    /// </summary>
    private async Task ReleaseClientInputAsync(CancellationToken ct)
    {
        (KeyEvent[] keys, Byte[] buttons) = _pressed.ReleaseOnClient();
        foreach (KeyEvent up in keys)
        {
            await connection.SendAsync(MessageType.Key, up.ToPayload(), ct);
        }

        foreach (Byte button in buttons)
        {
            await connection.SendAsync(MessageType.MouseButton, new MouseButton(button, ButtonState.Up).ToPayload(), ct);
        }
    }

    /// <summary>Wechselt auf den Client. <paramref name="entry"/> ist der Eintrittspunkt in dessen Koordinaten,
    /// (<paramref name="hostX"/>, <paramref name="hostY"/>) die Stelle, an der der Host-Cursor zuletzt stand.</summary>
    private async Task EnterRemote((Int32 X, Int32 Y) entry, Int32 hostX, Int32 hostY, CancellationToken ct)
    {
        _parkX = _desktopLeft + _desktopWidth / 2;
        _parkY = _desktopTop + _desktopHeight / 2;
        (_returnX, _returnY) = (hostX, hostY);
        // Für den absoluten Vergleichsmodus: Startpunkt anteilig auf die Fläche des Hosts übertragen.
        if (state.Layout is { } layout)
        {
            (UInt16 nx, UInt16 ny) = LayoutGeometry.NormalizeToVirtualDesktop(layout.ClientMonitors, entry.X, entry.Y);
            _virtualX = (Double)nx / UInt16.MaxValue * _desktopWidth;
            _virtualY = (Double)ny / UInt16.MaxValue * _desktopHeight;
        }
        state.IsRemote = true;

        // Im Beobachtungsmodus die Maus nicht kapern, nur die Deltas verfolgen. Zum Testen in die Mitte setzen, damit
        // in jede Richtung Platz bleibt. Über RDP hält das evtl. nicht, dann hilft Strg+Alt+F11.
        if (!options.Observe)
        {
            LowLevelHooks.Swallow = true;
        }
        PInvoke.SetCursorPos(_parkX, _parkY);
        Log.Write($"Remote-Modus an{(options.Observe ? " (Beobachtung)" : "")}, Eintritt beim Client bei ({entry.X}, {entry.Y})");

        await connection.SendAsync(MessageType.SwitchToClient, new SwitchToClient(entry.X, entry.Y).ToPayload(), ct);
        _ = Task.Run(() => SendClipboardAsync(ct), CancellationToken.None);

        foreach (KeyEvent down in _pressed.ForwardHeldModifiers())
        {
            await connection.SendAsync(MessageType.Key, down.ToPayload(), ct);
        }
    }

    /// <summary>
    /// Verlässt den Remote-Modus, damit die Windows-Taste auf dem Notebook nicht gedrückt hängen bleibt, und lässt das
    /// Notebook sich sperren.
    /// </summary>
    private async Task LockBothAsync(CancellationToken ct)
    {
        if (state.IsRemote)
        {
            await ExitRemote(ct);
        }

        _pressed.ForgetLockCombination();
        Log.Write("Win+L: sperre auch das Notebook.");
        await connection.SendAsync(MessageType.LockWorkstation, ReadOnlyMemory<Byte>.Empty, ct);
    }

    private async Task ExitRemote(CancellationToken ct)
    {
        state.IsRemote = false;
        LowLevelHooks.Swallow = false;
        await ReleaseClientInputAsync(ct);
        PInvoke.SetCursorPos(_returnX, _returnY);
        Log.Write("Remote-Modus aus");
        await connection.SendAsync(MessageType.SwitchToHost, ReadOnlyMemory<Byte>.Empty, ct);
    }
}
