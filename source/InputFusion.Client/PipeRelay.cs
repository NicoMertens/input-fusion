using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Reicht für die Dauer einer Host-Verbindung Frames zwischen Host und <see cref="Injector"/> weiter — die
/// Aufgabe des Dienstes aus Session 0, der selbst nicht abspielen kann. Eingaben gehen zum Injector, dessen
/// Meldungen (Austritt an der Kante) zurück zum Host. Der Injector darf zwischendurch wegfallen und sich neu
/// verbinden, etwa nach einem Sitzungswechsel; was in der Lücke kommt, verfällt. Endet die Host-Verbindung,
/// endet auch das Relay; der Injector sieht den Pipe-Abbruch und lässt alle Tasten los. Die Zwischenablage geht
/// nicht zum Injector, sondern über die <see cref="ClipboardBridge"/> zum Tray, die Anordnung in den
/// <see cref="ClientStatus"/> für die Einstellungen, ein neuer Schlüssel an <paramref name="saveKey"/>. Angaben zum
/// Notebook schickt der Dienst selbst (<see cref="ClientInfoReporter"/>), damit der Injector als SYSTEM nicht mehr tun
/// muss als abspielen.
/// </summary>
/// <param name="saveKey">Speichert einen neuen Schlüssel vom Host; null bei Erfolg, sonst der Grund.</param>
internal sealed class PipeRelay(MessageConnection host, ClipboardBridge clipboard, ClientStatus status, Func<string, string?> saveKey, Action<string> log)
{
    private volatile MessageConnection? _injector;
    // Die zuletzt vom Host geschickten Rückkehrkanten. Ein neu verbundener Injector bekommt sie sofort, weil der
    // Host sie nur bei einer Änderung des Layouts schickt.
    private volatile byte[]? _returnEdges;

    public async Task RunAsync(CancellationToken ct)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task injectors = AcceptInjectorsAsync(linked);
        Task monitor = WatchHost(linked);
        Task info = ClientInfoReporter.RunAsync(host, linked.Token);
        clipboard.SetHost(host);
        try
        {
            await foreach ((MessageType type, byte[] payload) in host.ReceiveAsync(linked.Token))
            {
                // Heartbeats sind Sache der Verbindung, nicht des Injectors.
                if (type == MessageType.Heartbeat)
                {
                    continue;
                }

                if (type == MessageType.ClipboardText)
                {
                    await clipboard.FromHostAsync(payload, linked.Token);
                    continue;
                }

                // Nur für die Einstellungen, der Injector braucht davon nichts.
                if (type == MessageType.Arrangement)
                {
                    if (ClientArrangement.FromPayload(payload) is { } arrangement)
                    {
                        status.Update(s => s with { Arrangement = arrangement });
                    }
                    continue;
                }

                // Geht nie zum Injector: Der Schlüssel gehört nur in client.json.
                if (type == MessageType.KeyChange)
                {
                    string? error = saveKey(Encoding.UTF8.GetString(payload));
                    await host.SendAsync(MessageType.KeyChangeResult, error is null ? [] : Encoding.UTF8.GetBytes(error), linked.Token);
                    continue;
                }

                if (type == MessageType.ReturnEdges)
                {
                    _returnEdges = payload;
                }

                if (type == MessageType.SwitchToHost)
                {
                    _ = clipboard.RequestAsync(linked.Token);
                }

                MessageConnection? injector = _injector;
                if (injector is null)
                {
                    continue;
                }

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
            clipboard.ClearHost(host);
            linked.Cancel();
            await injectors;
            await monitor;
            await info;
        }
    }

    /// <summary>Heartbeat zum Host. Schweigt er (PC im Standby, Netz weg), endet das Relay, und der Dienst
    /// verbindet neu; ohne das hinge er nach dem Aufwachen des Notebooks an einer toten Verbindung.</summary>
    private async Task WatchHost(CancellationTokenSource linked)
    {
        if (await host.MonitorAsync(linked.Token))
        {
            log($"Host antwortet seit {MessageConnection.HeartbeatTimeout.TotalSeconds:0} s nicht mehr, trenne.");
            linked.Cancel();
        }
    }

    /// <summary>Nimmt nacheinander Injector-Verbindungen an der Pipe an und reicht deren Meldungen zum Host.</summary>
    private async Task AcceptInjectorsAsync(CancellationTokenSource linked)
    {
        CancellationToken ct = linked.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server = InputPipe.CreateServer();
            using MessageConnection conn = new MessageConnection(server);
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
                if (_returnEdges is { } edges)
                {
                    await conn.SendAsync(MessageType.ReturnEdges, edges, ct);
                }

                await foreach ((MessageType type, byte[] payload) in conn.ReceiveAsync(ct))
                {
                    await host.SendAsync(type, payload, ct);
                    if (type == MessageType.SwitchBackRequest)
                    {
                        _ = clipboard.RequestAsync(ct);
                    }
                }
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
            {
                log("Injector von der Pipe getrennt, warte auf Neuverbindung.");
            }
        }
    }
}
