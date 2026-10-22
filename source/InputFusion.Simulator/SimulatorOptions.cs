using System.Globalization;
using InputFusion.Core.Layout;

namespace InputFusion.Simulator;

/// <summary>
/// Startparameter:
/// <c>--host &lt;name&gt;</c> (Standard localhost),
/// <c>--monitors "x,y,b,h[,dpi];…"</c> (simulierte Monitore des Clients),
/// <c>--delay &lt;ms&gt;</c> (künstliche Latenz je Nachricht).
/// </summary>
internal sealed record SimulatorOptions(string Host, IReadOnlyList<MonitorRect> Monitors, int DelayMs)
{
    private const string DefaultMonitors = "0,0,1920,1200,144;1920,-240,2560,1440,96";

    public static SimulatorOptions Parse(IReadOnlyList<string> args)
    {
        string host = "localhost";
        string monitors = DefaultMonitors;
        int delay = 0;

        for (int i = 0; i + 1 < args.Count; i += 2)
        {
            switch (args[i])
            {
                case "--host": host = args[i + 1]; break;
                case "--monitors": monitors = args[i + 1]; break;
                case "--delay": delay = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unbekannter Parameter {args[i]}");
            }
        }

        return new SimulatorOptions(host, ParseMonitors(monitors), delay);
    }

    private static List<MonitorRect> ParseMonitors(string spec)
    {
        var result = new List<MonitorRect>();
        foreach (string part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int[] v = part.Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (v.Length is not (4 or 5))
                throw new ArgumentException($"Monitor \"{part}\" braucht x,y,breite,höhe[,dpi]");
            result.Add(new MonitorRect(v[0], v[1], v[2], v[3], v.Length == 5 ? v[4] : 96, $"SIM-{result.Count + 1}"));
        }
        if (result.Count == 0)
            throw new ArgumentException("Mindestens ein Monitor nötig");
        return result;
    }
}
