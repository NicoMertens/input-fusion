using System.Buffers.Binary;
using System.Text;
using InputFusion.Core.Layout;

namespace InputFusion.Core.Protocol;

public enum ButtonState : byte { Up = 0, Down = 1 }

/// <summary>Relative Mausbewegung. Zwei solche Nachrichten lassen sich durch Summieren zusammenfassen.</summary>
public readonly record struct MouseMoveRelative(short Dx, short Dy)
{
    public byte[] ToPayload()
    {
        byte[] p = new byte[4];
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
        byte[] p = new byte[4];
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

/// <summary>Der Host gibt die Steuerung ab. (<paramref name="X"/>, <paramref name="Y"/>) ist der Eintrittspunkt
/// in Bildschirmkoordinaten des Clients, aus dem Layout berechnet.</summary>
public readonly record struct SwitchToClient(int X, int Y)
{
    public byte[] ToPayload()
    {
        byte[] p = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(p, X);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), Y);
        return p;
    }

    public static SwitchToClient FromPayload(ReadOnlySpan<byte> p) => new(
        BinaryPrimitives.ReadInt32LittleEndian(p),
        BinaryPrimitives.ReadInt32LittleEndian(p[4..]));
}

/// <summary>Der Client meldet den Austritt an seiner Kante <paramref name="Side"/> bei (<paramref name="X"/>,
/// <paramref name="Y"/>) in seinen Bildschirmkoordinaten. Der Host setzt seinen Cursor an die passende Stelle.</summary>
public readonly record struct SwitchBackRequest(EdgeSide Side, int X, int Y)
{
    public byte[] ToPayload()
    {
        byte[] p = new byte[9];
        p[0] = (byte)Side;
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(1), X);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(5), Y);
        return p;
    }

    public static SwitchBackRequest FromPayload(ReadOnlySpan<byte> p) => new(
        (EdgeSide)p[0],
        BinaryPrimitives.ReadInt32LittleEndian(p[1..]),
        BinaryPrimitives.ReadInt32LittleEndian(p[5..]));
}

/// <summary>Die Monitore einer Seite: je Monitor Rechteck, DPI und Geräte-ID (UTF-8 mit Länge).</summary>
public static class MonitorList
{
    public static byte[] ToPayload(IReadOnlyList<MonitorRect> monitors)
    {
        byte[][] ids = monitors.Select(m => Encoding.UTF8.GetBytes(m.DeviceId)).ToArray();
        byte[] p = new byte[1 + monitors.Count * 20 + ids.Sum(id => id.Length)];
        p[0] = (byte)monitors.Count;
        int o = 1;
        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorRect m = monitors[i];
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o), m.X);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 4), m.Y);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 8), m.Width);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 12), m.Height);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 16), (ushort)m.Dpi);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 18), (ushort)ids[i].Length);
            ids[i].CopyTo(p.AsSpan(o + 20));
            o += 20 + ids[i].Length;
        }
        return p;
    }

    public static MonitorRect[] FromPayload(ReadOnlySpan<byte> p)
    {
        MonitorRect[] result = new MonitorRect[p[0]];
        int o = 1;
        for (int i = 0; i < result.Length; i++)
        {
            int idLength = BinaryPrimitives.ReadUInt16LittleEndian(p[(o + 18)..]);
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
    public static byte[] ToPayload(IReadOnlyList<ReturnEdge> edges)
    {
        byte[] p = new byte[1 + edges.Count * 13];
        p[0] = (byte)edges.Count;
        for (int i = 0; i < edges.Count; i++)
        {
            int o = 1 + i * 13;
            p[o] = (byte)edges[i].Side;
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 1), edges[i].Edge);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 5), edges[i].From);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(o + 9), edges[i].To);
        }
        return p;
    }

    public static ReturnEdge[] FromPayload(ReadOnlySpan<byte> p)
    {
        ReturnEdge[] result = new ReturnEdge[p[0]];
        for (int i = 0; i < result.Length; i++)
        {
            int o = 1 + i * 13;
            result[i] = new ReturnEdge(
                (EdgeSide)p[o],
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 1)..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 5)..]),
                BinaryPrimitives.ReadInt32LittleEndian(p[(o + 9)..]));
        }
        return result;
    }
}

public readonly record struct KeyEvent(ushort VirtualKey, ushort ScanCode, ButtonState State, bool Extended)
{
    public byte[] ToPayload()
    {
        byte[] p = new byte[6];
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
