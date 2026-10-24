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

    /// <summary>Speichert eine neue Konfiguration; bei Fehler die Meldung, sonst null.</summary>
    public delegate string? ConfigSaver(ClientConfig config);

    /// <param name="saveConfig">Null, wenn die Konfiguration feststeht (<c>--relay</c>); dann lehnt der Dienst Änderungen aus den Einstellungen ab.</param>
    /// <param name="protocolMismatch">Wird gerufen, wenn der Host eine andere Protokollversion spricht; der Dienst sucht dann nach einem Update.</param>
    public static async Task RunAsync(ConfigLoader loadConfig, ConfigSaver? saveConfig, bool asService, Action<string> log, CancellationToken ct,
        Action? protocolMismatch = null)
    {
        ClientStatus status = new ClientStatus();
        // Der laufende Versuch samt Verbindung. Nach dem Speichern einer neuen Konfiguration wird er abgebrochen, und
        // die Schleife verbindet sofort mit der neuen, statt auf den nächsten Abbruch oder Versuch zu warten.
        CancellationTokenSource? attempt = null;
        ConfigSaver? save = saveConfig is null ? null : config =>
        {
            string? error = saveConfig(config);
            if (error is null)
            {
                log($"Konfiguration geändert: Host {config.Host}, Port {config.Port}{(config.Ip is null ? "" : $", IP {config.Ip}")}.");
                try { attempt?.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            return error;
        };

        Task injector = new SessionSupervisor("Injector", Injector.Argument, asUser: false, asService, log).RunAsync(ct);
        Task tray = new SessionSupervisor("Tray", ClientTray.ArgumentsFor(Environment.ProcessId), asUser: true, asService, log).RunAsync(ct);
        Task settings = new ClientSettingsServer(status, save, log).RunAsync(ct);

        TimeSpan delay = _minRetryDelay;
        string? lastProblem = null;
        while (!ct.IsCancellationRequested)
        {
            using CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt = current;
            ClientConfig? config = loadConfig(out string? error);
            status.SetConfig(config);
            string? problem = error;
            bool wasConnected = false;
            try
            {
                if (config is not null)
                {
                    (problem, wasConnected) = await AttemptAsync(config, status, log, protocolMismatch, current.Token);
                }

                if (wasConnected)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        log("Verbindung zum Host beendet, verbinde neu.");
                    }

                    delay = _minRetryDelay;
                    lastProblem = null;
                    continue;
                }

                // Dieselbe Störung nur einmal melden, sonst läuft das Ereignisprotokoll über, solange der PC aus ist.
                if (problem is not null && problem != lastProblem)
                {
                    log($"{problem} Neuer Versuch spätestens alle {_maxRetryDelay.TotalSeconds:0} s.");
                }

                lastProblem = problem;
                status.Update(s => s with { Problem = problem });
                await Task.Delay(delay, current.Token);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxRetryDelay.Ticks));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Neue Konfiguration gespeichert: sofort damit verbinden.
                delay = _minRetryDelay;
                lastProblem = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await injector;
        await tray;
        await settings;
    }

    /// <summary>Ein Versuch: verbinden, anmelden und, wenn das klappt, Eingaben weiterreichen, bis die Verbindung endet.</summary>
    /// <returns>Die Störung, wenn keine Verbindung zustande kam; ob eine Verbindung bestand.</returns>
    private static async Task<(string? Problem, bool WasConnected)> AttemptAsync(ClientConfig config, ClientStatus status, Action<string> log,
        Action? protocolMismatch, CancellationToken ct)
    {
        (TcpClient? tcp, string? connectError) = await ConnectAsync(config, ct);
        if (tcp is null)
        {
            return (connectError, false);
        }

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
                return (null, true);
            }
        }

        if (handshake.Outcome == HandshakeOutcome.ProtocolMismatch)
        {
            protocolMismatch?.Invoke();
            return ($"Host {config.Host} hat Version {handshake.PeerVersion} mit Protokoll {handshake.PeerProtocol}, "
                + $"dieser Client {AppVersion.Current} mit Protokoll {ProtocolVersion.Current}. Suche nach einem Update.", false);
        }

        // Falscher Schlüssel: wie ein nicht erreichbarer Host mit wachsendem Abstand neu versuchen, damit eine Korrektur
        // in den Einstellungen oder in client.json ohne Neustart greift.
        return ($"Host {config.Host} hat die Anmeldung abgelehnt oder sich nicht ausgewiesen; stimmt der Schlüssel?", false);
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
