namespace InputFusion.Core.Protocol;

/// <summary>
/// Fasst bei Stau aufeinanderfolgende relative Mausbewegungen zu einer zusammen, damit sich
/// bei WLAN-Latenz keine Bewegungen stauen. Andere Nachrichtentypen bleiben unverändert und in
/// Reihenfolge; eine dazwischenliegende Nachricht trennt zwei Bewegungsblöcke.
/// </summary>
public sealed class MouseMoveCoalescer
{
    private readonly List<(MessageType Type, byte[] Payload)> _pending = [];

    public int PendingCount => _pending.Count;

    public void Enqueue(MessageType type, byte[] payload) => _pending.Add((type, payload));

    /// <summary>
    /// Leert die Warteschlange und gibt die verdichtete Folge zurück. Summiert dabei je Block
    /// benachbarte <see cref="MessageType.MouseMoveRelative"/>; eine Summe, die den short-Bereich
    /// sprengen würde, wird in mehrere Bewegungen aufgeteilt.
    /// </summary>
    public List<(MessageType Type, byte[] Payload)> Flush()
    {
        List<(MessageType, byte[])> result = new List<(MessageType, byte[])>();
        int i = 0;
        while (i < _pending.Count)
        {
            (MessageType type, byte[] payload) = _pending[i];
            if (type != MessageType.MouseMoveRelative)
            {
                result.Add((type, payload));
                i++;
                continue;
            }

            int sumX = 0, sumY = 0;
            while (i < _pending.Count && _pending[i].Type == MessageType.MouseMoveRelative)
            {
                MouseMoveRelative move = MouseMoveRelative.FromPayload(_pending[i].Payload);
                sumX += move.Dx;
                sumY += move.Dy;
                i++;
            }

            foreach (MouseMoveRelative move in Split(sumX, sumY))
                result.Add((MessageType.MouseMoveRelative, move.ToPayload()));
        }

        _pending.Clear();
        return result;
    }

    private static IEnumerable<MouseMoveRelative> Split(int x, int y)
    {
        if (x is 0 && y is 0)
        {
            yield return new MouseMoveRelative(0, 0);
            yield break;
        }

        while (x != 0 || y != 0)
        {
            short dx = Clamp(ref x);
            short dy = Clamp(ref y);
            yield return new MouseMoveRelative(dx, dy);
        }
    }

    private static short Clamp(ref int remaining)
    {
        int step = Math.Clamp(remaining, short.MinValue, short.MaxValue);
        remaining -= step;
        return (short)step;
    }
}
