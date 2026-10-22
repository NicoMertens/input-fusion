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
        var sw = new SwitchToClient(40000);
        Assert.Equal(sw, SwitchToClient.FromPayload(sw.ToPayload()));
    }

    [Fact]
    public void SwitchBackRequest_HasNoPayload()
    {
        // Client → Host: reiner Typ ohne Nutzlast, muss sich als leerer Frame rahmen lassen
        var reader = new FrameReader();
        reader.Append(FrameCodec.Write(MessageType.SwitchBackRequest, []));
        Assert.True(reader.TryRead(out var type, out var payload));
        Assert.Equal(MessageType.SwitchBackRequest, type);
        Assert.Empty(payload);
    }

    [Fact]
    public void MouseButton_RoundTrips()
    {
        var button = new MouseButton(2, ButtonState.Up);
        Assert.Equal(button, MouseButton.FromPayload(button.ToPayload()));
    }
}
