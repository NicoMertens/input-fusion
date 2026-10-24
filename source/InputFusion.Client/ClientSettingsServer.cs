using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Hostet die Pipe für Tray und Einstellungen (<see cref="ClientPipe"/>). Jede verbundene Oberfläche bekommt sofort
/// und nach jeder Änderung den Zustand aus <see cref="ClientStatus"/>.
/// </summary>
internal sealed class ClientSettingsServer(ClientStatus status, Action<string> log)
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
        using MessageConnection connection = new MessageConnection(pipe);
        Action push = () => _ = SendAsync(connection, status.Snapshot(), ct);
        status.Changed += push;
        try
        {
            await SendAsync(connection, status.Snapshot(), ct);
            await foreach ((MessageType type, byte[] payload) in connection.ReceiveAsync(ct))
            {
                if (type != MessageType.Settings)
                {
                    continue;
                }

                if (SettingsMessage.FromPayload(payload) is not GetStateRequest)
                {
                    await SendAsync(connection, new SettingsError("Unbekannte Anfrage."), ct);
                }

                await SendAsync(connection, status.Snapshot(), ct);
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
