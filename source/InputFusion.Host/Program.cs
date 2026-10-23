using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Config;
using InputFusion.Core.Net;

namespace InputFusion.Host;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        PInvoke.AllocConsole();
        bool absolute = args.Contains("--absolute");

        // In einer RDP-Sitzung lässt sich die Maus nicht kapern, deshalb dort automatisch nur beobachten.
        // --observe erzwingt das auch lokal, --capture erzwingt das Kapern trotz RDP (zum Ausprobieren).
        bool remoteSession = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;
        // Die Diagnose misst das Kapern, braucht es also; sie schaltet die Beobachtung ab.
        bool diag = args.Contains("--diag");
        bool observe = !diag && (args.Contains("--observe") || (remoteSession && !args.Contains("--capture")));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var config = HostConfig.LoadOrCreate();
        Console.WriteLine($"InputFusion Host auf Port {Ports.Input}, Schlüssel {config.Key} (aus {HostConfig.FilePath}). Modus: {(absolute ? "absolut" : "relativ")}"
            + $"{(observe ? ", Beobachtung (kapert die Maus nicht)" : "")}"
            + $"{(diag ? ", Diagnose" : "")}{(remoteSession ? " [RDP erkannt]" : "")}.");

        // Ein Client zur Zeit. Nach jedem Abbruch wieder lauschen, bis Strg+C; sonst verbraucht schon ein
        // Verbindungstest (Test-NetConnection) den einzigen Platz.
        var listener = new TcpListener(IPAddress.Any, Ports.Input);
        while (!cts.IsCancellationRequested)
        {
            TcpClient tcp;
            listener.Start();
            Console.WriteLine("Warte auf Client...");
            try
            {
                tcp = await listener.AcceptTcpClientAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            finally
            {
                // Während der Verbindung nicht lauschen: Ein zweiter Client wird abgewiesen, statt im
                // Backlog zu warten und später mit veraltetem Zustand angenommen zu werden.
                listener.Stop();
            }

            await ServeAsync(tcp, config.Key, absolute, observe, diag, cts.Token);
        }
    }

    private static async Task ServeAsync(TcpClient tcp, string key, bool absolute, bool observe, bool diag, CancellationToken ct)
    {
        using var _ = tcp;
        using var conn = MessageConnection.FromTcp(tcp);
        bool accepted;
        try
        {
            accepted = await Handshake.HostAsync(conn, key, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!accepted)
        {
            Console.WriteLine($"Client {tcp.Client.RemoteEndPoint} abgewiesen: Anmeldung mit dem Schlüssel fehlgeschlagen.");
            return;
        }
        Console.WriteLine($"Client {tcp.Client.RemoteEndPoint} verbunden. Rechte Kante wechselt, Strg+Alt+F11 schaltet hier um (Test), "
            + "Strg+Alt+F12 zurueck, Strg+C beendet.");

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
            Console.WriteLine("Verbindung beendet, Remote-Modus aus.");
        }
    }
}
