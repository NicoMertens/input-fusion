using System.IO;
using System.Net.Sockets;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;

namespace InputFusion.Client;

/// <summary>
/// Reicht für die Dauer einer Host-Verbindung Frames zwischen Host und <see cref="Injector"/> weiter — die
/// Aufgabe des Dienstes aus Session 0, der selbst nicht abspielen kann. Eingaben gehen zum Injector, dessen
/// Meldungen (Austritt an der Kante) zurück zum Host. Der Injector darf zwischendurch wegfallen und sich neu
/// verbinden, etwa nach einem Sitzungswechsel; was in der Lücke kommt, verfällt. Endet die Host-Verbindung,
/// endet auch das Relay; der Injector sieht den Pipe-Abbruch und lässt alle Tasten los.
/// </summary>
internal sealed class PipeRelay(MessageConnection host, Action<string> log)
{
    private volatile MessageConnection? _injector;

    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var injectors = AcceptInjectorsAsync(linked);
        try
        {
            await foreach (var (type, payload) in host.ReceiveAsync(linked.Token))
            {
                var injector = _injector;
                if (injector is null)
                    continue;
                try
                {
                    await injector.SendAsync(type, payload, linked.Token);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    // Injector gerade weg; die Annahme-Schleife wartet auf den nächsten.
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
        }
        finally
        {
            linked.Cancel();
            await injectors;
        }
    }

    /// <summary>Nimmt nacheinander Injector-Verbindungen an der Pipe an und reicht deren Meldungen zum Host.</summary>
    private async Task AcceptInjectorsAsync(CancellationTokenSource linked)
    {
        var ct = linked.Token;
        while (!ct.IsCancellationRequested)
        {
            var server = InputPipe.CreateServer();
            using var conn = new MessageConnection(server);
            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _injector = conn;
            log("Injector an der Pipe verbunden.");
            try
            {
                await foreach (var (type, payload) in conn.ReceiveAsync(ct))
                    await host.SendAsync(type, payload, ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
                // Host weg: das ganze Relay beenden.
                linked.Cancel();
            }
            finally
            {
                _injector = null;
            }
            if (!ct.IsCancellationRequested)
                log("Injector von der Pipe getrennt, warte auf Neuverbindung.");
        }
    }
}
