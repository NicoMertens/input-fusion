namespace InputFusion.Core.Shell;

/// <summary>
/// Meldet Abbruch und Wiederverbindung als Benachrichtigung am Tray-Symbol, auf PC und Notebook. Ein Abbruch erscheint
/// erst, wenn die Verbindung nach <paramref name="delay"/> nicht wieder steht: Ein kurzer Wechsel, etwa nach einer
/// geänderten Konfiguration, soll nicht zwei Benachrichtigungen auslösen. Die Wiederverbindung erscheint nur nach einem
/// gemeldeten Abbruch, sonst käme sie bei jedem Start.
/// </summary>
public sealed class ConnectionNotifier(Action<string> show, TimeSpan delay)
{
    private readonly Lock _lock = new();
    private int _generation;
    private bool _announced;

    /// <param name="immediately">Ohne Wartezeit, etwa weil die Maus gerade zurückgesprungen ist und das erklärt gehört.</param>
    /// <returns>Erledigt, sobald entschieden ist, ob der Abbruch erscheint.</returns>
    public Task Lost(string message, bool immediately = false)
    {
        int generation;
        lock (_lock)
        {
            generation = ++_generation;
        }
        return AnnounceAsync(generation, message, immediately ? TimeSpan.Zero : delay);
    }

    public void Restored(string message)
    {
        bool announced;
        lock (_lock)
        {
            _generation++;
            announced = _announced;
            _announced = false;
        }

        if (announced)
        {
            show(message);
        }
    }

    /// <summary>Vergisst einen Abbruch, ohne etwas zu melden; etwa wenn der Tray die Verbindung zum Dienst verliert.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _generation++;
            _announced = false;
        }
    }

    private async Task AnnounceAsync(int generation, string message, TimeSpan wait)
    {
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait);
        }

        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            _announced = true;
        }
        show(message);
    }
}
