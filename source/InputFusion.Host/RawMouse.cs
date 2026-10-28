using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace InputFusion.Host;

/// <summary>
/// Liest die Mausbewegung beim Kapern über Raw Input statt aus dem Hook. Verschluckt der Hook eine
/// Bewegung, verwirft Windows den Pixel-Bruchteil der Zeigerbeschleunigung; langsame Bewegungen kommen
/// dann kaum an. Raw Input liefert die rohen Zählimpulse vollständig, auch während verschluckt wird.
/// Läuft auf dem Hook-Thread, dessen Nachrichtenschleife die WM_INPUT-Nachrichten abholt.
/// </summary>
internal static unsafe class RawMouse
{
    private static HWND _window;

    public static void Register()
    {
        // Ein Nachrichtenfenster der vordefinierten Klasse "Static" genügt als Ziel; ausgewertet wird
        // WM_INPUT direkt in der Schleife. RIDEV_INPUTSINK, damit es auch ohne Fokus ankommt.
        _window = PInvoke.CreateWindowEx(0, "Static", null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
        RAWINPUTDEVICE device = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01, // Generic Desktop
            usUsage = 0x02,     // Maus
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = _window,
        };
        PInvoke.RegisterRawInputDevices(&device, 1, (UInt32)sizeof(RAWINPUTDEVICE));
    }

    public static void Unregister()
    {
        if (!_window.IsNull)
        {
            PInvoke.DestroyWindow(_window);
        }
    }

    public static void Handle(LPARAM lParam)
    {
        // Nur beim Kapern; sonst misst der Hook, und der Kanal soll nicht mit jeder Bewegung volllaufen.
        if (!LowLevelHooks.Swallow)
        {
            return;
        }

        RAWINPUT raw;
        UInt32 size = (UInt32)sizeof(RAWINPUT);
        if (PInvoke.GetRawInputData(new HRAWINPUT(lParam.Value), RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT,
                &raw, &size, (UInt32)sizeof(RAWINPUTHEADER)) == unchecked((UInt32)(-1)))
        {
            return;
        }

        // hDevice 0 heißt per SendInput eingespielt; wie im Hook nicht weiterreichen.
        if (raw.header.dwType != (UInt32)RID_DEVICE_INFO_TYPE.RIM_TYPEMOUSE || raw.header.hDevice.IsNull)
        {
            return;
        }

        RAWMOUSE mouse = raw.data.mouse;
        if ((mouse.usFlags & MOUSE_STATE.MOUSE_MOVE_ABSOLUTE) != 0 || (mouse.lLastX == 0 && mouse.lLastY == 0))
        {
            return;
        }

        LowLevelHooks.Post(new RawMouseMoveEvent(mouse.lLastX, mouse.lLastY));
    }
}
