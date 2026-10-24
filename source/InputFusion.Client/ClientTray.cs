using System.IO;
using System.IO.Pipes;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;
using InputFusion.Core.Shell;

namespace InputFusion.Client;

/// <summary>
/// Das Tray-Symbol auf dem Notebook. Läuft als angemeldeter Benutzer, weil weder der Dienst (Session 0) noch der
/// Injector (SYSTEM, absichtlich ohne Oberfläche) ein Symbol zeigen können; der Dienst startet ihn je Sitzung.
/// Den Zustand holt er über die <see cref="ClientPipe"/> vom Dienst und zeigt ihn im Tooltip; Doppelklick öffnet die Einstellungen.
/// </summary>
internal static class ClientTray
{
    // Wie der Injector mit CREATE_NO_WINDOW gestartet; Konsolenausgaben sieht man nur beim Entwickeln.
    public const string Argument = "--tray";

    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(1);

    public static void Run(string[] rest) => RunAsync(OwnerProcess.Parse(rest)).GetAwaiter().GetResult();

    private static async Task RunAsync(int? ownerPid)
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        if (ownerPid is int pid)
        {
            _ = OwnerProcess.WatchAsync(pid, cts);
        }

        TrayIcon tray = new TrayIcon($"InputFusion Client {AppVersion.Current}",
            [new TrayCommand("Einstellungen …", () => SettingsLauncher.Open(Console.WriteLine, SettingsLauncher.ClientArgument))], Console.WriteLine);
        tray.Start();
        tray.SetTooltip("InputFusion: Dienst nicht erreichbar.");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using NamedPipeClientStream pipe = ClientPipe.CreateClient();
                    await pipe.ConnectAsync((int)_retryDelay.TotalMilliseconds, cts.Token);
                    ClientPipe.VerifyServer(pipe);
                    using MessageConnection connection = new MessageConnection(pipe);
                    await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(cts.Token))
                    {
                        if (type == MessageType.Settings && SettingsMessage.FromPayload(payload) is ClientStateMessage state)
                        {
                            tray.SetTooltip($"InputFusion: {state.Summary()}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    // Dienst läuft nicht oder startet gerade neu.
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine($"Pipe abgelehnt: {ex.Message}");
                }

                tray.SetTooltip("InputFusion: Dienst nicht erreichbar.");
                try { await Task.Delay(_retryDelay, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            tray.Stop();
        }
    }
}
