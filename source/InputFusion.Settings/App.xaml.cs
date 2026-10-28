using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using InputFusion.Core.Ipc;
using InputFusion.Core.Shell;

namespace InputFusion.Settings;

public partial class App : Application
{
    private const String InstanceMutexName = @"Local\InputFusion.Settings";
    private const String ClientExeName = "InputFusion.Client.exe";

    private Mutex? _instance;

    private void OnStartup(Object sender, StartupEventArgs e)
    {
        _instance = new Mutex(initiallyOwned: true, InstanceMutexName, out Boolean first);
        if (!first)
        {
            BringExistingToFront();
            Shutdown();
            return;
        }

        Window window = IsClientSide(e.Args) ? new ClientWindow() : new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Startet die Einstellungen per UAC erhöht neu. Die Instanz-Sperre wird vorher freigegeben, sonst holte der neue
    /// Prozess nur dieses Fenster nach vorn; lehnt der Benutzer ab, nimmt diese Instanz sie wieder.
    /// </summary>
    /// <returns>Ob der neue Prozess gestartet ist; dann schließt der Aufrufer sein Fenster.</returns>
    public Boolean RestartElevated(String argument)
    {
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        _instance = null;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, argument) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            // UAC-Abfrage abgelehnt.
            _instance = new Mutex(initiallyOwned: true, InstanceMutexName, out _);
            return false;
        }
    }

    /// <summary>
    /// Die Seite ergibt sich aus der laufenden Pipe: die des Agents auf dem PC, die des Dienstes auf dem Notebook. Läuft
    /// keins von beiden, entscheidet der Ordner: Auf dem Notebook liegt die Oberfläche neben der Client-exe.
    /// </summary>
    private static Boolean IsClientSide(String[] args)
    {
        if (args.Contains(SettingsLauncher.ClientArgument))
        {
            return true;
        }

        HashSet<String> pipes = Directory.EnumerateFiles(@"\\.\pipe\").Select(Path.GetFileName).OfType<String>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pipes.Contains(SettingsPipe.Name))
        {
            return false;
        }

        return pipes.Contains(ClientPipe.Name) || File.Exists(Path.Combine(AppContext.BaseDirectory, ClientExeName));
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

    private const Int32 SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern Boolean SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern Boolean ShowWindow(IntPtr hwnd, Int32 command);

    [DllImport("user32.dll")]
    private static extern Boolean IsIconic(IntPtr hwnd);
}
