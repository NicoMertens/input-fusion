using System.Collections.Concurrent;

namespace InputFusion.Client;

/// <summary>
/// Ein eigener Thread für alles, was Eingaben abspielt oder den Cursor setzt. Er sitzt immer auf dem aktuellen
/// Eingabe-Desktop (siehe <see cref="InputDesktop"/>); das geht nur mit einem eigenen Thread, weil
/// <c>SetThreadDesktop</c> pro Thread wirkt und asynchroner Code zwischen Threadpool-Threads springt.
/// </summary>
internal sealed class PlaybackThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public PlaybackThread()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "InputFusion-Playback" };
        _thread.Start();
    }

    /// <summary>Reiht die Arbeit ein; die Reihenfolge bleibt erhalten.</summary>
    public void Post(Action action)
    {
        if (!_work.IsAddingCompleted)
        {
            _work.Add(action);
        }
    }

    /// <summary>Arbeitet alles Eingereihte noch ab und beendet den Thread.</summary>
    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _work.Dispose();
    }

    private void Loop()
    {
        foreach (Action action in _work.GetConsumingEnumerable())
        {
            if (InputDesktop.Follow() is { } desktop)
            {
                Console.WriteLine($"Eingabe-Desktop gewechselt: {desktop}");
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Ein einzelnes Ereignis darf den Abspiel-Thread nicht beenden, sonst blieben alle weiteren liegen.
                Console.WriteLine($"Abspielen fehlgeschlagen: {ex.Message}");
            }
        }
    }
}
