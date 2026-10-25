using System.Text;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;

namespace InputFusion.Tests;

public class SettingsMessagesTests
{
    [Fact]
    public void HostState_RoundTrip()
    {
        MonitorRect monitor = new MonitorRect(-1920, 0, 1920, 1080, 96, @"\\?\DISPLAY#GSM5B7F#5&1");
        SettingsMessage message = new HostStateMessage("PC", "0.2.0", ["192.168.1.10"], 15111, "abcdefghijkmnpqr",
            [monitor], new ClientStateInfo("192.168.1.20", "0.2.0", [monitor with { DeviceId = "N1" }], 0, -1080));

        HostStateMessage back = Assert.IsType<HostStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal("PC", back.MachineName);
        Assert.Equal(monitor, Assert.Single(back.HostMonitors));
        Assert.Equal(-1080, back.Client!.OffsetY);
        Assert.Equal("N1", Assert.Single(back.Client.Monitors).DeviceId);
    }

    [Fact]
    public void ClientState_RoundTrip()
    {
        SettingsMessage message = new ClientStateMessage("0.5.0", "PC", "192.168.1.10", 15111, "abcdefghijkmnpqr", true, true, "192.168.1.10", "0.5.0", null);

        ClientStateMessage back = Assert.IsType<ClientStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal(message, back);
    }

    [Theory]
    [InlineData(true, "PC", null, "Verbunden mit PC (192.168.1.10), Host-Version 0.5.0.")]
    [InlineData(false, "PC", "Host PC:15111 nicht erreichbar (TimedOut).", "Nicht verbunden mit PC. Host PC:15111 nicht erreichbar (TimedOut).")]
    [InlineData(false, null, null, "Nicht eingerichtet.")]
    public void ClientState_Summary(bool connected, string? host, string? problem, string expected)
    {
        ClientStateMessage state = new ClientStateMessage("0.5.0", host, null, 15111, null, false, connected,
            connected ? "192.168.1.10" : null, connected ? "0.5.0" : null, problem);

        Assert.Equal(expected, state.Summary());
    }

    [Fact]
    public void Requests_CarryTypeDiscriminator()
    {
        string json = Encoding.UTF8.GetString(new SetKeyRequest("abcdefghijkmnpqr").ToPayload());
        Assert.Contains("\"type\":\"setKey\"", json);
        Assert.Equal(new SetPortRequest(15112), SettingsMessage.FromPayload(new SetPortRequest(15112).ToPayload()));
        Assert.IsType<SetClientOffsetRequest>(SettingsMessage.FromPayload(new SetClientOffsetRequest(1, 2).ToPayload()));
        SetClientConfigRequest config = new SetClientConfigRequest("PC", null, 15111, "abcdefghijkmnpqr");
        Assert.Equal(config, SettingsMessage.FromPayload(config.ToPayload()));
    }

    [Fact]
    public void Garbage_IsNull()
    {
        Assert.Null(SettingsMessage.FromPayload(Encoding.UTF8.GetBytes("{\"type\":\"unbekannt\"}")));
        Assert.Null(SettingsMessage.FromPayload([1, 2, 3]));
    }
}
