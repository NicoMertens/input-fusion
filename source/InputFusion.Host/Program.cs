using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
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

        var listener = new TcpListener(IPAddress.Any, Ports.Input);
        listener.Start();
        Console.WriteLine($"InputFusion Host auf Port {Ports.Input}. Modus: {(absolute ? "absolut" : "relativ")}"
            + $"{(observe ? ", Beobachtung (kapert die Maus nicht)" : "")}"
            + $"{(diag ? ", Diagnose" : "")}{(remoteSession ? " [RDP erkannt]" : "")}. Warte auf Client...");

        using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
        listener.Stop();
        using var conn = new MessageConnection(tcp);
        Console.WriteLine("Client verbunden. Rechte Kante wechselt, Strg+Alt+F12 zurueck, Strg+C beendet.");

        var hooks = new HookListener();
        hooks.Start();
        try
        {
            await new RemoteController(conn, absolute, observe, diag).RunAsync(cts.Token);
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
