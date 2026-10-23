using Microsoft.Extensions.Hosting.WindowsServices;

namespace InputFusion.Client;

/// <summary>
/// Der Windows-Dienst. Nimmt die Host-Adresse aus <see cref="ClientConfig"/> und startet den Injector als
/// SYSTEM in der aktiven Sitzung. Ohne Dienst-Umgebung (exe ohne Argument aus der Konsole) läuft derselbe
/// Ablauf mit dem Injector als Kindprozess. Daneben sucht der Dienst nach Updates, siehe <see cref="ClientUpdater"/>.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool asService = WindowsServiceHelpers.IsWindowsService();
        Action<string> log = message => logger.LogInformation("{Message}", message);
        ClientUpdater updater = new ClientUpdater(asService, log);
        Task updates = updater.RunAsync(stoppingToken);
        await ClientService.RunAsync(ClientConfig.Load, asService, log, stoppingToken, updater.CheckSoon);
        await updates;
    }
}
