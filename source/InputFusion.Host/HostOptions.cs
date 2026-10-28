using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NC2.InputFusion.Host;

/// <summary>Die Startschalter des Agents.</summary>
internal sealed record HostOptions(Boolean Absolute, Boolean Observe, Boolean Diag, Boolean RemoteSession)
{
    public static HostOptions Parse(String[] args)
    {
        // In einer RDP-Sitzung lässt sich die Maus nicht kapern, deshalb dort automatisch nur beobachten.
        // --observe erzwingt das auch lokal, --capture erzwingt das Kapern trotz RDP (zum Ausprobieren).
        Boolean remoteSession = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;
        // Die Diagnose misst das Kapern, braucht es also; sie schaltet die Beobachtung ab.
        Boolean diag = args.Contains("--diag");
        Boolean observe = !diag && (args.Contains("--observe") || (remoteSession && !args.Contains("--capture")));
        return new HostOptions(args.Contains("--absolute"), observe, diag, remoteSession);
    }

    public String Describe() =>
        $"{(Absolute ? "absolut" : "relativ")}"
        + $"{(Observe ? ", Beobachtung (kapert die Maus nicht)" : "")}"
        + $"{(Diag ? ", Diagnose" : "")}{(RemoteSession ? " [RDP erkannt]" : "")}";
}
