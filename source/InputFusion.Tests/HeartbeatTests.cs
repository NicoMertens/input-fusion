using System.Net;
using System.Net.Sockets;
using InputFusion.Core.Net;

namespace InputFusion.Tests;

/// <summary>
/// Prüft den Heartbeat über eine echte TCP-Verbindung auf localhost: Ein Gegenüber, das schweigt (Notebook
/// im Standby), muss nach dem Timeout erkannt werden; eines, das selbst Heartbeats schickt, nicht.
/// Dauert jeweils einige Sekunden, weil die echten Zeiten gelten.
/// </summary>
public class HeartbeatTests
{
    [Fact]
    public async Task Monitor_DetectsSilentPeer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (a, b) = await ConnectPairAsync(cts.Token);
        using var monitored = a;
        using var silent = b;

        // Die Gegenseite liest zwar (nimmt die Heartbeats ab), schickt aber selbst nichts.
        var draining = Task.Run(async () =>
        {
            try { await foreach (var _ in silent.ReceiveAsync(cts.Token)) { } }
            catch (OperationCanceledException) { }
        });
        var receiving = ReceiveAllAsync(monitored, cts.Token);

        var started = DateTime.UtcNow;
        bool lost = await monitored.MonitorAsync(cts.Token);
        var elapsed = DateTime.UtcNow - started;

        Assert.True(lost);
        Assert.InRange(elapsed, MessageConnection.HeartbeatTimeout, MessageConnection.HeartbeatTimeout + TimeSpan.FromSeconds(2));
        cts.Cancel();
        await Task.WhenAll(draining, receiving);
    }

    [Fact]
    public async Task Monitor_KeepsAnsweringPeer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (a, b) = await ConnectPairAsync(cts.Token);
        using var left = a;
        using var right = b;

        var receivingLeft = ReceiveAllAsync(left, cts.Token);
        var receivingRight = ReceiveAllAsync(right, cts.Token);
        var leftMonitor = left.MonitorAsync(cts.Token);
        var rightMonitor = right.MonitorAsync(cts.Token);

        // Deutlich länger als der Timeout; keine Seite darf die andere für verloren halten.
        await Task.Delay(MessageConnection.HeartbeatTimeout + TimeSpan.FromSeconds(2));
        Assert.False(leftMonitor.IsCompleted);
        Assert.False(rightMonitor.IsCompleted);

        cts.Cancel();
        Assert.False(await leftMonitor);
        Assert.False(await rightMonitor);
        await Task.WhenAll(receivingLeft, receivingRight);
    }

    private static async Task<(MessageConnection, MessageConnection)> ConnectPairAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var client = new TcpClient();
            var accept = listener.AcceptTcpClientAsync(ct).AsTask();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct);
            var server = await accept;
            return (MessageConnection.FromTcp(server), MessageConnection.FromTcp(client));
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Liest wie im Betrieb nebenher mit; erst das setzt die Zeit des letzten Empfangs.</summary>
    private static Task ReceiveAllAsync(MessageConnection connection, CancellationToken ct) => Task.Run(async () =>
    {
        try { await foreach (var _ in connection.ReceiveAsync(ct)) { } }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException) { }
    });
}
