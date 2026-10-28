using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RemoteDesktop;

namespace NC2.InputFusion.Client;

/// <summary>Die Sitzung, in der der Benutzer vor dem Notebook sitzt, und wer dort angemeldet ist.</summary>
internal static unsafe class ActiveSession
{
    /// <summary>
    /// Im Dienst die aktive Konsolensitzung. Läuft der Dienst-Ablauf beim Entwickeln als Konsole, die eigene Sitzung, die
    /// auch eine RDP-Sitzung sein kann.
    /// </summary>
    public static UInt32 Id()
    {
        Int32 own = Process.GetCurrentProcess().SessionId;
        return own == 0 ? PInvoke.WTSGetActiveConsoleSessionId() : (UInt32)own;
    }

    /// <summary>Null, solange niemand angemeldet ist, etwa auf dem Anmeldebildschirm nach dem Hochfahren.</summary>
    public static String? UserName()
    {
        PWSTR buffer;
        UInt32 bytes;
        if (!PInvoke.WTSQuerySessionInformation(HANDLE.Null, Id(), WTS_INFO_CLASS.WTSUserName, &buffer, &bytes))
        {
            return null;
        }

        try
        {
            String name = buffer.ToString();
            return name.Length == 0 ? null : name;
        }
        finally
        {
            PInvoke.WTSFreeMemory(buffer.Value);
        }
    }
}
