using InputFusion.Core.Layout;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class ClientArrangementTests
{
    private static readonly MonitorRect _host = new MonitorRect(0, 0, 2560, 1440, 96, @"\\?\DISPLAY#GSM5B7F#5&1");
    private static readonly MonitorRect _client = new MonitorRect(0, 0, 1920, 1200, 144, "N1");

    [Fact]
    public void RoundTrip_KeepsLayout()
    {
        MonitorLayout layout = new MonitorLayout([_host], [_client], 2560, 0);

        ClientArrangement? back = ClientArrangement.FromPayload(ClientArrangement.From(layout).ToPayload());

        MonitorLayout restored = back!.ToLayout()!;
        Assert.Equal(_host, Assert.Single(restored.HostMonitors));
        Assert.Equal(_client, Assert.Single(restored.ClientMonitors));
        Assert.Equal((2560, 0), (restored.OffsetX, restored.OffsetY));
        Assert.Equal(layout.Transitions, restored.Transitions);
    }

    [Fact]
    public void WithoutLayout_IsEmpty()
    {
        ClientArrangement? back = ClientArrangement.FromPayload(ClientArrangement.From(null).ToPayload());

        Assert.NotNull(back);
        Assert.Null(back.ToLayout());
    }

    [Fact]
    public void Garbage_IsNull()
    {
        Assert.Null(ClientArrangement.FromPayload([0xFF, 0x00]));
    }
}
