namespace InputFusion.Core.Diagnostics;

/// <summary>
/// Die letzten Zeilen eines Protokolls im Speicher, für den Reiter Diagnose der Einstellungen. Auf dem Notebook geht das
/// Protokoll sonst nur ins Ereignisprotokoll, das ein Benutzer erst suchen muss.
/// </summary>
public sealed class RecentLog(Int32 capacity = 100)
{
    private readonly Lock _lock = new();
    private readonly Queue<String> _lines = new();

    public void Add(String message)
    {
        String line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (_lock)
        {
            if (_lines.Count == capacity)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue(line);
        }
    }

    /// <summary>Älteste zuerst.</summary>
    public String[] Lines()
    {
        lock (_lock)
        {
            return [.. _lines];
        }
    }
}
