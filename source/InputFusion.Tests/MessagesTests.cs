using InputFusion.Core.Layout;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class MessagesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, -7)]
    [InlineData(Int16.MaxValue, Int16.MinValue)]
    public void MouseMoveRelative_RoundTrips(Int16 dx, Int16 dy)
    {
        MouseMoveRelative move = new MouseMoveRelative(dx, dy);
        Assert.Equal(move, MouseMoveRelative.FromPayload(move.ToPayload()));
    }

    [Theory]
    [InlineData((UInt16)0, (UInt16)0)]
    [InlineData((UInt16)65535, (UInt16)32768)]
    public void MouseMoveAbsolute_RoundTrips(UInt16 x, UInt16 y)
    {
        MouseMoveAbsolute move = new MouseMoveAbsolute(x, y);
        Assert.Equal(move, MouseMoveAbsolute.FromPayload(move.ToPayload()));
    }

    [Theory]
    [InlineData((Int16)120, false)]
    [InlineData((Int16)(-240), true)]
    public void MouseWheel_RoundTrips(Int16 delta, Boolean horizontal)
    {
        MouseWheel wheel = new MouseWheel(delta, horizontal);
        Assert.Equal(wheel, MouseWheel.FromPayload(wheel.ToPayload()));
    }

    [Fact]
    public void KeyEvent_RoundTrips()
    {
        KeyEvent key = new KeyEvent(0x5B, 0x1D, ButtonState.Down, Extended: true);
        Assert.Equal(key, KeyEvent.FromPayload(key.ToPayload()));
    }

    [Fact]
    public void SwitchToClient_RoundTrips()
    {
        // Negative Koordinaten kommen vor, wenn ein Monitor des Clients links vom Hauptmonitor steht.
        SwitchToClient sw = new SwitchToClient(-1920, 40000);
        Assert.Equal(sw, SwitchToClient.FromPayload(sw.ToPayload()));
    }

    [Fact]
    public void SwitchBackRequest_RoundTrips()
    {
        SwitchBackRequest request = new SwitchBackRequest(EdgeSide.Top, 800, -1);
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
        MouseButton button = new MouseButton(2, ButtonState.Up);
        Assert.Equal(button, MouseButton.FromPayload(button.ToPayload()));
    }
}
