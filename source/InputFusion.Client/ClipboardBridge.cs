using System.IO;
using System.Net.Sockets;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Vermittelt die Zwischenablage zwischen Host und <see cref="ClientTray"/>. Der Dienst liest und schreibt sie nicht
/// selbst: In Session 0 gibt es keine Zwischenablage des Benutzers, und der Injector als SYSTEM soll sie nicht sehen.
/// Die Pipe steht allen interaktiven Benutzern offen; Text geht deshalb nur an Trays der aktiven Sitzung und wird nur
/// von dort angenommen, geprüft bei jeder Übergabe, weil nach einem Benutzerwechsel der alte Tray noch verbunden sein kann.
/// </summary>
internal sealed class ClipboardBridge
{
    private readonly Lock _lock = new();
    private readonly Dictionary<MessageConnection, uint> _trays = new();
    private MessageConnection? _host;

    public void AddTray(MessageConnection tray, uint session)
    {
        lock (_lock)
        {
            _trays[tray] = session;
        }
    }

    public void RemoveTray(MessageConnection tray)
    {
        lock (_lock)
        {
            _trays.Remove(tray);
        }
    }

    /// <summary>Für die Dauer einer Host-Verbindung, gesetzt vom <see cref="PipeRelay"/>.</summary>
    public void SetHost(MessageConnection host)
    {
        lock (_lock)
        {
            _host = host;
        }
    }

    public void ClearHost(MessageConnection host)
    {
        lock (_lock)
        {
            if (_host == host)
            {
                _host = null;
            }
        }
    }

    /// <summary>Ein Stück Text vom Host an die Trays der aktiven Sitzung.</summary>
    public Task FromHostAsync(byte[] chunk, CancellationToken ct) => SendToTraysAsync(MessageType.ClipboardText, chunk, ct);

    /// <summary>Die Maus geht zurück zum Host: Die Trays schicken ihren Text, wenn er sich geändert hat.</summary>
    public Task RequestAsync(CancellationToken ct) => SendToTraysAsync(MessageType.ClipboardRequest, [], ct);

    /// <summary>Ein Stück Text von einem Tray zum Host, nur von einem angemeldeten Tray der aktiven Sitzung.</summary>
    public async Task FromTrayAsync(MessageConnection tray, byte[] chunk, CancellationToken ct)
    {
        MessageConnection? host;
        lock (_lock)
        {
            if (!_trays.TryGetValue(tray, out uint session) || session != ActiveSession.Id())
            {
                return;
            }

            host = _host;
        }

        if (host is null)
        {
            return;
        }

        try
        {
            await host.SendAsync(MessageType.ClipboardText, chunk, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Host gerade weg; das Relay merkt es selbst.
        }
    }

    private async Task SendToTraysAsync(MessageType type, byte[] payload, CancellationToken ct)
    {
        MessageConnection[] targets;
        lock (_lock)
        {
            uint allowed = ActiveSession.Id();
            targets = _trays.Where(t => t.Value == allowed).Select(t => t.Key).ToArray();
        }

        foreach (MessageConnection tray in targets)
        {
            try
            {
                await tray.SendAsync(type, payload, ct);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // Tray gerade weg; der Server meldet ihn ab.
            }
        }
    }
}
