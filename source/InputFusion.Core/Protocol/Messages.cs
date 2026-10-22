using System.Buffers.Binary;

namespace InputFusion.Core.Protocol;

public enum ButtonState : byte { Up = 0, Down = 1 }

/// <summary>Relative Mausbewegung. Zwei solche Nachrichten lassen sich durch Summieren zusammenfassen.</summary>
public readonly record struct MouseMoveRelative(short Dx, short Dy)
{
    public byte[] ToPayload()
    {
        var p = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(p, Dx);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(2), Dy);
        return p;
    }

    public static MouseMoveRelative FromPayload(ReadOnlySpan<byte> p) => new(
        BinaryPrimitives.ReadInt16LittleEndian(p),
        BinaryPrimitives.ReadInt16LittleEndian(p[2..]));
}

/// <summary>Absolute Position, auf 0…65535 über die Zielfläche normiert (wie beim Vorbild).</summary>
public readonly record struct MouseMoveAbsolute(ushort X, ushort Y)
{
    public byte[] ToPayload()
    {
        var p = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(p, X);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), Y);
        return p;
    }

    public static MouseMoveAbsolute FromPayload(ReadOnlySpan<byte> p) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(p),
        BinaryPrimitives.ReadUInt16LittleEndian(p[2..]));
}

/// <summary>Mausrad. <paramref name="Delta"/> in WHEEL_DELTA-Einheiten, <paramref name="Horizontal"/> für das Querrad.</summary>
public readonly record struct MouseWheel(short Delta, bool Horizontal)
{
    public byte[] ToPayload() => [(byte)(Delta & 0xFF), (byte)((Delta >> 8) & 0xFF), Horizontal ? (byte)1 : (byte)0];

    public static MouseWheel FromPayload(ReadOnlySpan<byte> p) => new(
        BinaryPrimitives.ReadInt16LittleEndian(p), p[2] != 0);
}

public readonly record struct MouseButton(byte Button, ButtonState State)
{
    public byte[] ToPayload() => [Button, (byte)State];

    public static MouseButton FromPayload(ReadOnlySpan<byte> p) => new(p[0], (ButtonState)p[1]);
}

/// <summary>Der Host gibt die Steuerung ab. <paramref name="Y"/> ist die Eintrittshöhe, auf 0…65535 normiert.</summary>
public readonly record struct SwitchToClient(ushort Y)
{
    public byte[] ToPayload()
    {
        var p = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(p, Y);
        return p;
    }

    public static SwitchToClient FromPayload(ReadOnlySpan<byte> p) => new(BinaryPrimitives.ReadUInt16LittleEndian(p));
}

public readonly record struct KeyEvent(ushort VirtualKey, ushort ScanCode, ButtonState State, bool Extended)
{
    public byte[] ToPayload()
    {
        var p = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(p, VirtualKey);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), ScanCode);
        p[4] = (byte)State;
        p[5] = Extended ? (byte)1 : (byte)0;
        return p;
    }

    public static KeyEvent FromPayload(ReadOnlySpan<byte> p) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(p),
        BinaryPrimitives.ReadUInt16LittleEndian(p[2..]),
        (ButtonState)p[4],
        p[5] != 0);
}
