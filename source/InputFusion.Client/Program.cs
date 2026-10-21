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

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "InputFusion Client");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
