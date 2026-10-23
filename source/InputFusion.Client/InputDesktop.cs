using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.StationsAndDesktops;

namespace InputFusion.Client;

/// <summary>
/// Desktop-Wechsel nach VNC-Muster. Gesperrter Bildschirm und Anmeldung liegen auf dem Desktop „Winlogon“, die
/// UAC-Abfrage auf einem eigenen sicheren Desktop; <c>SendInput</c> wirkt nur auf den Desktop des aufrufenden Threads.
/// Deshalb setzt der Abspiel-Thread sich vor dem Abspielen auf den aktuellen Eingabe-Desktop. Das darf er nur, solange
/// er weder Fenster noch Hooks besitzt, und es geht nur als SYSTEM; als Benutzer verweigert Windows den Zugriff auf
/// „Winlogon“. Nicht threadsicher: nur vom Abspiel-Thread aufrufen.
/// </summary>
internal static unsafe class InputDesktop
{
    // Ein Wechsel ist selten; öfter als alle 200 ms nachzusehen kostete bei schneller Mausbewegung nur Rechenzeit.
    private const long CheckIntervalMs = 200;
    // Volle Rechte auf den Desktop; ein Teil davon fehlt sonst beim Abspielen oder beim Setzen des Cursors.
    private const uint MaximumAllowed = 0x02000000;

    private static HDESK _owned;
    private static long _lastCheck;

    /// <summary>Gibt den Namen des Desktops zurück, wenn gewechselt wurde, sonst null.</summary>
    public static string? Follow()
    {
        long now = Environment.TickCount64;
        if (now - _lastCheck < CheckIntervalMs)
        {
            return null;
        }

        _lastCheck = now;
        HDESK input = PInvoke.OpenInputDesktop(0, false, (DESKTOP_ACCESS_FLAGS)MaximumAllowed);
        if (input.IsNull)
        {
            return null;
        }

        string inputName = Name(input);
        if (inputName == Name(PInvoke.GetThreadDesktop(PInvoke.GetCurrentThreadId())) || !PInvoke.SetThreadDesktop(input))
        {
            PInvoke.CloseDesktop(input);
            return null;
        }

        // Den vorher geöffneten Desktop erst jetzt schließen: Solange der Thread darauf saß, war er noch in Gebrauch.
        if (!_owned.IsNull)
        {
            PInvoke.CloseDesktop(_owned);
        }

        _owned = input;
        return inputName;
    }

    private static string Name(HDESK desktop)
    {
        char* buffer = stackalloc char[128];
        uint needed;
        return PInvoke.GetUserObjectInformation((HANDLE)desktop.Value, USER_OBJECT_INFORMATION_INDEX.UOI_NAME, buffer, 128 * sizeof(char), &needed)
            ? new string(buffer)
            : "";
    }
}
