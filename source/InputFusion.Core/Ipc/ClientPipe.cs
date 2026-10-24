using System.IO.Pipes;
using System.Security.Principal;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Named Pipe zwischen dem Dienst auf dem Notebook und den Prozessen im Benutzerkontext: Tray und Einstellungen.
/// Der Dienst hostet als SYSTEM. Verbinden dürfen SYSTEM, Administratoren und interaktive Benutzer; die
/// Integritätsstufe Mittel lässt auch nicht erhöhte Prozesse schreiben. Siehe <c>docs/ipc.md</c>.
/// </summary>
public static class ClientPipe
{
    public const string Name = "InputFusion.Client";

    // Lesen, Schreiben, Attribute, Berechtigungen lesen, Synchronisieren; ohne FILE_CREATE_PIPE_INSTANCE (0x4), das in
    // GENERIC_WRITE steckt. So kann ein Benutzer kein weiteres Exemplar neben dem des Dienstes anlegen.
    private const string UserRights = "0x120183";

    private const PipeAccessRights ClientRights = PipeAccessRights.ReadData | PipeAccessRights.WriteData
        | PipeAccessRights.ReadAttributes | PipeAccessRights.WriteAttributes | PipeAccessRights.ReadPermissions
        | PipeAccessRights.Synchronize;

    /// <summary>
    /// Legt einen weiteren Server an; die Pipe erlaubt beliebig viele gleichzeitige Verbindungen. Besitzer ist
    /// ausdrücklich das eigene Konto, daran erkennen die Clients den Dienst (<see cref="VerifyServer"/>).
    /// </summary>
    public static NamedPipeServerStream CreateServer()
    {
        string me = PipeIdentity.CurrentUser()?.Value ?? throw new InvalidOperationException("Prozess ohne Benutzer-SID");
        // O: eigenes Konto; D: SYSTEM, Administratoren und das eigene Konto alles (beim Entwickeln legt der Benutzer
        // selbst weitere Exemplare an), interaktive Benutzer nur Lesen und Schreiben, ohne Vererbung;
        // S: Mandatory Label Mittel, No-Write-Up.
        return SddlPipe.CreateServer(Name, $"O:{me}D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{me})(A;;{UserRights};;;IU)S:(ML;;NW;;;ME)");
    }

    /// <summary>Verbindet sich ohne das Recht, Exemplare anzulegen; das bekäme ein Benutzer ohnehin nicht.</summary>
    public static NamedPipeClientStream CreateClient() =>
        new(".", Name, ClientRights, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);

    /// <summary>
    /// Der Besitzer der Pipe muss SYSTEM sein (der Dienst) oder, beim Entwickeln, das eigene Konto. Den Besitzer setzt,
    /// wer die Pipe als Erster anlegt, und auf SYSTEM kann ihn nur SYSTEM setzen. Das Konto des Server-Prozesses lässt
    /// sich nicht prüfen: Ein Benutzer darf den Dienst-Prozess nicht öffnen.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Die Pipe gehört einem anderen Konto.</exception>
    public static void VerifyServer(NamedPipeClientStream pipe)
    {
        SecurityIdentifier? owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || (owner != PipeIdentity.LocalSystem && owner != PipeIdentity.CurrentUser()))
        {
            throw new UnauthorizedAccessException($"Die Pipe {Name} gehört {owner?.Value ?? "niemandem"}, nicht dem Dienst.");
        }
    }
}
