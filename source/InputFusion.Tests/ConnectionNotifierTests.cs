using InputFusion.Core.Shell;

namespace InputFusion.Tests;

public class ConnectionNotifierTests
{
    private readonly List<String> _shown = [];

    [Fact]
    public async Task Lost_ThenRestoredInTime_ShowsNothing()
    {
        ConnectionNotifier notifier = new ConnectionNotifier(_shown.Add, TimeSpan.FromMilliseconds(200));

        Task pending = notifier.Lost("weg");
        notifier.Restored("wieder da");
        await pending;

        Assert.Empty(_shown);
    }

    [Fact]
    public async Task Lost_ThenRestoredLate_ShowsBoth()
    {
        ConnectionNotifier notifier = new ConnectionNotifier(_shown.Add, TimeSpan.FromMilliseconds(10));

        await notifier.Lost("weg");
        notifier.Restored("wieder da");

        Assert.Equal(["weg", "wieder da"], _shown);
    }

    [Fact]
    public async Task Immediately_IgnoresDelay()
    {
        ConnectionNotifier notifier = new ConnectionNotifier(_shown.Add, TimeSpan.FromHours(1));

        await notifier.Lost("weg", immediately: true);

        Assert.Equal(["weg"], _shown);
    }

    [Fact]
    public void Restored_WithoutLoss_ShowsNothing()
    {
        ConnectionNotifier notifier = new ConnectionNotifier(_shown.Add, TimeSpan.Zero);

        notifier.Restored("wieder da");

        Assert.Empty(_shown);
    }

    [Fact]
    public async Task Reset_ForgetsLoss()
    {
        ConnectionNotifier notifier = new ConnectionNotifier(_shown.Add, TimeSpan.FromMilliseconds(10));

        await notifier.Lost("weg");
        notifier.Reset();
        notifier.Restored("wieder da");

        Assert.Equal(["weg"], _shown);
    }
}
