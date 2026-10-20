namespace InputFusion.Client.Service;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("InputFusion Client gestartet");
        return Task.CompletedTask;
    }
}
