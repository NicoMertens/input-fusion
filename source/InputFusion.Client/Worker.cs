using Microsoft.Extensions.Hosting.WindowsServices;

namespace InputFusion.Client;

/// <summary>
/// Der Windows-Dienst. Nimmt die Host-Adresse aus <see cref="ClientConfig"/> und startet den Injector als
/// SYSTEM in der aktiven Sitzung. Ohne Dienst-Umgebung (exe ohne Argument aus der Konsole) läuft derselbe
/// Ablauf mit dem Injector als Kindprozess.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ClientService.RunAsync(ClientConfig.Load, WindowsServiceHelpers.IsWindowsService(),
            message => logger.LogInformation("{Message}", message), stoppingToken);
}
