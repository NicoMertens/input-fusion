using System.Net;
using System.Net.Sockets;

namespace InputFusion.Core.Net;

/// <summary>Adressen des Hosts für den Verbindungsaufbau und für die Anzeige.</summary>
public static class HostAddresses
{
    /// <summary>
    /// Reihenfolge der Verbindungsversuche: aufgelöste IPv4-Adressen, dann die feste IP, dann aufgelöste IPv6-Adressen,
    /// ohne Doppelte. IPv4 zuerst, weil ältere Hosts nur dort lauschen und Versuche an IPv6-Adressen hängen bleiben können,
    /// statt abgewiesen zu werden.
    /// </summary>
    public static IReadOnlyList<IPAddress> Order(IEnumerable<IPAddress> resolved, IEnumerable<IPAddress> fixedIps)
    {
        IPAddress[] all = resolved.Select(Normalize).ToArray();
        return all.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Concat(fixedIps.Select(Normalize))
            .Concat(all.Where(a => a.AddressFamily != AddressFamily.InterNetwork))
            .Distinct()
            .ToArray();
    }

    /// <summary>Zur Anzeige: Ein Dual-Mode-Socket meldet eine IPv4-Gegenstelle als ::ffff:a.b.c.d.</summary>
    public static String Display(EndPoint? endPoint) =>
        endPoint is IPEndPoint ip ? Normalize(ip.Address).ToString() : "?";

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
