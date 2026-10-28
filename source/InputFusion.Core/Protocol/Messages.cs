using System.Buffers.Binary;
using System.Text;
using InputFusion.Core.Layout;

namespace InputFusion.Core.Protocol;

public enum ButtonState : Byte { Up = 0, Down = 1 }

/// <summary>Relative Mausbewegung. Zwei solche Nachrichten lassen sich durch Summieren zusammenfassen.</summary>
public readonly record struct MouseMoveRelative(Int16 Dx, Int16 Dy)
{
    public Byte[] ToPayload()
    {
        Byte[] p = new Byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(p, Dx);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(2), Dy);
        return p;
    }

    public static MouseMoveRelative FromPayload(ReadOnlySpan<Byte> p) => new(
        BinaryPrimitives.ReadInt16LittleEndian(p),
        BinaryPrimitives.ReadInt16LittleEndian(p[2..]));
}

/// <summary>Absolute Position, auf 0…65535 über die Zielfläche normiert (wie beim Vorbild).</summary>
public readonly record struct MouseMoveAbsolute(UInt16 X, UInt16 Y)
{
    public Byte[] ToPayload()
    {
        Byte[] p = new Byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(p, X);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), Y);
        return p;
    }

    public static MouseMoveAbsolute FromPayload(ReadOnlySpan<Byte> p) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(p),
        BinaryPrimitives.ReadUInt16LittleEndian(p[2..]));
}

/// <summary>Mausrad. <paramref name="Delta"/> in WHEEL_DELTA-Einheiten, <paramref name="Horizontal"/> für das Querrad.</summary>
public readonly record struct MouseWheel(Int16 Delta, Boolean Horizontal)
{
    public Byte[] ToPayload() => [(Byte)(Delta & 0xFF), (Byte)((Delta >> 8) & 0xFF), Horizontal ? (Byte)1 : (Byte)0];

    public static MouseWheel FromPayload(ReadOnlySpan<Byte> p) => new(
        BinaryPrimitives.ReadInt16LittleEndian(p), p[2] != 0);
}

public readonly record struct MouseButton(Byte Button, ButtonState State)
{
    public Byte[] ToPayload() => [Button, (Byte)State];

    public static MouseButton FromPayload(ReadOnlySpan<Byte> p) => new(p[0], (ButtonState)p[1]);
}

/// <summary>Der Host gibt die Steuerung ab. (<paramref name="X"/>, <paramref name="Y"/>) ist der Eintrittspunkt
/// in Bildschirmkoordinaten des Clients, aus dem Layout berechnet.</summary>
public readonly record struct SwitchToClient(Int32 X, Int32 Y)
{
    public Byte[] ToPayload()
    {
        Byte[] p = new Byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(p, X);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), Y);
        return p;
    }

    public static SwitchToClient FromPayload(ReadOnlySpan<Byte> p) => new(
        BinaryPrimitives.ReadInt32LittleEndian(p),
        BinaryPrimitives.ReadInt32LittleEndian(p[4..]));
}

/// <summary>Der Client meldet den Austritt an seiner Kante <paramref name="Side"/> bei (<paramref name="X"/>,
/// <paramref name="Y"/>) in seinen Bildschirmkoordinaten. Der Host setzt seinen Cursor an die passende Stelle.</summary>
public readonly record struct SwitchBackRequest(EdgeSide Side, Int32 X, Int32 Y)
{
    public Byte[] ToPayload()
    {
        Byte[] p = new Byte[9];
        p[0] = (Byte)Side;
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(1), X);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(5), Y);
        return p;
    }

    public static SwitchBackRequest FromPayload(ReadOnlySpan<Byte> p) => new(
        (EdgeSide)p[0],
        BinaryPrimitives.ReadInt32LittleEndian(p[1..]),
        BinaryPrimitives.ReadInt32LittleEndian(p[5..]));
}

/// <summary>Die Monitore einer Seite: je Monitor Rechteck, DPI und Geräte-ID (UTF-8 mit Länge).</summary>
public static class MonitorList
{
    public static Byte[] ToPayload(IReadOnlyList<MonitorRect> monitors)
    {
        Byte[][] ids = monitors.Select(m => Encoding.UTF8.GetBytes(m.DeviceId)).ToArray();
        Byte[] p = new Byte[1 + monitors.Count * 20 + ids.Sum(id => id.Length)];
        p[0] = (Byte)monitors.Count;
        Int32 o = 1;
        for (Int32 i = 0; i < monitors.Count; i++)
        {
            MonitorRect m = monitors[i];
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o), m.X);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 4), m.Y);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 8), m.Width);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 12), m.Height);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 16), (UInt16)m.Dpi);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 18), (UInt16)ids[i].Length);
            ids[i].CopyTo(p.AsSpan(o + 20));
            o += 20 + ids[i].Length;
        }
        return p;
    }

    public static MonitorRect[] FromPayload(ReadOnlySpan<Byte> p)
    {
        MonitorRect[] result = new MonitorRect[p[0]];
        Int32 o = 1;
        for (Int32 i = 0; i < result.Length; i++)
        {
            Int32 idLength = BinaryPrimitives.ReadUInt16LittleEndian(p[(o + 18)..]);
            result[i] = new MonitorRect(
                BinaryPrimitives.ReadInt32LittleEndian(p[o..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 4)..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 8)..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 12)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(p[(o + 16)..]),
                Encoding.UTF8.GetString(p.Slice(o + 20, idLength)));
            o += 20 + idLength;
        }
        return result;
    }
}

/// <summary>Die Rückwege des Clients, siehe <see cref="ReturnEdge"/>.</summary>
public static class ReturnEdgeList
{
    public static Byte[] ToPayload(IReadOnlyList<ReturnEdge> edges)
    {
        Byte[] p = new Byte[1 + edges.Count * 13];
        p[0] = (Byte)edges.Count;
        for (Int32 i = 0; i < edges.Count; i++)
        {
            Int32 o = 1 + i * 13;
            p[o] = (Byte)edges[i].Side;
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 1), edges[i].Edge);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 5), edges[i].From);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 9), edges[i].To);
        }
        return p;
    }

    public static ReturnEdge[] FromPayload(ReadOnlySpan<Byte> p)
    {
        ReturnEdge[] result = new ReturnEdge[p[0]];
        for (Int32 i = 0; i < result.Length; i++)
        {
            Int32 o = 1 + i * 13;
            result[i] = new ReturnEdge(
                (EdgeSide)p[o],
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 1)..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 5)..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 9)..]));
        }
        return result;
    }
}

public readonly record struct KeyEvent(UInt16 VirtualKey, UInt16 ScanCode, ButtonState State, Boolean Extended)
{
    public Byte[] ToPayload()
    {
        Byte[] p = new Byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(p, VirtualKey);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), ScanCode);
        p[4] = (Byte)State;
        p[5] = Extended ? (Byte)1 : (Byte)0;
        return p;
    }

    public static KeyEvent FromPayload(ReadOnlySpan<Byte> p) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(p),
        BinaryPrimitives.ReadUInt16LittleEndian(p[2..]),
        (ButtonState)p[4],
        p[5] != 0);
}
