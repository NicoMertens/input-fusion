using InputFusion.Core.Layout;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class MessagesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, -7)]
    [InlineData(short.MaxValue, short.MinValue)]
    public void MouseMoveRelative_RoundTrips(short dx, short dy)
    {
        var move = new MouseMoveRelative(dx, dy);
        Assert.Equal(move, MouseMoveRelative.FromPayload(move.ToPayload()));
    }

    [Theory]
    [InlineData((ushort)0, (ushort)0)]
    [InlineData((ushort)65535, (ushort)32768)]
    public void MouseMoveAbsolute_RoundTrips(ushort x, ushort y)
    {
        var move = new MouseMoveAbsolute(x, y);
        Assert.Equal(move, MouseMoveAbsolute.FromPayload(move.ToPayload()));
    }

    [Theory]
    [InlineData((short)120, false)]
    [InlineData((short)-240, true)]
    public void MouseWheel_RoundTrips(short delta, bool horizontal)
    {
        var wheel = new MouseWheel(delta, horizontal);
        Assert.Equal(wheel, MouseWheel.FromPayload(wheel.ToPayload()));
    }

    [Fact]
    public void KeyEvent_RoundTrips()
    {
        var key = new KeyEvent(0x5B, 0x1D, ButtonState.Down, Extended: true);
        Assert.Equal(key, KeyEvent.FromPayload(key.ToPayload()));
    }

    [Fact]
    public void SwitchToClient_RoundTrips()
    {
        // Negative Koordinaten kommen vor, wenn ein Monitor des Clients links vom Hauptmonitor steht.
        var sw = new SwitchToClient(-1920, 40000);
        Assert.Equal(sw, SwitchToClient.FromPayload(sw.ToPayload()));
    }

    [Fact]
    public void SwitchBackRequest_RoundTrips()
    {
        var request = new SwitchBackRequest(EdgeSide.Top, 800, -1);
        Assert.Equal(request, SwitchBackRequest.FromPayload(request.ToPayload()));
    }

    [Fact]
    public void MonitorList_RoundTrips()
    {
        MonitorRect[] monitors =
        [
            new(0, 0, 1920, 1200, 144, @"\\?\DISPLAY#LEN40A9#4&1b2c&0&UID265988#{e6f07b5f}"),
            new(1920, -240, 2560, 1440, 96, "ÄÖÜ"),
        ];
        Assert.Equal(monitors, MonitorList.FromPayload(MonitorList.ToPayload(monitors)));
    }

    [Fact]
    public void ReturnEdgeList_RoundTrips()
    {
        ReturnEdge[] edges = [new(EdgeSide.Left, 0, 0, 240), new(EdgeSide.Bottom, 1199, -100, 1920)];
        Assert.Equal(edges, ReturnEdgeList.FromPayload(ReturnEdgeList.ToPayload(edges)));
        Assert.Empty(ReturnEdgeList.FromPayload(ReturnEdgeList.ToPayload([])));
    }

    [Fact]
    public void MouseButton_RoundTrips()
    {
        var button = new MouseButton(2, ButtonState.Up);
        Assert.Equal(button, MouseButton.FromPayload(button.ToPayload()));
    }
}
