using InputFusion.Core.Protocol;

namespace InputFusion.Core.Shell;

/// <summary>
/// Abgleich der Zwischenablage mit der Gegenseite beim Wechsel. Gesendet wird nur, was sich seit dem letzten Abgleich
/// geändert hat; was von der Gegenseite kam, geht nicht wieder zurück. Ohne Text wird nichts gesendet, die Gegenseite
/// behält dann ihren. Threadsicher.
/// </summary>
public sealed class ClipboardSync(Action<string> log)
{
    private readonly Lock _lock = new();
    private readonly ClipboardAssembler _assembler = new();
    // 0 kommt als Stand nicht vor; beim ersten Wechsel geht der vorhandene Text also mit.
    private uint _synced;

    /// <summary>Die Stücke für die Gegenseite, leer, wenn es nichts zu senden gibt.</summary>
    public IReadOnlyList<byte[]> TakeChanged()
    {
        lock (_lock)
        {
            uint sequence = ClipboardText.SequenceNumber;
            if (sequence == _synced)
            {
                return [];
            }

            if (!ClipboardText.TryRead(out string? text))
            {
                log("Zwischenablage ließ sich nicht lesen, sie bleibt diesmal auf diesem Rechner.");
                return [];
            }

            _synced = sequence;
            if (string.IsNullOrEmpty(text))
            {
                return [];
            }

            if (ClipboardChunks.Split(text) is not { } chunks)
            {
                log($"Text in der Zwischenablage über {ClipboardChunks.MaxBytes / 1024} KB, er wird nicht übertragen.");
                return [];
            }
            return chunks;
        }
    }

    /// <summary>Nimmt ein Stück der Gegenseite an und schreibt den Text, sobald er vollständig ist.</summary>
    public void Receive(ReadOnlySpan<byte> chunk)
    {
        lock (_lock)
        {
            if (_assembler.Add(chunk) is not { } text)
            {
                return;
            }

            if (ClipboardText.Write(text))
            {
                // Erst nach dem Schreiben merken, sonst sähe der eigene Text wie eine Änderung aus und ginge zurück.
                _synced = ClipboardText.SequenceNumber;
            }
            else
            {
                log("Zwischenablage ließ sich nicht schreiben.");
            }
        }
    }
}
