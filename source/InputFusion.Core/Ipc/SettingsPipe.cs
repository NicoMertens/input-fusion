using System.IO.Pipes;
using System.Security.Principal;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Named Pipe zwischen den Einstellungen und dem Agent auf dem PC. Der Agent hostet, die Einstellungen
/// verbinden sich. Der Name enthält die SID des Benutzers, damit bei mehreren angemeldeten Benutzern jeder
/// seinen eigenen Agent erreicht.
///
/// <para>Der Agent läuft erhöht, die Einstellungen aus dem Startmenü nicht. Deshalb trägt die Pipe ausdrücklich
/// die Integritätsstufe Mittel; die ACL lässt nur den Benutzer selbst und SYSTEM zu.</para>
/// </summary>
public static class SettingsPipe
{
    public static String Name { get; } = "InputFusion.Settings." + CurrentUser().Value;

    /// <summary>Legt einen weiteren Server an; die Pipe erlaubt beliebig viele gleichzeitige Verbindungen.</summary>
    public static NamedPipeServerStream CreateServer() =>
        // D: nur Benutzer und SYSTEM, ohne Vererbung; S: Mandatory Label Mittel, No-Write-Up.
        SddlPipe.CreateServer(Name, $"D:P(A;;GA;;;{CurrentUser().Value})(A;;GA;;;SY)S:(ML;;NW;;;ME)");

    public static NamedPipeClientStream CreateClient() =>
        new(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);

    private static SecurityIdentifier CurrentUser() =>
        WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Prozess ohne Benutzer-SID");
}
