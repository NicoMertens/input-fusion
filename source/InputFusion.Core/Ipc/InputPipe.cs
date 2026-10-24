using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Named Pipe zwischen Dienst und Injector auf dem Notebook. Der Dienst hostet (Server), der Injector
/// verbindet sich (Client). Übertragen werden dieselben Frames wie im Netzwerk, siehe
/// <c>InputFusion.Core.Net.MessageConnection</c>; der Dienst reicht die Host-Frames nur weiter.
///
/// <para>Die ACL erlaubt immer SYSTEM. Laufen beide Enden als SYSTEM (Produktivfall: der Dienst startet
/// den Injector per <c>CreateProcessAsUser</c> mit dupliziertem SYSTEM-Token), bleibt es bei SYSTEM.
/// Zum Entwickeln laufen beide als der angemeldete Benutzer; dann wird dessen SID zusätzlich erlaubt,
/// sonst käme der Injector nicht an die Pipe. Siehe <c>docs/ipc.md</c>.</para>
/// </summary>
public static class InputPipe
{
    /// <summary>Pipe-Name ohne Präfix. Der Server liegt unter <c>\\.\pipe\InputFusion.Input</c>.</summary>
    public const string Name = "InputFusion.Input";

    /// <summary>Erzeugt den lauschenden Server mit der oben beschriebenen ACL.</summary>
    public static NamedPipeServerStream CreateServer()
    {
        PipeSecurity security = new PipeSecurity();
        SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));

        SecurityIdentifier? me = WindowsIdentity.GetCurrent().User;
        if (me is not null && me != system)
        {
            security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        // Kleiner Puffer, damit das Weiterreichen winziger Eingabe-Frames nicht auf die Lese-Kadenz des
        // Injectors wartet. Ohne Puffer blockiert schon ein einzelner Write, bis der Injector liest.
        return NamedPipeServerStreamAcl.Create(
            Name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }

    /// <summary>Verbindet sich als Client zum Server auf demselben Rechner.</summary>
    public static NamedPipeClientStream CreateClient() =>
        new(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);

    /// <summary>
    /// Prüft nach dem Verbinden, ob der Server der Pipe unter demselben Konto läuft wie der Aufrufer. Der
    /// Injector läuft als SYSTEM; ohne die Prüfung könnte ein beliebiger Prozess, der die Pipe vor dem Dienst
    /// anlegt, ihm Eingaben unterschieben, die dann auch Sperrbildschirm und UAC-Abfrage erreichen.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Der Server läuft unter einem anderen Konto, oder sein Konto ist nicht zu ermitteln.</exception>
    public static void VerifyServer(NamedPipeClientStream pipe) =>
        PipeIdentity.VerifyServer(pipe, PipeIdentity.CurrentUser());
}
