using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Net;

namespace NC2.InputFusion.Host;

/// <summary>Was die Einstellungen unter Diagnose zeigen: die Verbindung zum Client, seit wann sie steht, und die letzte Störung.</summary>
internal sealed class ConnectionDiagnostics
{
    private readonly Lock _lock = new();
    private MessageConnection? _connection;
    private DateTimeOffset? _connectedSince;
    private String? _lastError;
    private DateTimeOffset? _lastErrorAt;

    public void Connected(MessageConnection connection)
    {
        lock (_lock)
        {
            _connection = connection;
            _connectedSince = DateTimeOffset.Now;
        }
    }

    public void Disconnected()
    {
        lock (_lock)
        {
            _connection = null;
            _connectedSince = null;
        }
    }

    /// <summary>Merkt sich die Störung; ins Protokoll schreibt sie der Aufrufer.</summary>
    public void ReportError(String message)
    {
        lock (_lock)
        {
            _lastError = message;
            _lastErrorAt = DateTimeOffset.Now;
        }
    }

    public DiagnosticsMessage ToMessage()
    {
        lock (_lock)
        {
            ConnectionStats? stats = _connection?.Stats;
            return new DiagnosticsMessage(_connection is not null, _connectedSince, stats?.RoundTrip?.TotalMilliseconds,
                stats?.SentFrames ?? 0, stats?.ReceivedFrames ?? 0, _lastError, _lastErrorAt, Log.Recent.Lines(), Log.FilePath);
        }
    }
}
