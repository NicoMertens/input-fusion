using System.Globalization;
using InputFusion.Core.Config;
using InputFusion.Core.Layout;
using InputFusion.Core.Net;

namespace InputFusion.Simulator;

/// <summary>
/// Startparameter:
/// <c>--host &lt;name&gt;</c> (Standard localhost),
/// <c>--monitors "x,y,b,h[,dpi];…"</c> (simulierte Monitore des Clients),
/// <c>--delay &lt;ms&gt;</c> (künstliche Latenz je Nachricht),
/// <c>--key &lt;schlüssel&gt;</c> und <c>--port &lt;nummer&gt;</c> (Standard: aus der Konfiguration des Hosts auf
/// diesem Rechner).
/// </summary>
internal sealed record SimulatorOptions(String Host, IReadOnlyList<MonitorRect> Monitors, Int32 DelayMs, String Key, Int32 Port)
{
    private const String DefaultMonitors = "0,0,1920,1200,144;1920,-240,2560,1440,96";

    public static SimulatorOptions Parse(IReadOnlyList<String> args)
    {
        String host = "localhost";
        String monitors = DefaultMonitors;
        Int32 delay = 0;
        String? key = null;
        Int32? port = null;

        for (Int32 i = 0; i + 1 < args.Count; i += 2)
        {
            switch (args[i])
            {
                case "--host": host = args[i + 1]; break;
                case "--monitors": monitors = args[i + 1]; break;
                case "--delay": delay = Int32.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                case "--key": key = args[i + 1]; break;
                case "--port": port = Ports.Validate(args[i + 1], out Int32 value) is { } error ? throw new ArgumentException(error) : value; break;
                default: throw new ArgumentException($"Unbekannter Parameter {args[i]}");
            }
        }

        // Host und Simulator laufen beim Entwickeln meist auf demselben Rechner und unter demselben Benutzer.
        HostConfig? hostConfig = HostConfig.TryLoad();
        key ??= hostConfig?.Key;
        if (String.IsNullOrEmpty(key))
        {
            throw new ArgumentException($"Kein Schlüssel: --key angeben oder den Host auf diesem Rechner einmal starten ({HostConfig.FilePath}).");
        }

        return new SimulatorOptions(host, ParseMonitors(monitors), delay, key, port ?? hostConfig?.Port ?? Ports.Input);
    }

    private static List<MonitorRect> ParseMonitors(String spec)
    {
        List<MonitorRect> result = new List<MonitorRect>();
        foreach (String part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Int32[] v = part.Split(',').Select(s => Int32.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (v.Length is not (4 or 5))
            {
                throw new ArgumentException($"Monitor \"{part}\" braucht x,y,breite,höhe[,dpi]");
            }

            result.Add(new MonitorRect(v[0], v[1], v[2], v[3], v.Length == 5 ? v[4] : 96, $"SIM-{result.Count + 1}"));
        }
        if (result.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Monitor nötig");
        }

        return result;
    }
}
