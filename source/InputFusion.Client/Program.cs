using InputFusion.Client;
using InputFusion.Core.Config;
using InputFusion.Core.Net;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;

if (args is [UpdateApplier.Argument, String source, String target])
{
    Environment.Exit(UpdateApplier.Run(source, target));
}

if (args is [Injector.Argument, .. var injectorRest])
{
    Injector.Run(injectorRest);
    return;
}

if (args is [ClientTray.Argument, .. var trayRest])
{
    ClientTray.Run(trayRest);
    return;
}

if (args is ["--connect", var hostName, "--key", var spikeKey, .. var spikeRest])
{
    await SpikeClient.RunAsync(hostName, spikeKey, PortArgument(spikeRest));
    return;
}

// Dev-Einstieg: derselbe Ablauf wie im Dienst, aber mit Host und Schlüssel aus den Argumenten statt aus
// client.json und dem Injector als Kindprozess statt per CreateProcessAsUser.
if (args is ["--relay", var relayHost, "--key", var relayKey, .. var relayRest])
{
    Int32 relayPort = PortArgument(relayRest);
    using CancellationTokenSource cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    ClientService.ConfigLoader fixedHost = (out String? error) => { error = null; return new ClientConfig([new HostEntry(relayHost, relayKey, Port: relayPort)]); };
    await ClientService.RunAsync(fixedHost, saveConfig: null, asService: false, Console.WriteLine, cts.Token);
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

// Optional hinter den Dev-Einstiegen: --port <nummer>, sonst der Standard.
static Int32 PortArgument(String[] rest)
{
    if (rest is not ["--port", String text, ..])
    {
        return Ports.Input;
    }

    return Ports.Validate(text, out Int32 port) is { } error ? throw new ArgumentException(error) : port;
}
