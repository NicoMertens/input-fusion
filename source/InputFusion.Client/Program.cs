using NC2.InputFusion.Client;
using Microsoft.Extensions.Hosting.WindowsServices;

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

if (await DevCommands.TryRunAsync(args))
{
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
ServiceHost.Run(args);
