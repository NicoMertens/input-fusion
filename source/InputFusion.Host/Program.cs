using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Config;
using InputFusion.Core.Net;

namespace InputFusion.Host;

internal static class Program
{
    private const string InstanceMutexName = @"Local\InputFusion.Host";

    private static async Task Main(string[] args)
    {
        // Ein Agent je Sitzung; ein zweiter Start öffnet nur die Einstellungen.
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out bool first);
        if (!first)
        {
            SettingsLauncher.Open();
            return;
        }

        bool absolute = args.Contains("--absolute");
        // In einer RDP-Sitzung lässt sich die Maus nicht kapern, deshalb dort automatisch nur beobachten.
        // --observe erzwingt das auch lokal, --capture erzwingt das Kapern trotz RDP (zum Ausprobieren).
        bool remoteSession = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;
        // Die Diagnose misst das Kapern, braucht es also; sie schaltet die Beobachtung ab.
        bool diag = args.Contains("--diag");
        bool observe = !diag && (args.Contains("--observe") || (remoteSession && !args.Contains("--capture")));
        if (diag || args.Contains("--console"))
            PInvoke.AllocConsole();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var state = new HostState(HostConfig.LoadOrCreate());
        Log.Write($"InputFusion Host auf Port {Ports.Input}, Konfiguration {HostConfig.FilePath}. Modus: {(absolute ? "absolut" : "relativ")}"
            + $"{(observe ? ", Beobachtung (kapert die Maus nicht)" : "")}"
            + $"{(diag ? ", Diagnose" : "")}{(remoteSession ? " [RDP erkannt]" : "")}.");

        var tray = new TrayIcon(SettingsLauncher.Open, cts.Cancel, state.RefreshHostMonitors);
        tray.Start();
        tray.SetTooltip(state.StatusText);
        state.Changed += () => tray.SetTooltip(state.StatusText);
        var settings = new SettingsServer(state).RunAsync(cts.Token);

        try
        {
            await ListenAsync(state, absolute, observe, diag, cts.Token);
        }
        finally
        {
            cts.Cancel();
            await settings;
            tray.Stop();
            Log.Write("Host beendet.");
        }
    }

    /// <summary>
    /// Ein Client zur Zeit. Nach jedem Abbruch wieder lauschen; sonst verbraucht schon ein Verbindungstest
    /// (Test-NetConnection) den einzigen Platz.
    /// </summary>
    private static async Task ListenAsync(HostState state, bool absolute, bool observe, bool diag, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, Ports.Input);
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                listener.Start();
                Log.Write("Warte auf Client …");
                tcp = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // Port belegt, etwa von einem zweiten Agent unter einem anderen Benutzer: später erneut versuchen.
                Log.Write($"Port {Ports.Input} nicht verfügbar ({ex.SocketErrorCode}), neuer Versuch in 10 s.");
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            finally
            {
                // Während der Verbindung nicht lauschen: Ein zweiter Client wird abgewiesen, statt im
                // Backlog zu warten und später mit veraltetem Zustand angenommen zu werden.
                listener.Stop();
            }

            await ServeAsync(tcp, state, absolute, observe, diag, ct);
        }
    }

    private static async Task ServeAsync(TcpClient tcp, HostState state, bool absolute, bool observe, bool diag, CancellationToken ct)
    {
        using var _ = tcp;
        using var conn = MessageConnection.FromTcp(tcp);
        string address = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        bool accepted;
        try
        {
            accepted = await Handshake.HostAsync(conn, state.Key, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!accepted)
        {
            Log.Write($"Client {address} abgewiesen: Anmeldung mit dem Schlüssel fehlgeschlagen.");
            return;
        }
        Log.Write($"Client {address} verbunden. Strg+Alt+F11 schaltet hier um (Test), Strg+Alt+F12 zurück.");
        state.ClientConnected(address);

        // Ereignisse aus der vorigen Verbindung verwerfen, sonst löst etwa eine alte Bewegung an der Kante
        // sofort den Wechsel aus.
        LowLevelHooks.Drain();
        var hooks = new HookListener();
        hooks.Start();
        try
        {
            await new RemoteController(conn, absolute, observe, diag).RunAsync(ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
        }
        finally
        {
            LowLevelHooks.Swallow = false;
            hooks.Stop();
            state.ClientDisconnected();
            Log.Write("Verbindung beendet, Remote-Modus aus.");
        }
    }
}
