using InputFusion.Client;

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

// Ohne Argument: der Windows-Dienst. Aus einer Konsole gestartet läuft derselbe Ablauf sichtbar mit.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = ClientService.ServiceName);
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
