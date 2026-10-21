using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using InputFusion.Core.Net;

namespace InputFusion.Host;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        PInvoke.AllocConsole();
        bool absolute = args.Contains("--absolute");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var listener = new TcpListener(IPAddress.Any, Ports.Input);
        listener.Start();
        Console.WriteLine($"InputFusion Host auf Port {Ports.Input}. Modus: {(absolute ? "absolut" : "relativ")}. Warte auf Client...");

        using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
        listener.Stop();
        using var conn = new MessageConnection(tcp);
        Console.WriteLine("Client verbunden. Rechte Kante wechselt, Strg+Alt+F12 zurueck, Strg+C beendet.");

        var hooks = new HookListener();
        hooks.Start();
        try
        {
            await new RemoteController(conn, absolute).RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            LowLevelHooks.Swallow = false;
            hooks.Stop();
        }
    }
}
