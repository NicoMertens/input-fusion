using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Settings;

/// <summary>
/// Hält die Pipe zum Agent (<see cref="SettingsPipe"/>) bzw. zum Dienst (<see cref="ClientPipe"/>) und verbindet neu,
/// solange das Fenster offen ist: Agent oder Dienst können nach den Einstellungen starten oder zwischendurch neu starten.
/// Ereignisse kommen auf dem UI-Thread an.
/// </summary>
/// <param name="verifyServer">Prüft nach dem Verbinden die Gegenseite; wirft <see cref="UnauthorizedAccessException"/>, wenn sie fremd ist.</param>
internal sealed class SettingsConnection(Dispatcher dispatcher, Func<NamedPipeClientStream> createClient, Action<NamedPipeClientStream>? verifyServer = null)
{
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(1);

    private volatile MessageConnection? _connection;

    public event Action<SettingsMessage>? StateReceived;
    public event Action<String>? ErrorReceived;
    public event Action<Boolean>? ConnectedChanged;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using NamedPipeClientStream pipe = createClient();
                await pipe.ConnectAsync((Int32)_retryDelay.TotalMilliseconds, ct);
                verifyServer?.Invoke(pipe);
                using MessageConnection connection = new MessageConnection(pipe);
                _connection = connection;
                Post(() => ConnectedChanged?.Invoke(true));

                await foreach ((MessageType type, Byte[] payload) in connection.ReceiveAsync(ct))
                {
                    if (type != MessageType.Settings)
                    {
                        continue;
                    }

                    switch (SettingsMessage.FromPayload(payload))
                    {
                        case SettingsError error:
                            Post(() => ErrorReceived?.Invoke(error.Message));
                            break;
                        case { } state:
                            Post(() => StateReceived?.Invoke(state));
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
                // Agent bzw. Dienst läuft nicht, ist gerade weg oder die Pipe gehört einem fremden Prozess; gleich neu versuchen.
            }

            if (_connection is not null)
            {
                _connection = null;
                Post(() => ConnectedChanged?.Invoke(false));
            }
            try { await Task.Delay(_retryDelay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Schickt eine Anfrage; ohne Verbindung geht sie verloren, die Anzeige zeigt das ohnehin.</summary>
    public async Task SendAsync(SettingsMessage message)
    {
        MessageConnection? connection = _connection;
        if (connection is null)
        {
            return;
        }

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
