using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class ClipboardChunksTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(ClipboardChunks.ChunkSize)]
    [InlineData(ClipboardChunks.ChunkSize + 1)]
    [InlineData(3 * ClipboardChunks.ChunkSize + 17)]
    public void SplitAndAssemble_RoundTrips(int length)
    {
        string text = new string('x', length);

        List<byte[]> chunks = ClipboardChunks.Split(text)!;

        Assert.Equal(Math.Max(1, (length + ClipboardChunks.ChunkSize - 1) / ClipboardChunks.ChunkSize), chunks.Count);
        Assert.Equal(text, Assemble(new ClipboardAssembler(), chunks));
    }

    [Fact]
    public void Umlauts_SurviveChunkBoundary()
    {
        // „ä“ hat zwei Bytes in UTF-8; bei ungerader Länge davor fällt eins davon auf die Grenze zwischen zwei Stücken.
        string text = new string('a', ClipboardChunks.ChunkSize - 1) + "äöü€";

        Assert.Equal(text, Assemble(new ClipboardAssembler(), ClipboardChunks.Split(text)!));
    }

    [Fact]
    public void Chunks_FitEncryptedFrame()
    {
        List<byte[]> chunks = ClipboardChunks.Split(new string('x', 2 * ClipboardChunks.ChunkSize))!;

        Assert.All(chunks, c => Assert.True(c.Length <= FrameCipher.MaxPayload));
    }

    [Fact]
    public void TooLong_IsNotSplit()
    {
        Assert.Null(ClipboardChunks.Split(new string('x', ClipboardChunks.MaxBytes + 1)));
        Assert.NotNull(ClipboardChunks.Split(new string('x', ClipboardChunks.MaxBytes)));
    }

    [Fact]
    public void FirstChunk_DiscardsAbortedTransfer()
    {
        ClipboardAssembler assembler = new ClipboardAssembler();
        List<byte[]> aborted = ClipboardChunks.Split(new string('a', 2 * ClipboardChunks.ChunkSize))!;
        Assert.Null(assembler.Add(aborted[0]));

        Assert.Equal("neu", Assemble(assembler, ClipboardChunks.Split("neu")!));
    }

    [Fact]
    public void ChunkWithoutStart_IsIgnored()
    {
        ClipboardAssembler assembler = new ClipboardAssembler();
        List<byte[]> chunks = ClipboardChunks.Split(new string('a', 2 * ClipboardChunks.ChunkSize))!;

        Assert.Null(assembler.Add(chunks[1]));
        Assert.Null(assembler.Add([]));
    }

    [Fact]
    public void Assembler_RejectsMoreThanMax()
    {
        ClipboardAssembler assembler = new ClipboardAssembler();
        byte[] middle = new byte[1 + ClipboardChunks.ChunkSize];
        byte[] first = (byte[])middle.Clone();
        first[0] = 1;
        byte[] last = new byte[2];
        last[0] = 2;

        Assert.Null(assembler.Add(first));
        for (int i = 0; i < ClipboardChunks.MaxBytes / ClipboardChunks.ChunkSize; i++)
        {
            Assert.Null(assembler.Add(middle));
        }

        Assert.Null(assembler.Add(last));
    }

    private static string? Assemble(ClipboardAssembler assembler, List<byte[]> chunks)
    {
        string? text = null;
        foreach (byte[] chunk in chunks)
        {
            Assert.Null(text);
            text = assembler.Add(chunk);
        }
        return text;
    }
}
