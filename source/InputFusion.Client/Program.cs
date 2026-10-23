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

// Dev-Einstieg für Phase 2: Dienst-Rolle als Konsole. Verbindet zum Host, hostet die Pipe und startet
// den Injector als Kindprozess. Ersetzt im echten Dienst CreateProcessAsUser noch nicht (siehe PipeRelay).
if (args is ["--relay", var relayHost, ..])
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    await PipeRelay.RelayAsync(relayHost, launchInjector: true, cts.Token);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "InputFusion Client");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
