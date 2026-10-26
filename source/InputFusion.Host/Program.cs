using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Config;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;
using InputFusion.Core.Shell;
using Velopack;

namespace InputFusion.Host;

internal static class Program
{
    private const string InstanceMutexName = @"Local\InputFusion.Host";

    // So lange darf das Notebook für eine Neuverbindung brauchen, ohne dass der Abbruch gemeldet wird.
    private static readonly TimeSpan _notifyDelay = TimeSpan.FromSeconds(5);

    private static async Task Main(string[] args)
    {
        // Vor allem anderen: Setup und Updater starten die exe mit Hook-Argumenten und erwarten ein sofortiges Ende.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => ElevatedStartup.Uninstall())
            .Run();

        // Ein Agent je Sitzung; ein zweiter Start öffnet nur die Einstellungen. Läuft der Agent erhöht, darf ein
        // nicht erhöhter Prozess dessen Mutex nicht öffnen; auch das heißt: läuft schon.
        Mutex instance;
        bool first;
        try
        {
            instance = new Mutex(initiallyOwned: true, InstanceMutexName, out first);
        }
        catch (UnauthorizedAccessException)
        {
            SettingsLauncher.Open(Log.Write);
            return;
        }
        using Mutex firstInstance = instance;
        if (!first)
        {
            SettingsLauncher.Open(Log.Write);
            return;
        }

        // Eine installierte Kopie startet sich erhöht über die geplante Aufgabe, siehe ElevatedStartup. Den Mutex
        // vorher freigeben, sonst hielte die Instanz aus der Aufgabe sich für die zweite.
        UpdateService updates = new UpdateService();
        if (updates.IsInstalled && !ElevatedStartup.IsElevated
            && (ElevatedStartup.TaskExists() || ElevatedStartup.Install(Environment.ProcessPath!, HostConfig.LoadOrCreate().Port)))
        {
            instance.ReleaseMutex();
            instance.Dispose();
            if (ElevatedStartup.StartTask())
            {
                return;
            }

            Log.Write("Geplante Aufgabe ließ sich nicht starten; der Host läuft ohne Adminrechte.");
            instance = new Mutex(initiallyOwned: true, InstanceMutexName, out first);
            if (!first)
            {
                return;
            }
        }

        bool absolute = args.Contains("--absolute");
        // In einer RDP-Sitzung lässt sich die Maus nicht kapern, deshalb dort automatisch nur beobachten.
        // --observe erzwingt das auch lokal, --capture erzwingt das Kapern trotz RDP (zum Ausprobieren).
        bool remoteSession = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;
        // Die Diagnose misst das Kapern, braucht es also; sie schaltet die Beobachtung ab.
        bool diag = args.Contains("--diag");
        bool observe = !diag && (args.Contains("--observe") || (remoteSession && !args.Contains("--capture")));
        if (diag || args.Contains("--console"))
        {
            PInvoke.AllocConsole();
        }

        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        HostState state = new HostState(HostConfig.LoadOrCreate());
        if (updates.IsInstalled && ElevatedStartup.IsElevated)
        {
            // Erhöht aus der Aufgabe: Die Firewall-Regel folgt dem Port ohne UAC-Abfrage, auch einem von Hand geänderten.
            ElevatedStartup.UpdateFirewall(state.Port);
            state.PortChanged += ElevatedStartup.UpdateFirewall;
        }

        Log.Write($"InputFusion Host auf Port {state.Port}, Konfiguration {HostConfig.FilePath}. Modus: {(absolute ? "absolut" : "relativ")}"
            + $"{(observe ? ", Beobachtung (kapert die Maus nicht)" : "")}"
            + $"{(diag ? ", Diagnose" : "")}{(remoteSession ? " [RDP erkannt]" : "")}.");

        TrayIcon tray = new TrayIcon($"Version {AppVersion.Current}",
            [new TrayCommand("Einstellungen …", () => SettingsLauncher.Open(Log.Write)), new TrayCommand("Beenden", cts.Cancel)],
            Log.Write, state.RefreshHostMonitors, updates.CheckNowAsync);
        tray.Start();
        tray.SetTooltip(state.StatusText);
        ConnectionNotifier notifier = new ConnectionNotifier(tray.ShowBalloon, _notifyDelay);
        state.Changed += () => tray.SetTooltip(state.StatusText);
        Task settings = new SettingsServer(state).RunAsync(cts.Token);

        state.ProtocolMismatch += updates.CheckSoon;
        updates.UpdateReady += version => _ = ApplyWhenIdleAsync(updates, state, version, cts);
        Task updateLoop = updates.RunAsync(cts.Token);

        try
        {
            await ListenAsync(state, notifier, absolute, observe, diag, cts.Token);
        }
        finally
        {
            cts.Cancel();
            await settings;
            await updateLoop;
            tray.Stop();
            Log.Write("Host beendet.");
        }
    }

    /// <summary>
    /// Spielt ein geladenes Update ein, sobald die Maus nicht gerade auf dem Notebook ist: Der Host beendet sich, der
    /// Updater von Velopack tauscht die Dateien und startet ihn neu. Das Notebook verbindet sich danach von selbst neu.
    /// </summary>
    private static async Task ApplyWhenIdleAsync(UpdateService updates, HostState state, string version, CancellationTokenSource cts)
    {
        try
        {
            while (state.IsRemote)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (updates.ApplyOnExit())
        {
            Log.Write($"Spiele Update auf {version} ein, der Host startet neu.");
            cts.Cancel();
        }
    }

    /// <summary>
    /// Ein Client zur Zeit. Nach jedem Abbruch wieder lauschen; sonst verbraucht schon ein Verbindungstest
    /// (Test-NetConnection) den einzigen Platz. Ein neuer Port bricht das Warten ab, damit gleich auf ihm gelauscht wird.
    /// </summary>
    private static async Task ListenAsync(HostState state, ConnectionNotifier notifier, bool absolute, bool observe, bool diag, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using CancellationTokenSource portChanged = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Action<int> restart = _ =>
            {
                try { portChanged.Cancel(); }
                catch (ObjectDisposedException) { }
            };
            // Vor dem Lesen des Ports anmelden, sonst ginge eine Änderung dazwischen verloren.
            state.PortChanged += restart;
            int port = state.Port;
            TcpClient tcp;
            // Je Durchgang neu: Stop verwirft den Socket, ein neu angelegter wäre nicht mehr Dual-Mode.
            TcpListener listener = CreateListener(port);
            try
            {
                listener.Start();
                Log.Write($"Warte auf Client an Port {port} …");
                tcp = await listener.AcceptTcpClientAsync(portChanged.Token);
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (SocketException ex)
            {
                // Port belegt, etwa von einem zweiten Agent unter einem anderen Benutzer: später erneut versuchen.
                Log.Write($"Port {port} nicht verfügbar ({ex.SocketErrorCode}), neuer Versuch in 10 s.");
                try { await Task.Delay(TimeSpan.FromSeconds(10), portChanged.Token); }
                catch (OperationCanceledException) { }
                continue;
            }
            finally
            {
                state.PortChanged -= restart;
                // Während der Verbindung nicht lauschen: Ein zweiter Client wird abgewiesen, statt im
                // Backlog zu warten und später mit veraltetem Zustand angenommen zu werden.
                listener.Stop();
            }

            await ServeAsync(tcp, state, notifier, absolute, observe, diag, ct);
        }
    }

    /// <summary>
    /// Lauscht auf IPv4 und IPv6. Löst das Notebook den Rechnernamen zuerst in IPv6-Adressen auf, gelingt die Verbindung
    /// so schon dort; nur auf IPv4 blieben solche Versuche je nach Firewall hängen. Ohne IPv6 im System nur IPv4.
    /// </summary>
    internal static TcpListener CreateListener(int port)
    {
        if (!Socket.OSSupportsIPv6)
        {
            return new TcpListener(IPAddress.Any, port);
        }

        TcpListener listener = new TcpListener(IPAddress.IPv6Any, port);
        listener.Server.DualMode = true;
        return listener;
    }

    private static async Task ServeAsync(TcpClient tcp, HostState state, ConnectionNotifier notifier, bool absolute, bool observe, bool diag,
        CancellationToken ct)
    {
        using TcpClient owned = tcp;
        using MessageConnection conn = MessageConnection.FromTcp(tcp);
        string address = HostAddresses.Display(tcp.Client.RemoteEndPoint);
        HandshakeResult handshake;
        try
        {
            handshake = await Handshake.HostAsync(conn, state.Key, AppVersion.Current, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        switch (handshake.Outcome)
        {
            case HandshakeOutcome.Accepted:
                break;
            case HandshakeOutcome.ProtocolMismatch:
                Log.Write($"Client {address} abgewiesen: Version {handshake.PeerVersion} mit Protokoll {handshake.PeerProtocol}, "
                    + $"der Host hat {AppVersion.Current} mit Protokoll {ProtocolVersion.Current}. Suche nach einem Update.");
                state.ReportProtocolMismatch();
                return;
            default:
                Log.Write($"Client {address} abgewiesen: Anmeldung mit dem Schlüssel fehlgeschlagen.");
                return;
        }

        Log.Write($"Client {address} (Version {handshake.PeerVersion}) verbunden. Strg+Alt+F11 schaltet hier um (Test), Strg+Alt+F12 zurück.");
        state.ClientConnected(address, handshake.PeerVersion);
        notifier.Restored($"Wieder mit dem Notebook verbunden ({address}).");

        // Ereignisse aus der vorigen Verbindung verwerfen, sonst löst etwa eine alte Bewegung an der Kante
        // sofort den Wechsel aus.
        LowLevelHooks.Drain();
        HookListener hooks = new HookListener();
        hooks.Start();
        RemoteController controller = new RemoteController(conn, state, absolute, observe, diag);
        try
        {
            await controller.RunAsync(ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
        }
        finally
        {
            LowLevelHooks.Swallow = false;
            hooks.Stop();
            string peer = state.Snapshot().Client?.MachineName is { } name ? $"zu {name}" : "zum Notebook";
            state.ClientDisconnected();
            Log.Write("Verbindung beendet, Remote-Modus aus.");
            // Beim Beenden des Hosts, auch für ein Update, ist der Abbruch gewollt.
            if (!ct.IsCancellationRequested)
            {
                string reason = controller.EndReason ?? "Die Verbindung ist abgebrochen.";
                _ = notifier.Lost($"Verbindung {peer} beendet. {reason}{(controller.MouseReturned ? " Die Maus ist zurück auf dem PC." : "")}",
                    immediately: controller.MouseReturned);
            }
        }
    }
}
