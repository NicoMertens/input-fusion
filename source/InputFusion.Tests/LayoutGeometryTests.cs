using InputFusion.Core.Layout;

namespace InputFusion.Tests;

public class LayoutGeometryTests
{
    private static readonly MonitorRect _pc = new(0, 0, 1920, 1080, 96, "PC-1");
    private static readonly MonitorRect _notebook = new(1920, 0, 2560, 1440, 144, "NB-1");

    [Fact]
    public void MonitorAt_FindsContainingMonitor()
    {
        MonitorRect[] monitors = new[] { _pc, _notebook };
        Assert.Equal("PC-1", LayoutGeometry.MonitorAt(monitors, 100, 100)!.Value.DeviceId);
        Assert.Equal("NB-1", LayoutGeometry.MonitorAt(monitors, 2000, 100)!.Value.DeviceId);
        Assert.Null(LayoutGeometry.MonitorAt(monitors, -5, 0));
    }

    [Fact]
    public void MapProportional_MapsMidpointToMidpoint()
    {
        Assert.Equal(50, LayoutGeometry.MapProportional(5, 0, 10, 0, 100));
    }

    [Fact]
    public void MapProportional_ClampsOutOfRange()
    {
        Assert.Equal(0, LayoutGeometry.MapProportional(-5, 0, 10, 0, 100));
        Assert.Equal(100, LayoutGeometry.MapProportional(15, 0, 10, 0, 100));
    }

    [Fact]
    public void MapProportional_ZeroSourceSpan_ReturnsTargetMidpoint()
    {
        Assert.Equal(50, LayoutGeometry.MapProportional(7, 5, 5, 0, 100));
    }

    [Fact]
    public void MapAcrossRightEdge_ScalesForDifferentHeights()
    {
        // Austritt in der Mitte der PC-Höhe (540 von 1080) -> Mitte der Notebook-Höhe (720 von 1440)
        (int x, int y) = LayoutGeometry.MapAcrossRightEdge(_pc, _notebook, 540);
        Assert.Equal(_notebook.Left, x);
        Assert.Equal(720, y);
    }

    [Fact]
    public void MapAcrossRightEdge_TopStaysTop()
    {
        (int _, int y) = LayoutGeometry.MapAcrossRightEdge(_pc, _notebook, _pc.Top);
        Assert.Equal(_notebook.Top, y);
    }

    [Fact]
    public void NormalizeToVirtualDesktop_CornersMapToBounds()
    {
        MonitorRect[] monitors = new[] { _pc, _notebook };
        Assert.Equal((ushort)0, LayoutGeometry.NormalizeToVirtualDesktop(monitors, 0, 0).X);
        (ushort x, ushort _) = LayoutGeometry.NormalizeToVirtualDesktop(monitors, _notebook.Right, 0);
        Assert.Equal((ushort)65535, x);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2240, 720)]
    [InlineData(4480, 1440)]
    public void DenormalizeFromVirtualDesktop_InvertsNormalize(int x, int y)
    {
        MonitorRect[] monitors = new[] { _pc, _notebook };
        (ushort nx, ushort ny) = LayoutGeometry.NormalizeToVirtualDesktop(monitors, x, y);
        (int rx, int ry) = LayoutGeometry.DenormalizeFromVirtualDesktop(monitors, nx, ny);

        // Normierung auf 16 Bit kostet höchstens einen Pixel Genauigkeit
        Assert.InRange(rx, x - 1, x + 1);
        Assert.InRange(ry, y - 1, y + 1);
    }

    [Fact]
    public void NormalizeToVirtualDesktop_EmptyList_ReturnsZero()
    {
        Assert.Equal(((ushort)0, (ushort)0), LayoutGeometry.NormalizeToVirtualDesktop([], 10, 10));
    }
}
