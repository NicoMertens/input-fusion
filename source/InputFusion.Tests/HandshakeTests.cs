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
    public async Task Accepted_ConnectionIsEncryptedBothWays()
    {
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> host = Handshake.HostAsync(pair.Host, Key, "1.0.0", CancellationToken.None);
        Task<HandshakeResult> client = Handshake.ClientAsync(pair.Client, Key, "1.0.0", CancellationToken.None);
        Assert.Equal(HandshakeOutcome.Accepted, (await host).Outcome);
        Assert.Equal(HandshakeOutcome.Accepted, (await client).Outcome);

        byte[] key = new KeyEvent(0x41, 0x1E, ButtonState.Down, false).ToPayload();
        Task sent = pair.Host.SendAsync(MessageType.Key, key);
        (MessageType type, byte[] payload) = await ReceiveOneAsync(pair.Client);
        await sent;
        Assert.Equal(MessageType.Key, type);
        Assert.Equal(key, payload);

        sent = pair.Client.SendAsync(MessageType.Heartbeat, ReadOnlyMemory<byte>.Empty);
        (type, payload) = await ReceiveOneAsync(pair.Host);
        await sent;
        Assert.Equal(MessageType.Heartbeat, type);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task HostWithProtocol1_ClientAnswersInProtocol1Form_AndReportsMismatch()
    {
        // So meldet sich ein Host aus 0.3.0: ohne Feld für den öffentlichen Schlüssel.
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> client = Handshake.ClientAsync(pair.Client, Key, "1.0.0", CancellationToken.None);

        byte[] hostNonce = RandomNumberGenerator.GetBytes(Handshake.NonceSize);
        byte[] hello = [.. hostNonce, .. LegacyVersions(1, "0.3.0")];
        await pair.Host.SendAsync(MessageType.Hello, hello);

        // Der Client antwortet trotz abweichender Version; die Pipe puffert nicht, also abholen.
        (MessageType type, byte[] response) = await ReceiveOneAsync(pair.Host);
        Assert.Equal(MessageType.HelloResponse, type);

        // Der alte Host muss den Nachweis prüfen und die Protokollversion an alter Stelle lesen können.
        byte[] clientNonce = response[..Handshake.NonceSize];
        Assert.Equal(LegacyMac("client", hostNonce, clientNonce), response[Handshake.NonceSize..(Handshake.NonceSize + 32)]);
        Assert.Equal(LegacyVersions(ProtocolVersion.Current, "1.0.0"), response[(Handshake.NonceSize + 32)..]);

        HandshakeResult result = await client;
        Assert.Equal(HandshakeOutcome.ProtocolMismatch, result.Outcome);
        Assert.Equal(1, result.PeerProtocol);
        Assert.Equal("0.3.0", result.PeerVersion);
    }

    [Fact]
    public async Task ClientWithProtocol1_HostReportsMismatch_AndDoesNotAccept()
    {
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> host = Handshake.HostAsync(pair.Host, Key, "1.0.0", CancellationToken.None);

        // Ein Client aus 0.3.0 mit richtigem Schlüssel: Nachweis und Versionen wie in Protokoll 1.
        (MessageType _, byte[] hello) = await ReceiveOneAsync(pair.Client);
        byte[] hostNonce = hello[..Handshake.NonceSize];
        byte[] clientNonce = new byte[Handshake.NonceSize];
        byte[] response = [.. clientNonce, .. LegacyMac("client", hostNonce, clientNonce), .. LegacyVersions(1, "0.3.0")];
        await pair.Client.SendAsync(MessageType.HelloResponse, response);

        HandshakeResult result = await host;
        Assert.Equal(HandshakeOutcome.ProtocolMismatch, result.Outcome);
        Assert.Equal("0.3.0", result.PeerVersion);
    }

    [Fact]
    public async Task ClientWithProtocol1_AndWrongKey_IsRejected_NotMismatch()
    {
        // Ohne gültigen Nachweis darf eine fremde Version keine Suche nach Updates auslösen.
        await using Pair pair = await Pair.ConnectAsync();
        Task<HandshakeResult> host = Handshake.HostAsync(pair.Host, Key, "1.0.0", CancellationToken.None);

        (MessageType _, byte[] _) = await ReceiveOneAsync(pair.Client);
        byte[] response = [.. new byte[Handshake.NonceSize], .. new byte[32], .. LegacyVersions(1, "0.3.0")];
        await pair.Client.SendAsync(MessageType.HelloResponse, response);

        Assert.Equal(HandshakeOutcome.Rejected, (await host).Outcome);
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

    /// <summary>Versionen wie in Protokoll 1: ohne Feld für den öffentlichen Schlüssel.</summary>
    private static byte[] LegacyVersions(ushort protocol, string version)
    {
        byte[] p = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(p, protocol);
        return [.. p, .. Encoding.UTF8.GetBytes(version)];
    }

    /// <summary>Nachweis wie in Protokoll 1, nur über die beiden Zufallswerte.</summary>
    private static byte[] LegacyMac(string role, byte[] hostNonce, byte[] clientNonce)
    {
        byte[] signed = [.. Encoding.ASCII.GetBytes("InputFusion-" + role), .. hostNonce, .. clientNonce];
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), signed);
    }

    private static async Task<(MessageType Type, byte[] Payload)> ReceiveOneAsync(MessageConnection connection)
    {
        await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync())
        {
            return (type, payload);
        }

        throw new InvalidOperationException("Verbindung ohne Nachricht beendet.");
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
