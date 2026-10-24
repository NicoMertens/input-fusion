using System.IO;
using System.IO.Pipes;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Der Injector läuft in der Benutzersitzung und spielt die Eingaben ab, die der Dienst über die Pipe
/// schickt (siehe <see cref="InputPipe"/>). Absichtlich dumm: kein Netzwerk, keine Konfiguration — wenig
/// Code im privilegierten Kontext. Den sitzungs- und desktopabhängigen Start übernimmt der Dienst
/// (Phase 2, Schritt 2); hier wird nur abgespielt.
/// </summary>
internal static class Injector
{
    // Der Dienst startet diesen Modus mit CREATE_NO_WINDOW, weil die exe eine Konsolenanwendung ist.
    public const string Argument = "--injector";

    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        InputPlayer player = new InputPlayer();
        InputDispatcher dispatcher = new InputDispatcher(player);
        // Abgespielt wird auf einem eigenen Thread, der dem Eingabe-Desktop folgt: Sperrbildschirm, Anmeldung, UAC.
        using PlaybackThread playback = new PlaybackThread();
        Console.WriteLine($"Injector gestartet. Warte auf Pipe \\\\.\\pipe\\{InputPipe.Name}. Strg+C beendet.");

        // Bricht die Pipe ab (Dienst neu gestartet, Sitzungswechsel), erneut verbinden. Nach jedem Abbruch
        // alle gedrückten Tasten lösen, damit auf dem Notebook nichts hängen bleibt.
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using NamedPipeClientStream pipe = InputPipe.CreateClient();
                await pipe.ConnectAsync(cts.Token);
                InputPipe.VerifyServer(pipe);
                using MessageConnection conn = new MessageConnection(pipe);
                Console.WriteLine("Mit dem Dienst verbunden, spiele Eingaben ab.");
                using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                Task monitors = MonitorReporter.RunAsync(conn, session.Token);

                try
                {
                    // Den Austritt an der Kante meldet der Injector über die Pipe; der Dienst reicht ihn weiter.
                    await foreach ((MessageType type, byte[] payload) in conn.ReceiveAsync(cts.Token))
                    {
                        playback.Post(() =>
                        {
                            if (dispatcher.Dispatch(type, payload) is { } exit)
                            {
                                _ = SendExitAsync(conn, exit, cts.Token);
                            }
                        });
                    }
                }
                finally
                {
                    session.Cancel();
                    await monitors;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Pipe weg; gleich neu verbinden.
            }
            catch (UnauthorizedAccessException ex)
            {
                // Fremder Prozess hat die Pipe angelegt. Nichts abspielen; der echte Dienst kommt erst an die
                // Pipe, wenn der fremde Server sie freigibt.
                Console.WriteLine($"Pipe abgelehnt: {ex.Message}");
            }

            playback.Post(player.ReleaseAll);
            if (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(500, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        playback.Post(player.ReleaseAll);
        Console.WriteLine("Injector beendet.");
    }

    /// <summary>Vom Abspiel-Thread aus, ohne auf das Senden zu warten; eine weggebrochene Pipe merkt die Empfangsschleife.</summary>
    private static async Task SendExitAsync(MessageConnection conn, SwitchBackRequest exit, CancellationToken ct)
    {
        try
        {
            await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
