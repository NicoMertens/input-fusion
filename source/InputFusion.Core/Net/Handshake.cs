using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Core.Net;

/// <summary>
/// Gegenseitige Anmeldung mit dem gemeinsamen Schlüssel, bevor irgendeine Eingabe fließt, und danach die
/// Verschlüsselung. Der Schlüssel selbst geht nie über das Netz, nur HMAC-SHA256 über die Zufallswerte und die
/// ECDH-Schlüssel beider Seiten:
/// <list type="number">
/// <item>Host → Client <see cref="MessageType.Hello"/>: Zufallswert, Versionen, öffentlicher ECDH-Schlüssel des Hosts.</item>
/// <item>Client → Host <see cref="MessageType.HelloResponse"/>: Zufallswert des Clients, sein Nachweis, seine Versionen
/// und sein öffentlicher Schlüssel.</item>
/// <item>Host → Client <see cref="MessageType.HelloAccept"/>: Nachweis des Hosts.</item>
/// </list>
/// Gegenseitig, weil der Injector als SYSTEM abspielt: Ein fremder Host darf dem Notebook keine Eingaben schicken.
/// Weil beide Nachweise die ECDH-Schlüssel abdecken, kann sich niemand ohne den gemeinsamen Schlüssel
/// dazwischenschalten. Aus dem ECDH-Geheimnis entsteht je Richtung ein AES-GCM-Schlüssel
/// (<see cref="FrameCipher"/>). Die ECDH-Schlüssel gelten nur für diese Verbindung; mitgeschnittener Verkehr
/// bleibt deshalb auch dann unlesbar, wenn der gemeinsame Schlüssel später bekannt wird.
///
/// <para>Host und Client aktualisieren sich unabhängig voneinander. Sprechen sie verschiedene
/// <see cref="ProtocolVersion"/>, endet die Anmeldung mit <see cref="HandshakeOutcome.ProtocolMismatch"/>; die
/// Aufrufer suchen dann sofort nach einem Update. Damit das auch mit Protokoll 1 ohne Verschlüsselung klappt,
/// bleiben Zufallswert, Nachweis und Protokollversion vorn an derselben Stelle, und bei abweichender Version
/// gilt der Nachweis von Protokoll 1, nur über die Zufallswerte.</para>
/// </summary>
public static class Handshake
{
    public const Int32 NonceSize = 32;
    private const Int32 MacSize = 32;

    /// <summary>Ab dieser Protokollversion steht hinter der Protokollversion der öffentliche ECDH-Schlüssel.</summary>
    private const UInt16 FirstEncryptedProtocol = 2;

    /// <summary>So lange darf die Anmeldung insgesamt dauern.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Hostseite. Bei <see cref="HandshakeOutcome.Accepted"/> ist auch der Nachweis des Hosts verschickt und
    /// die Verbindung verschlüsselt.</summary>
    public static async Task<HandshakeResult> HostAsync(MessageConnection connection, String key, String appVersion, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            Byte[] hostPublic = ecdh.ExportSubjectPublicKeyInfo();
            Byte[] hostNonce = RandomNumberGenerator.GetBytes(NonceSize);
            Byte[] hello = [.. hostNonce, .. Versions(appVersion, hostPublic)];
            await connection.SendAsync(MessageType.Hello, hello, timeout.Token);

            Byte[]? response = await ReceiveAsync(connection, MessageType.HelloResponse, NonceSize + MacSize + 2, timeout.Token);
            if (response is null || ReadVersions(response.AsSpan(NonceSize + MacSize)) is not { } peer)
            {
                return HandshakeResult.Failed;
            }

            Byte[] clientNonce = response[..NonceSize];
            Byte[] clientMac = response[NonceSize..(NonceSize + MacSize)];
            if (peer.Protocol != ProtocolVersion.Current)
            {
                // Erst der Nachweis, dann die Version: Ein Fremder darf keine Suche nach Updates auslösen.
                return CryptographicOperations.FixedTimeEquals(clientMac, Mac(key, "client", hostNonce, clientNonce, [], []))
                    ? new HandshakeResult(HandshakeOutcome.ProtocolMismatch, peer.Protocol, peer.Version)
                    : HandshakeResult.Rejected;
            }

            if (!CryptographicOperations.FixedTimeEquals(clientMac, Mac(key, "client", hostNonce, clientNonce, hostPublic, peer.PublicKey)))
            {
                return HandshakeResult.Rejected;
            }

            (Byte[] hostToClient, Byte[] clientToHost) = SessionKeys(ecdh, peer.PublicKey, key, hostNonce, clientNonce);
            await connection.SendAsync(MessageType.HelloAccept, Mac(key, "host", hostNonce, clientNonce, hostPublic, peer.PublicKey), timeout.Token);
            connection.EnableEncryption(hostToClient, clientToHost);
            return new HandshakeResult(HandshakeOutcome.Accepted, peer.Protocol, peer.Version);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or CryptographicException or ArgumentException)
        {
            ct.ThrowIfCancellationRequested();
            return HandshakeResult.Failed;
        }
    }

    /// <summary>Clientseite. <see cref="HandshakeOutcome.Accepted"/>, wenn der Host angenommen hat und seinerseits
    /// den Schlüssel kennt; die Verbindung ist dann verschlüsselt. Bei abweichender Protokollversion antwortet der
    /// Client trotzdem, damit der Host im Protokoll sieht, welche Version anklopft, und wartet dann nicht mehr auf
    /// die Annahme.</summary>
    public static async Task<HandshakeResult> ClientAsync(MessageConnection connection, String key, String appVersion, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            Byte[]? hello = await ReceiveAsync(connection, MessageType.Hello, NonceSize + 2, timeout.Token);
            if (hello is null || ReadVersions(hello.AsSpan(NonceSize)) is not { } peer)
            {
                return HandshakeResult.Failed;
            }

            Byte[] hostNonce = hello[..NonceSize];
            Byte[] clientNonce = RandomNumberGenerator.GetBytes(NonceSize);
            if (peer.Protocol != ProtocolVersion.Current)
            {
                // In der Form, die der Host versteht: Nachweis nur über die Zufallswerte; ein Host mit Protokoll 1
                // kennt auch das Feld für den öffentlichen Schlüssel nicht.
                Byte[] versions = peer.Protocol >= FirstEncryptedProtocol ? Versions(appVersion, []) : LegacyVersions(appVersion);
                Byte[] mismatch = [.. clientNonce, .. Mac(key, "client", hostNonce, clientNonce, [], []), .. versions];
                await connection.SendAsync(MessageType.HelloResponse, mismatch, timeout.Token);
                return new HandshakeResult(HandshakeOutcome.ProtocolMismatch, peer.Protocol, peer.Version);
            }

            using ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            Byte[] clientPublic = ecdh.ExportSubjectPublicKeyInfo();
            Byte[] response = [.. clientNonce, .. Mac(key, "client", hostNonce, clientNonce, peer.PublicKey, clientPublic), .. Versions(appVersion, clientPublic)];
            await connection.SendAsync(MessageType.HelloResponse, response, timeout.Token);

            Byte[]? accept = await ReceiveAsync(connection, MessageType.HelloAccept, MacSize, timeout.Token);
            if (accept is null || accept.Length != MacSize
                || !CryptographicOperations.FixedTimeEquals(accept, Mac(key, "host", hostNonce, clientNonce, peer.PublicKey, clientPublic)))
            {
                return HandshakeResult.Rejected;
            }

            (Byte[] hostToClient, Byte[] clientToHost) = SessionKeys(ecdh, peer.PublicKey, key, hostNonce, clientNonce);
            connection.EnableEncryption(clientToHost, hostToClient);
            return new HandshakeResult(HandshakeOutcome.Accepted, peer.Protocol, peer.Version);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or CryptographicException or ArgumentException)
        {
            ct.ThrowIfCancellationRequested();
            return HandshakeResult.Failed;
        }
    }

    /// <summary>Die Rolle steht mit im HMAC, damit ein Nachweis der einen Seite nicht als der der anderen durchgeht;
    /// die öffentlichen Schlüssel, damit niemand sie austauschen kann. Ohne Schlüssel ist es der Nachweis von
    /// Protokoll 1.</summary>
    private static Byte[] Mac(String key, String role, Byte[] hostNonce, Byte[] clientNonce, Byte[] hostPublic, Byte[] clientPublic)
    {
        Byte[] message = [.. Encoding.ASCII.GetBytes("InputFusion-" + role), .. hostNonce, .. clientNonce, .. hostPublic, .. clientPublic];
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), message);
    }

    /// <summary>Je Richtung ein eigener Schlüssel per HKDF aus dem ECDH-Geheimnis. Der gemeinsame Schlüssel steckt
    /// über das Salz mit darin, damit die Sitzungsschlüssel nicht allein am ECDH hängen.</summary>
    private static (Byte[] HostToClient, Byte[] ClientToHost) SessionKeys(ECDiffieHellman own, Byte[] peerPublic, String key, Byte[] hostNonce, Byte[] clientNonce)
    {
        using ECDiffieHellman peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublic, out _);
        Byte[] secret = own.DeriveRawSecretAgreement(peer.PublicKey);
        try
        {
            Byte[] context = [.. Encoding.ASCII.GetBytes("InputFusion-session"), .. hostNonce, .. clientNonce];
            Byte[] salt = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), context);
            return (
                HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, FrameCipher.KeySize, salt, Encoding.ASCII.GetBytes("InputFusion-h2c")),
                HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, FrameCipher.KeySize, salt, Encoding.ASCII.GetBytes("InputFusion-c2h")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>Protokollversion (2 Byte), Länge (1 Byte) und öffentlicher Schlüssel, danach die App-Version als
    /// UTF-8 bis zum Ende der Nachricht.</summary>
    private static Byte[] Versions(String appVersion, Byte[] publicKey) =>
        [.. Protocol(), (Byte)publicKey.Length, .. publicKey, .. Encoding.UTF8.GetBytes(appVersion)];

    /// <summary>Wie in Protokoll 1: ohne Feld für den öffentlichen Schlüssel.</summary>
    private static Byte[] LegacyVersions(String appVersion) => [.. Protocol(), .. Encoding.UTF8.GetBytes(appVersion)];

    private static Byte[] Protocol()
    {
        Byte[] protocol = new Byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(protocol, ProtocolVersion.Current);
        return protocol;
    }

    /// <summary>Die Versionen und, ab <see cref="FirstEncryptedProtocol"/>, der öffentliche Schlüssel; null, wenn
    /// die Länge nicht passt.</summary>
    private static PeerInfo? ReadVersions(ReadOnlySpan<Byte> data)
    {
        UInt16 protocol = BinaryPrimitives.ReadUInt16LittleEndian(data);
        ReadOnlySpan<Byte> rest = data[2..];
        Byte[] publicKey = [];
        if (protocol >= FirstEncryptedProtocol)
        {
            if (rest.IsEmpty || rest.Length < 1 + rest[0])
            {
                return null;
            }

            publicKey = rest.Slice(1, rest[0]).ToArray();
            rest = rest[(1 + publicKey.Length)..];
        }

        return new PeerInfo(protocol, publicKey, Encoding.UTF8.GetString(rest));
    }

    /// <summary>Die nächste Nachricht, wenn sie den erwarteten Typ und mindestens die erwartete Länge hat, sonst null.</summary>
    private static async Task<Byte[]?> ReceiveAsync(MessageConnection connection, MessageType expected, Int32 minLength, CancellationToken ct)
    {
        await foreach ((MessageType type, Byte[] payload) in connection.ReceiveAsync(ct))
        {
            return type == expected && payload.Length >= minLength ? payload : null;
        }

        return null;
    }

    private sealed record PeerInfo(UInt16 Protocol, Byte[] PublicKey, String Version);
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
public sealed record HandshakeResult(HandshakeOutcome Outcome, UInt16 PeerProtocol, String PeerVersion)
{
    public static HandshakeResult Rejected { get; } = new(HandshakeOutcome.Rejected, 0, "");

    public static HandshakeResult Failed { get; } = new(HandshakeOutcome.Failed, 0, "");
}
