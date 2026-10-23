using System.Net.NetworkInformation;
using System.Net.Sockets;
using InputFusion.Core.Config;
using InputFusion.Core.Display;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Host;

/// <summary>
/// Der Zustand des Agents an einer Stelle: Konfiguration, eigene Monitore, verbundener Client und das daraus
/// berechnete Layout. Geschrieben von Netzwerk, Tray-Thread (Monitorwechsel) und Einstellungen, deshalb unter
/// einem Lock; das Layout selbst ist unveränderlich und wird nur ausgetauscht, damit der Hook-Pfad es ohne
/// Lock lesen kann.
/// </summary>
internal sealed class HostState(HostConfig config)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<MonitorRect> _hostMonitors = MonitorEnumerator.GetMonitors();
    private string? _clientAddress;
    private string _clientVersion = "";
    private IReadOnlyList<MonitorRect> _clientMonitors = [];
    private MonitorLayout? _layout;

    /// <summary>Irgendetwas hat sich geändert; die Einstellungen und der Tray holen sich den neuen Stand.</summary>
    public event Action? Changed;

    /// <summary>Das Layout wurde ausgetauscht; die Verbindung schickt dem Client die neuen Rückwege.</summary>
    public event Action<MonitorLayout?>? LayoutChanged;

    /// <summary>Ein Client mit anderer Protokollversion hat sich gemeldet; vielleicht ist der Host der ältere.</summary>
    public event Action? ProtocolMismatch;

    /// <summary>Null, solange kein Client seine Monitore gemeldet hat.</summary>
    public MonitorLayout? Layout => Volatile.Read(ref _layout);

    public string Key
    {
        get
        {
            lock (_lock)
            {
                return config.Key;
            }
        }
    }

    public string StatusText
    {
        get
        {
            lock (_lock)
            {
                return _clientAddress is null ? "InputFusion – wartet auf das Notebook" : $"InputFusion – verbunden mit {_clientAddress}";
            }
        }
    }

    /// <summary>Null bei Erfolg, sonst der Grund der Ablehnung. Gilt ab der nächsten Verbindung.</summary>
    public string? SetKey(string key)
    {
        string? error = HostConfig.ValidateKey(key);
        if (error is not null)
        {
            return error;
        }

        lock (_lock)
        {
            if (config.Key == key)
            {
                return null;
            }

            config.Key = key;
            config.Save();
        }
        Log.Write("Schlüssel geändert, gilt ab der nächsten Verbindung.");
        Changed?.Invoke();
        return null;
    }

    public void RefreshHostMonitors()
    {
        IReadOnlyList<MonitorRect> monitors = MonitorEnumerator.GetMonitors();
        lock (_lock)
        {
            if (monitors.SequenceEqual(_hostMonitors))
            {
                return;
            }

            _hostMonitors = monitors;
        }
        Log.Write($"Eigene Monitore geändert: {Describe(monitors)}");
        RebuildLayout();
    }

    public void ClientConnected(string address, string version)
    {
        lock (_lock)
        {
            _clientAddress = address;
            _clientVersion = version;
            _clientMonitors = [];
        }
        RebuildLayout();
    }

    public void ClientMonitorsReported(IReadOnlyList<MonitorRect> monitors)
    {
        lock (_lock)
        {
            if (_clientAddress is null || monitors.SequenceEqual(_clientMonitors))
            {
                return;
            }

            _clientMonitors = monitors;
        }
        Log.Write($"Monitore des Clients: {Describe(monitors)}");
        RebuildLayout();
    }

    public void ReportProtocolMismatch() => ProtocolMismatch?.Invoke();

    public void ClientDisconnected()
    {
        lock (_lock)
        {
            _clientAddress = null;
            _clientMonitors = [];
        }
        RebuildLayout();
    }

    /// <summary>Verschiebt die Client-Gruppe, rastet sie ein und speichert die Lage für diese Monitor-Kombination.</summary>
    public string? SetClientOffset(int offsetX, int offsetY)
    {
        MonitorLayout layout;
        lock (_lock)
        {
            if (_clientMonitors.Count == 0)
            {
                return "Kein Client mit Monitoren verbunden.";
            }

            (int x, int y) = MonitorLayout.Snap(_hostMonitors, _clientMonitors, offsetX, offsetY);
            layout = new MonitorLayout(_hostMonitors, _clientMonitors, x, y);
            if (!layout.IsValid)
            {
                return "An dieser Stelle grenzt kein Client-Monitor an einen eigenen Monitor.";
            }

            string combination = MonitorLayout.CombinationKey(_hostMonitors, _clientMonitors);
            config.Layouts.RemoveAll(l => l.Combination == combination);
            config.Layouts.Add(new SavedLayout(combination, x, y));
            config.Save();
            Volatile.Write(ref _layout, layout);
        }
        LayoutChanged?.Invoke(layout);
        Changed?.Invoke();
        return null;
    }

    public HostStateMessage Snapshot()
    {
        lock (_lock)
        {
            MonitorLayout? layout = Volatile.Read(ref _layout);
            ClientStateInfo? client = _clientAddress is null
                ? null
                : new ClientStateInfo(_clientAddress, _clientVersion, _clientMonitors, layout?.OffsetX ?? 0, layout?.OffsetY ?? 0);
            return new HostStateMessage(Environment.MachineName, AppVersion.Current, Addresses(), Ports.Input, config.Key, _hostMonitors, client);
        }
    }

    /// <summary>Gespeicherte Lage für die aktuelle Kombination, sonst der Standard; eine nicht mehr passende Lage verfällt.</summary>
    private void RebuildLayout()
    {
        MonitorLayout? layout = null;
        lock (_lock)
        {
            if (_clientMonitors.Count > 0 && _hostMonitors.Count > 0)
            {
                string combination = MonitorLayout.CombinationKey(_hostMonitors, _clientMonitors);
                SavedLayout? saved = config.Layouts.FirstOrDefault(l => l.Combination == combination);
                if (saved is not null)
                {
                    layout = new MonitorLayout(_hostMonitors, _clientMonitors, saved.OffsetX, saved.OffsetY);
                }

                if (layout is not { IsValid: true })
                {
                    (int x, int y) = MonitorLayout.DefaultOffset(_hostMonitors, _clientMonitors);
                    layout = new MonitorLayout(_hostMonitors, _clientMonitors, x, y);
                }
            }
            Volatile.Write(ref _layout, layout);
        }
        LayoutChanged?.Invoke(layout);
        Changed?.Invoke();
    }

    /// <summary>Die IPv4-Adressen aller aktiven Netzwerkkarten außer Loopback, für die Anzeige in den Einstellungen.</summary>
    private static string[] Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .Distinct()
            .ToArray();

    private static string Describe(IReadOnlyList<MonitorRect> monitors) =>
        string.Join(", ", monitors.Select(m => $"{m.Width}×{m.Height} bei ({m.X}, {m.Y}), {m.Dpi} DPI"));
}
