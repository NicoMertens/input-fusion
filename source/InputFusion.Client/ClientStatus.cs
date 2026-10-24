using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Der Zustand des Dienstes für Tray und Einstellungen. <see cref="ClientService"/> schreibt, der
/// <see cref="ClientSettingsServer"/> schickt jede Änderung an alle verbundenen Oberflächen, je nach Rechten mit oder
/// ohne Schlüssel.
/// </summary>
internal sealed class ClientStatus
{
    private readonly Lock _lock = new();
    private ClientStateMessage _state = new(AppVersion.Current, null, null, Ports.Input, null, false, false, null, null, null);
    private ClientConfig? _config;

    public event Action? Changed;

    /// <param name="administrator">Ob die Oberfläche als Administrator verbunden ist: nur dann mit Schlüssel.</param>
    /// <param name="canEdit">Ob sie ändern darf; Administrator und eine änderbare Konfiguration.</param>
    public ClientStateMessage Snapshot(bool administrator, bool canEdit)
    {
        lock (_lock)
        {
            return _state with
            {
                Host = _config?.Host,
                Ip = _config?.Ip,
                Port = _config?.Port ?? Ports.Input,
                Key = administrator ? _config?.Key : null,
                CanEdit = canEdit,
            };
        }
    }

    /// <summary>Die zuletzt gelesene Konfiguration; null, wenn <c>client.json</c> fehlt oder nicht lesbar ist.</summary>
    public void SetConfig(ClientConfig? config)
    {
        lock (_lock)
        {
            if (config == _config)
            {
                return;
            }

            _config = config;
        }
        Changed?.Invoke();
    }

    public void Update(Func<ClientStateMessage, ClientStateMessage> change)
    {
        bool changed;
        lock (_lock)
        {
            ClientStateMessage next = change(_state);
            changed = next != _state;
            _state = next;
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }
}
