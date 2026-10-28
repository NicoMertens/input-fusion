using System.IO;
using System.Text;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Text der Zwischenablage als Folge von <see cref="MessageType.ClipboardText"/>-Frames. Ein Frame fasst verschlüsselt
/// knapp 64 KB, längerer Text geht deshalb in Stücken. Jedes Stück beginnt mit einem Flag-Byte (erstes, letztes),
/// dahinter folgt UTF-8.
/// </summary>
public static class ClipboardChunks
{
    /// <summary>Größter übertragener Text in UTF-8-Bytes. Längerer wird nicht übertragen, weil er die Eingaben aufhielte.</summary>
    public const Int32 MaxBytes = 1024 * 1024;

    public const Int32 ChunkSize = 32 * 1024;

    internal const Byte First = 1;
    internal const Byte Last = 2;

    /// <summary>Null, wenn der Text länger als <see cref="MaxBytes"/> ist.</summary>
    public static List<Byte[]>? Split(String text)
    {
        Byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxBytes)
        {
            return null;
        }

        List<Byte[]> chunks = [];
        Int32 offset = 0;
        do
        {
            Int32 length = Math.Min(ChunkSize, bytes.Length - offset);
            Byte[] chunk = new Byte[1 + length];
            chunk[0] = (Byte)((offset == 0 ? First : 0) | (offset + length == bytes.Length ? Last : 0));
            bytes.AsSpan(offset, length).CopyTo(chunk.AsSpan(1));
            chunks.Add(chunk);
            offset += length;
        }
        while (offset < bytes.Length);
        return chunks;
    }
}

/// <summary>Setzt die Stücke aus <see cref="ClipboardChunks.Split"/> wieder zusammen. Nicht threadsicher.</summary>
public sealed class ClipboardAssembler
{
    private readonly MemoryStream _buffer = new();
    private Boolean _receiving;

    /// <summary>Der ganze Text, sobald das letzte Stück da ist, sonst null.</summary>
    public String? Add(ReadOnlySpan<Byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return null;
        }

        Byte flags = chunk[0];
        // Ein erstes Stück beginnt immer neu: Eine abgebrochene Übertragung darf die nächste nicht verderben.
        if ((flags & ClipboardChunks.First) != 0)
        {
            _buffer.SetLength(0);
            _receiving = true;
        }

        if (!_receiving)
        {
            return null;
        }

        if (_buffer.Length + chunk.Length - 1 > ClipboardChunks.MaxBytes)
        {
            _receiving = false;
            _buffer.SetLength(0);
            return null;
        }

        _buffer.Write(chunk[1..]);
        if ((flags & ClipboardChunks.Last) == 0)
        {
            return null;
        }

        _receiving = false;
        String text = Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (Int32)_buffer.Length);
        _buffer.SetLength(0);
        return text;
    }
}
