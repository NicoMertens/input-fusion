using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace InputFusion.Client;

/// <summary>
/// Der Helfer, der ein geladenes Update einspielt (<c>--apply-update &lt;quelle&gt; &lt;ziel&gt;</c>). Läuft als SYSTEM
/// aus dem Staging-Ordner, gestartet vom Dienst: Dienst stoppen, übrige Client-Prozesse (Injector) beenden, den
/// Installationsordner zur Sicherung umbenennen, die neuen Dateien hineinkopieren, Dienst starten. Schlägt das
/// Kopieren fehl, kommt die Sicherung zurück. Protokoll in <c>Program Files\InputFusion\update.log</c>.
/// </summary>
internal static class UpdateApplier
{
    public const String Argument = "--apply-update";
    public const String ExeName = "InputFusion.Client.exe";

    private static readonly TimeSpan _serviceTimeout = TimeSpan.FromSeconds(60);

    public static Int32 Run(String source, String target)
    {
        String backup = target + ".old";
        try
        {
            Log($"Spiele {source} nach {target} ein.");
            using ServiceController service = new ServiceController(ClientService.ServiceName);
            StopService(service);
            ClientProcesses.StopOthers();

            DeleteIfExists(backup);
            MoveWithRetry(target, backup);
            try
            {
                CopyDirectory(source, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log($"Kopieren fehlgeschlagen, stelle die alte Version wieder her: {ex.Message}");
                DeleteIfExists(target);
                MoveWithRetry(backup, target);
                StartService(service);
                return 1;
            }

            StartService(service);
            DeleteIfExists(backup);
            Log("Update eingespielt.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or Win32Exception or System.ServiceProcess.TimeoutException)
        {
            Log($"Update abgebrochen: {ex.Message}");
            return 1;
        }
    }

    private static void StopService(ServiceController service)
    {
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, _serviceTimeout);
        }
    }

    private static void StartService(ServiceController service)
    {
        service.Refresh();
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            service.Start();
        }
    }

    /// <summary>Ein gerade beendeter Prozess gibt seine Dateien nicht immer sofort frei; einige Male nachfassen.</summary>
    private static void MoveWithRetry(String from, String to)
    {
        for (Int32 attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void CopyDirectory(String source, String target)
    {
        Directory.CreateDirectory(target);
        foreach (String file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (String directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void DeleteIfExists(String directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Log(String message)
    {
        try
        {
            String path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "InputFusion", "update.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
