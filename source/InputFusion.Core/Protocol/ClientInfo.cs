using System.Text.Json;
using System.Text.Json.Serialization;

namespace NC2.InputFusion.Core.Protocol;

/// <summary>
/// Angaben zum Notebook, die der Host nur anzeigt, als JSON im Frame-Typ <see cref="MessageType.ClientInfo"/>. Alle
/// Felder dürfen fehlen; so lassen sich später weitere ergänzen, ohne dass ältere Gegenseiten scheitern.
/// </summary>
/// <param name="UserName">Null, solange niemand angemeldet ist.</param>
public sealed record ClientInfo(String? MachineName, String? OsVersion, String? UserName)
{
    public Byte[] ToPayload() => JsonSerializer.SerializeToUtf8Bytes(this, ClientInfoJson.Default.ClientInfo);

    /// <summary>Null bei unlesbarer Nutzlast.</summary>
    public static ClientInfo? FromPayload(Byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, ClientInfoJson.Default.ClientInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClientInfo))]
internal sealed partial class ClientInfoJson : JsonSerializerContext;
