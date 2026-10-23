using System.IO;
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
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var player = new InputPlayer();
        var dispatcher = new InputDispatcher(player);
        Console.WriteLine($"Injector gestartet. Warte auf Pipe \\\\.\\pipe\\{InputPipe.Name}. Strg+C beendet.");

        // Bricht die Pipe ab (Dienst neu gestartet, Sitzungswechsel), erneut verbinden. Nach jedem Abbruch
        // alle gedrückten Tasten lösen, damit auf dem Notebook nichts hängen bleibt.
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = InputPipe.CreateClient();
                await pipe.ConnectAsync(cts.Token);
                using var conn = new MessageConnection(pipe);
                Console.WriteLine("Mit dem Dienst verbunden, spiele Eingaben ab.");
                using var session = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                var monitors = MonitorReporter.RunAsync(conn, session.Token);

                try
                {
                    // Den Austritt an der Kante meldet der Injector über die Pipe; der Dienst reicht ihn weiter.
                    await foreach (var (type, payload) in conn.ReceiveAsync(cts.Token))
                    {
                        if (dispatcher.Dispatch(type, payload) is { } exit)
                            await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), cts.Token);
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

            player.ReleaseAll();
            if (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(500, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        player.ReleaseAll();
        Console.WriteLine("Injector beendet.");
    }
}
