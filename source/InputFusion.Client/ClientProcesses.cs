using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace InputFusion.Client;

/// <summary>Beendet vor dem Kopieren alles, was Dateien im Installationsordner sperren könnte.</summary>
internal static class ClientProcesses
{
    private const string SettingsName = "InputFusion.Settings";

    /// <summary>
    /// Injector, Tray, Durchstich-Client und offene Einstellungen aus dem Installationsordner, außer dem eigenen
    /// Prozess. Einstellungen anderswo (etwa die des Hosts auf einem Entwicklungs-PC) bleiben offen.
    /// </summary>
    public static void StopOthers()
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(UpdateApplier.ExeName)))
        {
            using (process)
            {
                if (process.Id != Environment.ProcessId)
                {
                    Stop(process);
                }
            }
        }

        foreach (Process process in Process.GetProcessesByName(SettingsName))
        {
            using (process)
            {
                if (IsInInstallDirectory(process))
                {
                    Stop(process);
                }
            }
        }
    }

    private static bool IsInInstallDirectory(Process process)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            return path is not null && string.Equals(Path.GetDirectoryName(path), ClientUpdater.InstallDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }
}
