using System.IO;

namespace InputFusion.Client;

/// <summary>
/// Packt die Einstellungen aus, die der Release als einzelne Datei in die Client-exe einbettet. So bringen Setup per
/// Doppelklick, Installationsskript und Selbst-Update sie mit, ohne dass sie einzeln verteilt werden. Der installierte
/// Dienst ruft das bei jedem Start auf; eine gleiche Datei bleibt liegen.
/// </summary>
internal static class SettingsExtractor
{
    private const string FileName = "InputFusion.Settings.exe";

    public static void Run(Action<string> log)
    {
        using Stream? embedded = typeof(SettingsExtractor).Assembly.GetManifestResourceStream(FileName);
        if (embedded is null)
        {
            // Build ohne eingebettete Einstellungen, etwa beim Entwickeln.
            return;
        }

        string target = Path.Combine(AppContext.BaseDirectory, FileName);
        using MemoryStream buffer = new MemoryStream();
        embedded.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        try
        {
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
            {
                return;
            }

            // Erst daneben schreiben, dann tauschen: Ein Abbruch hinterlässt keine halbe exe.
            File.WriteAllBytes(target + ".new", bytes);
            File.Move(target + ".new", target, overwrite: true);
            log($"Einstellungen nach {target} ausgepackt.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Etwa weil sie gerade offen sind; beim nächsten Start des Dienstes wieder versuchen.
            log($"Einstellungen ließen sich nicht auspacken: {ex.Message}");
        }
    }
}
