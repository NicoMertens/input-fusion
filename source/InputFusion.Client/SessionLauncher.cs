using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace NC2.InputFusion.Client;

/// <summary>
/// Startet einen Prozess aus dem Dienst heraus in einer Benutzersitzung, als SYSTEM oder als der angemeldete
/// Benutzer. Der Dienst läuft in Session 0 und kann dort nichts anzeigen oder abspielen.
/// </summary>
internal static unsafe class SessionLauncher
{
    /// <summary>
    /// Als SYSTEM, für den Injector: Das eigene SYSTEM-Token wird kopiert, auf die Zielsitzung gesetzt (braucht
    /// SeTcbPrivilege, das nur SYSTEM hat) und damit gestartet. Als SYSTEM greift UIPI nicht, der Injector erreicht
    /// auch erhöhte Fenster.
    /// </summary>
    public static Process StartAsSystem(UInt32 sessionId, String exePath, String arguments)
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
                if (!PInvoke.SetTokenInformation(primary, TOKEN_INFORMATION_CLASS.TokenSessionId, &sessionId, sizeof(UInt32)))
                {
                    throw new Win32Exception();
                }

                return CreateProcess(primary, exePath, arguments, null);
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

    /// <summary>
    /// Als der angemeldete Benutzer der Sitzung, für das Tray-Symbol. Schlägt fehl, solange niemand angemeldet ist.
    /// Die Umgebung kommt aus dem Profil des Benutzers, sonst erbte der Prozess die von SYSTEM (TEMP, APPDATA).
    /// </summary>
    public static Process StartAsUser(UInt32 sessionId, String exePath, String arguments)
    {
        HANDLE token;
        if (!PInvoke.WTSQueryUserToken(sessionId, &token))
        {
            throw new Win32Exception();
        }

        try
        {
            void* environment;
            if (!PInvoke.CreateEnvironmentBlock(&environment, token, false))
            {
                throw new Win32Exception();
            }

            try
            {
                return CreateProcess(token, exePath, arguments, environment);
            }
            finally
            {
                PInvoke.DestroyEnvironmentBlock(environment);
            }
        }
        finally
        {
            PInvoke.CloseHandle(token);
        }
    }

    private static Process CreateProcess(HANDLE token, String exePath, String arguments, void* environment)
    {
        // CreateProcessAsUser darf die Befehlszeile verändern, deshalb ein eigener, beschreibbarer Puffer.
        Char[] commandLine = $"\"{exePath}\" {arguments}\0".ToCharArray();
        fixed (Char* desktop = "winsta0\\default")
        fixed (Char* cmd = commandLine)
        {
            STARTUPINFOW startup = new STARTUPINFOW { cb = (UInt32)sizeof(STARTUPINFOW), lpDesktop = desktop };
            PROCESS_INFORMATION info;
            // CREATE_NO_WINDOW: Die exe ist eine Konsolenanwendung, sonst öffnet sich beim Benutzer ein Fenster.
            PROCESS_CREATION_FLAGS flags = PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW;
            if (environment is not null)
            {
                flags |= PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT;
            }

            if (!PInvoke.CreateProcessAsUser(token, null, cmd, null, null, false, flags, environment, null, &startup, &info))
            {
                throw new Win32Exception();
            }

            PInvoke.CloseHandle(info.hThread);
            try
            {
                return Process.GetProcessById((Int32)info.dwProcessId);
            }
            finally
            {
                PInvoke.CloseHandle(info.hProcess);
            }
        }
    }
}
