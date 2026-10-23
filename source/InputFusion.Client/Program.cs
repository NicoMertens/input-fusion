using InputFusion.Client;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;

if (args is [UpdateApplier.Argument, string source, string target])
{
    Environment.Exit(UpdateApplier.Run(source, target));
}

if (args is [Injector.Argument, ..])
{
    Injector.Run();
    return;
}

if (args is ["--connect", var hostName, "--key", var spikeKey, ..])
{
    await SpikeClient.RunAsync(hostName, spikeKey);
    return;
}

// Dev-Einstieg: derselbe Ablauf wie im Dienst, aber mit Host und Schlüssel aus den Argumenten statt aus
// client.json und dem Injector als Kindprozess statt per CreateProcessAsUser.
if (args is ["--relay", var relayHost, "--key", var relayKey, ..])
{
    using CancellationTokenSource cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    ClientService.ConfigLoader fixedHost = (out string? error) => { error = null; return new ClientConfig(relayHost, relayKey); };
    await ClientService.RunAsync(fixedHost, asService: false, Console.WriteLine, cts.Token);
    return;
}

if (args is [ClientSetup.Argument])
{
    Environment.Exit(ClientSetup.Run());
}

// Per Doppelklick außerhalb des Installationsordners: nachfragen und sich selbst installieren.
if (ClientSetup.IsDoubleClick(args, WindowsServiceHelpers.IsWindowsService()))
{
    Environment.Exit(ClientSetup.Start());
}

// Ohne Argument: der Windows-Dienst. Aus einer Konsole im Installationsordner gestartet läuft derselbe Ablauf sichtbar mit.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
// Im Code statt in appsettings.json, damit die einzelne exe aus dem Release dasselbe tut: Das Ereignisprotokoll
// bekommt auch Verbindungsaufbau, Abbrüche und Injector-Starts, nicht erst Warnungen.
builder.Logging.AddFilter<EventLogLoggerProvider>(null, LogLevel.Information);
builder.Logging.AddFilter<EventLogLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Warning);
builder.Services.AddWindowsService(options => options.ServiceName = ClientService.ServiceName);
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
