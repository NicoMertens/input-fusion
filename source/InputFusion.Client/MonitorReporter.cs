using System.IO;
using System.Net.Sockets;
using NC2.InputFusion.Core.Display;
using NC2.InputFusion.Core.Layout;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Client;

/// <summary>
/// Meldet dem Host die Monitore dieser Sitzung: sofort und danach bei jeder Änderung (Andocken, Auflösung).
/// Der Injector hat kein Fenster für <c>WM_DISPLAYCHANGE</c>, deshalb fragt er alle zwei Sekunden nach; das
/// Messen kostet kaum etwas.
/// </summary>
internal static class MonitorReporter
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

    public static async Task RunAsync(MessageConnection connection, CancellationToken ct)
    {
        IReadOnlyList<MonitorRect> last = [];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                IReadOnlyList<MonitorRect> monitors = MonitorEnumerator.GetMonitors();
                if (monitors.Count > 0 && !monitors.SequenceEqual(last))
                {
                    await connection.SendAsync(MessageType.Monitors, MonitorList.ToPayload(monitors), ct);
                    last = monitors;
                }
                await Task.Delay(_pollInterval, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
    }
}
