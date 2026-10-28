using System.IO;
using System.Net.Sockets;
using Microsoft.Win32;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Meldet dem Host Name, Windows-Version und angemeldeten Benutzer des Notebooks: sofort nach der Anmeldung und danach
/// bei jeder Änderung. Der Benutzer wechselt, ohne dass der Dienst davon erfährt (Anmelden nach dem Hochfahren
/// geschieht in derselben Sitzung), deshalb fragt er wie <see cref="MonitorReporter"/> alle zwei Sekunden nach.
/// </summary>
internal static class ClientInfoReporter
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);
    private static readonly Lazy<String> _osVersion = new(ReadOsVersion);

    public static async Task RunAsync(MessageConnection host, CancellationToken ct)
    {
        ClientInfo? last = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ClientInfo info = new ClientInfo(Environment.MachineName, _osVersion.Value, ActiveSession.UserName());
                if (info != last)
                {
                    await host.SendAsync(MessageType.ClientInfo, info.ToPayload(), ct);
                    last = info;
                }
                await Task.Delay(_pollInterval, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Etwa „Windows 11 Pro 24H2 (Build 26100.4061)“.</summary>
    private static String ReadOsVersion()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        Int32 build = Environment.OSVersion.Version.Build;
        String product = key?.GetValue("ProductName") as String ?? "Windows";
        // ProductName nennt auch unter Windows 11 noch „Windows 10“; erkennbar ist 11 nur an der Build-Nummer.
        if (build >= 22000)
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        String display = key?.GetValue("DisplayVersion") is String version ? $" {version}" : "";
        String revision = key?.GetValue("UBR") is Int32 ubr ? $".{ubr}" : "";
        return $"{product}{display} (Build {build}{revision})";
    }
}
