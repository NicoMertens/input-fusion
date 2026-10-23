using System.IO;

namespace InputFusion.Host;

/// <summary>
/// Protokoll des Agents: in die Konsole, falls eine da ist (<c>--console</c>), und immer nach
/// <c>%LOCALAPPDATA%\InputFusion\host.log</c>. Als Tray-Anwendung hat der Agent sonst keinen Ort für Meldungen.
/// </summary>
internal static class Log
{
    private const long MaxFileSize = 1024 * 1024;

    private static readonly Lock _lock = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputFusion", "host.log");

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        Console.WriteLine(line);
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Eine Vorgängerdatei genügt; das Protokoll dient der Fehlersuche, nicht der Nachweisführung.
                FileInfo info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxFileSize)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
