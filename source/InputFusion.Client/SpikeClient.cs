using System.IO;
using System.Net.Sockets;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Durchstich-Client für Phase 1: verbindet sich zum Host und spielt die empfangenen Eingaben
/// direkt ab. Ab Phase 2 macht das der Injector in der Benutzersitzung, gestartet vom Dienst.
/// </summary>
internal static class SpikeClient
{
    public static async Task RunAsync(string host, string key)
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        using TcpClient tcp = new TcpClient();
        Console.WriteLine($"Verbinde zu {host}:{Ports.Input}...");
        await tcp.ConnectAsync(host, Ports.Input, cts.Token);
        using MessageConnection conn = MessageConnection.FromTcp(tcp);
        if (!await Handshake.ClientAsync(conn, key, cts.Token))
        {
            Console.WriteLine("Anmeldung abgelehnt; stimmt der Schlüssel?");
            return;
        }
        InputDispatcher dispatcher = new InputDispatcher(new InputPlayer());
        Console.WriteLine("Verbunden. Spiele Eingaben ab. Strg+C beendet.");

        // Heartbeat zum Host, sonst trennt er nach dem Timeout. Schweigt der Host, endet der Client.
        Task monitors = MonitorReporter.RunAsync(conn, cts.Token);
        Task monitor = Task.Run(async () =>
        {
            if (await conn.MonitorAsync(cts.Token))
            {
                Console.WriteLine("Host antwortet nicht mehr.");
                cts.Cancel();
            }
        });
        try
        {
            await foreach ((MessageType type, byte[] payload) in conn.ReceiveAsync(cts.Token))
            {
                if (dispatcher.Dispatch(type, payload) is { } exit)
                    await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), cts.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
        }
        cts.Cancel();
        await monitor;
        await monitors;
        Console.WriteLine("Verbindung beendet.");
    }
}
