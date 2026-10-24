using System.IO.Pipes;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Ermittelt nach dem Verbinden, unter welchem Konto der Server einer Pipe läuft. Eine ACL schützt nur die Pipe des
/// echten Servers; legt ein anderer Prozess eine Pipe gleichen Namens an, verbände sich der Client sonst mit ihm.
/// </summary>
public static unsafe class PipeServerIdentity
{
    public static SecurityIdentifier LocalSystem { get; } = new(WellKnownSidType.LocalSystemSid, null);

    /// <summary>
    /// Prüft, ob der Server unter einem der erlaubten Konten läuft. Ist sein Konto nicht zu ermitteln, gilt er als fremd.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Der Server läuft unter keinem der erlaubten Konten.</exception>
    public static void Verify(NamedPipeClientStream pipe, params SecurityIdentifier?[] allowed)
    {
        uint pid;
        SecurityIdentifier? server = PInvoke.GetNamedPipeServerProcessId((HANDLE)pipe.SafePipeHandle.DangerousGetHandle(), &pid)
            ? GetProcessUser(pid)
            : null;
        if (server is null || !allowed.Contains(server))
        {
            throw new UnauthorizedAccessException(
                $"Server der Pipe (PID {pid}) läuft als {server?.Value ?? "unbekannt"}, erlaubt: {string.Join(", ", allowed.Select(sid => sid?.Value))}.");
        }
    }

    /// <summary>Das Konto des aufrufenden Prozesses.</summary>
    public static SecurityIdentifier? CurrentUser() => WindowsIdentity.GetCurrent().User;

    /// <summary>Konto, unter dem ein Prozess läuft; <see langword="null"/>, wenn Prozess oder Token nicht zu öffnen sind.</summary>
    private static SecurityIdentifier? GetProcessUser(uint pid)
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
