using System.IO.Pipes;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

/// <summary>
/// Prüft die IPC-Schicht zwischen Dienst und Injector: Der Server (Dienst) reicht Frames über die Named
/// Pipe an den Client (Injector) weiter, in derselben Rahmung wie im Netzwerk. Braucht Windows.
/// </summary>
public class InputPipeTests
{
    [Fact]
    public async Task ServerToClient_RoundTripsFrames()
    {
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        NamedPipeServerStream server = InputPipe.CreateServer();
        NamedPipeClientStream client = InputPipe.CreateClient();

        Task accept = server.WaitForConnectionAsync(cts.Token);
        await client.ConnectAsync(cts.Token);
        await accept;

        using MessageConnection serverConn = new MessageConnection(server);
        using MessageConnection clientConn = new MessageConnection(client);

        // Nebenläufig lesen wie der Injector, während der Dienst sendet.
        List<(MessageType Type, byte[] Payload)> received = new List<(MessageType Type, byte[] Payload)>();
        Task reading = Task.Run(async () =>
        {
            await foreach ((MessageType Type, byte[] Payload) frame in clientConn.ReceiveAsync(cts.Token))
            {
                received.Add(frame);
                if (received.Count == 3)
                {
                    break;
                }
            }
        }, cts.Token);

        await serverConn.SendAsync(MessageType.SwitchToClient, new SwitchToClient(-1920, 12345).ToPayload(), cts.Token);
        await serverConn.SendAsync(MessageType.Key, new KeyEvent(65, 30, ButtonState.Down, false).ToPayload(), cts.Token);
        await serverConn.SendAsync(MessageType.MouseMoveRelative, new MouseMoveRelative(7, -3).ToPayload(), cts.Token);

        await reading;

        Assert.Equal(MessageType.SwitchToClient, received[0].Type);
        Assert.Equal(12345, SwitchToClient.FromPayload(received[0].Payload).Y);
        Assert.Equal(new KeyEvent(65, 30, ButtonState.Down, false), KeyEvent.FromPayload(received[1].Payload));
        Assert.Equal(new MouseMoveRelative(7, -3), MouseMoveRelative.FromPayload(received[2].Payload));
    }

    [Fact]
    public async Task VerifyServer_AcceptsServerOfSameUser()
    {
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using NamedPipeServerStream server = InputPipe.CreateServer();
        using NamedPipeClientStream client = InputPipe.CreateClient();

        Task accept = server.WaitForConnectionAsync(cts.Token);
        await client.ConnectAsync(cts.Token);
        await accept;

        InputPipe.VerifyServer(client);
    }
}
