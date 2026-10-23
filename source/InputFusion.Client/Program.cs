using InputFusion.Client;

if (args is [Injector.Argument, ..])
{
    Injector.Run();
    return;
}

if (args is ["--connect", var hostName, ..])
{
    await SpikeClient.RunAsync(hostName);
    return;
}

// Dev-Einstieg: derselbe Ablauf wie im Dienst, aber mit dem Host aus dem Argument statt aus client.json
// und dem Injector als Kindprozess statt per CreateProcessAsUser.
if (args is ["--relay", var relayHost, ..])
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    ClientService.ConfigLoader fixedHost = (out string? error) => { error = null; return new ClientConfig(relayHost); };
    await ClientService.RunAsync(fixedHost, asService: false, Console.WriteLine, cts.Token);
    return;
}

// Ohne Argument: der Windows-Dienst. Aus einer Konsole gestartet läuft derselbe Ablauf sichtbar mit.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = ClientService.ServiceName);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
