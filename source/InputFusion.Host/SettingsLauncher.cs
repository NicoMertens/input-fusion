using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace InputFusion.Host;

/// <summary>Startet die Einstellungen. Sie halten selbst nur eine Instanz und holen ein offenes Fenster nach vorn.</summary>
internal static class SettingsLauncher
{
    private const string ExeName = "InputFusion.Settings.exe";

    public static void Open()
    {
        // Installiert liegen beide Programme im selben Ordner, im Build je Projekt unter _out\.
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, ExeName),
            Path.Combine(AppContext.BaseDirectory, "..", "InputFusion.Settings", ExeName),
        ];
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            Log.Write($"Einstellungen nicht gefunden, gesucht: {string.Join(", ", candidates)}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            Log.Write($"Einstellungen ließen sich nicht starten: {ex.Message}");
        }
    }
}
