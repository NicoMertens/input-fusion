using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace InputFusion.Core.Shell;

/// <summary>
/// Unicode-Text der Zwischenablage über die Win32-API, ohne WinForms und WPF (AOT-fähig). Braucht kein Fenster:
/// <c>OpenClipboard(NULL)</c> genügt auch zum Schreiben, entgegen dem Hinweis in der Dokumentation (geprüft unter
/// Windows 11).
/// </summary>
public static unsafe class ClipboardText
{
    // Hält eine andere Anwendung die Zwischenablage gerade offen, kurz warten statt aufzugeben.
    private const int OpenAttempts = 10;
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>Steigt bei jeder Änderung der Zwischenablage, auch bei der eigenen.</summary>
    public static uint SequenceNumber => PInvoke.GetClipboardSequenceNumber();

    /// <summary>False, wenn sich die Zwischenablage nicht öffnen ließ. <paramref name="text"/> ist null, wenn sie keinen Text enthält.</summary>
    public static bool TryRead(out string? text)
    {
        text = null;
        if (!Open())
        {
            return false;
        }

        try
        {
            if (!PInvoke.IsClipboardFormatAvailable((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT))
            {
                return true;
            }

            HGLOBAL data = (HGLOBAL)PInvoke.GetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT).Value;
            char* chars = (char*)PInvoke.GlobalLock(data);
            if (chars is null)
            {
                return true;
            }

            try
            {
                // Nicht über das Ende des Speicherblocks hinaus lesen, falls die Null am Ende fehlt.
                int max = (int)(PInvoke.GlobalSize(data) / sizeof(char));
                int length = new ReadOnlySpan<char>(chars, max).IndexOf('\0');
                text = new string(chars, 0, length < 0 ? max : length);
            }
            finally
            {
                PInvoke.GlobalUnlock(data);
            }
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    public static bool Write(string text)
    {
        // Erst den Text vorbereiten, dann leeren: Scheitert die Vorbereitung, bleibt der alte Inhalt stehen.
        nuint size = (nuint)((text.Length + 1) * sizeof(char));
        HGLOBAL data = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, size);
        if (data.IsNull)
        {
            return false;
        }

        char* chars = (char*)PInvoke.GlobalLock(data);
        if (chars is null)
        {
            PInvoke.GlobalFree(data);
            return false;
        }

        text.AsSpan().CopyTo(new Span<char>(chars, text.Length));
        chars[text.Length] = '\0';
        PInvoke.GlobalUnlock(data);

        if (!Open())
        {
            PInvoke.GlobalFree(data);
            return false;
        }

        try
        {
            PInvoke.EmptyClipboard();
            // Bei Erfolg gehört der Speicher der Zwischenablage, sonst bleibt er beim Aufrufer.
            if (PInvoke.SetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT, (HANDLE)data.Value).IsNull)
            {
                PInvoke.GlobalFree(data);
                return false;
            }
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    private static bool Open()
    {
        for (int i = 0; i < OpenAttempts; i++)
        {
            if (PInvoke.OpenClipboard(HWND.Null))
            {
                return true;
            }

            Thread.Sleep(_retryDelay);
        }
        return false;
    }
}
