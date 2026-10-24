using System.Net;
using System.Net.Sockets;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Die Dienst-Rolle: hält Injector und Tray-Symbol am Leben und die Verbindung zum Host. Ist der Host nicht erreichbar
/// oder bricht die Verbindung ab, wird es mit wachsendem Abstand bis <see cref="_maxRetryDelay"/> erneut
/// versucht. Läuft im Windows-Dienst (<see cref="Worker"/>) und zum Entwickeln als Konsole (<c>--relay</c>).
/// </summary>
internal static class ClientService
{
    public const string ServiceName = "InputFusionClient";

    private static readonly TimeSpan _minRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(5);

    public delegate ClientConfig? ConfigLoader(out string? error);

    /// <param name="protocolMismatch">Wird gerufen, wenn der Host eine andere Protokollversion spricht; der Dienst sucht dann nach einem Update.</param>
    public static async Task RunAsync(ConfigLoader loadConfig, bool asService, Action<string> log, CancellationToken ct, Action? protocolMismatch = null)
    {
        ClientStatus status = new ClientStatus();
        Task injector = new SessionSupervisor("Injector", Injector.Argument, asUser: false, asService, log).RunAsync(ct);
        Task tray = new SessionSupervisor("Tray", ClientTray.ArgumentsFor(Environment.ProcessId), asUser: true, asService, log).RunAsync(ct);
        Task settings = new ClientSettingsServer(status, log).RunAsync(ct);

        TimeSpan delay = _minRetryDelay;
        string? lastProblem = null;
        while (!ct.IsCancellationRequested)
        {
            ClientConfig? config = loadConfig(out string? error);
            string? problem = error;
            status.Update(s => s with { Host = config?.Host, Port = config?.Port ?? s.Port });
            if (config is not null)
            {
                (TcpClient? tcp, string? connectError) = await ConnectAsync(config, ct);
                problem = connectError;
                if (tcp is not null)
                {
                    HandshakeResult handshake;
                    using (tcp)
                    using (MessageConnection conn = MessageConnection.FromTcp(tcp))
                    {
                        handshake = await Handshake.ClientAsync(conn, config.Key, AppVersion.Current, ct);
                        if (handshake.Outcome == HandshakeOutcome.Accepted)
                        {
                            log($"Mit dem Host {tcp.Client.RemoteEndPoint} (Version {handshake.PeerVersion}) verbunden.");
                            IPAddress? ip = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
                            // Der Socket ist Dual-Mode; eine IPv4-Gegenstelle erscheint sonst als ::ffff:a.b.c.d.
                            string? address = (ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip)?.ToString();
                            status.Update(s => s with { Connected = true, HostAddress = address, HostVersion = handshake.PeerVersion, Problem = null });
                            try
                            {
                                await new PipeRelay(conn, log).RunAsync(ct);
                            }
                            finally
                            {
                                status.Update(s => s with { Connected = false, HostAddress = null, HostVersion = null });
                            }
                        }
                    }
                    if (handshake.Outcome == HandshakeOutcome.ProtocolMismatch)
                    {
                        problem = $"Host {config.Host} hat Version {handshake.PeerVersion} mit Protokoll {handshake.PeerProtocol}, "
                            + $"dieser Client {AppVersion.Current} mit Protokoll {ProtocolVersion.Current}. Suche nach einem Update.";
                        protocolMismatch?.Invoke();
                    }
                    else if (handshake.Outcome == HandshakeOutcome.Accepted)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            log("Verbindung zum Host beendet, verbinde neu.");
                        }

                        delay = _minRetryDelay;
                        lastProblem = null;
                        continue;
                    }
                    else
                    {
                        // Falscher Schlüssel: wie ein nicht erreichbarer Host mit wachsendem Abstand neu versuchen,
                        // damit eine Korrektur in client.json ohne Neustart greift.
                        problem = $"Host {config.Host} hat die Anmeldung abgelehnt oder sich nicht ausgewiesen; stimmt der Schlüssel?";
                    }
                }
            }

            // Dieselbe Störung nur einmal melden, sonst läuft das Ereignisprotokoll über, solange der PC aus ist.
            if (problem != lastProblem)
            {
                log($"{problem} Neuer Versuch spätestens alle {_maxRetryDelay.TotalSeconds:0} s.");
            }

            lastProblem = problem;
            status.Update(s => s with { Problem = problem });

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxRetryDelay.Ticks));
        }

        await injector;
        await tray;
        await settings;
    }

    /// <summary>Erst der Rechnername per DNS, dann die feste IP, falls eingetragen.</summary>
    private static async Task<(TcpClient? Client, string? Error)> ConnectAsync(ClientConfig config, CancellationToken ct)
    {
        string? error = null;
        foreach (string? target in config.Ip is null ? [config.Host] : new[] { config.Host, config.Ip })
        {
            TcpClient tcp = new TcpClient();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_connectTimeout);
            try
            {
                await tcp.ConnectAsync(target, config.Port, timeout.Token);
                return (tcp, null);
            }
            catch (SocketException ex)
            {
                error = $"Host {target}:{config.Port} nicht erreichbar ({ex.SocketErrorCode}).";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"Host {target}:{config.Port} antwortet nicht.";
            }
            catch (OperationCanceledException)
            {
                tcp.Dispose();
                return (null, null);
            }
            tcp.Dispose();
        }
        return (null, error);
    }
}
