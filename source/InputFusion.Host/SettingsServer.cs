using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Host;

/// <summary>
/// Hostet die Pipe für die Einstellungen (<see cref="SettingsPipe"/>). Jede verbundene Instanz bekommt sofort und
/// nach jeder Änderung den ganzen Zustand; Änderungen gehen an <see cref="HostState"/>, der sie prüft und speichert.
/// </summary>
internal sealed class SettingsServer(HostState state)
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = SettingsPipe.CreateServer();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                Log.Write($"Pipe für die Einstellungen ließ sich nicht anlegen: {ex.Message}");
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
        using MessageConnection connection = new MessageConnection(pipe);
        Action push = () => _ = SendAsync(connection, state.Snapshot(), ct);
        state.Changed += push;
        try
        {
            await SendAsync(connection, state.Snapshot(), ct);
            await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(ct))
            {
                if (type != MessageType.Settings)
                {
                    continue;
                }

                string? error = SettingsMessage.FromPayload(payload) switch
                {
                    GetStateRequest => null,
                    SetKeyRequest request => state.SetKey(request.Key),
                    SetClientOffsetRequest request => state.SetClientOffset(request.OffsetX, request.OffsetY),
                    _ => "Unbekannte Anfrage.",
                };
                // Nach jeder Anfrage den Stand schicken: Bei einer Ablehnung setzt er die Anzeige zurück.
                if (error is not null)
                {
                    await SendAsync(connection, new SettingsError(error), ct);
                }

                await SendAsync(connection, state.Snapshot(), ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            state.Changed -= push;
        }
    }

    private static async Task SendAsync(MessageConnection connection, SettingsMessage message, CancellationToken ct)
    {
        try
        {
            await connection.SendAsync(MessageType.Settings, message.ToPayload(), ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Einstellungen geschlossen; die Empfangsschleife endet von selbst.
        }
    }
}
