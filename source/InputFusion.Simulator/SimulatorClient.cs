using System.IO;
using System.Net.Sockets;
using System.Threading.Channels;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Simulator;

/// <summary>
/// Die Verbindung des Simulators zum Host: verbindet sich wie der echte Client, meldet die simulierten Monitore und
/// reicht jede Nachricht an den <see cref="SimulatedDesktop"/> weiter. Bricht die Verbindung ab, wird neu verbunden.
/// </summary>
internal sealed class SimulatorClient(SimulatorOptions options, SimulatedDesktop desktop, Action<String> log)
{
    /// <summary>Der Stand der Verbindung als Text für die Anzeige.</summary>
    public String Connection { get; private set; } = "Nicht verbunden";

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan retry = TimeSpan.FromSeconds(1);
            try
            {
                retry = await ConnectOnceAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                Connection = $"Kein Host unter {options.Host}:{options.Port}, neuer Versuch …";
            }

            await Task.Delay(retry, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>Eine Verbindung von Aufbau bis Abbruch; liefert die Wartezeit bis zum nächsten Versuch.</summary>
    private async Task<TimeSpan> ConnectOnceAsync(CancellationToken ct)
    {
        using TcpClient tcp = new TcpClient();
        Connection = $"Verbinde zu {options.Host}:{options.Port} …";
        await tcp.ConnectAsync(options.Host, options.Port, ct);
        using MessageConnection conn = MessageConnection.FromTcp(tcp);
        HandshakeResult handshake = await Handshake.ClientAsync(conn, options.Key, AppVersion.Current, ct);
        if (handshake.Outcome != HandshakeOutcome.Accepted)
        {
            Connection = handshake.Outcome == HandshakeOutcome.ProtocolMismatch
                ? $"Host spricht Protokoll {handshake.PeerProtocol} (Version {handshake.PeerVersion}), der Simulator {ProtocolVersion.Current}. Neuer Versuch …"
                : $"{options.Host}:{options.Port} hat die Anmeldung abgelehnt, stimmt der Schlüssel? Neuer Versuch …";
            return TimeSpan.FromSeconds(3);
        }

        await conn.SendAsync(MessageType.Monitors, MonitorList.ToPayload(options.Monitors), ct);
        await conn.SendAsync(MessageType.ClientInfo, new ClientInfo($"{Environment.MachineName} (Simulator)", null, Environment.UserName).ToPayload(), ct);
        Connection = $"Verbunden mit {options.Host}:{options.Port}";
        log(Connection);

        await RunSessionAsync(conn, ct);
        desktop.Disconnected();
        log("Verbindung vom Host beendet");
        return TimeSpan.FromSeconds(1);
    }

    private async Task RunSessionAsync(MessageConnection conn, CancellationToken ct)
    {
        // Heartbeat zum Host, sonst trennt er nach dem Timeout; schweigt der Host, neu verbinden.
        using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task monitor = Task.Run(async () =>
        {
            if (await conn.MonitorAsync(session.Token))
            {
                log("Host antwortet nicht mehr");
                session.Cancel();
            }
        }, CancellationToken.None);
        try
        {
            if (options.DelayMs > 0)
            {
                await ReceiveDelayedAsync(conn, session.Token);
            }
            else
            {
                await foreach ((MessageType type, Byte[] payload) in conn.ReceiveAsync(session.Token))
                {
                    await ApplyAsync(conn, type, payload, session.Token);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        finally
        {
            session.Cancel();
            await monitor;
        }
    }

    /// <summary>Hält jede Nachricht um die eingestellte Latenz zurück, ohne den Durchsatz zu drosseln.</summary>
    private async Task ReceiveDelayedAsync(MessageConnection conn, CancellationToken ct)
    {
        Channel<(DateTime Due, MessageType Type, Byte[] Payload)> delayed = Channel.CreateUnbounded<(DateTime Due, MessageType Type, Byte[] Payload)>();
        Task apply = Task.Run(async () =>
        {
            await foreach ((DateTime due, MessageType type, Byte[] payload) in delayed.Reader.ReadAllAsync(ct))
            {
                TimeSpan wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                await ApplyAsync(conn, type, payload, ct);
            }
        }, ct);

        await foreach ((MessageType type, Byte[] payload) in conn.ReceiveAsync(ct))
        {
            delayed.Writer.TryWrite((DateTime.UtcNow.AddMilliseconds(options.DelayMs), type, payload));
        }

        delayed.Writer.Complete();
        await apply;
    }

    private async Task ApplyAsync(MessageConnection conn, MessageType type, Byte[] payload, CancellationToken ct)
    {
        desktop.Apply(type, payload);
        if (desktop.TryTakeExitRequest() is { } exit)
        {
            await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), ct);
        }
    }
}
