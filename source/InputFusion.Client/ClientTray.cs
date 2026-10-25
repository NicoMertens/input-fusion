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
/// Außerdem liest und schreibt er die Zwischenablage für den Abgleich mit dem Host, weil sie dem Benutzer gehört.
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

        ClipboardSync clipboard = new ClipboardSync(Console.WriteLine);
        // Die Verbindung zum Dienst, solange eine steht, und die Suche nach Updates, deren Antwort noch aussteht.
        MessageConnection? current = null;
        TaskCompletionSource<string>? pendingCheck = null;

        async Task<string> CheckForUpdatesAsync()
        {
            MessageConnection? connection = current;
            if (connection is null)
            {
                return "Der InputFusion-Dienst läuft nicht.";
            }

            TaskCompletionSource<string> result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingCheck = result;
            try
            {
                await connection.SendAsync(MessageType.Settings, new CheckForUpdatesRequest().ToPayload(), cts.Token);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return "Der InputFusion-Dienst läuft nicht.";
            }
            return await result.Task;
        }

        TrayIcon tray = new TrayIcon($"InputFusion Client {AppVersion.Current}",
            [new TrayCommand("Einstellungen …", () => SettingsLauncher.Open(Console.WriteLine, SettingsLauncher.ClientArgument))], Console.WriteLine,
            checkForUpdates: CheckForUpdatesAsync);
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
                    await connection.SendAsync(MessageType.ClipboardReceiver, ReadOnlyMemory<byte>.Empty, cts.Token);
                    current = connection;
                    await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(cts.Token))
                    {
                        SettingsMessage? message = type == MessageType.Settings ? SettingsMessage.FromPayload(payload) : null;
                        if (message is ClientStateMessage state)
                        {
                            tray.SetTooltip($"InputFusion: {state.Summary()}");
                        }
                        else if (message is UpdateCheckResult check)
                        {
                            pendingCheck?.TrySetResult(check.Message);
                        }
                        else if (type == MessageType.ClipboardText)
                        {
                            clipboard.Receive(payload);
                        }
                        else if (type == MessageType.ClipboardRequest)
                        {
                            foreach (byte[] chunk in clipboard.TakeChanged())
                            {
                                await connection.SendAsync(MessageType.ClipboardText, chunk, cts.Token);
                            }
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

                current = null;
                // Etwa weil der Dienst gerade für ein Update neu startet; die Antwort käme nie mehr.
                pendingCheck?.TrySetResult("Die Verbindung zum Dienst ist abgebrochen, vielleicht startet er für ein Update neu.");
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
