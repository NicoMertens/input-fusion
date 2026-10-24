using System.IO.Pipes;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Named Pipe zwischen dem Dienst auf dem Notebook und den Prozessen im Benutzerkontext: Tray und Einstellungen.
/// Der Dienst hostet als SYSTEM. Verbinden dürfen SYSTEM, Administratoren und interaktive Benutzer; die
/// Integritätsstufe Mittel lässt auch nicht erhöhte Prozesse schreiben. Siehe <c>docs/ipc.md</c>.
/// </summary>
public static class ClientPipe
{
    public const string Name = "InputFusion.Client";

    /// <summary>Legt einen weiteren Server an; die Pipe erlaubt beliebig viele gleichzeitige Verbindungen.</summary>
    public static NamedPipeServerStream CreateServer() =>
        // D: SYSTEM und Administratoren alles, interaktive Benutzer lesen und schreiben, ohne Vererbung;
        // S: Mandatory Label Mittel, No-Write-Up.
        SddlPipe.CreateServer(Name, "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)S:(ML;;NW;;;ME)");

    public static NamedPipeClientStream CreateClient() =>
        new(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);

    /// <summary>
    /// Der Server muss der Dienst sein (SYSTEM) oder, beim Entwickeln, unter dem eigenen Konto laufen. Interaktive
    /// Benutzer dürfen weitere Exemplare der Pipe anlegen; ohne die Prüfung könnte einer davon den Dienst spielen.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Der Server läuft unter einem anderen Konto.</exception>
    public static void VerifyServer(NamedPipeClientStream pipe) =>
        PipeServerIdentity.Verify(pipe, PipeServerIdentity.LocalSystem, PipeServerIdentity.CurrentUser());
}
