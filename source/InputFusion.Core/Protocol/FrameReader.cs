using System.Buffers.Binary;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Setzt aus einem Byte-Strom, der in beliebigen Stücken ankommt, vollständige Frames zusammen.
/// Nicht threadsicher: ein Reader je Verbindung.
/// </summary>
public sealed class FrameReader
{
    private Byte[] _buffer;
    private Int32 _count;

    public FrameReader(Int32 initialCapacity = 1024) => _buffer = new Byte[initialCapacity];

    /// <summary>Hängt empfangene Bytes an.</summary>
    public void Append(ReadOnlySpan<Byte> data)
    {
        EnsureCapacity(_count + data.Length);
        data.CopyTo(_buffer.AsSpan(_count));
        _count += data.Length;
    }

    /// <summary>
    /// Gibt den nächsten vollständigen Frame zurück, sonst false. In einer Schleife aufrufen,
    /// bis false. Die Nutzlast ist eine Kopie, der interne Puffer wird danach verschoben.
    /// </summary>
    public Boolean TryRead(out MessageType type, out Byte[] payload)
    {
        type = default;
        payload = [];
        if (_count < FrameCodec.HeaderSize)
        {
            return false;
        }

        Int32 payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
        Int32 frameLength = FrameCodec.HeaderSize + payloadLength;
        if (_count < frameLength)
        {
            return false;
        }

        type = (MessageType)_buffer[2];
        payload = _buffer.AsSpan(FrameCodec.HeaderSize, payloadLength).ToArray();

        Int32 rest = _count - frameLength;
        _buffer.AsSpan(frameLength, rest).CopyTo(_buffer);
        _count = rest;
        return true;
    }

    private void EnsureCapacity(Int32 needed)
    {
        if (needed <= _buffer.Length)
        {
            return;
        }

        Int32 size = _buffer.Length * 2;
        while (size < needed)
        {
            size *= 2;
        }

        Array.Resize(ref _buffer, size);
    }
}
