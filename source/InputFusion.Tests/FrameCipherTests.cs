using System.IO;
using System.Security.Cryptography;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class FrameCipherTests
{
    private static readonly Byte[] _key = RandomNumberGenerator.GetBytes(FrameCipher.KeySize);

    [Fact]
    public void SealThenOpen_ReturnsTypeAndPayload_InOrder()
    {
        using FrameCipher sender = new FrameCipher(_key);
        using FrameCipher receiver = new FrameCipher(_key);

        Byte[] first = sender.Seal(MessageType.Key, [1, 2, 3]);
        Byte[] second = sender.Seal(MessageType.Heartbeat, []);

        (MessageType type, Byte[] payload) = receiver.Open(first);
        Assert.Equal(MessageType.Key, type);
        Assert.Equal(new Byte[] { 1, 2, 3 }, payload);
        (type, payload) = receiver.Open(second);
        Assert.Equal(MessageType.Heartbeat, type);
        Assert.Empty(payload);
    }

    [Fact]
    public void SameMessageTwice_LooksDifferent()
    {
        using FrameCipher sender = new FrameCipher(_key);
        Assert.NotEqual(sender.Seal(MessageType.Key, [1, 2, 3]), sender.Seal(MessageType.Key, [1, 2, 3]));
    }

    [Fact]
    public void ChangedByte_IsRejected()
    {
        using FrameCipher sender = new FrameCipher(_key);
        using FrameCipher receiver = new FrameCipher(_key);
        Byte[] frame = sender.Seal(MessageType.Key, [1, 2, 3]);
        frame[1] ^= 0x01;
        Assert.Throws<IOException>(() => receiver.Open(frame));
    }

    [Fact]
    public void ReplayedFrame_IsRejected()
    {
        using FrameCipher sender = new FrameCipher(_key);
        using FrameCipher receiver = new FrameCipher(_key);
        Byte[] frame = sender.Seal(MessageType.Key, [1, 2, 3]);
        receiver.Open(frame);
        Assert.Throws<IOException>(() => receiver.Open(frame));
    }

    [Fact]
    public void DroppedFrame_BreaksTheNext()
    {
        using FrameCipher sender = new FrameCipher(_key);
        using FrameCipher receiver = new FrameCipher(_key);
        sender.Seal(MessageType.Key, [1]);
        Byte[] second = sender.Seal(MessageType.Key, [2]);
        Assert.Throws<IOException>(() => receiver.Open(second));
    }

    [Fact]
    public void OtherKey_IsRejected()
    {
        // Wie ein Frame der Gegenrichtung, die einen eigenen Schlüssel hat.
        using FrameCipher sender = new FrameCipher(_key);
        using FrameCipher receiver = new FrameCipher(RandomNumberGenerator.GetBytes(FrameCipher.KeySize));
        Assert.Throws<IOException>(() => receiver.Open(sender.Seal(MessageType.Key, [1])));
    }

    [Fact]
    public void TooShortFrame_IsRejected()
    {
        using FrameCipher receiver = new FrameCipher(_key);
        Assert.Throws<IOException>(() => receiver.Open(new Byte[FrameCipher.TagSize]));
    }
}
