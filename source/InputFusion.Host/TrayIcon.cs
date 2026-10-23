using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace InputFusion.Host;

/// <summary>
/// Das Tray-Symbol, direkt über <c>Shell_NotifyIcon</c> ohne WinForms und WPF (AOT-fähig). Läuft auf einem eigenen
/// Thread mit verstecktem Fenster, nicht auf dem Hook-Thread: Ein offenes Kontextmenü blockiert seine
/// Nachrichtenschleife, und der Hook darf nie warten. Das Fenster ist ein echtes Top-Level-Fenster und kein
/// Nachrichtenfenster, weil nur solche <c>WM_DISPLAYCHANGE</c> und <c>TaskbarCreated</c> bekommen.
/// </summary>
internal sealed unsafe class TrayIcon(Action openSettings, Action exit, Action displayChanged, string versionLabel)
{
    private readonly Action _openSettings = openSettings;
    private readonly Action _exit = exit;
    private readonly Action _displayChanged = displayChanged;
    private readonly string _versionLabel = versionLabel;

    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint UpdateMessage = PInvoke.WM_APP + 2;
    private const uint MenuSettings = 1;
    private const uint MenuExit = 2;
    // Die .NET-SDK legt das ApplicationIcon unter dieser Ressourcen-ID ab.
    private const int ApplicationIconId = 32512;

    private static TrayIcon? _instance;

    private Thread? _thread;
    private HWND _window;
    private HICON _icon;
    private uint _taskbarCreated;
    private volatile string _tooltip = "InputFusion";

    public void Start()
    {
        _instance = this;
        ManualResetEventSlim ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            CreateWindow();
            ready.Set();
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0))
            {
                PInvoke.TranslateMessage(msg);
                PInvoke.DispatchMessage(msg);
            }
        })
        { IsBackground = true, Name = "InputFusion-Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    /// <summary>Darf von jedem Thread aus gerufen werden; das Symbol selbst ändert der Tray-Thread.</summary>
    public void SetTooltip(string text)
    {
        _tooltip = text.Length > 127 ? text[..127] : text;
        PInvoke.PostMessage(_window, UpdateMessage, default, default);
    }

    public void Stop()
    {
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
        _thread?.Join(2000);
    }

    private void CreateWindow()
    {
        FreeLibrarySafeHandle module = PInvoke.GetModuleHandle((string?)null);
        fixed (char* className = "InputFusion.Tray")
        {
            WNDCLASSEXW windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = (HINSTANCE)module.DangerousGetHandle(),
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(windowClass);
            _window = PInvoke.CreateWindowEx(0, "InputFusion.Tray", "InputFusion", 0, 0, 0, 0, 0, HWND.Null, null, module, null);
        }
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        _icon = LoadIcon(module.DangerousGetHandle());
        Notify(NOTIFY_ICON_MESSAGE.NIM_ADD);
    }

    private static HICON LoadIcon(nint module)
    {
        int size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        HANDLE handle = PInvoke.LoadImage((HINSTANCE)module, (char*)ApplicationIconId, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, 0);
        return handle.IsNull ? PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION) : (HICON)handle.Value;
    }

    private void Notify(NOTIFY_ICON_MESSAGE message)
    {
        NOTIFYICONDATAW data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window,
            uID = 1,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        data.szTip = _tooltip;
        PInvoke.Shell_NotifyIcon(message, in data);
    }

    private void ShowMenu()
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        fixed (char* version = _versionLabel)
        fixed (char* settings = "Einstellungen …")
        fixed (char* exit = "Beenden")
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, version);
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, MenuSettings, settings);
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, MenuExit, exit);
        }
        PInvoke.GetCursorPos(out Point cursor);
        // Ohne Vordergrund schließt das Menü nicht, wenn daneben geklickt wird (bekanntes Shell-Verhalten).
        PInvoke.SetForegroundWindow(_window);
        PInvoke.TrackPopupMenu(menu, TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON, cursor.X, cursor.Y, 0, _window, null);
        PInvoke.DestroyMenu(menu);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        TrayIcon? tray = _instance;
        if (tray is null)
        {
            return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }

        try
        {
            switch (message)
            {
                case CallbackMessage when (uint)lParam.Value == PInvoke.WM_LBUTTONDBLCLK:
                    tray._openSettings();
                    return default;
                case CallbackMessage when (uint)lParam.Value == PInvoke.WM_RBUTTONUP:
                    tray.ShowMenu();
                    return default;
                case PInvoke.WM_COMMAND when (wParam.Value & 0xFFFF) == MenuSettings:
                    tray._openSettings();
                    return default;
                case PInvoke.WM_COMMAND when (wParam.Value & 0xFFFF) == MenuExit:
                    tray._exit();
                    return default;
                case UpdateMessage:
                    tray.Notify(NOTIFY_ICON_MESSAGE.NIM_MODIFY);
                    return default;
                case PInvoke.WM_DISPLAYCHANGE:
                    tray._displayChanged();
                    break;
                case PInvoke.WM_CLOSE:
                    PInvoke.DestroyWindow(hwnd);
                    return default;
                case PInvoke.WM_DESTROY:
                    tray.Notify(NOTIFY_ICON_MESSAGE.NIM_DELETE);
                    PInvoke.PostQuitMessage(0);
                    return default;
            }
            // Nach einem Neustart des Explorers ist das Symbol weg und muss neu angelegt werden.
            if (message == tray._taskbarCreated && message != 0)
            {
                tray.Notify(NOTIFY_ICON_MESSAGE.NIM_ADD);
            }
        }
        catch (Exception ex)
        {
            // Eine Ausnahme darf nicht über die Fensterprozedur hinaus, das beendete den Prozess.
            Log.Write($"Fehler im Tray: {ex.Message}");
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
