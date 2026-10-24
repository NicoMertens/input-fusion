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
/// Schlüssel bestimmt, bestimmt, wer als SYSTEM auf diesem Rechner Eingaben abspielt.
/// </summary>
/// <param name="save">Null, wenn die Konfiguration feststeht (<c>--relay</c>).</param>
internal sealed class ClientSettingsServer(ClientStatus status, ClientService.ConfigSaver? save, Action<string> log)
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
        bool administrator = PipeIdentity.IsClientAdministrator(pipe);
        bool canEdit = administrator && save is not null;
        using MessageConnection connection = new MessageConnection(pipe);
        Action push = () => _ = SendAsync(connection, status.Snapshot(administrator, canEdit), ct);
        status.Changed += push;
        try
        {
            await SendAsync(connection, status.Snapshot(administrator, canEdit), ct);
            await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(ct))
            {
                if (type != MessageType.Settings)
                {
                    continue;
                }

                string? error = SettingsMessage.FromPayload(payload) switch
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
        }
    }

    private string? Apply(SetClientConfigRequest request, bool administrator)
    {
        if (!administrator)
        {
            return "Nur Administratoren dürfen die Verbindung ändern.";
        }

        if (save is null)
        {
            return "Im Entwicklungsmodus mit --relay steht die Verbindung fest.";
        }

        string? ip = string.IsNullOrWhiteSpace(request.Ip) ? null : request.Ip.Trim();
        return ClientConfigRules.Validate(request.Host, ip, request.Port, request.Key)
            ?? save(new ClientConfig(request.Host, request.Key, ip, request.Port));
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
