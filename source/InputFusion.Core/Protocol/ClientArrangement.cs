using System.Text.Json;
using System.Text.Json.Serialization;
using InputFusion.Core.Layout;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Die Anordnung, mit der der Host rechnet, als JSON im Frame-Typ <see cref="MessageType.Arrangement"/>. Der Client
/// zeigt sie nur an. Die Client-Monitore sind die, die der Host gemeldet bekam, denn nur zu ihnen passt die Lage. Leere
/// Listen heißen: kein Layout.
/// </summary>
public sealed record ClientArrangement(IReadOnlyList<MonitorRect> HostMonitors, IReadOnlyList<MonitorRect> ClientMonitors,
    int OffsetX, int OffsetY)
{
    public static ClientArrangement From(MonitorLayout? layout) =>
        layout is null ? new([], [], 0, 0) : new(layout.HostMonitors, layout.ClientMonitors, layout.OffsetX, layout.OffsetY);

    /// <summary>Null ohne Layout, sonst die Anordnung, aus der sich auch die Übergänge ergeben.</summary>
    public MonitorLayout? ToLayout() =>
        HostMonitors.Count == 0 || ClientMonitors.Count == 0 ? null : new MonitorLayout(HostMonitors, ClientMonitors, OffsetX, OffsetY);

    public byte[] ToPayload() => JsonSerializer.SerializeToUtf8Bytes(this, ClientArrangementJson.Default.ClientArrangement);

    /// <summary>Null bei unlesbarer Nutzlast.</summary>
    public static ClientArrangement? FromPayload(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, ClientArrangementJson.Default.ClientArrangement);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClientArrangement))]
internal sealed partial class ClientArrangementJson : JsonSerializerContext;
