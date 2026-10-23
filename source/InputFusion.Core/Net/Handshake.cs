using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using InputFusion.Core.Protocol;

namespace InputFusion.Core.Net;

/// <summary>
/// Gegenseitige Anmeldung mit dem gemeinsamen Schlüssel, bevor irgendeine Eingabe fließt. Der Schlüssel selbst
/// geht nie über das Netz, nur HMAC-SHA256 über zwei Zufallswerte:
/// <list type="number">
/// <item>Host → Client <see cref="MessageType.Hello"/>: Zufallswert, Protokoll- und App-Version des Hosts.</item>
/// <item>Client → Host <see cref="MessageType.HelloResponse"/>: Zufallswert des Clients, sein Nachweis, seine Versionen.</item>
/// <item>Host → Client <see cref="MessageType.HelloAccept"/>: Nachweis des Hosts.</item>
/// </list>
/// Gegenseitig, weil der Injector als SYSTEM abspielt: Ein fremder Host darf dem Notebook keine Eingaben schicken.
/// Ohne TLS bleibt der Verkehr danach lesbar; die Verschlüsselung ist ein eigener Schritt.
///
/// <para>Host und Client aktualisieren sich unabhängig voneinander. Sprechen sie verschiedene
/// <see cref="ProtocolVersion"/>, endet die Anmeldung mit <see cref="HandshakeOutcome.ProtocolMismatch"/>; die
/// Aufrufer suchen dann sofort nach einem Update.</para>
/// </summary>
public static class Handshake
{
    public const int NonceSize = 32;
    private const int MacSize = 32;

    /// <summary>So lange darf die Anmeldung insgesamt dauern.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Hostseite. Bei <see cref="HandshakeOutcome.Accepted"/> ist auch der Nachweis des Hosts verschickt.</summary>
    public static async Task<HandshakeResult> HostAsync(MessageConnection connection, string key, string appVersion, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            byte[] hostNonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] hello = [.. hostNonce, .. Versions(appVersion)];
            await connection.SendAsync(MessageType.Hello, hello, timeout.Token);

            byte[]? response = await ReceiveAsync(connection, MessageType.HelloResponse, NonceSize + MacSize + 2, timeout.Token);
            if (response is null)
            {
                return HandshakeResult.Failed;
            }

            byte[] clientNonce = response[..NonceSize];
            if (!CryptographicOperations.FixedTimeEquals(response.AsSpan(NonceSize, MacSize), Mac(key, "client", hostNonce, clientNonce)))
            {
                return HandshakeResult.Rejected;
            }

            (ushort protocol, string version) = ReadVersions(response.AsSpan(NonceSize + MacSize));
            if (protocol != ProtocolVersion.Current)
            {
                return new HandshakeResult(HandshakeOutcome.ProtocolMismatch, protocol, version);
            }

            await connection.SendAsync(MessageType.HelloAccept, Mac(key, "host", hostNonce, clientNonce), timeout.Token);
            return new HandshakeResult(HandshakeOutcome.Accepted, protocol, version);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            ct.ThrowIfCancellationRequested();
            return HandshakeResult.Failed;
        }
    }

    /// <summary>Clientseite. <see cref="HandshakeOutcome.Accepted"/>, wenn der Host angenommen hat und seinerseits
    /// den Schlüssel kennt. Bei abweichender Protokollversion antwortet der Client trotzdem, damit der Host im
    /// Protokoll sieht, welche Version anklopft, und wartet dann nicht mehr auf die Annahme.</summary>
    public static async Task<HandshakeResult> ClientAsync(MessageConnection connection, string key, string appVersion, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            byte[]? hello = await ReceiveAsync(connection, MessageType.Hello, NonceSize + 2, timeout.Token);
            if (hello is null)
            {
                return HandshakeResult.Failed;
            }

            byte[] hostNonce = hello[..NonceSize];
            (ushort protocol, string version) = ReadVersions(hello.AsSpan(NonceSize));
            byte[] clientNonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] response = [.. clientNonce, .. Mac(key, "client", hostNonce, clientNonce), .. Versions(appVersion)];
            await connection.SendAsync(MessageType.HelloResponse, response, timeout.Token);
            if (protocol != ProtocolVersion.Current)
            {
                return new HandshakeResult(HandshakeOutcome.ProtocolMismatch, protocol, version);
            }

            byte[]? accept = await ReceiveAsync(connection, MessageType.HelloAccept, MacSize, timeout.Token);
            if (accept is null || accept.Length != MacSize
                || !CryptographicOperations.FixedTimeEquals(accept, Mac(key, "host", hostNonce, clientNonce)))
            {
                return HandshakeResult.Rejected;
            }

            return new HandshakeResult(HandshakeOutcome.Accepted, protocol, version);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            ct.ThrowIfCancellationRequested();
            return HandshakeResult.Failed;
        }
    }

    /// <summary>Die Rolle steht mit im HMAC, damit ein Nachweis der einen Seite nicht als der der anderen durchgeht.</summary>
    private static byte[] Mac(string key, string role, byte[] hostNonce, byte[] clientNonce)
    {
        byte[] message = [.. Encoding.ASCII.GetBytes("InputFusion-" + role), .. hostNonce, .. clientNonce];
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), message);
    }

    /// <summary>Protokollversion (2 Byte) und danach die App-Version als UTF-8 bis zum Ende der Nachricht.</summary>
    private static byte[] Versions(string appVersion)
    {
        byte[] protocol = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(protocol, ProtocolVersion.Current);
        return [.. protocol, .. Encoding.UTF8.GetBytes(appVersion)];
    }

    private static (ushort Protocol, string Version) ReadVersions(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(data), Encoding.UTF8.GetString(data[2..]));

    /// <summary>Die nächste Nachricht, wenn sie den erwarteten Typ und mindestens die erwartete Länge hat, sonst null.</summary>
    private static async Task<byte[]?> ReceiveAsync(MessageConnection connection, MessageType expected, int minLength, CancellationToken ct)
    {
        await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(ct))
        {
            return type == expected && payload.Length >= minLength ? payload : null;
        }

        return null;
    }
}

public enum HandshakeOutcome
{
    Accepted,

    /// <summary>Ein Nachweis stimmte nicht: anderer Schlüssel.</summary>
    Rejected,

    /// <summary>Schlüssel stimmt, aber die Gegenseite spricht eine andere Protokollversion.</summary>
    ProtocolMismatch,

    /// <summary>Zeitüberschreitung, Verbindung weg oder unerwartete Nachricht.</summary>
    Failed,
}

/// <summary>Ergebnis der Anmeldung mit den Versionen der Gegenseite, soweit sie übermittelt wurden.</summary>
public sealed record HandshakeResult(HandshakeOutcome Outcome, ushort PeerProtocol, string PeerVersion)
{
    public static HandshakeResult Rejected { get; } = new(HandshakeOutcome.Rejected, 0, "");

    public static HandshakeResult Failed { get; } = new(HandshakeOutcome.Failed, 0, "");
}
