using System.IO;
using InputFusion.Core.Diagnostics;

namespace InputFusion.Host;

/// <summary>
/// Protokoll des Agents: in die Konsole, falls eine da ist (<c>--console</c>), und immer nach
/// <c>%LOCALAPPDATA%\InputFusion\host.log</c>. Als Tray-Anwendung hat der Agent sonst keinen Ort für Meldungen.
/// </summary>
internal static class Log
{
    private const Int64 MaxFileSize = 1024 * 1024;

    private static readonly Lock _lock = new();

    public static String FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputFusion", "host.log");

    /// <summary>Die letzten Meldungen für den Reiter Diagnose der Einstellungen.</summary>
    public static RecentLog Recent { get; } = new();

    public static void Write(String message)
    {
        Recent.Add(message);
        String line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        Console.WriteLine(line);
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Eine Vorgängerdatei genügt; das Protokoll dient der Fehlersuche, nicht der Nachweisführung.
                FileInfo info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxFileSize)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
