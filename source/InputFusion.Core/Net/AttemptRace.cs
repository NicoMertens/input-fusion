namespace InputFusion.Core.Net;

/// <summary>
/// Mehrere Versuche zugleich, etwa der Verbindungsaufbau zu allen eingetragenen PCs: Der erste erfolgreiche gewinnt,
/// die übrigen werden abgebrochen. Gelingt einer im selben Moment ebenfalls, wird er verworfen.
/// </summary>
public static class AttemptRace
{
    /// <param name="attempts">Müssen auf ihr Token hin mit einem Ergebnis enden, nicht mit einer Ausnahme; sonst
    /// risse ein abgebrochener Verlierer den Gewinner mit.</param>
    /// <param name="discard">Räumt ein erfolgreiches Ergebnis ab, das nicht gewonnen hat, etwa eine offene Verbindung.</param>
    /// <returns>Den Gewinner oder default und alle Ergebnisse in der Reihenfolge der Versuche.</returns>
    public static async Task<(T? Winner, IReadOnlyList<T> Results)> FirstAsync<T>(IReadOnlyList<Func<CancellationToken, Task<T>>> attempts,
        Func<T, bool> succeeded, Action<T> discard, CancellationToken ct)
    {
        using CancellationTokenSource race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<T>[] tasks = attempts.Select(a => a(race.Token)).ToArray();
        List<Task<T>> pending = [.. tasks];
        T? winner = default;
        bool won = false;
        while (pending.Count > 0)
        {
            Task<T> done = await Task.WhenAny(pending);
            pending.Remove(done);
            T result = await done;
            if (!succeeded(result))
            {
                continue;
            }

            if (won)
            {
                discard(result);
                continue;
            }

            winner = result;
            won = true;
            race.Cancel();
        }

        return (winner, tasks.Select(t => t.Result).ToArray());
    }
}
