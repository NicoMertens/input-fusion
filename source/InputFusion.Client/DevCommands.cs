using NC2.InputFusion.Core.Config;
using NC2.InputFusion.Core.Net;

namespace NC2.InputFusion.Client;

/// <summary>Einstiege zum Entwickeln und Testen, ohne Installation und ohne Dienst.</summary>
internal static class DevCommands
{
    public static async Task<Boolean> TryRunAsync(String[] args)
    {
        if (args is ["--connect", String hostName, "--key", String spikeKey, .. String[] spikeRest])
        {
            await SpikeClient.RunAsync(hostName, spikeKey, PortArgument(spikeRest));
            return true;
        }

        if (args is ["--relay", String relayHost, "--key", String relayKey, .. String[] relayRest])
        {
            await RelayAsync(relayHost, relayKey, PortArgument(relayRest));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Derselbe Ablauf wie im Dienst, aber mit Host und Schlüssel aus den Argumenten statt aus client.json und dem
    /// Injector als Kindprozess statt per CreateProcessAsUser.
    /// </summary>
    private static async Task RelayAsync(String host, String key, Int32 port)
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        ClientService.ConfigLoader fixedHost = (out String? error) => { error = null; return new ClientConfig([new HostEntry(host, key, Port: port)]); };
        await ClientService.RunAsync(fixedHost, saveConfig: null, asService: false, Console.WriteLine, cts.Token);
    }

    // Optional hinter den Dev-Einstiegen: --port <nummer>, sonst der Standard.
    private static Int32 PortArgument(String[] rest)
    {
        if (rest is not ["--port", String text, ..])
        {
            return Ports.Input;
        }

        return Ports.Validate(text, out Int32 port) is { } error ? throw new ArgumentException(error) : port;
    }
}
