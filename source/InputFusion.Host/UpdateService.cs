using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace InputFusion.Host;

/// <summary>
/// Holt neue Versionen über Velopack aus den GitHub-Releases: beim Start, danach alle sechs Stunden und sofort,
/// wenn sich ein Client mit anderer Protokollversion meldet. Nur eine installierte Kopie kann sich aktualisieren;
/// Builds unter <c>_out</c> suchen nicht.
/// </summary>
internal sealed class UpdateService
{
    private static readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
    // Untergrenze für außerplanmäßige Prüfungen: Die GitHub-API erlaubt ohne Anmeldung 60 Anfragen je Stunde.
    private static readonly TimeSpan _minimumGap = TimeSpan.FromMinutes(10);

    private readonly UpdateManager? _manager;
    private readonly SemaphoreSlim _soon = new(0, 1);
    private VelopackAsset? _pending;
    private long _lastCheck;
    private bool _handedOver;

    public UpdateService()
    {
        // Der Release-Workflow übergibt -p:RepositoryUrl; die SDK macht daraus dieses Attribut. Lokale Builds haben keins.
        string? repoUrl = typeof(UpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            return;
        }

        UpdateManager manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));
        if (manager.IsInstalled)
        {
            _manager = manager;
        }
    }

    public bool IsInstalled => _manager is not null;

    /// <summary>Ein Update ist geladen und wartet aufs Einspielen. Kommt auf einem Threadpool-Thread.</summary>
    public event Action<string>? UpdateReady;

    public async Task RunAsync(CancellationToken ct)
    {
        if (_manager is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested && _pending is null)
        {
            await CheckAsync();
            try
            {
                await _soon.WaitAsync(_checkInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Außerplanmäßig prüfen, etwa nach einer abweichenden Protokollversion; höchstens alle zehn Minuten.</summary>
    public void CheckSoon()
    {
        long last = Interlocked.Read(ref _lastCheck);
        if (last != 0 && Environment.TickCount64 - last < _minimumGap.TotalMilliseconds)
        {
            return;
        }

        try
        {
            _soon.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    /// <summary>
    /// Übergibt das geladene Update an den Updater von Velopack, der es einspielt, sobald dieser Prozess beendet ist,
    /// und den Host danach neu startet. Der Aufrufer beendet sich danach regulär, damit das Tray-Symbol verschwindet;
    /// <c>ApplyUpdatesAndRestart</c> beendete den Prozess hart, und das Symbol bliebe als Geist stehen.
    /// </summary>
    public bool ApplyOnExit()
    {
        if (_manager is null || _pending is null || _handedOver)
        {
            return false;
        }

        _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: true);
        _handedOver = true;
        return true;
    }

    private async Task CheckAsync()
    {
        Interlocked.Exchange(ref _lastCheck, Environment.TickCount64);
        try
        {
            UpdateInfo? info = await _manager!.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return;
            }

            Log.Write($"Update auf {info.TargetFullRelease.Version} gefunden, lade herunter.");
            await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _pending = info.TargetFullRelease;
            UpdateReady?.Invoke(_pending.Version.ToString());
        }
        catch (Exception ex)
        {
            // Offline oder GitHub drosselt: Die nächste Prüfung versucht es einfach wieder.
            Log.Write($"Suche nach Updates fehlgeschlagen: {ex.Message}");
        }
    }
}
