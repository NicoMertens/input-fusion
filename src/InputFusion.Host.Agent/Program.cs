namespace InputFusion.Host.Agent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Setzt Per-Monitor-V2 aus ApplicationHighDpiMode, bevor Hooks oder Fenster entstehen.
        ApplicationConfiguration.Initialize();
    }
}
