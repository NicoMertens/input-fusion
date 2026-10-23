using System.Net.Sockets;
using InputFusion.Core.Net;

namespace InputFusion.Client;

/// <summary>
/// Die Dienst-Rolle: hält den Injector am Leben und die Verbindung zum Host. Ist der Host nicht erreichbar
/// oder bricht die Verbindung ab, wird es mit wachsendem Abstand bis <see cref="MaxRetryDelay"/> erneut
/// versucht. Läuft im Windows-Dienst (<see cref="Worker"/>) und zum Entwickeln als Konsole (<c>--relay</c>).
/// </summary>
internal static class ClientService
{
    public const string ServiceName = "InputFusionClient";

    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public delegate ClientConfig? ConfigLoader(out string? error);

    public static async Task RunAsync(ConfigLoader loadConfig, bool asService, Action<string> log, CancellationToken ct)
    {
        var supervisor = new InjectorSupervisor(asService, log).RunAsync(ct);

        var delay = MinRetryDelay;
        string? lastProblem = null;
        while (!ct.IsCancellationRequested)
        {
            var config = loadConfig(out string? error);
            string? problem = error;
            if (config is not null)
            {
                var (tcp, connectError) = await ConnectAsync(config, ct);
                problem = connectError;
                if (tcp is not null)
                {
                    bool accepted;
                    using (tcp)
                    using (var conn = MessageConnection.FromTcp(tcp))
                    {
                        accepted = await Handshake.ClientAsync(conn, config.Key, ct);
                        if (accepted)
                        {
                            log($"Mit dem Host {tcp.Client.RemoteEndPoint} verbunden.");
                            await new PipeRelay(conn, log).RunAsync(ct);
                        }
                    }
                    if (accepted)
                    {
                        if (!ct.IsCancellationRequested)
                            log("Verbindung zum Host beendet, verbinde neu.");
                        delay = MinRetryDelay;
                        lastProblem = null;
                        continue;
                    }
                    // Falscher Schlüssel: wie ein nicht erreichbarer Host mit wachsendem Abstand neu versuchen,
                    // damit eine Korrektur in client.json ohne Neustart greift.
                    problem = $"Host {config.Host} hat die Anmeldung abgelehnt oder sich nicht ausgewiesen; stimmt der Schlüssel?";
                }
            }

            // Dieselbe Störung nur einmal melden, sonst läuft das Ereignisprotokoll über, solange der PC aus ist.
            if (problem != lastProblem)
                log($"{problem} Neuer Versuch spätestens alle {MaxRetryDelay.TotalSeconds:0} s.");
            lastProblem = problem;

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }

        await supervisor;
    }

    /// <summary>Erst der Rechnername per DNS, dann die feste IP, falls eingetragen.</summary>
    private static async Task<(TcpClient? Client, string? Error)> ConnectAsync(ClientConfig config, CancellationToken ct)
    {
        string? error = null;
        foreach (var target in config.Ip is null ? [config.Host] : new[] { config.Host, config.Ip })
        {
            var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await tcp.ConnectAsync(target, Ports.Input, timeout.Token);
                return (tcp, null);
            }
            catch (SocketException ex)
            {
                error = $"Host {target}:{Ports.Input} nicht erreichbar ({ex.SocketErrorCode}).";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"Host {target}:{Ports.Input} antwortet nicht.";
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
