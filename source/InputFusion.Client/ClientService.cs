using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NC2.InputFusion.Core.Config;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Client;

/// <summary>
/// Die Dienst-Rolle: hält Injector und Tray-Symbol am Leben und die Verbindung zu einem der eingetragenen PCs. Ohne
/// Verbindung versucht sie alle zugleich, der erste mit gelungener Anmeldung gewinnt. Klappt das bei keinem oder bricht
/// die Verbindung ab, wird es mit wachsendem Abstand bis <see cref="_maxRetryDelay"/> erneut versucht, nach einem
/// Netzwechsel sofort. Läuft im Windows-Dienst (<see cref="Worker"/>) und zum Entwickeln als Konsole (<c>--relay</c>).
/// </summary>
internal static class ClientService
{
    public const String ServiceName = "InputFusionClient";

    private static readonly TimeSpan _minRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(15);
    // Je Adresse; drei Sekunden lassen Platz für eine Wiederholung des Verbindungsaufbaus im WLAN.
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _resolveTimeout = TimeSpan.FromSeconds(5);

    public delegate ClientConfig? ConfigLoader(out String? error);

    /// <summary>Speichert eine neue Konfiguration; bei Fehler die Meldung, sonst null.</summary>
    public delegate String? ConfigSaver(ClientConfig config);

    /// <param name="saveConfig">Null, wenn die Konfiguration feststeht (<c>--relay</c>); dann lehnt der Dienst Änderungen aus den Einstellungen ab.</param>
    /// <param name="protocolMismatch">Wird gerufen, wenn der Host eine andere Protokollversion spricht; der Dienst sucht dann nach einem Update.</param>
    public static async Task RunAsync(ConfigLoader loadConfig, ConfigSaver? saveConfig, Boolean asService, Action<String> log, CancellationToken ct,
        Action? protocolMismatch = null, Func<CancellationToken, Task<String>>? checkForUpdates = null)
    {
        ClientStatus status = new ClientStatus();
        // Alles, was der Dienst protokolliert, auch für den Reiter Diagnose der Einstellungen.
        Action<String> write = log;
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
            String? error = saveConfig(config);
            if (error is null)
            {
                log($"Konfiguration geändert: {Describe(config)}.");
                try { attempt?.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            return error;
        };

        // Ein Netzwechsel (WLAN, LAN, VPN) kürzt nur das Warten auf den nächsten Versuch ab. Eine bestehende Verbindung
        // bleibt; ob sie noch lebt, entscheidet der Heartbeat.
        Boolean networkChanged = false;
        CancellationTokenSource? waiting = null;
        NetworkAddressChangedEventHandler onNetworkChange = (_, _) =>
        {
            Volatile.Write(ref networkChanged, true);
            try { waiting?.Cancel(); }
            catch (ObjectDisposedException) { }
        };
        NetworkChange.NetworkAddressChanged += onNetworkChange;

        Task injector = new SessionSupervisor("Injector", OwnerProcess.Arguments(Injector.Argument), asUser: false, asService, log).RunAsync(ct);
        Task tray = new SessionSupervisor("Tray", OwnerProcess.Arguments(ClientTray.Argument), asUser: true, asService, log).RunAsync(ct);
        Task settings = new ClientSettingsServer(status, save, clipboard, checkForUpdates, log).RunAsync(ct);

        TimeSpan delay = _minRetryDelay;
        String? lastProblem = null;
        while (!ct.IsCancellationRequested)
        {
            using CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt = current;
            // Ein Wechsel während des Versuchs zählt mit: Er kann gerade daran gescheitert sein, dass das Netz noch fehlte.
            Volatile.Write(ref networkChanged, false);
            ClientConfig? config = loadConfig(out String? error);
            status.SetConfig(config);
            String? problem = error;
            Boolean wasConnected = false;
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
                delay = await WaitAsync(delay, () => Volatile.Read(ref networkChanged), wait => waiting = wait, log, current.Token);
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

        NetworkChange.NetworkAddressChanged -= onNetworkChange;
        await injector;
        await tray;
        await settings;
    }

    /// <summary>Wartet bis zum nächsten Versuch, nach einem Netzwechsel nicht länger.</summary>
    /// <param name="setWaiting">Bekommt das Token des Wartens, damit der Netzwechsel es abbrechen kann, danach null.</param>
    /// <returns>Der Abstand für das nächste Warten.</returns>
    private static async Task<TimeSpan> WaitAsync(TimeSpan delay, Func<Boolean> networkChanged, Action<CancellationTokenSource?> setWaiting,
        Action<String> log, CancellationToken ct)
    {
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        setWaiting(wait);
        try
        {
            // Erst danach prüfen: Ein Wechsel kurz vor setWaiting fände sonst kein Warten zum Abbrechen.
            if (networkChanged())
            {
                wait.Cancel();
            }

            await Task.Delay(delay, wait.Token);
            return TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxRetryDelay.Ticks));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log("Netzwerk geändert, verbinde sofort neu.");
            return _minRetryDelay;
        }
        finally
        {
            setWaiting(null);
        }
    }

    /// <summary>
    /// Ein Versuch: mit einem der PCs verbinden und anmelden und, wenn das klappt, Eingaben weiterreichen, bis die
    /// Verbindung endet.
    /// </summary>
    /// <returns>Die Störung, wenn keine Verbindung zustande kam; ob eine Verbindung bestand.</returns>
    private static async Task<(String? Problem, Boolean WasConnected)> AttemptAsync(ClientConfig config, ConfigSaver? saveConfig, ClientStatus status,
        ClipboardBridge clipboard, Action<String> log, Action? protocolMismatch, CancellationToken ct)
    {
        (Candidate? winner, String? problem, Boolean mismatch) = await ConnectAnyAsync(config.Hosts, ct);
        if (winner is null)
        {
            if (mismatch)
            {
                protocolMismatch?.Invoke();
            }
            return (problem, false);
        }

        using (winner.Connection)
        {
            HostEntry entry = winner.Entry;
            log($"Mit dem Host {entry.Host} ({winner.Address}, Version {winner.Version}) verbunden.");
            status.Update(s => s with { Connected = true, Host = entry.Host, HostAddress = winner.Address, HostVersion = winner.Version, Problem = null });
            status.SetConnection(winner.Connection);
            PipeRelay relay = new PipeRelay(winner.Connection, clipboard, status, key => SaveKey(config, entry, key, saveConfig, status, log), log);
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
                status.Update(s => s with
                {
                    Connected = false, Host = null, HostAddress = null, HostVersion = null, Arrangement = null, Problem = relay.EndReason,
                });
            }
        }
        return (null, true);
    }

    /// <summary>Ein PC, bei dem die Anmeldung gelungen ist; die Verbindung ist verschlüsselt und bereit.</summary>
    private sealed record Candidate(HostEntry Entry, MessageConnection Connection, String Address, String Version);

    /// <summary>Ausgang des Versuchs bei einem PC: angemeldet oder die Störung; ob er eine andere Protokollversion sprach.</summary>
    private sealed record HostAttempt(Candidate? Candidate, String? Problem = null, Boolean Mismatch = false);

    /// <summary>
    /// Versucht alle PCs zugleich. Der erste mit gelungener Anmeldung gewinnt, die übrigen Versuche werden abgebrochen.
    /// Ist ein anderer im selben Moment ebenfalls angemeldet, trennt der Client ihn wieder; der sieht dann einen Abbruch.
    /// </summary>
    /// <returns>Den Gewinner oder die Störungen aller Versuche in der Reihenfolge der Liste, damit dieselbe Lage
    /// dieselbe Meldung ergibt; ob ein PC eine andere Protokollversion sprach.</returns>
    private static async Task<(Candidate? Winner, String? Problem, Boolean ProtocolMismatch)> ConnectAnyAsync(IReadOnlyList<HostEntry> hosts,
        CancellationToken ct)
    {
        (HostAttempt? won, IReadOnlyList<HostAttempt> attempts) = await AttemptRace.FirstAsync(
            hosts.Select(h => (Func<CancellationToken, Task<HostAttempt>>)(token => TryHostAsync(h, token))).ToArray(),
            a => a.Candidate is not null, a => a.Candidate!.Connection.Dispose(), ct);
        Candidate? winner = won?.Candidate;
        if (ct.IsCancellationRequested)
        {
            winner?.Connection.Dispose();
            return (null, null, false);
        }

        if (winner is not null)
        {
            return (winner, null, false);
        }

        String[] problems = attempts.Select(a => a.Problem).OfType<String>().ToArray();
        return (null, problems.Length == 0 ? null : String.Join(" ", problems), attempts.Any(a => a.Mismatch));
    }

    /// <summary>
    /// Verbinden und anmelden bei einem PC. Ein Abbruch, auch weil ein anderer PC gewonnen hat, endet ohne Störung und
    /// ohne Ausnahme; sonst risse er im Rennen den Gewinner mit.
    /// </summary>
    private static async Task<HostAttempt> TryHostAsync(HostEntry entry, CancellationToken ct)
    {
        MessageConnection? connection = null;
        try
        {
            (TcpClient? tcp, String? connectError) = await ConnectAsync(entry, ct);
            if (tcp is null)
            {
                return new HostAttempt(null, connectError);
            }

            String address = HostAddresses.Display(tcp.Client.RemoteEndPoint);
            connection = MessageConnection.FromTcp(tcp);
            HandshakeResult handshake = await Handshake.ClientAsync(connection, entry.Key, AppVersion.Current, ct);
            if (handshake.Outcome == HandshakeOutcome.Accepted)
            {
                Candidate candidate = new Candidate(entry, connection, address, handshake.PeerVersion);
                connection = null;
                return new HostAttempt(candidate);
            }

            if (handshake.Outcome == HandshakeOutcome.ProtocolMismatch)
            {
                return new HostAttempt(null, $"Host {entry.Host} hat Version {handshake.PeerVersion} mit Protokoll {handshake.PeerProtocol}, "
                    + $"dieser Client {AppVersion.Current} mit Protokoll {ProtocolVersion.Current}. Suche nach einem Update.", Mismatch: true);
            }

            // Falscher Schlüssel: wie ein nicht erreichbarer Host mit wachsendem Abstand neu versuchen, damit eine Korrektur
            // in den Einstellungen oder in client.json ohne Neustart greift.
            return new HostAttempt(null, $"Host {entry.Host} hat die Anmeldung abgelehnt oder sich nicht ausgewiesen; stimmt der Schlüssel?");
        }
        catch (OperationCanceledException)
        {
            return new HostAttempt(null);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    /// <summary>
    /// Speichert einen neuen Schlüssel, den der Host über die angemeldete Verbindung schickt, in dessen Eintrag. Direkt
    /// mit <paramref name="saveConfig"/>, nicht über den Weg der Einstellungen: Der bräche die Verbindung ab, und die
    /// neue Anmeldung käme beim Host an, bevor er den Schlüssel selbst gespeichert hat.
    /// </summary>
    private static String? SaveKey(ClientConfig config, HostEntry entry, String key, ConfigSaver? saveConfig, ClientStatus status, Action<String> log)
    {
        // Nach dem Eintrag selbst, nicht nach dem Namen: Derselbe PC darf zweimal stehen, etwa mit verschiedenen festen IPs.
        Int32 index = config.Hosts.ToList().FindIndex(h => ReferenceEquals(h, entry));
        ClientConfig updated = new ClientConfig(config.Hosts.Select((h, i) => i == index ? h with { Key = key } : h).ToArray());
        String? error = saveConfig is null
            ? "Im Entwicklungsmodus mit --relay steht die Konfiguration fest."
            : ClientConfigRules.Validate(updated.Hosts) ?? saveConfig(updated);
        if (error is null)
        {
            status.SetConfig(updated);
            log($"Neuen Schlüssel vom Host {entry.Host} übernommen.");
        }
        else
        {
            log($"Neuen Schlüssel vom Host {entry.Host} nicht übernommen: {error}");
        }
        return error;
    }

    /// <summary>Für das Protokoll, ohne Schlüssel.</summary>
    private static String Describe(ClientConfig config) =>
        String.Join("; ", config.Hosts.Select(h => $"Host {h.Host}, Port {h.Port}{(h.Ip is null ? "" : $", IP {h.Ip}")}"));

    /// <summary>
    /// Löst den Rechnernamen selbst auf und versucht die Adressen einzeln in der Reihenfolge von
    /// <see cref="HostAddresses.Order"/>, jede mit eigenem Zeitlimit. Mit dem Namen versuchte <c>ConnectAsync</c> sie
    /// nacheinander in einem gemeinsamen Zeitlimit; blieb eine IPv6-Adresse hängen, kam die IPv4-Adresse nie dran.
    /// </summary>
    private static async Task<(TcpClient? Client, String? Error)> ConnectAsync(HostEntry entry, CancellationToken ct)
    {
        (IPAddress[] resolved, String? error) = await ResolveAsync(entry.Host, ct);
        // Ältere Setups ließen in "ip" auch einen Namen zu.
        IPAddress[] fixedIps = entry.Ip is null ? []
            : IPAddress.TryParse(entry.Ip, out IPAddress? parsed) ? [parsed]
            : (await ResolveAsync(entry.Ip, ct)).Addresses;

        foreach (IPAddress address in HostAddresses.Order(resolved, fixedIps))
        {
            TcpClient tcp = new TcpClient(address.AddressFamily);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_connectTimeout);
            try
            {
                await tcp.ConnectAsync(address, entry.Port, timeout.Token);
                return (tcp, null);
            }
            catch (SocketException ex)
            {
                error = $"Host {entry.Host} ({address}:{entry.Port}) nicht erreichbar ({ex.SocketErrorCode}).";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"Host {entry.Host} ({address}:{entry.Port}) antwortet nicht.";
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

    private static async Task<(IPAddress[] Addresses, String? Error)> ResolveAsync(String name, CancellationToken ct)
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
