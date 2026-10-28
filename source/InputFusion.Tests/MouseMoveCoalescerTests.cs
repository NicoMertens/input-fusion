using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Tests;

public class MouseMoveCoalescerTests
{
    private static MouseMoveRelative Move(MessageType type, Byte[] payload)
    {
        Assert.Equal(MessageType.MouseMoveRelative, type);
        return MouseMoveRelative.FromPayload(payload);
    }

    [Fact]
    public void AdjacentMoves_AreSummed()
    {
        MouseMoveCoalescer c = new MouseMoveCoalescer();
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(3, 4).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(5, -1).ToPayload());

        List<(MessageType Type, Byte[] Payload)> flushed = c.Flush();

        (MessageType Type, Byte[] Payload) single = Assert.Single(flushed);
        Assert.Equal(new MouseMoveRelative(8, 3), Move(single.Type, single.Payload));
        Assert.Equal(0, c.PendingCount);
    }

    [Fact]
    public void OtherMessage_SeparatesBlocks()
    {
        MouseMoveCoalescer c = new MouseMoveCoalescer();
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(1, 1).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(2, 2).ToPayload());
        c.Enqueue(MessageType.MouseButton, new MouseButton(0, ButtonState.Down).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(4, 4).ToPayload());

        List<(MessageType Type, Byte[] Payload)> flushed = c.Flush();

        Assert.Equal(3, flushed.Count);
        Assert.Equal(new MouseMoveRelative(3, 3), Move(flushed[0].Type, flushed[0].Payload));
        Assert.Equal(MessageType.MouseButton, flushed[1].Type);
        Assert.Equal(new MouseMoveRelative(4, 4), Move(flushed[2].Type, flushed[2].Payload));
    }

    [Fact]
    public void SumBeyondShortRange_IsSplit()
    {
        MouseMoveCoalescer c = new MouseMoveCoalescer();
        // 20000 + 20000 = 40000 > short.MaxValue (32767): muss auf zwei Bewegungen aufgeteilt werden
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(20000, 0).ToPayload());
        c.Enqueue(MessageType.MouseMoveRelative, new MouseMoveRelative(20000, 0).ToPayload());

        List<(MessageType Type, Byte[] Payload)> flushed = c.Flush();

        Assert.Equal(2, flushed.Count);
        Int32 totalX = flushed.Sum(f => (Int32)Move(f.Type, f.Payload).Dx);
        Assert.Equal(40000, totalX);
        Assert.All(flushed, f => Assert.InRange(Move(f.Type, f.Payload).Dx, Int16.MinValue, Int16.MaxValue));
    }

    [Fact]
    public void Flush_OnEmpty_ReturnsNothing()
    {
        Assert.Empty(new MouseMoveCoalescer().Flush());
    }
}
