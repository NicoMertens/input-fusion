using System.Text.Json;
using System.Text.Json.Serialization;
using InputFusion.Core.Layout;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Nachrichten zwischen den Einstellungen und dem Agent bzw. Tray und Dienst, als JSON im Frame-Typ
/// <see cref="Protocol.MessageType.Settings"/>. Die Oberflächen fragen und ändern, Agent und Dienst antworten mit
/// ihrem ganzen Zustand und schicken ihn bei jeder Änderung von selbst erneut.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GetStateRequest), "getState")]
[JsonDerivedType(typeof(SetKeyRequest), "setKey")]
[JsonDerivedType(typeof(SetPortRequest), "setPort")]
[JsonDerivedType(typeof(SetClientOffsetRequest), "setClientOffset")]
[JsonDerivedType(typeof(SetClientConfigRequest), "setClientConfig")]
[JsonDerivedType(typeof(HostStateMessage), "hostState")]
[JsonDerivedType(typeof(ClientStateMessage), "clientState")]
[JsonDerivedType(typeof(SettingsError), "error")]
[JsonDerivedType(typeof(CheckForUpdatesRequest), "checkForUpdates")]
[JsonDerivedType(typeof(UpdateCheckResult), "updateCheckResult")]
[JsonDerivedType(typeof(GetDiagnosticsRequest), "getDiagnostics")]
[JsonDerivedType(typeof(DiagnosticsMessage), "diagnostics")]
public abstract record SettingsMessage
{
    public byte[] ToPayload() => JsonSerializer.SerializeToUtf8Bytes(this, SettingsJson.Default.SettingsMessage);

    /// <summary>Null bei unlesbarer Nutzlast; die Gegenseite ist eine eigene Anwendung und kann veraltet sein.</summary>
    public static SettingsMessage? FromPayload(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, SettingsJson.Default.SettingsMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record GetStateRequest : SettingsMessage;

public sealed record SetKeyRequest(string Key) : SettingsMessage;

/// <summary>Neuer Port, auf dem der Agent lauscht. Eine bestehende Verbindung bleibt.</summary>
public sealed record SetPortRequest(int Port) : SettingsMessage;

/// <summary>Neue Lage der Client-Gruppe auf der gemeinsamen Fläche. Der Agent rastet sie selbst noch einmal ein.</summary>
public sealed record SetClientOffsetRequest(int OffsetX, int OffsetY) : SettingsMessage;

/// <summary>Neue Liste der PCs für den Dienst auf dem Notebook, als Ganzes. Nur Administratoren dürfen sie schicken.</summary>
public sealed record SetClientConfigRequest(IReadOnlyList<Config.HostEntry> Hosts) : SettingsMessage;

/// <summary>
/// Der ganze Zustand des Agents, wie die Einstellungen ihn anzeigen. <see cref="PendingKey"/> ist ein neuer Schlüssel,
/// der gerade an den Client geht; bis der ihn übernommen hat, gilt noch <see cref="Key"/>. <see cref="KeyStatus"/> sagt,
/// wie die letzte Übertragung ausging.
/// </summary>
public sealed record HostStateMessage(
    string MachineName,
    string Version,
    IReadOnlyList<string> Addresses,
    int Port,
    string Key,
    IReadOnlyList<MonitorRect> HostMonitors,
    ClientStateInfo? Client,
    string? PendingKey = null,
    string? KeyStatus = null) : SettingsMessage;

/// <summary>
/// Der verbundene Client. <see cref="Monitors"/> ist leer, solange er seine Monitore nicht gemeldet hat; Name,
/// Windows-Version und Benutzer sind null, solange er sie nicht gemeldet hat, siehe <see cref="Protocol.ClientInfo"/>.
/// </summary>
public sealed record ClientStateInfo(
    string Address,
    string Version,
    IReadOnlyList<MonitorRect> Monitors,
    int OffsetX,
    int OffsetY,
    string? MachineName,
    string? OsVersion,
    string? UserName);

/// <summary>
/// Der Zustand des Dienstes auf dem Notebook. <see cref="Hosts"/> ist leer, solange <c>client.json</c> fehlt oder
/// nicht lesbar ist; die Schlüssel darin bekommen nur Administratoren, sonst sind sie leer. <see cref="CanEdit"/> sagt,
/// ob diese Verbindung ändern darf. <see cref="Host"/> ist der Name des PCs, mit dem der Dienst gerade verbunden ist;
/// <see cref="Problem"/> die letzte Störung beim Verbinden, solange keine Verbindung steht. <see cref="Arrangement"/> ist
/// die Anordnung, die der Host zuletzt geschickt hat; null ohne Verbindung oder bei einem Host, der sie noch nicht schickt.
/// </summary>
public sealed record ClientStateMessage(
    string Version,
    IReadOnlyList<Config.HostEntry> Hosts,
    bool CanEdit,
    bool Connected,
    string? Host,
    string? HostAddress,
    string? HostVersion,
    string? Problem,
    Protocol.ClientArrangement? Arrangement = null) : SettingsMessage
{
    /// <summary>Eine Zeile für Tooltip und Statuszeile.</summary>
    public string Summary() =>
        Connected ? $"Verbunden mit {Host} ({HostAddress}), Host-Version {HostVersion}."
        : Hosts.Count == 0 ? $"Nicht eingerichtet. {Problem}".TrimEnd()
        : $"Nicht verbunden mit {HostNames()}. {Problem}".TrimEnd();

    /// <summary>Die eingetragenen PCs für Meldungen, etwa „PC-A oder PC-B“.</summary>
    public string HostNames() =>
        Hosts.Count <= 1 ? Hosts.FirstOrDefault()?.Host ?? "dem PC"
        : $"{string.Join(", ", Hosts.SkipLast(1).Select(h => h.Host))} oder {Hosts[^1].Host}";
}

/// <summary>Eine Änderung wurde abgelehnt; geht nur an die Einstellungen, die sie verlangt haben.</summary>
public sealed record SettingsError(string Message) : SettingsMessage;

/// <summary>Tray → Dienst: sofort nach Updates suchen. Der Tray darf es nicht selbst, aktualisieren kann nur der Dienst.</summary>
public sealed record CheckForUpdatesRequest : SettingsMessage;

/// <summary>Dienst → Tray: Ergebnis der Suche als Satz für die Benachrichtigung; geht nur an den, der gefragt hat.</summary>
public sealed record UpdateCheckResult(string Message) : SettingsMessage;

/// <summary>
/// Einstellungen → Agent oder Dienst: den Stand für den Reiter Diagnose. Gefragt wird jede Sekunde, solange er offen ist;
/// im Zustand stünde er sonst bei jeder Änderung des Zählers und ginge auch an alle anderen Oberflächen.
/// </summary>
public sealed record GetDiagnosticsRequest : SettingsMessage;

/// <summary>
/// Antwort auf <see cref="GetDiagnosticsRequest"/>, nur an den, der gefragt hat. Die Zähler gelten seit
/// <see cref="ConnectedSince"/>; Nachrichten pro Sekunde rechnen die Einstellungen aus zwei Antworten aus.
/// </summary>
/// <param name="RoundTripMs">Laufzeit eines Heartbeats hin und zurück; null ohne Verbindung oder bei einer älteren Gegenseite.</param>
/// <param name="LastError">Die letzte Störung, auch wenn die Verbindung inzwischen wieder steht.</param>
/// <param name="Log">Die letzten Zeilen des Protokolls, älteste zuerst.</param>
/// <param name="LogPath">Die Protokolldatei; null, wenn es keine gibt (Dienst: nur Ereignisprotokoll).</param>
public sealed record DiagnosticsMessage(
    bool Connected,
    DateTimeOffset? ConnectedSince,
    double? RoundTripMs,
    long SentFrames,
    long ReceivedFrames,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    IReadOnlyList<string> Log,
    string? LogPath) : SettingsMessage;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsMessage))]
internal sealed partial class SettingsJson : JsonSerializerContext;
