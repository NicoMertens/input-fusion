using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace InputFusion.Host;

/// <summary>
/// Globale Low-Level-Hooks für Maus und Tastatur. Statisch, weil die Callbacks als
/// <c>[UnmanagedCallersOnly]</c> keinen Instanzzustand haben dürfen. Ein Host je Rechner.
/// Der Callback schreibt nur in einen Channel und entscheidet anhand von <see cref="Swallow"/>,
/// ob die Eingabe lokal verschluckt wird. Länger darf er nicht brauchen, sonst entfernt Windows
/// den Hook wegen <c>LowLevelHooksTimeout</c>.
/// </summary>
internal static class LowLevelHooks
{
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    private const uint WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    private static readonly Channel<InputEvent> _channel =
        Channel.CreateUnbounded<InputEvent>(new UnboundedChannelOptions { SingleReader = true });

    private static UnhookWindowsHookExSafeHandle? _mouseHook;
    private static UnhookWindowsHookExSafeHandle? _keyboardHook;
    private static FreeLibrarySafeHandle? _module;

    /// <summary>Wenn true, verschluckt der Hook die Eingabe lokal (Remote-Modus).</summary>
    public static volatile bool Swallow;

    public static ChannelReader<InputEvent> Events => _channel.Reader;

    /// <summary>Für Ereignisse, die nicht aus den Hooks kommen (Raw Input), damit alles in einer Reihenfolge ankommt.</summary>
    public static void Post(InputEvent e) => _channel.Writer.TryWrite(e);

    public static unsafe void Install()
    {
        // Nicht disposen: GetModuleHandle gibt ein nicht-besitzendes Handle auf die eigene exe zurück.
        _module = PInvoke.GetModuleHandle((string?)null);
        _mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, &MouseProc, _module, 0);
        _keyboardHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &KeyboardProc, _module, 0);
    }

    public static void Uninstall()
    {
        _mouseHook?.Dispose();
        _keyboardHook?.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT MouseProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam.Value);
            bool injected = (data.flags & 1) != 0;
            if (!injected)
                Publish((uint)wParam.Value, data);
            if (Swallow)
                return (LRESULT)1;
        }
        return PInvoke.CallNextHookEx((HHOOK)default, nCode, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT KeyboardProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam.Value);
            bool injected = ((uint)data.flags & 0x10) != 0;
            if (!injected)
            {
                uint msg = (uint)wParam.Value;
                bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool extended = ((uint)data.flags & 0x01) != 0;
                _channel.Writer.TryWrite(new KeyInputEvent((ushort)data.vkCode, (ushort)data.scanCode, down, extended));
            }
            if (Swallow)
                return (LRESULT)1;
        }
        return PInvoke.CallNextHookEx((HHOOK)default, nCode, wParam, lParam);
    }

    private static void Publish(uint message, MSLLHOOKSTRUCT data)
    {
        InputEvent? e = message switch
        {
            WM_MOUSEMOVE => MouseMove(data),
            WM_LBUTTONDOWN => new MouseButtonEvent(0, true),
            WM_LBUTTONUP => new MouseButtonEvent(0, false),
            WM_RBUTTONDOWN => new MouseButtonEvent(1, true),
            WM_RBUTTONUP => new MouseButtonEvent(1, false),
            WM_MBUTTONDOWN => new MouseButtonEvent(2, true),
            WM_MBUTTONUP => new MouseButtonEvent(2, false),
            WM_MOUSEWHEEL => new MouseWheelEvent((short)(data.mouseData >> 16), false),
            WM_MOUSEHWHEEL => new MouseWheelEvent((short)(data.mouseData >> 16), true),
            _ => null,
        };
        if (e is not null)
            _channel.Writer.TryWrite(e);
    }

    /// <summary>
    /// Im Hook steht der Cursor noch vor der Bewegung, <c>pt</c> ist das Ziel. Die Differenz ist die
    /// Bewegung, unabhängig davon, wo der Cursor gerade steht und ob verschluckt wird (gemessen, siehe
    /// Fallstricke). Erst später im Controller wäre die Cursorposition schon eine andere.
    /// </summary>
    private static MouseMoveEvent MouseMove(MSLLHOOKSTRUCT data)
    {
        PInvoke.GetCursorPos(out var cursor);
        return new MouseMoveEvent(data.pt.X, data.pt.Y, data.pt.X - cursor.X, data.pt.Y - cursor.Y);
    }
}
