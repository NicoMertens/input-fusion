using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace InputFusion.Client;

/// <summary>
/// Startet einen Prozess als SYSTEM in einer Benutzersitzung. Der Dienst läuft in Session 0 und kann dort
/// nichts abspielen; der Injector muss in die aktive Konsolensitzung. Dazu wird das eigene SYSTEM-Token
/// kopiert, auf die Zielsitzung gesetzt (braucht SeTcbPrivilege, das nur SYSTEM hat) und damit gestartet.
/// Als SYSTEM greift UIPI nicht, der Injector erreicht auch erhöhte Fenster.
/// </summary>
internal static unsafe class SessionLauncher
{
    public static Process Start(uint sessionId, string exePath, string arguments)
    {
        HANDLE own, primary;
        if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(),
                TOKEN_ACCESS_MASK.TOKEN_DUPLICATE | TOKEN_ACCESS_MASK.TOKEN_QUERY, &own))
        {
            throw new Win32Exception();
        }

        try
        {
            if (!PInvoke.DuplicateTokenEx(own, TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS, null,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, &primary))
            {
                throw new Win32Exception();
            }

            try
            {
                if (!PInvoke.SetTokenInformation(primary, TOKEN_INFORMATION_CLASS.TokenSessionId, &sessionId, sizeof(uint)))
                {
                    throw new Win32Exception();
                }

                return CreateProcess(primary, exePath, arguments);
            }
            finally
            {
                PInvoke.CloseHandle(primary);
            }
        }
        finally
        {
            PInvoke.CloseHandle(own);
        }
    }

    private static Process CreateProcess(HANDLE token, string exePath, string arguments)
    {
        // CreateProcessAsUser darf die Befehlszeile verändern, deshalb ein eigener, beschreibbarer Puffer.
        char[] commandLine = $"\"{exePath}\" {arguments}\0".ToCharArray();
        fixed (char* desktop = "winsta0\\default")
        fixed (char* cmd = commandLine)
        {
            STARTUPINFOW startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW), lpDesktop = desktop };
            PROCESS_INFORMATION info;
            // CREATE_NO_WINDOW: Die exe ist eine Konsolenanwendung, sonst öffnet sich beim Benutzer ein Fenster.
            if (!PInvoke.CreateProcessAsUser(token, null, cmd, null, null, false,
                    PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW, null, null, &startup, &info))
            {
                throw new Win32Exception();
            }

            PInvoke.CloseHandle(info.hThread);
            try
            {
                return Process.GetProcessById((int)info.dwProcessId);
            }
            finally
            {
                PInvoke.CloseHandle(info.hProcess);
            }
        }
    }
}
