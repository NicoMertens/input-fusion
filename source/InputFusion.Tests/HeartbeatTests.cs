using System.Net;
using System.Net.Sockets;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

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
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        (MessageConnection a, MessageConnection b) = await ConnectPairAsync(cts.Token);
        using MessageConnection monitored = a;
        using MessageConnection silent = b;

        // Die Gegenseite liest zwar (nimmt die Heartbeats ab), schickt aber selbst nichts.
        Task draining = Task.Run(async () =>
        {
            try { await foreach ((MessageType Type, byte[] Payload) _ in silent.ReceiveAsync(cts.Token)) { } }
            catch (OperationCanceledException) { }
        });
        Task receiving = ReceiveAllAsync(monitored, cts.Token);

        DateTime started = DateTime.UtcNow;
        bool lost = await monitored.MonitorAsync(cts.Token);
        TimeSpan elapsed = DateTime.UtcNow - started;

        Assert.True(lost);
        Assert.InRange(elapsed, MessageConnection.HeartbeatTimeout, MessageConnection.HeartbeatTimeout + TimeSpan.FromSeconds(2));
        // Wer nur liest, schickt die Heartbeats nicht zurück; sonst gälte er nicht mehr als schweigend.
        Assert.Null(monitored.Stats.RoundTrip);
        cts.Cancel();
        await Task.WhenAll(draining, receiving);
    }

    [Fact]
    public async Task Monitor_KeepsAnsweringPeer()
    {
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        (MessageConnection a, MessageConnection b) = await ConnectPairAsync(cts.Token);
        using MessageConnection left = a;
        using MessageConnection right = b;

        Task receivingLeft = ReceiveAllAsync(left, cts.Token);
        Task receivingRight = ReceiveAllAsync(right, cts.Token);
        Task<bool> leftMonitor = left.MonitorAsync(cts.Token);
        Task<bool> rightMonitor = right.MonitorAsync(cts.Token);

        // Deutlich länger als der Timeout; keine Seite darf die andere für verloren halten.
        await Task.Delay(MessageConnection.HeartbeatTimeout + TimeSpan.FromSeconds(2));
        Assert.False(leftMonitor.IsCompleted);
        Assert.False(rightMonitor.IsCompleted);
        // Beide überwachen, also schicken beide die Heartbeats der anderen zurück, und beide kennen die Laufzeit.
        Assert.InRange(left.Stats.RoundTrip!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.NotNull(right.Stats.RoundTrip);
        Assert.True(left.Stats.SentFrames >= 3 && left.Stats.ReceivedFrames >= 3);

        cts.Cancel();
        Assert.False(await leftMonitor);
        Assert.False(await rightMonitor);
        await Task.WhenAll(receivingLeft, receivingRight);
    }

    private static async Task<(MessageConnection, MessageConnection)> ConnectPairAsync(CancellationToken ct)
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            TcpClient client = new TcpClient();
            Task<TcpClient> accept = listener.AcceptTcpClientAsync(ct).AsTask();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct);
            TcpClient server = await accept;
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
        try { await foreach ((MessageType Type, byte[] Payload) _ in connection.ReceiveAsync(ct)) { } }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException) { }
    });
}
