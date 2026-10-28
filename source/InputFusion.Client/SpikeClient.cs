using System.IO;
using System.Net.Sockets;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Client;

/// <summary>
/// Durchstich-Client für Phase 1: verbindet sich zum Host und spielt die empfangenen Eingaben
/// direkt ab. Ab Phase 2 macht das der Injector in der Benutzersitzung, gestartet vom Dienst.
/// </summary>
internal static class SpikeClient
{
    public static async Task RunAsync(String host, String key, Int32 port)
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        using TcpClient tcp = new TcpClient();
        Console.WriteLine($"Verbinde zu {host}:{port}...");
        await tcp.ConnectAsync(host, port, cts.Token);
        using MessageConnection conn = MessageConnection.FromTcp(tcp);
        HandshakeResult handshake = await Handshake.ClientAsync(conn, key, AppVersion.Current, cts.Token);
        if (handshake.Outcome == HandshakeOutcome.ProtocolMismatch)
        {
            Console.WriteLine($"Host hat Protokoll {handshake.PeerProtocol} (Version {handshake.PeerVersion}), dieser Client {ProtocolVersion.Current}.");
            return;
        }
        if (handshake.Outcome != HandshakeOutcome.Accepted)
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
            await foreach ((MessageType type, Byte[] payload) in conn.ReceiveAsync(cts.Token))
            {
                if (dispatcher.Dispatch(type, payload) is { } exit)
                {
                    await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), cts.Token);
                }
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
