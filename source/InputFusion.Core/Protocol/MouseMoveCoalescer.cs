namespace NC2.InputFusion.Core.Protocol;

/// <summary>
/// Fasst bei Stau aufeinanderfolgende relative Mausbewegungen zu einer zusammen, damit sich
/// bei WLAN-Latenz keine Bewegungen stauen. Andere Nachrichtentypen bleiben unverändert und in
/// Reihenfolge; eine dazwischenliegende Nachricht trennt zwei Bewegungsblöcke.
/// </summary>
public sealed class MouseMoveCoalescer
{
    private readonly List<(MessageType Type, Byte[] Payload)> _pending = [];

    public Int32 PendingCount => _pending.Count;

    public void Enqueue(MessageType type, Byte[] payload) => _pending.Add((type, payload));

    /// <summary>
    /// Leert die Warteschlange und gibt die verdichtete Folge zurück. Summiert dabei je Block
    /// benachbarte <see cref="MessageType.MouseMoveRelative"/>; eine Summe, die den short-Bereich
    /// sprengen würde, wird in mehrere Bewegungen aufgeteilt.
    /// </summary>
    public List<(MessageType Type, Byte[] Payload)> Flush()
    {
        List<(MessageType, Byte[])> result = new List<(MessageType, Byte[])>();
        Int32 i = 0;
        while (i < _pending.Count)
        {
            (MessageType type, Byte[] payload) = _pending[i];
            if (type != MessageType.MouseMoveRelative)
            {
                result.Add((type, payload));
                i++;
                continue;
            }

            Int32 sumX = 0, sumY = 0;
            while (i < _pending.Count && _pending[i].Type == MessageType.MouseMoveRelative)
            {
                MouseMoveRelative move = MouseMoveRelative.FromPayload(_pending[i].Payload);
                sumX += move.Dx;
                sumY += move.Dy;
                i++;
            }

            foreach (MouseMoveRelative move in Split(sumX, sumY))
            {
                result.Add((MessageType.MouseMoveRelative, move.ToPayload()));
            }
        }

        _pending.Clear();
        return result;
    }

    private static IEnumerable<MouseMoveRelative> Split(Int32 x, Int32 y)
    {
        if (x is 0 && y is 0)
        {
            yield return new MouseMoveRelative(0, 0);
            yield break;
        }

        while (x != 0 || y != 0)
        {
            Int16 dx = Clamp(ref x);
            Int16 dy = Clamp(ref y);
            yield return new MouseMoveRelative(dx, dy);
        }
    }

    private static Int16 Clamp(ref Int32 remaining)
    {
        Int32 step = Math.Clamp(remaining, Int16.MinValue, Int16.MaxValue);
        remaining -= step;
        return (Int16)step;
    }
}
