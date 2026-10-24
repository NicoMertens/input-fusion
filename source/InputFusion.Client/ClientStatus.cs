using InputFusion.Core.Ipc;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Der Zustand des Dienstes für Tray und Einstellungen. <see cref="ClientService"/> schreibt, der
/// <see cref="ClientSettingsServer"/> schickt jede Änderung an alle verbundenen Oberflächen.
/// </summary>
internal sealed class ClientStatus
{
    private readonly Lock _lock = new();
    private ClientStateMessage _state = new(AppVersion.Current, null, Ports.Input, false, null, null, null);

    public event Action? Changed;

    public ClientStateMessage Snapshot()
    {
        lock (_lock)
        {
            return _state;
        }
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
