using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace InputFusion.Settings;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\InputFusion.Settings";

    private Mutex? _instance;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _instance = new Mutex(initiallyOwned: true, InstanceMutexName, out bool first);
        if (!first)
        {
            BringExistingToFront();
            Shutdown();
            return;
        }
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Ein zweiter Start (etwa ein weiterer Doppelklick aufs Tray-Symbol) holt das offene Fenster nach vorn.</summary>
    private static void BringExistingToFront()
    {
        using Process current = Process.GetCurrentProcess();
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id || process.MainWindowHandle == 0)
                {
                    continue;
                }

                if (IsIconic(process.MainWindowHandle))
                {
                    ShowWindow(process.MainWindowHandle, SwRestore);
                }

                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);
}
