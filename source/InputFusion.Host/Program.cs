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

        // Bricht die Verbindung ab, muss der Remote-Modus sofort enden, sonst bleibt die lokale Eingabe verschluckt.
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in conn.ReceiveAsync(cts.Token))
                {
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
            }
            LowLevelHooks.Swallow = false;
            cts.Cancel();
        });

        var hooks = new HookListener();
        hooks.Start();
        try
        {
            await new RemoteController(conn, absolute).RunAsync(cts.Token);
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
