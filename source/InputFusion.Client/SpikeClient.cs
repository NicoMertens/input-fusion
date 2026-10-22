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
    public static async Task RunAsync(string host)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        using var tcp = new TcpClient();
        Console.WriteLine($"Verbinde zu {host}:{Ports.Input}...");
        await tcp.ConnectAsync(host, Ports.Input, cts.Token);
        using var conn = new MessageConnection(tcp);
        var player = new InputPlayer();
        Console.WriteLine("Verbunden. Spiele Eingaben ab. Strg+C beendet.");

        try
        {
            await foreach (var (type, payload) in conn.ReceiveAsync(cts.Token))
            {
                switch (type)
                {
                    case MessageType.SwitchToClient:
                        player.MoveAbsolute(0, SwitchToClient.FromPayload(payload).Y);
                        break;
                    case MessageType.MouseMoveRelative:
                        var move = MouseMoveRelative.FromPayload(payload);
                        player.MoveRelative(move.Dx, move.Dy);
                        break;
                    case MessageType.MouseMoveAbsolute:
                        var abs = MouseMoveAbsolute.FromPayload(payload);
                        player.MoveAbsolute(abs.X, abs.Y);
                        break;
                    case MessageType.MouseButton:
                        player.Button(MouseButton.FromPayload(payload));
                        break;
                    case MessageType.MouseWheel:
                        player.Wheel(MouseWheel.FromPayload(payload));
                        break;
                    case MessageType.Key:
                        player.Key(KeyEvent.FromPayload(payload));
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        Console.WriteLine("Verbindung beendet.");
    }
}
