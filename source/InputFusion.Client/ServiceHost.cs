using Microsoft.Extensions.Logging.EventLog;

namespace NC2.InputFusion.Client;

/// <summary>Richtet den Windows-Dienst ein und lässt ihn laufen.</summary>
internal static class ServiceHost
{
    public static void Run(String[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        // Im Code statt in appsettings.json, damit die einzelne exe aus dem Release dasselbe tut: Das Ereignisprotokoll
        // bekommt auch Verbindungsaufbau, Abbrüche und Injector-Starts, nicht erst Warnungen.
        builder.Logging.AddFilter<EventLogLoggerProvider>(null, LogLevel.Information);
        builder.Logging.AddFilter<EventLogLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Services.AddWindowsService(options => options.ServiceName = ClientService.ServiceName);
        builder.Services.AddHostedService<Worker>();

        IHost host = builder.Build();
        host.Run();
    }
}
