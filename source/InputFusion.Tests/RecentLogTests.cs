using InputFusion.Core.Diagnostics;

namespace InputFusion.Tests;

public class RecentLogTests
{
    [Fact]
    public void KeepsNewestLines()
    {
        RecentLog log = new RecentLog(capacity: 2);

        log.Add("eins");
        log.Add("zwei");
        log.Add("drei");

        string[] lines = log.Lines();
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("  zwei", lines[0]);
        Assert.EndsWith("  drei", lines[1]);
    }
}
