using System.Net.Sockets;
using InputFusion.Core.Net;

namespace InputFusion.Client;

/// <summary>
/// Durchstich-Client für Phase 1: verbindet sich zum Host und spielt die empfangenen Eingaben
/// direkt ab. Ab Phase 2 macht das der Injector in der Benutzersitzung, gestartet vom Dienst.
/// </summary>
internal static class SpikeClient
{
    public static async Task RunAsync(string host)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        using var tcp = new TcpClient();
        Console.WriteLine($"Verbinde zu {host}:{Ports.Input}...");
        await tcp.ConnectAsync(host, Ports.Input, cts.Token);
        using var conn = MessageConnection.FromTcp(tcp);
        var dispatcher = new InputDispatcher(new InputPlayer());
        Console.WriteLine("Verbunden. Spiele Eingaben ab. Strg+C beendet.");

        try
        {
            await foreach (var (type, payload) in conn.ReceiveAsync(cts.Token))
                dispatcher.Dispatch(type, payload);
        }
        catch (OperationCanceledException)
        {
        }
        Console.WriteLine("Verbindung beendet.");
    }
}
