using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using InputFusion.Core.Protocol;
using InputFusion.Core.Updates;

namespace InputFusion.Client;

/// <summary>
/// Selbst-Update des Dienstes aus den GitHub-Releases. Velopack passt hier nicht: Es installiert pro Benutzer nach
/// <c>%LOCALAPPDATA%</c>, und ein SYSTEM-Dienst, der eine exe aus einem vom Benutzer beschreibbaren Ordner startet,
/// wäre eine Rechteausweitung. Deshalb lädt der Dienst das Client-Paket selbst, prüft dessen SHA-256 und entpackt es
/// nach <see cref="StagingRoot"/>, also unter Program Files, wo nur Administratoren und SYSTEM schreiben. Den Tausch
/// übernimmt <see cref="UpdateApplier"/> in einem eigenen Prozess, weil die laufende exe gesperrt ist.
/// </summary>
internal sealed class ClientUpdater
{
    private static readonly TimeSpan _firstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
    // Untergrenze für außerplanmäßige Prüfungen: Die GitHub-API erlaubt ohne Anmeldung 60 Anfragen je Stunde.
    private static readonly TimeSpan _minimumGap = TimeSpan.FromMinutes(10);

    private readonly Action<string> _log;
    private readonly Uri? _latestRelease;
    private readonly SemaphoreSlim _soon = new(0, 1);
    // Die geplante und eine Suche aus dem Tray-Menü sollen nicht gleichzeitig laden und einspielen.
    private readonly SemaphoreSlim _checking = new(1, 1);
    private long _lastCheck;
    private bool _applying;

    /// <summary>Installationsort laut <c>install-client.ps1</c>; nur von dort aktualisiert sich der Dienst.</summary>
    public static string InstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "InputFusion", "Client");

    public static string StagingRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "InputFusion", "Update");

    /// <summary>Ob diese exe die installierte ist und nicht etwa ein Build unter <c>_out</c>.</summary>
    public static bool RunsFromInstallDirectory { get; } =
        string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), InstallDirectory, StringComparison.OrdinalIgnoreCase);

    public ClientUpdater(bool asService, Action<string> log)
    {
        _log = log;
        // Der Release-Workflow übergibt -p:RepositoryUrl; die SDK macht daraus dieses Attribut. Lokale Builds haben keins.
        string? repoUrl = typeof(ClientUpdater).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
        if (asService && RunsFromInstallDirectory && !string.IsNullOrWhiteSpace(repoUrl))
        {
            _latestRelease = ReleaseCheck.LatestReleaseApi(repoUrl);
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_latestRelease is null)
        {
            return;
        }

        try
        {
            await _soon.WaitAsync(_firstCheckDelay, ct);
            // Erst jetzt: Direkt nach einem Update läuft der Helfer noch kurz aus dem Staging-Ordner.
            CleanUpPreviousUpdate();
            while (!ct.IsCancellationRequested && !_applying)
            {
                await CheckAsync(ct);
                await _soon.WaitAsync(_checkInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
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

    /// <summary>Suche aus dem Tray-Menü: sofort, ohne die Untergrenze, mit dem Ergebnis als Satz für die Benachrichtigung.</summary>
    public async Task<string> CheckNowAsync(CancellationToken ct)
    {
        if (_latestRelease is null)
        {
            return "Nur der installierte Dienst sucht nach Updates.";
        }

        return await CheckAsync(ct);
    }

    private async Task<string> CheckAsync(CancellationToken ct)
    {
        await _checking.WaitAsync(ct);
        try
        {
            if (_applying)
            {
                return "Ein Update wird gerade eingespielt.";
            }

            Interlocked.Exchange(ref _lastCheck, Environment.TickCount64);
            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("InputFusion-Client", AppVersion.Current));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            GitHubRelease? release = GitHubRelease.Parse(await http.GetStringAsync(_latestRelease, ct));
            if (release is null)
            {
                return "Die Antwort von GitHub war nicht lesbar.";
            }

            Version? version = release.Draft || release.Prerelease ? null : ReleaseCheck.ParseTag(release.TagName);
            if (version is null || !ReleaseCheck.IsNewer(version, AppVersion.Current))
            {
                return $"InputFusion Client {AppVersion.Current} ist aktuell.";
            }

            // Der Workflow lädt das Client-Paket kurz nach dem Release hoch; fehlt es noch, beim nächsten Mal.
            GitHubAsset? package = release.Asset(ReleaseCheck.ClientPackage);
            GitHubAsset? checksum = release.Asset(ReleaseCheck.ClientChecksum);
            if (package is null || checksum is null)
            {
                _log($"Release {release.TagName} hat noch kein Client-Paket, neuer Versuch später.");
                return $"Release {release.TagName} hat noch kein Client-Paket; der Dienst versucht es später wieder.";
            }

            string? expected = ReleaseCheck.ParseChecksum(await http.GetStringAsync(checksum.DownloadUrl, ct));
            byte[] data = await http.GetByteArrayAsync(package.DownloadUrl, ct);
            if (expected is null || !ReleaseCheck.MatchesChecksum(data, expected))
            {
                _log($"Client-Paket {release.TagName} verworfen: Prüfsumme stimmt nicht.");
                return $"Client-Paket {release.TagName} verworfen: Prüfsumme stimmt nicht.";
            }

            string staging = Stage(version, data);
            _log($"Update auf {version} geladen, spiele es ein; der Dienst startet dabei neu.");
            StartApplier(staging);
            _applying = true;
            return $"Update auf {version} geladen; der Dienst startet zum Einspielen neu.";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            // Offline oder GitHub drosselt: Die nächste Prüfung versucht es einfach wieder.
            _log($"Suche nach Updates fehlgeschlagen: {ex.Message}");
            return $"Suche nach Updates fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>Entpackt das Paket nach Program Files\InputFusion\Update\&lt;Version&gt;.</summary>
    private static string Stage(Version version, byte[] package)
    {
        string staging = Path.Combine(StagingRoot, version.ToString());
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        using (MemoryStream stream = new MemoryStream(package))
        {
            ZipFile.ExtractToDirectory(stream, staging);
        }

        if (!File.Exists(Path.Combine(staging, UpdateApplier.ExeName)))
        {
            throw new InvalidDataException($"{UpdateApplier.ExeName} fehlt im Client-Paket.");
        }

        return staging;
    }

    /// <summary>Startet die neue exe aus dem Staging-Ordner als Helfer; sie stoppt diesen Dienst und tauscht die Dateien.</summary>
    private static void StartApplier(string staging)
    {
        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(staging, UpdateApplier.ExeName))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = staging,
        };
        info.ArgumentList.Add(UpdateApplier.Argument);
        info.ArgumentList.Add(staging);
        info.ArgumentList.Add(InstallDirectory);
        Process.Start(info)?.Dispose();
    }

    /// <summary>Reste des letzten Tauschs: Der Helfer läuft aus dem Staging-Ordner und kann ihn selbst nicht löschen.</summary>
    private void CleanUpPreviousUpdate()
    {
        try
        {
            if (Directory.Exists(StagingRoot))
            {
                Directory.Delete(StagingRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"Update-Ordner {StagingRoot} ließ sich nicht aufräumen: {ex.Message}");
        }
    }
}
