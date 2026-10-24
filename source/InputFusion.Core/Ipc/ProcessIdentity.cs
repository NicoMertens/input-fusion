using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace InputFusion.Core.Ipc;

/// <summary>Das Token eines anderen Prozesses als <see cref="WindowsIdentity"/>, für die Prüfung der Gegenseite einer Pipe.</summary>
internal static unsafe class ProcessIdentity
{
    /// <summary>
    /// Null, wenn Prozess oder Token nicht zu öffnen sind, etwa ein erhöhter Prozess aus einem nicht erhöhten heraus.
    /// TOKEN_DUPLICATE braucht <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/> für die Gruppenprüfung.
    /// </summary>
    public static WindowsIdentity? Open(uint pid)
    {
        HANDLE process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            HANDLE token;
            if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, &token))
            {
                return null;
            }

            try
            {
                return new WindowsIdentity((IntPtr)token.Value);
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
