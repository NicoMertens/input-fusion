using InputFusion.Core.Net;

namespace InputFusion.Tests;

public class AttemptRaceTests
{
    /// <summary>Liefert nach <paramref name="delayMs"/> Erfolg oder Störung; ein Abbruch endet als „abgebrochen“.</summary>
    private static Func<CancellationToken, Task<String>> Attempt(Int32 delayMs, String result) => async ct =>
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

    private static Boolean Succeeded(String result) => result.StartsWith("ok", StringComparison.Ordinal);

    [Fact]
    public async Task FirstSuccess_Wins_AndCancelsTheRest()
    {
        List<String> discarded = [];

        (String? winner, IReadOnlyList<String> results) = await AttemptRace.FirstAsync(
            [Attempt(5000, "ok langsam"), Attempt(10, "ok schnell"), Attempt(5000, "fehler")], Succeeded, discarded.Add, CancellationToken.None);

        Assert.Equal("ok schnell", winner);
        Assert.Equal(["abgebrochen", "ok schnell", "abgebrochen"], results);
        Assert.Empty(discarded);
    }

    [Fact]
    public async Task FailureBeforeSuccess_DoesNotEndTheRace()
    {
        (String? winner, _) = await AttemptRace.FirstAsync(
            [Attempt(0, "fehler"), Attempt(50, "ok")], Succeeded, _ => { }, CancellationToken.None);

        Assert.Equal("ok", winner);
    }

    [Fact]
    public async Task SecondSuccess_IsDiscarded()
    {
        List<String> discarded = [];
        // Gelingt trotz Abbruch, wie eine Anmeldung, die im selben Moment fertig wird.
        Func<CancellationToken, Task<String>> ignoresCancel = async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return "ok spät";
        };

        (String? winner, _) = await AttemptRace.FirstAsync([Attempt(0, "ok früh"), ignoresCancel], Succeeded, discarded.Add, CancellationToken.None);

        Assert.Equal("ok früh", winner);
        Assert.Equal(["ok spät"], discarded);
    }

    [Fact]
    public async Task AllFail_ReturnsResultsInOrder()
    {
        (String? winner, IReadOnlyList<String> results) = await AttemptRace.FirstAsync(
            [Attempt(50, "fehler A"), Attempt(0, "fehler B")], Succeeded, _ => { }, CancellationToken.None);

        Assert.Null(winner);
        Assert.Equal(["fehler A", "fehler B"], results);
    }

    [Fact]
    public async Task OuterCancel_EndsAllAttempts()
    {
        using CancellationTokenSource cts = new CancellationTokenSource(50);

        (String? winner, IReadOnlyList<String> results) = await AttemptRace.FirstAsync(
            [Attempt(5000, "ok"), Attempt(5000, "ok")], Succeeded, _ => { }, cts.Token);

        Assert.Null(winner);
        Assert.Equal(["abgebrochen", "abgebrochen"], results);
    }
}
