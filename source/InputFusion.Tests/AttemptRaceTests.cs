using InputFusion.Core.Net;

namespace InputFusion.Tests;

public class AttemptRaceTests
{
    /// <summary>Liefert nach <paramref name="delayMs"/> Erfolg oder Störung; ein Abbruch endet als „abgebrochen“.</summary>
    private static Func<CancellationToken, Task<string>> Attempt(int delayMs, string result) => async ct =>
    {
        try
        {
            await Task.Delay(delayMs, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            return "abgebrochen";
        }
    };

    private static bool Succeeded(string result) => result.StartsWith("ok", StringComparison.Ordinal);

    [Fact]
    public async Task FirstSuccess_Wins_AndCancelsTheRest()
    {
        List<string> discarded = [];

        (string? winner, IReadOnlyList<string> results) = await AttemptRace.FirstAsync(
            [Attempt(5000, "ok langsam"), Attempt(10, "ok schnell"), Attempt(5000, "fehler")], Succeeded, discarded.Add, CancellationToken.None);

        Assert.Equal("ok schnell", winner);
        Assert.Equal(["abgebrochen", "ok schnell", "abgebrochen"], results);
        Assert.Empty(discarded);
    }

    [Fact]
    public async Task FailureBeforeSuccess_DoesNotEndTheRace()
    {
        (string? winner, _) = await AttemptRace.FirstAsync(
            [Attempt(0, "fehler"), Attempt(50, "ok")], Succeeded, _ => { }, CancellationToken.None);

        Assert.Equal("ok", winner);
    }

    [Fact]
    public async Task SecondSuccess_IsDiscarded()
    {
        List<string> discarded = [];
        // Gelingt trotz Abbruch, wie eine Anmeldung, die im selben Moment fertig wird.
        Func<CancellationToken, Task<string>> ignoresCancel = async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return "ok spät";
        };

        (string? winner, _) = await AttemptRace.FirstAsync([Attempt(0, "ok früh"), ignoresCancel], Succeeded, discarded.Add, CancellationToken.None);

        Assert.Equal("ok früh", winner);
        Assert.Equal(["ok spät"], discarded);
    }

    [Fact]
    public async Task AllFail_ReturnsResultsInOrder()
    {
        (string? winner, IReadOnlyList<string> results) = await AttemptRace.FirstAsync(
            [Attempt(50, "fehler A"), Attempt(0, "fehler B")], Succeeded, _ => { }, CancellationToken.None);

        Assert.Null(winner);
        Assert.Equal(["fehler A", "fehler B"], results);
    }

    [Fact]
    public async Task OuterCancel_EndsAllAttempts()
    {
        using CancellationTokenSource cts = new CancellationTokenSource(50);

        (string? winner, IReadOnlyList<string> results) = await AttemptRace.FirstAsync(
            [Attempt(5000, "ok"), Attempt(5000, "ok")], Succeeded, _ => { }, cts.Token);

        Assert.Null(winner);
        Assert.Equal(["abgebrochen", "abgebrochen"], results);
    }
}
