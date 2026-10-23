using System.IO;
using System.Windows.Threading;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Settings;

/// <summary>
/// Hält die Pipe zum Agent (<see cref="SettingsPipe"/>) und verbindet neu, solange das Fenster offen ist: Der
/// Agent kann nach den Einstellungen starten oder zwischendurch neu starten. Ereignisse kommen auf dem UI-Thread an.
/// </summary>
internal sealed class SettingsConnection(Dispatcher dispatcher)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private volatile MessageConnection? _connection;

    public event Action<HostStateMessage>? StateReceived;
    public event Action<string>? ErrorReceived;
    public event Action<bool>? ConnectedChanged;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = SettingsPipe.CreateClient();
                await pipe.ConnectAsync((int)RetryDelay.TotalMilliseconds, ct);
                using var connection = new MessageConnection(pipe);
                _connection = connection;
                Post(() => ConnectedChanged?.Invoke(true));

                await foreach (var (type, payload) in connection.ReceiveAsync(ct))
                {
                    if (type != MessageType.Settings)
                        continue;
                    switch (SettingsMessage.FromPayload(payload))
                    {
                        case HostStateMessage state:
                            Post(() => StateReceived?.Invoke(state));
                            break;
                        case SettingsError error:
                            Post(() => ErrorReceived?.Invoke(error.Message));
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // Agent läuft nicht oder ist gerade weg; gleich neu versuchen.
            }

            if (_connection is not null)
            {
                _connection = null;
                Post(() => ConnectedChanged?.Invoke(false));
            }
            try { await Task.Delay(RetryDelay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Schickt eine Anfrage; ohne Verbindung geht sie verloren, die Anzeige zeigt das ohnehin.</summary>
    public async Task SendAsync(SettingsMessage message)
    {
        var connection = _connection;
        if (connection is null)
            return;
        try
        {
            await connection.SendAsync(MessageType.Settings, message.ToPayload());
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private void Post(Action action) => dispatcher.InvokeAsync(action);
}
