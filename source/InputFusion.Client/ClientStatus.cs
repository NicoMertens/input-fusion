using InputFusion.Core.Config;
using InputFusion.Core.Diagnostics;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Der Zustand des Dienstes für Tray und Einstellungen. <see cref="ClientService"/> schreibt, der
/// <see cref="ClientSettingsServer"/> schickt jede Änderung an alle verbundenen Oberflächen, je nach Rechten mit oder
/// ohne Schlüssel.
/// </summary>
internal sealed class ClientStatus
{
    private readonly Lock _lock = new();
    private ClientStateMessage _state = new(AppVersion.Current, [], false, false, null, null, null, null);
    private ClientConfig? _config;
    // Nur für die Diagnose: die Verbindung zum Host, seit wann sie steht, und die letzte Störung.
    private MessageConnection? _connection;
    private DateTimeOffset? _connectedSince;
    private String? _lastError;
    private DateTimeOffset? _lastErrorAt;

    public event Action? Changed;

    /// <summary>Die letzten Meldungen des Dienstes; das ganze Protokoll steht nur im Ereignisprotokoll.</summary>
    public RecentLog Log { get; } = new();

    /// <summary>Die angemeldete Verbindung zum Host oder null.</summary>
    public void SetConnection(MessageConnection? connection)
    {
        lock (_lock)
        {
            _connection = connection;
            _connectedSince = connection is null ? null : DateTimeOffset.Now;
        }
    }

    /// <summary>Merkt sich die Störung für die Diagnose; ins Protokoll schreibt sie der Aufrufer.</summary>
    public void ReportError(String message)
    {
        lock (_lock)
        {
            _lastError = message;
            _lastErrorAt = DateTimeOffset.Now;
        }
    }

    public DiagnosticsMessage Diagnostics()
    {
        lock (_lock)
        {
            ConnectionStats? stats = _connection?.Stats;
            return new DiagnosticsMessage(_connection is not null, _connectedSince, stats?.RoundTrip?.TotalMilliseconds,
                stats?.SentFrames ?? 0, stats?.ReceivedFrames ?? 0, _lastError, _lastErrorAt, Log.Lines(), LogPath: null);
        }
    }

    /// <param name="administrator">Ob die Oberfläche als Administrator verbunden ist: nur dann mit Schlüssel.</param>
    /// <param name="canEdit">Ob sie ändern darf; Administrator und eine änderbare Konfiguration.</param>
    public ClientStateMessage Snapshot(Boolean administrator, Boolean canEdit)
    {
        lock (_lock)
        {
            return _state with
            {
                Hosts = _config?.Hosts.Select(h => administrator ? h : h with { Key = "" }).ToArray() ?? [],
                CanEdit = canEdit,
            };
        }
    }

    /// <summary>Die zuletzt gelesene Konfiguration; null, wenn <c>client.json</c> fehlt oder nicht lesbar ist.</summary>
    public void SetConfig(ClientConfig? config)
    {
        lock (_lock)
        {
            if (config == _config)
            {
                return;
            }

            _config = config;
        }
        Changed?.Invoke();
    }

    public void Update(Func<ClientStateMessage, ClientStateMessage> change)
    {
        Boolean changed;
        lock (_lock)
        {
            ClientStateMessage next = change(_state);
            changed = next != _state;
            _state = next;
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }
}
