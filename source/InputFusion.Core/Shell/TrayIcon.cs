using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace InputFusion.Core.Shell;

/// <summary>Ein Eintrag im Kontextmenü des Tray-Symbols. Der erste ist zugleich die Aktion beim Doppelklick.</summary>
public sealed record TrayCommand(String Text, Action Action);

/// <summary>
/// Das Tray-Symbol, direkt über <c>Shell_NotifyIcon</c> ohne WinForms und WPF (AOT-fähig). Läuft auf einem eigenen
/// Thread mit verstecktem Fenster, nicht auf dem Hook-Thread des Hosts: Ein offenes Kontextmenü blockiert seine
/// Nachrichtenschleife, und der Hook darf nie warten. Das Fenster ist ein echtes Top-Level-Fenster und kein
/// Nachrichtenfenster, weil nur solche <c>WM_DISPLAYCHANGE</c> und <c>TaskbarCreated</c> bekommen.
/// Das Symbol ist das <c>ApplicationIcon</c> der exe.
/// </summary>
/// <param name="checkForUpdates">
/// Sucht nach Updates und liefert das Ergebnis als Satz; steht im Menü unter der Version, das Ergebnis erscheint als
/// Benachrichtigung am Symbol.
/// </param>
public sealed unsafe class TrayIcon(String versionLabel, IReadOnlyList<TrayCommand> commands, Action<String> log, Action? displayChanged = null,
    Func<Task<String>>? checkForUpdates = null)
{
    private readonly String _versionLabel = versionLabel;
    private readonly IReadOnlyList<TrayCommand> _commands = commands;
    private readonly Action<String> _log = log;
    private readonly Action? _displayChanged = displayChanged;
    private readonly Func<Task<String>>? _checkForUpdates = checkForUpdates;

    private const UInt32 CallbackMessage = PInvoke.WM_APP + 1;
    private const UInt32 UpdateMessage = PInvoke.WM_APP + 2;
    private const UInt32 BalloonMessage = PInvoke.WM_APP + 3;
    // Menü-IDs der Befehle sind ihr Index plus eins; 0 heißt im Menü „kein Befehl“.
    private const UInt32 FirstCommandId = 1;
    private const UInt32 CheckForUpdatesId = 0x1000;
    // Die .NET-SDK legt das ApplicationIcon unter dieser Ressourcen-ID ab.
    private const Int32 ApplicationIconId = 32512;

    private static TrayIcon? _instance;

    private Thread? _thread;
    private HWND _window;
    private HICON _icon;
    private UInt32 _taskbarCreated;
    private volatile String _tooltip = "InputFusion";
    private volatile String _balloon = "";
    // Solange gesucht wird, ist der Menüeintrag gesperrt; ein zweiter Klick brächte nur eine zweite Anfrage an GitHub.
    private volatile Boolean _checking;

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
    public void SetTooltip(String text)
    {
        _tooltip = text.Length > 127 ? text[..127] : text;
        PInvoke.PostMessage(_window, UpdateMessage, default, default);
    }

    /// <summary>Eine Benachrichtigung am Symbol; darf von jedem Thread aus gerufen werden.</summary>
    public void ShowBalloon(String text)
    {
        _balloon = text.Length > 255 ? text[..255] : text;
        PInvoke.PostMessage(_window, BalloonMessage, default, default);
    }

    public void Stop()
    {
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
        _thread?.Join(2000);
    }

    private void CreateWindow()
    {
        FreeLibrarySafeHandle module = PInvoke.GetModuleHandle((String?)null);
        fixed (Char* className = "InputFusion.Tray")
        {
            WNDCLASSEXW windowClass = new WNDCLASSEXW
            {
                cbSize = (UInt32)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = (HINSTANCE)module.DangerousGetHandle(),
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(windowClass);
            _window = PInvoke.CreateWindowEx(0, "InputFusion.Tray", "InputFusion", 0, 0, 0, 0, 0, HWND.Null, null, module, null);
        }
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        _icon = LoadIcon(module.DangerousGetHandle());
        Notify(NOTIFY_ICON_MESSAGE.NIM_ADD, balloon: false);
    }

    private static HICON LoadIcon(IntPtr module)
    {
        Int32 size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        HANDLE handle = PInvoke.LoadImage((HINSTANCE)module, (Char*)ApplicationIconId, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, 0);
        return handle.IsNull ? PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION) : (HICON)handle.Value;
    }

    private void Notify(NOTIFY_ICON_MESSAGE message, Boolean balloon)
    {
        NOTIFYICONDATAW data = new NOTIFYICONDATAW
        {
            cbSize = (UInt32)sizeof(NOTIFYICONDATAW),
            hWnd = _window,
            uID = 1,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        data.szTip = _tooltip;
        if (balloon)
        {
            data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
            data.szInfoTitle = "InputFusion";
            data.szInfo = _balloon;
            data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO;
        }
        PInvoke.Shell_NotifyIcon(message, in data);
    }

    private void ShowMenu()
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        fixed (Char* version = _versionLabel)
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, version);
        }
        if (_checkForUpdates is not null)
        {
            fixed (Char* text = _checking ? "Suche nach Updates …" : "Nach Updates suchen")
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | (_checking ? MENU_ITEM_FLAGS.MF_GRAYED : 0), CheckForUpdatesId, text);
            }
        }
        for (Int32 i = 0; i < _commands.Count; i++)
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
            fixed (Char* text = _commands[i].Text)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, FirstCommandId + (UInt32)i, text);
            }
        }
        PInvoke.GetCursorPos(out Point cursor);
        // Ohne Vordergrund schließt das Menü nicht, wenn daneben geklickt wird (bekanntes Shell-Verhalten).
        PInvoke.SetForegroundWindow(_window);
        PInvoke.TrackPopupMenu(menu, TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON, cursor.X, cursor.Y, 0, _window, null);
        PInvoke.DestroyMenu(menu);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, UInt32 message, WPARAM wParam, LPARAM lParam)
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
                case CallbackMessage when (UInt32)lParam.Value == PInvoke.WM_LBUTTONDBLCLK:
                    tray.Run(FirstCommandId);
                    return default;
                case CallbackMessage when (UInt32)lParam.Value == PInvoke.WM_RBUTTONUP:
                    tray.ShowMenu();
                    return default;
                case PInvoke.WM_COMMAND:
                    tray.Run((UInt32)(wParam.Value & 0xFFFF));
                    return default;
                case UpdateMessage:
                    tray.Notify(NOTIFY_ICON_MESSAGE.NIM_MODIFY, balloon: false);
                    return default;
                case BalloonMessage:
                    tray.Notify(NOTIFY_ICON_MESSAGE.NIM_MODIFY, balloon: true);
                    return default;
                case PInvoke.WM_DISPLAYCHANGE:
                    tray._displayChanged?.Invoke();
                    break;
                case PInvoke.WM_CLOSE:
                    PInvoke.DestroyWindow(hwnd);
                    return default;
                case PInvoke.WM_DESTROY:
                    tray.Notify(NOTIFY_ICON_MESSAGE.NIM_DELETE, balloon: false);
                    PInvoke.PostQuitMessage(0);
                    return default;
            }
            // Nach einem Neustart des Explorers ist das Symbol weg und muss neu angelegt werden.
            if (message == tray._taskbarCreated && message != 0)
            {
                tray.Notify(NOTIFY_ICON_MESSAGE.NIM_ADD, balloon: false);
            }
        }
        catch (Exception ex)
        {
            // Eine Ausnahme darf nicht über die Fensterprozedur hinaus, das beendete den Prozess.
            tray._log($"Fehler im Tray: {ex.Message}");
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Run(UInt32 commandId)
    {
        if (commandId == CheckForUpdatesId)
        {
            CheckForUpdates();
            return;
        }

        Int32 index = (Int32)(commandId - FirstCommandId);
        if (index >= 0 && index < _commands.Count)
        {
            _commands[index].Action();
        }
    }

    // Ohne async: In einer unsafe-Klasse ist await nicht erlaubt.
    private void CheckForUpdates()
    {
        if (_checkForUpdates is null || _checking)
        {
            return;
        }

        _checking = true;
        Task.Run(_checkForUpdates).ContinueWith(check =>
        {
            _checking = false;
            ShowBalloon(check.IsCompletedSuccessfully
                ? check.Result
                : $"Suche nach Updates fehlgeschlagen: {check.Exception?.GetBaseException().Message}");
        }, TaskScheduler.Default);
    }
}
