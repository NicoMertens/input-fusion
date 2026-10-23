using InputFusion.Core.Layout;

namespace InputFusion.Tests;

public class MonitorLayoutTests
{
    // Zwei Host-Monitore nebeneinander, der rechte höher; ein Notebook mit einem Monitor bei (0, 0).
    private static readonly MonitorRect HostLeft = new(0, 0, 1920, 1080, 96, "H1");
    private static readonly MonitorRect HostRight = new(1920, -200, 2560, 1440, 96, "H2");
    private static readonly MonitorRect[] Host = [HostLeft, HostRight];
    private static readonly MonitorRect Notebook = new(0, 0, 1920, 1200, 144, "N1");
    private static readonly MonitorRect[] Client = [Notebook];

    [Fact]
    public void DefaultOffset_PlacesClientRightOfRightmostHostMonitor_TopAligned()
    {
        var (x, y) = MonitorLayout.DefaultOffset(Host, Client);
        Assert.Equal(HostRight.Right, x);
        Assert.Equal(HostRight.Top, y);
    }

    [Fact]
    public void Transition_RightEdge_CoversOnlyTheSharedSection()
    {
        // Notebook ab y = 1000 rechts am rechten Host-Monitor: gemeinsam sind y 1000 bis 1240 (dessen Unterkante).
        var layout = new MonitorLayout(Host, Client, HostRight.Right, 1000);

        var t = Assert.Single(layout.Transitions);
        Assert.Equal(EdgeSide.Right, t.Side);
        Assert.Equal("H2", t.HostMonitor.DeviceId);
        Assert.Equal(1000, t.From);
        Assert.Equal(1240, t.To);
        Assert.True(layout.IsValid);
    }

    [Fact]
    public void ReturnEdge_IsInClientCoordinates()
    {
        var layout = new MonitorLayout(Host, Client, HostRight.Right, 1000);

        var edge = Assert.Single(layout.ReturnEdges);
        Assert.Equal(new ReturnEdge(EdgeSide.Left, 0, 0, 240), edge);
    }

    [Fact]
    public void TryLeaveHost_AtRightEdge_EntersClientAtSameHeight()
    {
        var layout = new MonitorLayout(Host, Client, HostRight.Right, -200);

        // Cursor bei x = Rand − 3 auf Höhe 500, Bewegung um 10 nach rechts.
        int x = HostRight.Right - 3 + 10;
        Assert.True(layout.TryLeaveHost(x, 500, 10, 0, out var t, out var entry));
        Assert.Equal(EdgeSide.Right, t.Side);
        Assert.Equal((0, 700), entry);
    }

    [Fact]
    public void TryLeaveHost_BesideTheSharedSection_StaysOnHost()
    {
        var layout = new MonitorLayout(Host, Client, HostRight.Right, 1000);
        Assert.False(layout.TryLeaveHost(HostRight.Right, 500, 5, 0, out _, out _));
    }

    [Fact]
    public void TryLeaveHost_MovingAlongTheEdge_DoesNotLeave()
    {
        var layout = new MonitorLayout(Host, Client, HostRight.Right, -200);
        Assert.False(layout.TryLeaveHost(HostRight.Right - 1, 500, 0, 5, out _, out _));
    }

    [Fact]
    public void TryLeaveHost_ClientAbove_EntersAtBottomOfClient()
    {
        // Notebook über dem linken Host-Monitor, links bündig.
        var layout = new MonitorLayout(Host, Client, 0, -1200);

        Assert.True(layout.TryLeaveHost(300, -4, 0, -4, out var t, out var entry));
        Assert.Equal(EdgeSide.Top, t.Side);
        Assert.Equal((300, 1199), entry);
    }

    [Fact]
    public void TryReturn_MapsClientEdgeBackInsideHostMonitor()
    {
        var layout = new MonitorLayout(Host, Client, HostRight.Right, -200);

        Assert.True(layout.TryReturn(EdgeSide.Left, 0, 700, out var hostPoint));
        Assert.Equal((HostRight.Right - 1 - MonitorLayout.ReturnInset, 500), hostPoint);
    }

    [Fact]
    public void TryReturn_WrongSide_IsRejected()
    {
        var layout = new MonitorLayout(Host, Client, HostRight.Right, -200);
        Assert.False(layout.TryReturn(EdgeSide.Right, 1919, 700, out _));
    }

    [Fact]
    public void LeaveAndReturn_RoundTrip()
    {
        var layout = new MonitorLayout(Host, Client, 0, HostLeft.Bottom);

        Assert.True(layout.TryLeaveHost(800, HostLeft.Bottom - 1, 0, 3, out _, out var entry));
        Assert.Equal((800, 0), entry);

        var edge = ReturnEdge.Find(layout.ReturnEdges, entry.X, entry.Y, 0, -1);
        Assert.NotNull(edge);
        Assert.True(layout.TryReturn(edge.Value.Side, entry.X, entry.Y, out var back));
        Assert.Equal((800, HostLeft.Bottom - 1 - MonitorLayout.ReturnInset), back);
    }

    [Fact]
    public void Snap_FromFarAway_LandsAdjacentWithoutOverlap()
    {
        var (x, y) = MonitorLayout.Snap(Host, Client, 9000, 300);
        var layout = new MonitorLayout(Host, Client, x, y);

        Assert.Equal(HostRight.Right, x);
        Assert.True(layout.IsValid);
    }

    [Fact]
    public void Snap_OnTopOfHost_PushesToNearestEdge()
    {
        // Wunsch mitten auf dem linken Host-Monitor, etwas nach oben versetzt: am nächsten ist die Lage darüber.
        var (x, y) = MonitorLayout.Snap(Host, Client, 0, -300);
        var layout = new MonitorLayout(Host, Client, x, y);

        Assert.True(layout.IsValid);
        Assert.Equal(-1200, y);
    }

    [Fact]
    public void Snap_NearlyAligned_SnapsFlush()
    {
        var (_, y) = MonitorLayout.Snap(Host, Client, HostRight.Right, HostRight.Top + 25);
        Assert.Equal(HostRight.Top, y);
    }

    [Fact]
    public void Snap_KeepsMinimumOverlap()
    {
        // Gewünscht rechts neben dem linken Host-Monitor, aber fast ganz darunter.
        var (x, y) = MonitorLayout.Snap([HostLeft], Client, 2500, 1100);
        Assert.Equal(HostLeft.Right, x);
        Assert.Equal(HostLeft.Bottom - MonitorLayout.MinOverlap, y);
    }

    [Fact]
    public void IsValid_OverlappingLayout_IsFalse()
    {
        Assert.False(new MonitorLayout(Host, Client, 100, 100).IsValid);
    }

    [Fact]
    public void CombinationKey_IgnoresOrder()
    {
        Assert.Equal(
            MonitorLayout.CombinationKey([HostLeft, HostRight], Client),
            MonitorLayout.CombinationKey([HostRight, HostLeft], Client));
    }

    [Theory]
    [InlineData(EdgeSide.Left, 0, 100, -1, 0, true)]
    [InlineData(EdgeSide.Left, 0, 100, 1, 0, false)]
    [InlineData(EdgeSide.Left, 1, 100, -1, 0, false)]
    [InlineData(EdgeSide.Left, 0, 300, -1, 0, false)]
    [InlineData(EdgeSide.Bottom, 50, 1199, 0, 1, true)]
    public void ReturnEdge_IsHit(EdgeSide side, int x, int y, int dx, int dy, bool expected)
    {
        var edge = side == EdgeSide.Left ? new ReturnEdge(side, 0, 0, 240) : new ReturnEdge(side, 1199, 0, 1920);
        Assert.Equal(expected, edge.IsHit(x, y, dx, dy));
    }
}
