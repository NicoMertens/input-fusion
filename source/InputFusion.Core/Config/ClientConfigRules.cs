using System.Net;
using System.Net.Sockets;
using InputFusion.Core.Net;

namespace InputFusion.Core.Config;

/// <summary>
/// Regeln für die Verbindungsdaten des Notebooks. Die Einstellungen prüfen damit schon bei der Eingabe, der Dienst
/// noch einmal vor dem Speichern, weil die Anfrage von einer beliebigen Anwendung kommen kann.
/// </summary>
public static class ClientConfigRules
{
    /// <returns>Die erste Beanstandung oder null.</returns>
    public static string? Validate(string host, string? ip, int port, string key)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "Bitte den PC-Namen des Hosts angeben.";
        }

        if (host.Trim() != host || host.Any(char.IsWhiteSpace))
        {
            return "Der PC-Name darf keine Leerzeichen enthalten.";
        }

        // TryParse nimmt auch Kurzformen wie 192.168.1 (= 192.168.0.1); bei IPv4 deshalb vier Teile verlangen.
        if (!string.IsNullOrEmpty(ip)
            && (!IPAddress.TryParse(ip, out IPAddress? parsed)
                || (parsed.AddressFamily == AddressFamily.InterNetwork && ip.Count(c => c == '.') != 3)))
        {
            return "Die feste IP ist keine gültige Adresse.";
        }

        return Ports.Validate(port) ?? HostConfig.ValidateKey(key);
    }

    /// <returns>Die erste Beanstandung, bei mehreren PCs mit dessen Nummer, oder null.</returns>
    public static string? Validate(IReadOnlyList<HostEntry> hosts)
    {
        if (hosts.Count == 0)
        {
            return "Bitte mindestens einen PC eintragen.";
        }

        for (int i = 0; i < hosts.Count; i++)
        {
            HostEntry entry = hosts[i];
            if (Validate(entry.Host, entry.Ip, entry.Port, entry.Key) is { } error)
            {
                return hosts.Count == 1 ? error : $"{i + 1}. PC: {error}";
            }
        }

        return null;
    }
}
