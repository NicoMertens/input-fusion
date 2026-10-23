using System.Buffers.Binary;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Rahmt Nachrichten für den TCP-Strom: [ushort Nutzlastlänge][byte Typ][Nutzlast].
/// Die Länge zählt nur die Nutzlast, nicht Kopf und Typ-Byte.
/// </summary>
public static class FrameCodec
{
    public const int HeaderSize = 3;
    public const int MaxPayload = ushort.MaxValue;

    /// <summary>Schreibt einen Frame nach <paramref name="destination"/> und gibt die geschriebene Byte-Zahl zurück.</summary>
    public static int Write(Span<byte> destination, MessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentException($"Nutzlast über {MaxPayload} Bytes", nameof(payload));
        }

        int total = HeaderSize + payload.Length;
        if (destination.Length < total)
        {
            throw new ArgumentException("Zielpuffer zu klein", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)payload.Length);
        destination[2] = (byte)type;
        payload.CopyTo(destination[HeaderSize..]);
        return total;
    }

    /// <summary>Allokierende Variante für Tests und selten gesendete Nachrichten.</summary>
    public static byte[] Write(MessageType type, ReadOnlySpan<byte> payload)
    {
        byte[] buffer = new byte[HeaderSize + payload.Length];
        Write(buffer, type, payload);
        return buffer;
    }
}
