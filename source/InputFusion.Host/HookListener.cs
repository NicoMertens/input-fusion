using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace InputFusion.Host;

/// <summary>
/// Installiert die Hooks auf einem eigenen Thread mit eigener Nachrichtenschleife. Die Schleife ist
/// nötig, damit der Thread die Hook-Callbacks verarbeitet; beendet wird sie per WM_QUIT.
/// </summary>
internal sealed class HookListener
{
    private Thread? _thread;
    private uint _threadId;

    public void Start()
    {
        ManualResetEventSlim ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = PInvoke.GetCurrentThreadId();
            LowLevelHooks.Install();
            RawMouse.Register();
            ready.Set();
            while (PInvoke.GetMessage(out MSG msg, (HWND)default, 0, 0))
            {
                // Die Hooks feuern von selbst; abzuholen ist nur WM_INPUT. DispatchMessage gibt es an die
                // Fensterprozedur weiter, die den Raw-Input-Puffer aufräumt.
                if (msg.message == PInvoke.WM_INPUT)
                {
                    RawMouse.Handle(msg.lParam);
                }

                PInvoke.DispatchMessage(msg);
            }
            RawMouse.Unregister();
            LowLevelHooks.Uninstall();
        })
        { IsBackground = true, Name = "InputFusion-Hooks" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    public void Stop()
    {
        if (_threadId != 0)
        {
            PInvoke.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, default, default);
        }

        _thread?.Join(1000);
    }
}
