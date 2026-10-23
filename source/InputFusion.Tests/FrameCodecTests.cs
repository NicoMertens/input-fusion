using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class FrameCodecTests
{
    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        byte[] payload = [1, 2, 3, 4];
        byte[] frame = FrameCodec.Write(MessageType.MouseMoveRelative, payload);

        FrameReader reader = new FrameReader();
        reader.Append(frame);

        Assert.True(reader.TryRead(out MessageType type, out byte[] read));
        Assert.Equal(MessageType.MouseMoveRelative, type);
        Assert.Equal(payload, read);
        Assert.False(reader.TryRead(out _, out _));
    }

    [Fact]
    public void Reader_ReassemblesSplitBytes()
    {
        byte[] frame = FrameCodec.Write(MessageType.Key, new KeyEvent(65, 30, ButtonState.Down, false).ToPayload());

        FrameReader reader = new FrameReader();
        // byteweise anhängen: der Reader muss über mehrere Append-Aufrufe zusammensetzen
        foreach (byte b in frame)
        {
            reader.Append([b]);
        }

        Assert.True(reader.TryRead(out MessageType type, out byte[] payload));
        Assert.Equal(MessageType.Key, type);
        Assert.Equal(new KeyEvent(65, 30, ButtonState.Down, false), KeyEvent.FromPayload(payload));
    }

    [Fact]
    public void Reader_YieldsMultipleFramesFromOneChunk()
    {
        FrameReader reader = new FrameReader();
        reader.Append(FrameCodec.Write(MessageType.Heartbeat, []));
        reader.Append(FrameCodec.Write(MessageType.MouseButton, new MouseButton(0, ButtonState.Down).ToPayload()));

        Assert.True(reader.TryRead(out MessageType first, out _));
        Assert.Equal(MessageType.Heartbeat, first);
        Assert.True(reader.TryRead(out MessageType second, out byte[] payload));
        Assert.Equal(MessageType.MouseButton, second);
        Assert.Equal(new MouseButton(0, ButtonState.Down), MouseButton.FromPayload(payload));
        Assert.False(reader.TryRead(out _, out _));
    }

    [Fact]
    public void Reader_WaitsForIncompletePayload()
    {
        byte[] frame = FrameCodec.Write(MessageType.MouseMoveAbsolute, new MouseMoveAbsolute(100, 200).ToPayload());
        FrameReader reader = new FrameReader();
        reader.Append(frame.AsSpan(0, frame.Length - 1));

        Assert.False(reader.TryRead(out _, out _));

        reader.Append(frame.AsSpan(frame.Length - 1));
        Assert.True(reader.TryRead(out MessageType type, out byte[] payload));
        Assert.Equal(MessageType.MouseMoveAbsolute, type);
        Assert.Equal(new MouseMoveAbsolute(100, 200), MouseMoveAbsolute.FromPayload(payload));
    }
}
