using System.IO;
using System.Net.Sockets;
using InputFusion.Core.Display;
using InputFusion.Core.Layout;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Meldet dem Host die Monitore dieser Sitzung: sofort und danach bei jeder Änderung (Andocken, Auflösung).
/// Der Injector hat kein Fenster für <c>WM_DISPLAYCHANGE</c>, deshalb fragt er alle zwei Sekunden nach; das
/// Messen kostet kaum etwas.
/// </summary>
internal static class MonitorReporter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static async Task RunAsync(MessageConnection connection, CancellationToken ct)
    {
        IReadOnlyList<MonitorRect> last = [];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var monitors = MonitorEnumerator.GetMonitors();
                if (monitors.Count > 0 && !monitors.SequenceEqual(last))
                {
                    await connection.SendAsync(MessageType.Monitors, MonitorList.ToPayload(monitors), ct);
                    last = monitors;
                }
                await Task.Delay(PollInterval, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
    }
}
