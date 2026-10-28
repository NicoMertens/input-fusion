using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using InputFusion.Core.Config;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Hostet die Pipe für Tray und Einstellungen (<see cref="ClientPipe"/>). Jede verbundene Oberfläche bekommt sofort
/// und nach jeder Änderung den Zustand aus <see cref="ClientStatus"/>. Ändern dürfen nur Administratoren: Wer Host und
/// Schlüssel bestimmt, bestimmt, wer als SYSTEM auf diesem Rechner Eingaben abspielt. Über dieselbe Pipe meldet sich
/// der Tray als Empfänger der Zwischenablage an, siehe <see cref="ClipboardBridge"/>.
/// </summary>
/// <param name="save">Null, wenn die Konfiguration feststeht (<c>--relay</c>).</param>
/// <param name="checkForUpdates">Null, wenn sich dieser Ablauf nicht aktualisiert (<c>--relay</c>).</param>
internal sealed class ClientSettingsServer(ClientStatus status, ClientService.ConfigSaver? save, ClipboardBridge clipboard,
    Func<CancellationToken, Task<String>>? checkForUpdates, Action<String> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = ClientPipe.CreateServer();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                log($"Pipe für Tray und Einstellungen ließ sich nicht anlegen: {ex.Message}");
                return;
            }

            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                return;
            }
            _ = Task.Run(() => ServeAsync(pipe, ct), CancellationToken.None);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Einmal je Verbindung: Ein Prozess wechselt seine Erhöhung nicht, die Einstellungen starten dafür neu.
        Boolean administrator = PipeIdentity.IsClientAdministrator(pipe);
        Boolean canEdit = administrator && save is not null;
        using MessageConnection connection = new MessageConnection(pipe);
        Action push = () => _ = SendAsync(connection, status.Snapshot(administrator, canEdit), ct);
        status.Changed += push;
        try
        {
            await SendAsync(connection, status.Snapshot(administrator, canEdit), ct);
            await foreach ((MessageType type, Byte[] payload) in connection.ReceiveAsync(ct))
            {
                if (type == MessageType.ClipboardReceiver && PipeIdentity.ClientSessionId(pipe) is UInt32 session)
                {
                    clipboard.AddTray(connection, session);
                    continue;
                }

                if (type == MessageType.ClipboardText)
                {
                    await clipboard.FromTrayAsync(connection, payload, ct);
                    continue;
                }

                if (type != MessageType.Settings)
                {
                    continue;
                }

                SettingsMessage? message = SettingsMessage.FromPayload(payload);
                // Ohne den Zustand danach: Gefragt wird jede Sekunde, und am Zustand ändert die Frage nichts.
                if (message is GetDiagnosticsRequest)
                {
                    await SendAsync(connection, status.Diagnostics(), ct);
                    continue;
                }

                if (message is CheckForUpdatesRequest)
                {
                    // Nebenher: Laden und Entpacken dauern, und die Zwischenablage soll so lange weiterlaufen.
                    _ = Task.Run(() => ReportUpdateCheckAsync(connection, ct), CancellationToken.None);
                    continue;
                }

                String? error = message switch
                {
                    GetStateRequest => null,
                    SetClientConfigRequest request => Apply(request, administrator),
                    _ => "Unbekannte Anfrage.",
                };
                // Nach jeder Anfrage den Stand schicken: Bei einer Ablehnung setzt er die Anzeige zurück.
                if (error is not null)
                {
                    await SendAsync(connection, new SettingsError(error), ct);
                }

                await SendAsync(connection, status.Snapshot(administrator, canEdit), ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            status.Changed -= push;
            clipboard.RemoveTray(connection);
        }
    }

    /// <summary>Jeder Benutzer darf suchen lassen: Eingespielt wird nur ein Release aus dem eigenen Repo mit passender Prüfsumme.</summary>
    private async Task ReportUpdateCheckAsync(MessageConnection connection, CancellationToken ct)
    {
        String result;
        try
        {
            result = checkForUpdates is null ? "Im Entwicklungsmodus mit --relay sucht der Dienst nicht nach Updates." : await checkForUpdates(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // Ohne Antwort wartete der Tray ewig.
            log($"Suche nach Updates fehlgeschlagen: {ex.Message}");
            result = $"Suche nach Updates fehlgeschlagen: {ex.Message}";
        }

        await SendAsync(connection, new UpdateCheckResult(result), ct);
    }

    private String? Apply(SetClientConfigRequest request, Boolean administrator)
    {
        if (!administrator)
        {
            return "Nur Administratoren dürfen die Verbindung ändern.";
        }

        if (save is null)
        {
            return "Im Entwicklungsmodus mit --relay steht die Verbindung fest.";
        }

        // Die Anfrage kann von einer beliebigen Anwendung kommen; JSON lässt null zu, wo der Typ es nicht vorsieht.
        if (request.Hosts is null || request.Hosts.Any(h => h is null || h.Host is null || h.Key is null))
        {
            return "Ungültige Anfrage.";
        }

        ClientConfig config = new ClientConfig(request.Hosts
            .Select(h => h with { Ip = String.IsNullOrWhiteSpace(h.Ip) ? null : h.Ip.Trim() }).ToArray());
        return ClientConfigRules.Validate(config.Hosts) ?? save(config);
    }

    private static async Task SendAsync(MessageConnection connection, SettingsMessage message, CancellationToken ct)
    {
        try
        {
            await connection.SendAsync(MessageType.Settings, message.ToPayload(), ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Oberfläche geschlossen; die Empfangsschleife endet von selbst.
        }
    }
}
