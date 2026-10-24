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
[JsonDerivedType(typeof(SetClientOffsetRequest), "setClientOffset")]
[JsonDerivedType(typeof(HostStateMessage), "hostState")]
[JsonDerivedType(typeof(ClientStateMessage), "clientState")]
[JsonDerivedType(typeof(SettingsError), "error")]
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

/// <summary>Neue Lage der Client-Gruppe auf der gemeinsamen Fläche. Der Agent rastet sie selbst noch einmal ein.</summary>
public sealed record SetClientOffsetRequest(int OffsetX, int OffsetY) : SettingsMessage;

/// <summary>Der ganze Zustand des Agents, wie die Einstellungen ihn anzeigen.</summary>
public sealed record HostStateMessage(
    string MachineName,
    string Version,
    IReadOnlyList<string> Addresses,
    int Port,
    string Key,
    IReadOnlyList<MonitorRect> HostMonitors,
    ClientStateInfo? Client) : SettingsMessage;

/// <summary>Der verbundene Client. <see cref="Monitors"/> ist leer, solange er seine Monitore nicht gemeldet hat.</summary>
public sealed record ClientStateInfo(string Address, string Version, IReadOnlyList<MonitorRect> Monitors, int OffsetX, int OffsetY);

/// <summary>
/// Der Zustand des Dienstes auf dem Notebook. <see cref="Host"/> ist null, solange <c>client.json</c> fehlt oder
/// nicht lesbar ist; <see cref="Problem"/> ist die letzte Störung beim Verbinden, solange keine Verbindung steht.
/// </summary>
public sealed record ClientStateMessage(
    string Version,
    string? Host,
    int Port,
    bool Connected,
    string? HostAddress,
    string? HostVersion,
    string? Problem) : SettingsMessage
{
    /// <summary>Eine Zeile für Tooltip und Statuszeile.</summary>
    public string Summary() =>
        Connected ? $"Verbunden mit {Host} ({HostAddress}), Host-Version {HostVersion}."
        : Host is null ? $"Nicht eingerichtet. {Problem}".TrimEnd()
        : $"Nicht verbunden mit {Host}. {Problem}".TrimEnd();
}

/// <summary>Eine Änderung wurde abgelehnt; geht nur an die Einstellungen, die sie verlangt haben.</summary>
public sealed record SettingsError(string Message) : SettingsMessage;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsMessage))]
internal sealed partial class SettingsJson : JsonSerializerContext;
