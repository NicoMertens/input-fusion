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
    public void SplitAndAssemble_RoundTrips(Int32 length)
    {
        String text = new String('x', length);

        List<Byte[]> chunks = ClipboardChunks.Split(text)!;

        Assert.Equal(Math.Max(1, (length + ClipboardChunks.ChunkSize - 1) / ClipboardChunks.ChunkSize), chunks.Count);
        Assert.Equal(text, Assemble(new ClipboardAssembler(), chunks));
    }

    [Fact]
    public void Umlauts_SurviveChunkBoundary()
    {
        // „ä“ hat zwei Bytes in UTF-8; bei ungerader Länge davor fällt eins davon auf die Grenze zwischen zwei Stücken.
        String text = new String('a', ClipboardChunks.ChunkSize - 1) + "äöü€";

        Assert.Equal(text, Assemble(new ClipboardAssembler(), ClipboardChunks.Split(text)!));
    }

    [Fact]
    public void Chunks_FitEncryptedFrame()
    {
        List<Byte[]> chunks = ClipboardChunks.Split(new String('x', 2 * ClipboardChunks.ChunkSize))!;

        Assert.All(chunks, c => Assert.True(c.Length <= FrameCipher.MaxPayload));
    }

    [Fact]
    public void TooLong_IsNotSplit()
    {
        Assert.Null(ClipboardChunks.Split(new String('x', ClipboardChunks.MaxBytes + 1)));
        Assert.NotNull(ClipboardChunks.Split(new String('x', ClipboardChunks.MaxBytes)));
    }

    [Fact]
    public void FirstChunk_DiscardsAbortedTransfer()
    {
        ClipboardAssembler assembler = new ClipboardAssembler();
        List<Byte[]> aborted = ClipboardChunks.Split(new String('a', 2 * ClipboardChunks.ChunkSize))!;
        Assert.Null(assembler.Add(aborted[0]));

        Assert.Equal("neu", Assemble(assembler, ClipboardChunks.Split("neu")!));
    }

    [Fact]
    public void ChunkWithoutStart_IsIgnored()
    {
        ClipboardAssembler assembler = new ClipboardAssembler();
        List<Byte[]> chunks = ClipboardChunks.Split(new String('a', 2 * ClipboardChunks.ChunkSize))!;

        Assert.Null(assembler.Add(chunks[1]));
        Assert.Null(assembler.Add([]));
    }

    [Fact]
    public void Assembler_RejectsMoreThanMax()
    {
        ClipboardAssembler assembler = new ClipboardAssembler();
        Byte[] middle = new Byte[1 + ClipboardChunks.ChunkSize];
        Byte[] first = (Byte[])middle.Clone();
        first[0] = 1;
        Byte[] last = new Byte[2];
        last[0] = 2;

        Assert.Null(assembler.Add(first));
        for (Int32 i = 0; i < ClipboardChunks.MaxBytes / ClipboardChunks.ChunkSize; i++)
        {
            Assert.Null(assembler.Add(middle));
        }

        Assert.Null(assembler.Add(last));
    }

    private static String? Assemble(ClipboardAssembler assembler, List<Byte[]> chunks)
    {
        String? text = null;
        foreach (Byte[] chunk in chunks)
        {
            Assert.Null(text);
            text = assembler.Add(chunk);
        }
        return text;
    }
}
