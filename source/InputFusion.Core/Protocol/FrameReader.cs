using System.Buffers.Binary;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Setzt aus einem Byte-Strom, der in beliebigen Stücken ankommt, vollständige Frames zusammen.
/// Nicht threadsicher: ein Reader je Verbindung.
/// </summary>
public sealed class FrameReader
{
    private byte[] _buffer;
    private int _count;

    public FrameReader(int initialCapacity = 1024) => _buffer = new byte[initialCapacity];

    /// <summary>Hängt empfangene Bytes an.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(_count + data.Length);
        data.CopyTo(_buffer.AsSpan(_count));
        _count += data.Length;
    }

    /// <summary>
    /// Gibt den nächsten vollständigen Frame zurück, sonst false. In einer Schleife aufrufen,
    /// bis false. Die Nutzlast ist eine Kopie, der interne Puffer wird danach verschoben.
    /// </summary>
    public bool TryRead(out MessageType type, out byte[] payload)
    {
        type = default;
        payload = [];
        if (_count < FrameCodec.HeaderSize)
            return false;

        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
        int frameLength = FrameCodec.HeaderSize + payloadLength;
        if (_count < frameLength)
            return false;

        type = (MessageType)_buffer[2];
        payload = _buffer.AsSpan(FrameCodec.HeaderSize, payloadLength).ToArray();

        int rest = _count - frameLength;
        _buffer.AsSpan(frameLength, rest).CopyTo(_buffer);
        _count = rest;
        return true;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length)
            return;
        int size = _buffer.Length * 2;
        while (size < needed)
            size *= 2;
        Array.Resize(ref _buffer, size);
    }
}
