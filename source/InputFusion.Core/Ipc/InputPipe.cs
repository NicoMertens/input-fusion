using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

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
    public static unsafe void VerifyServer(NamedPipeClientStream pipe)
    {
        uint pid;
        if (!PInvoke.GetNamedPipeServerProcessId((HANDLE)pipe.SafePipeHandle.DangerousGetHandle(), &pid))
        {
            throw new UnauthorizedAccessException($"Server der Pipe nicht ermittelbar: {new Win32Exception().Message}");
        }

        SecurityIdentifier? me = WindowsIdentity.GetCurrent().User;
        SecurityIdentifier? server = GetProcessUser(pid);
        if (me is null || server is null || server != me)
        {
            throw new UnauthorizedAccessException(
                $"Server der Pipe (PID {pid}) läuft als {server?.Value ?? "unbekannt"}, erwartet {me?.Value ?? "unbekannt"}.");
        }
    }

    /// <summary>Konto, unter dem ein Prozess läuft; <see langword="null"/>, wenn Prozess oder Token nicht zu öffnen sind.</summary>
    private static unsafe SecurityIdentifier? GetProcessUser(uint pid)
    {
        HANDLE process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            HANDLE token;
            if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
            {
                return null;
            }

            try
            {
                using WindowsIdentity identity = new WindowsIdentity((IntPtr)token.Value);
                return identity.User;
            }
            finally
            {
                PInvoke.CloseHandle(token);
            }
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}
