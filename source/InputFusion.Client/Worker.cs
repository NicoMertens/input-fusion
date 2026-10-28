using Microsoft.Extensions.Hosting.WindowsServices;

namespace NC2.InputFusion.Client;

/// <summary>
/// Der Windows-Dienst. Nimmt die PCs aus <see cref="ClientConfigFile"/> und startet den Injector als
/// SYSTEM in der aktiven Sitzung. Ohne Dienst-Umgebung (exe ohne Argument aus der Konsole) läuft derselbe
/// Ablauf mit dem Injector als Kindprozess. Daneben sucht der Dienst nach Updates, siehe <see cref="ClientUpdater"/>.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Boolean asService = WindowsServiceHelpers.IsWindowsService();
        Action<String> log = message => logger.LogInformation("{Message}", message);
        if (asService && ClientUpdater.RunsFromInstallDirectory)
        {
            SettingsExtractor.Run(log);
        }

        ClientUpdater updater = new ClientUpdater(asService, log);
        Task updates = updater.RunAsync(stoppingToken);
        await ClientService.RunAsync(ClientConfigFile.Load, ClientConfigFile.Save, asService, log, stoppingToken, updater.CheckSoon, updater.CheckNowAsync);
        await updates;
    }
}
