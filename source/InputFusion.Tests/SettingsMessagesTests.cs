using System.Text;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;

namespace InputFusion.Tests;

public class SettingsMessagesTests
{
    [Fact]
    public void HostState_RoundTrip()
    {
        var monitor = new MonitorRect(-1920, 0, 1920, 1080, 96, @"\\?\DISPLAY#GSM5B7F#5&1");
        SettingsMessage message = new HostStateMessage("PC", ["192.168.1.10"], 15111, "abcdefghijkmnpqr",
            [monitor], new ClientStateInfo("192.168.1.20", [monitor with { DeviceId = "N1" }], 0, -1080));

        var back = Assert.IsType<HostStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal("PC", back.MachineName);
        Assert.Equal(monitor, Assert.Single(back.HostMonitors));
        Assert.Equal(-1080, back.Client!.OffsetY);
        Assert.Equal("N1", Assert.Single(back.Client.Monitors).DeviceId);
    }

    [Fact]
    public void Requests_CarryTypeDiscriminator()
    {
        string json = Encoding.UTF8.GetString(new SetKeyRequest("abcdefghijkmnpqr").ToPayload());
        Assert.Contains("\"type\":\"setKey\"", json);
        Assert.IsType<SetClientOffsetRequest>(SettingsMessage.FromPayload(new SetClientOffsetRequest(1, 2).ToPayload()));
    }

    [Fact]
    public void Garbage_IsNull()
    {
        Assert.Null(SettingsMessage.FromPayload(Encoding.UTF8.GetBytes("{\"type\":\"unbekannt\"}")));
        Assert.Null(SettingsMessage.FromPayload([1, 2, 3]));
    }
}
