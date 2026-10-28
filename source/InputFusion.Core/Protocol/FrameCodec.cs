using System.Buffers.Binary;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Rahmt Nachrichten für den TCP-Strom: [ushort Nutzlastlänge][byte Typ][Nutzlast].
/// Die Länge zählt nur die Nutzlast, nicht Kopf und Typ-Byte.
/// </summary>
public static class FrameCodec
{
    public const Int32 HeaderSize = 3;
    public const Int32 MaxPayload = UInt16.MaxValue;

    /// <summary>Schreibt einen Frame nach <paramref name="destination"/> und gibt die geschriebene Byte-Zahl zurück.</summary>
    public static Int32 Write(Span<Byte> destination, MessageType type, ReadOnlySpan<Byte> payload)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentException($"Nutzlast über {MaxPayload} Bytes", nameof(payload));
        }

        Int32 total = HeaderSize + payload.Length;
        if (destination.Length < total)
        {
            throw new ArgumentException("Zielpuffer zu klein", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (UInt16)payload.Length);
        destination[2] = (Byte)type;
        payload.CopyTo(destination[HeaderSize..]);
        return total;
    }

    /// <summary>Allokierende Variante für Tests und selten gesendete Nachrichten.</summary>
    public static Byte[] Write(MessageType type, ReadOnlySpan<Byte> payload)
    {
        Byte[] buffer = new Byte[HeaderSize + payload.Length];
        Write(buffer, type, payload);
        return buffer;
    }
}
