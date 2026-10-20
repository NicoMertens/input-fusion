using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class MouseMoveCoalescerTests
{
    private static MouseMoveRelative Move(MessageType type, byte[] payload)
    {
        Assert.Equal(MessageType.MouseMoveRelative, type);
        return MouseMoveRelative.FromPayload(payload);
    }

    [Fact]
    public void AdjacentMoves_AreSummed()
    {
        var c = new MouseMoveCoalescer();
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(3, 4).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(5, -1).ToPayload());

        var flushed = c.Flush();

        var single = Assert.Single(flushed);
        Assert.Equal(new MouseMoveRelative(8, 3), Move(single.Type, single.Payload));
        Assert.Equal(0, c.PendingCount);
    }

    [Fact]
    public void OtherMessage_SeparatesBlocks()
    {
        var c = new MouseMoveCoalescer();
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(1, 1).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(2, 2).ToPayload());
        c.Enqueue(MessageType.MouseButton, new MouseButton(0, ButtonState.Down).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(4, 4).ToPayload());

        var flushed = c.Flush();

        Assert.Equal(3, flushed.Count);
        Assert.Equal(new MouseMoveRelative(3, 3), Move(flushed[0].Type, flushed[0].Payload));
        Assert.Equal(MessageType.MouseButton, flushed[1].Type);
        Assert.Equal(new MouseMoveRelative(4, 4), Move(flushed[2].Type, flushed[2].Payload));
    }

    [Fact]
    public void SumBeyondShortRange_IsSplit()
    {
        var c = new MouseMoveCoalescer();
        // 20000 + 20000 = 40000 > short.MaxValue (32767): muss auf zwei Bewegungen aufgeteilt werden
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(20000, 0).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(20000, 0).ToPayload());

        var flushed = c.Flush();

        Assert.Equal(2, flushed.Count);
        int totalX = flushed.Sum(f => (int)Move(f.Type, f.Payload).Dx);
        Assert.Equal(40000, totalX);
        Assert.All(flushed, f => Assert.InRange(Move(f.Type, f.Payload).Dx, short.MinValue, short.MaxValue));
    }

    [Fact]
    public void Flush_OnEmpty_ReturnsNothing()
    {
        Assert.Empty(new MouseMoveCoalescer().Flush());
    }
}
