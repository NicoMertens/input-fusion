using NC2.InputFusion.Core.Config;

namespace NC2.InputFusion.Host;

/// <summary>
/// Ändert den Schlüssel. Ohne Client gilt er ab der nächsten Verbindung. Mit Client geht er erst an ihn und wird
/// gespeichert, wenn er ihn übernommen hat; sonst sperrte sich das Notebook aus, wenn es ihn nicht speichern kann.
/// Läuft unter dem Lock von <see cref="HostState"/>, weil beide die Konfiguration schreiben und speichern.
/// </summary>
internal sealed class KeyTransfer(HostConfig config, Lock configLock, Func<Boolean> clientConnected)
{
    // So lange wartet ein neuer Schlüssel auf die Antwort des Notebooks; ältere Clients antworten nie.
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    private String? _pendingKey;
    private String? _status;

    /// <summary>Ein neuer Schlüssel soll an den verbundenen Client; die Verbindung schickt ihn.</summary>
    public event Action<String>? Requested;

    public event Action? Changed;

    /// <summary>Der Schlüssel, der gerade übertragen wird, und wie die letzte Übertragung ausging.</summary>
    public (String? Pending, String? Status) Current
    {
        get
        {
            lock (configLock)
            {
                return (_pendingKey, _status);
            }
        }
    }

    /// <summary>Null bei Erfolg, sonst der Grund der Ablehnung.</summary>
    public String? Set(String key)
    {
        String? error = HostConfig.ValidateKey(key);
        if (error is not null)
        {
            return error;
        }

        Boolean transfer;
        lock (configLock)
        {
            if (_pendingKey is not null)
            {
                return "Der vorige Schlüssel wird noch an das Notebook übertragen.";
            }

            if (config.Key == key)
            {
                return null;
            }

            transfer = clientConnected();
            _status = null;
            if (transfer)
            {
                _pendingKey = key;
            }
            else
            {
                config.Key = key;
                config.Save();
            }
        }

        if (transfer)
        {
            Log.Write("Schlüssel geändert, übertrage ihn an das Notebook.");
            Requested?.Invoke(key);
            _ = FinishLaterAsync(key);
        }
        else
        {
            Log.Write("Schlüssel geändert, gilt ab der nächsten Verbindung.");
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>Antwort des Clients auf den neuen Schlüssel: null, wenn er ihn gespeichert hat, sonst der Grund.</summary>
    public void Answered(String? error)
    {
        String? key;
        lock (configLock)
        {
            key = _pendingKey;
        }

        if (key is null)
        {
            return;
        }

        if (error is null)
        {
            Finish(key, commit: true, "Das Notebook hat den neuen Schlüssel übernommen.");
        }
        else
        {
            Finish(key, commit: false, $"Das Notebook hat den Schlüssel nicht übernommen, er bleibt unverändert: {error}");
        }
    }

    /// <summary>Die Anmeldung mit dem Schlüssel hat geklappt; was beim vorigen Client daraus wurde, ist erledigt.</summary>
    public void ClientConnected()
    {
        lock (configLock)
        {
            _status = null;
        }
    }

    /// <summary>
    /// Ob der Client den Schlüssel noch gespeichert hat, ist offen. Speichern wie ohne Antwort; verbindet er sich nicht
    /// wieder, muss der Schlüssel dort von Hand nachgetragen werden.
    /// </summary>
    public void ClientDisconnected()
    {
        String? key;
        lock (configLock)
        {
            key = _pendingKey;
        }

        if (key is not null)
        {
            Finish(key, commit: true,
                "Die Verbindung endete während der Übertragung. Verbindet sich das Notebook nicht wieder, den Schlüssel dort von Hand eintragen.");
        }
    }

    /// <summary>
    /// Ohne Antwort trotzdem speichern: Ein älterer Client versteht die Nachricht nicht, und der Nutzer will den Schlüssel
    /// ändern. Dann muss er auf dem Notebook von Hand nachgetragen werden, wie vor der Übertragung.
    /// </summary>
    private async Task FinishLaterAsync(String key)
    {
        await Task.Delay(_timeout);
        Finish(key, commit: true,
            "Das Notebook hat nicht geantwortet, vielleicht eine ältere Version. Den Schlüssel dort von Hand eintragen.");
    }

    /// <summary>Nur für die Übertragung von <paramref name="key"/>; ist sie schon beendet, bleibt alles, wie es ist.</summary>
    private void Finish(String key, Boolean commit, String status)
    {
        lock (configLock)
        {
            if (_pendingKey != key)
            {
                return;
            }

            _pendingKey = null;
            _status = status;
            if (commit)
            {
                config.Key = key;
                config.Save();
            }
        }
        Log.Write(status);
        Changed?.Invoke();
    }
}
