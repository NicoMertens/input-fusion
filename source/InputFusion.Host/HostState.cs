using System.Net.NetworkInformation;
using System.Net.Sockets;
using NC2.InputFusion.Core.Config;
using NC2.InputFusion.Core.Display;
using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Layout;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Host;

/// <summary>
/// Der Zustand des Agents an einer Stelle: Konfiguration, eigene Monitore, verbundener Client und das daraus
/// berechnete Layout. Geschrieben von Netzwerk, Tray-Thread (Monitorwechsel) und Einstellungen, deshalb unter
/// einem Lock; das Layout selbst ist unveränderlich und wird nur ausgetauscht, damit der Hook-Pfad es ohne
/// Lock lesen kann. Den Schlüsselwechsel führt <see cref="KeyTransfer"/>, die Diagnose <see cref="ConnectionDiagnostics"/>.
/// </summary>
internal sealed class HostState
{
    private readonly HostConfig _config;
    private readonly Lock _lock = new();
    private readonly KeyTransfer _keyTransfer;
    private readonly ConnectionDiagnostics _diagnostics = new();
    private IReadOnlyList<MonitorRect> _hostMonitors = MonitorEnumerator.GetMonitors();
    private String? _clientAddress;
    private String _clientVersion = "";
    private IReadOnlyList<MonitorRect> _clientMonitors = [];
    private ClientInfo? _clientInfo;
    private MonitorLayout? _layout;
    private Boolean _isRemote;

    public HostState(HostConfig config)
    {
        _config = config;
        _keyTransfer = new KeyTransfer(config, _lock, () => _clientAddress is not null);
        _keyTransfer.Requested += key => KeyTransferRequested?.Invoke(key);
        _keyTransfer.Changed += () => Changed?.Invoke();
    }

    /// <summary>Irgendetwas hat sich geändert; die Einstellungen und der Tray holen sich den neuen Stand.</summary>
    public event Action? Changed;

    /// <summary>Das Layout wurde ausgetauscht; die Verbindung schickt dem Client die neuen Rückwege.</summary>
    public event Action<MonitorLayout?>? LayoutChanged;

    /// <summary>Der Port wurde geändert; der Listener lauscht neu, die Firewall-Regel zieht nach.</summary>
    public event Action<Int32>? PortChanged;

    /// <summary>Ein neuer Schlüssel soll an den verbundenen Client; die Verbindung schickt ihn.</summary>
    public event Action<String>? KeyTransferRequested;

    /// <summary>Ein Client mit anderer Protokollversion hat sich gemeldet; vielleicht ist der Host der ältere.</summary>
    public event Action? ProtocolMismatch;

    /// <summary>Die Maus ist gerade auf dem Notebook; solange wird kein Update eingespielt.</summary>
    public Boolean IsRemote
    {
        get => Volatile.Read(ref _isRemote);
        set => Volatile.Write(ref _isRemote, value);
    }

    /// <summary>Null, solange kein Client seine Monitore gemeldet hat.</summary>
    public MonitorLayout? Layout => Volatile.Read(ref _layout);

    public String Key
    {
        get
        {
            lock (_lock)
            {
                return _config.Key;
            }
        }
    }

    public Int32 Port
    {
        get
        {
            lock (_lock)
            {
                return _config.Port;
            }
        }
    }

    public String StatusText
    {
        get
        {
            lock (_lock)
            {
                return _clientAddress is null ? "InputFusion – wartet auf das Notebook"
                    : _clientInfo?.MachineName is { } name ? $"InputFusion – verbunden mit {name} ({_clientAddress})"
                    : $"InputFusion – verbunden mit {_clientAddress}";
            }
        }
    }

    /// <summary>Null bei Erfolg, sonst der Grund der Ablehnung; siehe <see cref="KeyTransfer"/>.</summary>
    public String? SetKey(String key) => _keyTransfer.Set(key);

    /// <summary>Antwort des Clients auf den neuen Schlüssel: null, wenn er ihn gespeichert hat, sonst der Grund.</summary>
    public void KeyTransferred(String? error) => _keyTransfer.Answered(error);

    /// <summary>
    /// Null bei Erfolg, sonst der Grund der Ablehnung. Der Listener zieht sofort um, eine bestehende Verbindung bleibt.
    /// Vorher kurz binden: Ein belegter Port fiele sonst erst im Protokoll auf, und das Notebook fände den Host nicht mehr.
    /// </summary>
    public String? SetPort(Int32 port)
    {
        String? error = Ports.Validate(port);
        if (error is not null)
        {
            return error;
        }

        lock (_lock)
        {
            if (_config.Port == port)
            {
                return null;
            }

            TcpListener probe = Program.CreateListener(port);
            try
            {
                probe.Start();
            }
            catch (SocketException ex)
            {
                return $"Port {port} ist nicht verfügbar ({ex.SocketErrorCode}).";
            }
            finally
            {
                probe.Stop();
            }

            _config.Port = port;
            _config.Save();
        }
        Log.Write($"Port auf {port} geändert.");
        PortChanged?.Invoke(port);
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

    public void ClientConnected(String address, String version, MessageConnection connection)
    {
        _diagnostics.Connected(connection);
        lock (_lock)
        {
            _clientAddress = address;
            _clientVersion = version;
            _clientMonitors = [];
            _clientInfo = null;
            _keyTransfer.ClientConnected();
        }
        RebuildLayout();
    }

    /// <summary>Nur zur Anzeige; ältere Clients schicken keine Angaben.</summary>
    public void ClientInfoReported(ClientInfo info)
    {
        lock (_lock)
        {
            if (_clientAddress is null || info == _clientInfo)
            {
                return;
            }

            _clientInfo = info;
        }
        Log.Write($"Client meldet: Name {info.MachineName}, {info.OsVersion}, angemeldet {info.UserName ?? "niemand"}");
        Changed?.Invoke();
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

    /// <summary>Merkt sich die Störung für die Diagnose; ins Protokoll schreibt sie der Aufrufer.</summary>
    public void ReportError(String message) => _diagnostics.ReportError(message);

    public DiagnosticsMessage Diagnostics() => _diagnostics.ToMessage();

    public void ClientDisconnected()
    {
        _diagnostics.Disconnected();
        lock (_lock)
        {
            _clientAddress = null;
            _clientMonitors = [];
            _clientInfo = null;
        }
        _keyTransfer.ClientDisconnected();
        RebuildLayout();
    }

    /// <summary>Verschiebt die Client-Gruppe, rastet sie ein und speichert die Lage für diese Monitor-Kombination.</summary>
    public String? SetClientOffset(Int32 offsetX, Int32 offsetY)
    {
        MonitorLayout layout;
        lock (_lock)
        {
            if (_clientMonitors.Count == 0)
            {
                return "Kein Client mit Monitoren verbunden.";
            }

            (Int32 x, Int32 y) = MonitorLayout.Snap(_hostMonitors, _clientMonitors, offsetX, offsetY);
            layout = new MonitorLayout(_hostMonitors, _clientMonitors, x, y);
            if (!layout.IsValid)
            {
                return "An dieser Stelle grenzt kein Client-Monitor an einen eigenen Monitor.";
            }

            String combination = MonitorLayout.CombinationKey(_hostMonitors, _clientMonitors);
            _config.Layouts.RemoveAll(l => l.Combination == combination);
            _config.Layouts.Add(new SavedLayout(combination, x, y));
            _config.Save();
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
                : new ClientStateInfo(_clientAddress, _clientVersion, _clientMonitors, layout?.OffsetX ?? 0, layout?.OffsetY ?? 0,
                    _clientInfo?.MachineName, _clientInfo?.OsVersion, _clientInfo?.UserName);
            (String? pendingKey, String? keyStatus) = _keyTransfer.Current;
            return new HostStateMessage(Environment.MachineName, AppVersion.Current, Addresses(), _config.Port, _config.Key, _hostMonitors, client,
                pendingKey, keyStatus);
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
                String combination = MonitorLayout.CombinationKey(_hostMonitors, _clientMonitors);
                SavedLayout? saved = _config.Layouts.FirstOrDefault(l => l.Combination == combination);
                if (saved is not null)
                {
                    layout = new MonitorLayout(_hostMonitors, _clientMonitors, saved.OffsetX, saved.OffsetY);
                }

                if (layout is not { IsValid: true })
                {
                    (Int32 x, Int32 y) = MonitorLayout.DefaultOffset(_hostMonitors, _clientMonitors);
                    layout = new MonitorLayout(_hostMonitors, _clientMonitors, x, y);
                }
            }
            Volatile.Write(ref _layout, layout);
        }
        LayoutChanged?.Invoke(layout);
        Changed?.Invoke();
    }

    /// <summary>Die IPv4-Adressen aller aktiven Netzwerkkarten außer Loopback, für die Anzeige in den Einstellungen.</summary>
    private static String[] Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .Distinct()
            .ToArray();

    private static String Describe(IReadOnlyList<MonitorRect> monitors) =>
        String.Join(", ", monitors.Select(m => $"{m.Width}×{m.Height} bei ({m.X}, {m.Y}), {m.Dpi} DPI"));
}
