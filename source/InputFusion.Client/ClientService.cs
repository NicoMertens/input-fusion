using System.Net;
using System.Net.Sockets;
using InputFusion.Core.Config;
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
    // Je Adresse; drei Sekunden lassen Platz für eine Wiederholung des Verbindungsaufbaus im WLAN.
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _resolveTimeout = TimeSpan.FromSeconds(5);

    public delegate ClientConfig? ConfigLoader(out string? error);

    /// <summary>Speichert eine neue Konfiguration; bei Fehler die Meldung, sonst null.</summary>
    public delegate string? ConfigSaver(ClientConfig config);

    /// <param name="saveConfig">Null, wenn die Konfiguration feststeht (<c>--relay</c>); dann lehnt der Dienst Änderungen aus den Einstellungen ab.</param>
    /// <param name="protocolMismatch">Wird gerufen, wenn der Host eine andere Protokollversion spricht; der Dienst sucht dann nach einem Update.</param>
    public static async Task RunAsync(ConfigLoader loadConfig, ConfigSaver? saveConfig, bool asService, Action<string> log, CancellationToken ct,
        Action? protocolMismatch = null, Func<CancellationToken, Task<string>>? checkForUpdates = null)
    {
        ClientStatus status = new ClientStatus();
        // Alles, was der Dienst protokolliert, auch für den Reiter Diagnose der Einstellungen.
        Action<string> write = log;
        log = message =>
        {
            status.Log.Add(message);
            write(message);
        };
        ClipboardBridge clipboard = new ClipboardBridge();
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

        Task injector = new SessionSupervisor("Injector", OwnerProcess.Arguments(Injector.Argument), asUser: false, asService, log).RunAsync(ct);
        Task tray = new SessionSupervisor("Tray", OwnerProcess.Arguments(ClientTray.Argument), asUser: true, asService, log).RunAsync(ct);
        Task settings = new ClientSettingsServer(status, save, clipboard, checkForUpdates, log).RunAsync(ct);

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
                    (problem, wasConnected) = await AttemptAsync(config, saveConfig, status, clipboard, log, protocolMismatch, current.Token);
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
                    status.ReportError(problem);
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
    private static async Task<(string? Problem, bool WasConnected)> AttemptAsync(ClientConfig config, ConfigSaver? saveConfig, ClientStatus status,
        ClipboardBridge clipboard, Action<string> log, Action? protocolMismatch, CancellationToken ct)
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
                string address = HostAddresses.Display(tcp.Client.RemoteEndPoint);
                log($"Mit dem Host {address} (Version {handshake.PeerVersion}) verbunden.");
                status.Update(s => s with { Connected = true, HostAddress = address, HostVersion = handshake.PeerVersion, Problem = null });
                status.SetConnection(conn);
                PipeRelay relay = new PipeRelay(conn, clipboard, status, key => SaveKey(config, key, saveConfig, status, log), log);
                try
                {
                    await relay.RunAsync(ct);
                }
                finally
                {
                    status.SetConnection(null);
                    if (relay.EndReason is { } reason)
                    {
                        status.ReportError(reason);
                    }

                    // Der Grund bleibt als Störung stehen, bis der nächste Versuch gelingt oder einen eigenen hat; der Tray meldet ihn.
                    status.Update(s => s with { Connected = false, HostAddress = null, HostVersion = null, Arrangement = null, Problem = relay.EndReason });
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

    /// <summary>
    /// Speichert einen neuen Schlüssel, den der Host über die angemeldete Verbindung schickt. Direkt mit
    /// <paramref name="saveConfig"/>, nicht über den Weg der Einstellungen: Der bräche die Verbindung ab, und die neue
    /// Anmeldung käme beim Host an, bevor er den Schlüssel selbst gespeichert hat.
    /// </summary>
    private static string? SaveKey(ClientConfig config, string key, ConfigSaver? saveConfig, ClientStatus status, Action<string> log)
    {
        ClientConfig updated = config with { Key = key };
        string? error = saveConfig is null
            ? "Im Entwicklungsmodus mit --relay steht die Konfiguration fest."
            : ClientConfigRules.Validate(updated.Host, updated.Ip, updated.Port, updated.Key) ?? saveConfig(updated);
        if (error is null)
        {
            status.SetConfig(updated);
            log("Neuen Schlüssel vom Host übernommen.");
        }
        else
        {
            log($"Neuen Schlüssel vom Host nicht übernommen: {error}");
        }
        return error;
    }

    /// <summary>
    /// Löst den Rechnernamen selbst auf und versucht die Adressen einzeln in der Reihenfolge von
    /// <see cref="HostAddresses.Order"/>, jede mit eigenem Zeitlimit. Mit dem Namen versuchte <c>ConnectAsync</c> sie
    /// nacheinander in einem gemeinsamen Zeitlimit; blieb eine IPv6-Adresse hängen, kam die IPv4-Adresse nie dran.
    /// </summary>
    private static async Task<(TcpClient? Client, string? Error)> ConnectAsync(ClientConfig config, CancellationToken ct)
    {
        (IPAddress[] resolved, string? error) = await ResolveAsync(config.Host, ct);
        // Ältere Setups ließen in "ip" auch einen Namen zu.
        IPAddress[] fixedIps = config.Ip is null ? []
            : IPAddress.TryParse(config.Ip, out IPAddress? parsed) ? [parsed]
            : (await ResolveAsync(config.Ip, ct)).Addresses;

        foreach (IPAddress address in HostAddresses.Order(resolved, fixedIps))
        {
            TcpClient tcp = new TcpClient(address.AddressFamily);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_connectTimeout);
            try
            {
                await tcp.ConnectAsync(address, config.Port, timeout.Token);
                return (tcp, null);
            }
            catch (SocketException ex)
            {
                error = $"Host {config.Host} ({address}:{config.Port}) nicht erreichbar ({ex.SocketErrorCode}).";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"Host {config.Host} ({address}:{config.Port}) antwortet nicht.";
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

    private static async Task<(IPAddress[] Addresses, string? Error)> ResolveAsync(string name, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_resolveTimeout);
        try
        {
            return (await Dns.GetHostAddressesAsync(name, timeout.Token), null);
        }
        catch (SocketException ex)
        {
            return ([], $"Host {name} nicht auflösbar ({ex.SocketErrorCode}).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ([], $"Host {name} nicht auflösbar (Zeitüberschreitung).");
        }
    }
}
