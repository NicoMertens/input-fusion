using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using InputFusion.Core.Config;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class HandshakeTests
{
    private const string Key = "abcdefghijkmnpqrstuvwxyz";

    [Fact]
    public async Task SameKey_BothSidesAccept_AndLearnTheOtherVersion()
    {
        (HandshakeResult host, HandshakeResult client) = await RunAsync(Key, Key);
        Assert.Equal(HandshakeOutcome.Accepted, host.Outcome);
        Assert.Equal(HandshakeOutcome.Accepted, client.Outcome);
        Assert.Equal("1.2.3-client", host.PeerVersion);
        Assert.Equal("1.2.3-host", client.PeerVersion);
        Assert.Equal(ProtocolVersion.Current, host.PeerProtocol);
    }

    [Fact]
    public async Task DifferentKey_HostRejects_ClientRejects()
    {
        (HandshakeResult host, HandshakeResult client) = await RunAsync(Key, Key + "x");
        Assert.Equal(HandshakeOutcome.Rejected, host.Outcome);
        Assert.Equal(HandshakeOutcome.Rejected, client.Outcome);
    }

    [Fact]
    public async Task HostWithOtherProtocol_ClientReportsMismatch()
    {
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> client = Handshake.ClientAsync(pair.Client, Key, "1.0.0", CancellationToken.None);

        byte[] hello = [.. new byte[Handshake.NonceSize], .. Versions(99, "9.9.9")];
        await pair.Host.SendAsync(MessageType.Hello, hello);

        // Der Client antwortet trotz abweichender Version; die Pipe puffert nicht, also abholen.
        await foreach ((MessageType type, byte[] _) in pair.Host.ReceiveAsync())
        {
            Assert.Equal(MessageType.HelloResponse, type);
            break;
        }

        HandshakeResult result = await client;
        Assert.Equal(HandshakeOutcome.ProtocolMismatch, result.Outcome);
        Assert.Equal(99, result.PeerProtocol);
        Assert.Equal("9.9.9", result.PeerVersion);
    }

    [Fact]
    public async Task ClientWithOtherProtocol_HostReportsMismatch_AndDoesNotAccept()
    {
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> host = Handshake.HostAsync(pair.Host, Key, "1.0.0", CancellationToken.None);

        // Ein Client mit richtigem Schlüssel, aber Protokoll 99: Nachweis wie im Original berechnet.
        byte[] hostNonce = [];
        await foreach ((MessageType _, byte[] payload) in pair.Client.ReceiveAsync())
        {
            hostNonce = payload[..Handshake.NonceSize];
            break;
        }
        byte[] clientNonce = new byte[Handshake.NonceSize];
        byte[] signed = [.. Encoding.ASCII.GetBytes("InputFusion-client"), .. hostNonce, .. clientNonce];
        byte[] mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), signed);
        byte[] response = [.. clientNonce, .. mac, .. Versions(99, "9.9.9")];
        await pair.Client.SendAsync(MessageType.HelloResponse, response);

        HandshakeResult result = await host;
        Assert.Equal(HandshakeOutcome.ProtocolMismatch, result.Outcome);
        Assert.Equal("9.9.9", result.PeerVersion);
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

    private static byte[] Versions(ushort protocol, string version)
    {
        byte[] p = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(p, protocol);
        return [.. p, .. Encoding.UTF8.GetBytes(version)];
    }

    private static async Task<(HandshakeResult Host, HandshakeResult Client)> RunAsync(string hostKey, string clientKey)
    {
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> host = Handshake.HostAsync(pair.Host, hostKey, "1.2.3-host", CancellationToken.None);
        Task<HandshakeResult> client = Handshake.ClientAsync(pair.Client, clientKey, "1.2.3-client", CancellationToken.None);

        // Lehnt der Host ab, schickt er nichts mehr; der Client wartet dann bis zum Timeout. Die Verbindung zu
        // schließen beendet das wie im echten Betrieb sofort.
        HandshakeResult hostResult = await host;
        if (hostResult.Outcome != HandshakeOutcome.Accepted)
        {
            pair.Host.Dispose();
        }

        return (hostResult, await client);
    }

    /// <summary>Host und Client über eine Named Pipe, wie über eine TCP-Verbindung.</summary>
    private sealed class Pair : IAsyncDisposable
    {
        private Pair(MessageConnection host, MessageConnection client)
        {
            Host = host;
            Client = client;
        }

        public MessageConnection Host { get; }

        public MessageConnection Client { get; }

        public static async Task<Pair> ConnectAsync()
        {
            string name = "InputFusion.Test." + Guid.NewGuid().ToString("N");
            NamedPipeServerStream serverPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            NamedPipeClientStream clientPipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            Task accept = serverPipe.WaitForConnectionAsync();
            await clientPipe.ConnectAsync();
            await accept;
            return new Pair(new MessageConnection(serverPipe), new MessageConnection(clientPipe));
        }

        public ValueTask DisposeAsync()
        {
            Host.Dispose();
            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
