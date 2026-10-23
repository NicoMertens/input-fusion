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
/// <item>Host → Client <see cref="MessageType.Hello"/>: Zufallswert des Hosts.</item>
/// <item>Client → Host <see cref="MessageType.HelloResponse"/>: Zufallswert des Clients und sein Nachweis.</item>
/// <item>Host → Client <see cref="MessageType.HelloAccept"/>: Nachweis des Hosts.</item>
/// </list>
/// Gegenseitig, weil der Injector als SYSTEM abspielt: Ein fremder Host darf dem Notebook keine Eingaben schicken.
/// Ohne TLS bleibt der Verkehr danach lesbar; die Verschlüsselung ist ein eigener Schritt.
/// </summary>
public static class Handshake
{
    public const int NonceSize = 32;
    private const int MacSize = 32;

    /// <summary>So lange darf die Anmeldung insgesamt dauern.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Hostseite. True, wenn der Client den Schlüssel kennt; dann ist auch der Nachweis des Hosts verschickt.</summary>
    public static async Task<bool> HostAsync(MessageConnection connection, string key, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            byte[] hostNonce = RandomNumberGenerator.GetBytes(NonceSize);
            await connection.SendAsync(MessageType.Hello, hostNonce, timeout.Token);

            var response = await ReceiveAsync(connection, MessageType.HelloResponse, NonceSize + MacSize, timeout.Token);
            if (response is null)
                return false;
            byte[] clientNonce = response[..NonceSize];
            if (!CryptographicOperations.FixedTimeEquals(response.AsSpan(NonceSize), Mac(key, "client", hostNonce, clientNonce)))
                return false;

            await connection.SendAsync(MessageType.HelloAccept, Mac(key, "host", hostNonce, clientNonce), timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    /// <summary>Clientseite. True, wenn der Host angenommen hat und seinerseits den Schlüssel kennt.</summary>
    public static async Task<bool> ClientAsync(MessageConnection connection, string key, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            byte[]? hostNonce = await ReceiveAsync(connection, MessageType.Hello, NonceSize, timeout.Token);
            if (hostNonce is null)
                return false;
            byte[] clientNonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] response = [.. clientNonce, .. Mac(key, "client", hostNonce, clientNonce)];
            await connection.SendAsync(MessageType.HelloResponse, response, timeout.Token);

            byte[]? accept = await ReceiveAsync(connection, MessageType.HelloAccept, MacSize, timeout.Token);
            return accept is not null && CryptographicOperations.FixedTimeEquals(accept, Mac(key, "host", hostNonce, clientNonce));
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    /// <summary>Die Rolle steht mit im HMAC, damit ein Nachweis der einen Seite nicht als der der anderen durchgeht.</summary>
    private static byte[] Mac(string key, string role, byte[] hostNonce, byte[] clientNonce)
    {
        byte[] message = [.. Encoding.ASCII.GetBytes("InputFusion-" + role), .. hostNonce, .. clientNonce];
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), message);
    }

    /// <summary>Die nächste Nachricht, wenn sie den erwarteten Typ und die erwartete Länge hat, sonst null.</summary>
    private static async Task<byte[]?> ReceiveAsync(MessageConnection connection, MessageType expected, int length, CancellationToken ct)
    {
        await foreach (var (type, payload) in connection.ReceiveAsync(ct))
            return type == expected && payload.Length == length ? payload : null;
        return null;
    }
}
