using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace InputFusion.Core.Shell;

/// <summary>
/// Startet die Einstellungen aus dem Tray von Host oder Client, als derselbe Benutzer. Sie halten selbst nur eine
/// Instanz und holen ein offenes Fenster nach vorn.
/// </summary>
public static class SettingsLauncher
{
    private const String ExeName = "InputFusion.Settings.exe";

    /// <summary>
    /// Erzwingt das Fenster des Notebooks. Ohne erkennen die Einstellungen die Seite an der laufenden Pipe, und auf
    /// einem Entwicklungs-PC mit Host und Client gewönne die des Hosts.
    /// </summary>
    public const String ClientArgument = "--client";

    public static void Open(Action<String> log, String? argument = null)
    {
        // Installiert liegen die Programme im selben Ordner, im Build je Projekt unter _out\.
        String[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, ExeName),
            Path.Combine(AppContext.BaseDirectory, "..", "InputFusion.Settings", ExeName),
        ];
        String? path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            log($"Einstellungen nicht gefunden, gesucht: {String.Join(", ", candidates)}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path), argument ?? "") { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            log($"Einstellungen ließen sich nicht starten: {ex.Message}");
        }
    }
}
