using System.IO.Pipes;
using InputFusion.Core.Config;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class HandshakeTests
{
    private const string Key = "abcdefghijkmnpqrstuvwxyz";

    [Fact]
    public async Task SameKey_BothSidesAccept()
    {
        (bool host, bool client) = await RunAsync(Key, Key);
        Assert.True(host);
        Assert.True(client);
    }

    [Fact]
    public async Task DifferentKey_HostRejects_ClientRejects()
    {
        (bool host, bool client) = await RunAsync(Key, Key + "x");
        Assert.False(host);
        Assert.False(client);
    }

    [Fact]
    public async Task FramesAfterHandshake_ArriveEvenIfReadTogether()
    {
        using AnonymousPipeServerStream server = new AnonymousPipeServerStream(PipeDirection.Out);
        using AnonymousPipeClientStream reader = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle);
        using MessageConnection outgoing = new MessageConnection(server);
        using MessageConnection incoming = new MessageConnection(reader);

        // Zwei Frames in einem Rutsch: Der erste wird einzeln gelesen, der zweite muss beim nächsten Lesen kommen.
        await outgoing.SendAsync(MessageType.Hello, new byte[Handshake.NonceSize]);
        await outgoing.SendAsync(MessageType.Key, new KeyEvent(0x41, 0x1E, ButtonState.Down, false).ToPayload());

        await foreach ((MessageType type, byte[] _) in incoming.ReceiveAsync())
        {
            Assert.Equal(MessageType.Hello, type);
            break;
        }
        await foreach ((MessageType type, byte[] _) in incoming.ReceiveAsync())
        {
            Assert.Equal(MessageType.Key, type);
            break;
        }
    }

    [Fact]
    public void GeneratedKey_IsValid()
    {
        string key = HostConfig.GenerateKey();
        Assert.Equal(HostConfig.GeneratedKeyLength, key.Length);
        Assert.Null(HostConfig.ValidateKey(key));
        Assert.NotEqual(key, HostConfig.GenerateKey());
    }

    [Theory]
    [InlineData("kurz")]
    [InlineData(" mit Leerzeichen vorne")]
    public void ValidateKey_RejectsBadKeys(string key)
    {
        Assert.NotNull(HostConfig.ValidateKey(key));
    }

    /// <summary>Host und Client über zwei Pipes in je eine Richtung, wie über eine TCP-Verbindung.</summary>
    private static async Task<(bool Host, bool Client)> RunAsync(string hostKey, string clientKey)
    {
        string name = "InputFusion.Test." + Guid.NewGuid().ToString("N");
        using NamedPipeServerStream serverPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using NamedPipeClientStream clientPipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task accept = serverPipe.WaitForConnectionAsync();
        await clientPipe.ConnectAsync();
        await accept;

        using MessageConnection hostConn = new MessageConnection(serverPipe);
        using MessageConnection clientConn = new MessageConnection(clientPipe);
        Task<bool> host = Handshake.HostAsync(hostConn, hostKey, CancellationToken.None);
        Task<bool> client = Handshake.ClientAsync(clientConn, clientKey, CancellationToken.None);

        // Lehnt der Host ab, schickt er nichts mehr; der Client wartet dann bis zum Timeout. Die Verbindung zu
        // schließen beendet das wie im echten Betrieb sofort.
        bool hostResult = await host;
        if (!hostResult)
            hostConn.Dispose();
        return (hostResult, await client);
    }
}
