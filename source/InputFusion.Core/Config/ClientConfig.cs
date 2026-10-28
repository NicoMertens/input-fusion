using System.Text.Json;
using System.Text.Json.Serialization;
using InputFusion.Core.Net;

namespace InputFusion.Core.Config;

/// <summary>
/// Ein PC, mit dem sich das Notebook verbinden darf. <see cref="Host"/> ist sein Rechnername, per DNS aufgelöst;
/// <see cref="Ip"/> eine optionale feste Adresse, die zusätzlich versucht wird. <see cref="Key"/> ist der gemeinsame
/// Schlüssel aus seinen Einstellungen, siehe <c>Handshake</c>; im Zustand für Nicht-Administratoren leer.
/// <see cref="Port"/> muss zu dem des Hosts passen.
/// </summary>
public sealed record HostEntry(String Host, String Key, String? Ip = null, Int32 Port = Ports.Input);

/// <summary>
/// Konfiguration des Dienstes auf dem Notebook (<c>client.json</c>): die PCs, mit denen er sich verbinden darf, etwa
/// zu Hause und im Büro. Ohne Verbindung versucht er alle zugleich, der erste mit gelungener Anmeldung gewinnt.
/// </summary>
public sealed record ClientConfig(IReadOnlyList<HostEntry> Hosts)
{
    // Wertgleich über die Einträge: Der Dienst liest die Datei vor jedem Versuch neu, und nur eine echte Änderung
    // soll an Tray und Einstellungen gehen.
    public Boolean Equals(ClientConfig? other) => other is not null && Hosts.SequenceEqual(other.Hosts);

    public override Int32 GetHashCode() => Hosts.Count;

    public Byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, ClientConfigJson.Default.ClientConfig);

    /// <summary>
    /// Liest den Inhalt von <c>client.json</c>. Bis 0.11 stand dort genau ein PC, flach mit <c>host</c>, <c>key</c>,
    /// <c>ip</c> und <c>port</c>; das wird zu einem Eintrag. Bei Fehler null und eine Meldung in <paramref name="error"/>.
    /// </summary>
    public static ClientConfig? Parse(ReadOnlySpan<Byte> json, out String? error)
    {
        StoredClientConfig? stored;
        try
        {
            stored = JsonSerializer.Deserialize(json, ClientConfigJson.Default.StoredClientConfig);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }

        IReadOnlyList<HostEntry?> hosts = stored?.Hosts
            ?? (stored?.Host is null ? [] : [new HostEntry(stored.Host, stored.Key ?? "", stored.Ip, stored.Port)]);
        if (hosts.Count == 0)
        {
            error = "\"hosts\" fehlt oder ist leer";
            return null;
        }

        for (Int32 i = 0; i < hosts.Count; i++)
        {
            HostEntry? entry = hosts[i];
            error = entry is null || String.IsNullOrWhiteSpace(entry.Host) ? "\"host\" fehlt"
                : String.IsNullOrEmpty(entry.Key) ? "\"key\" fehlt"
                : Ports.Validate(entry.Port) is { } portError ? $"\"port\": {portError}"
                : null;
            if (error is not null)
            {
                error = $"hosts[{i}]: {error}";
                return null;
            }
        }

        error = null;
        return new ClientConfig(hosts.Select(h => h!).ToArray());
    }
}

/// <summary>Die Datei, wie sie auf der Platte stehen kann: neu mit <c>hosts</c>, bis 0.11 flach mit einem PC.</summary>
internal sealed record StoredClientConfig(
    IReadOnlyList<HostEntry?>? Hosts, String? Host = null, String? Key = null, String? Ip = null, Int32 Port = Ports.Input);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ClientConfig))]
[JsonSerializable(typeof(StoredClientConfig))]
internal sealed partial class ClientConfigJson : JsonSerializerContext;
