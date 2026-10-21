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
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = PInvoke.GetCurrentThreadId();
            LowLevelHooks.Install();
            ready.Set();
            while (PInvoke.GetMessage(out MSG msg, (HWND)default, 0, 0))
            {
                // keine Fenster, nichts zu dispatchen; die Hooks feuern von selbst
            }
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
            PInvoke.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, default, default);
        _thread?.Join(1000);
    }
}
