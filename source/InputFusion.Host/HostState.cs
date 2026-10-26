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
    // So lange wartet ein neuer Schlüssel auf die Antwort des Notebooks; ältere Clients antworten nie.
    private static readonly TimeSpan _keyTransferTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private string? _pendingKey;
    private string? _keyStatus;
    private IReadOnlyList<MonitorRect> _hostMonitors = MonitorEnumerator.GetMonitors();
    private string? _clientAddress;
    private string _clientVersion = "";
    private IReadOnlyList<MonitorRect> _clientMonitors = [];
    private ClientInfo? _clientInfo;
    private MonitorLayout? _layout;
    private bool _isRemote;

    /// <summary>Irgendetwas hat sich geändert; die Einstellungen und der Tray holen sich den neuen Stand.</summary>
    public event Action? Changed;

    /// <summary>Das Layout wurde ausgetauscht; die Verbindung schickt dem Client die neuen Rückwege.</summary>
    public event Action<MonitorLayout?>? LayoutChanged;

    /// <summary>Der Port wurde geändert; der Listener lauscht neu, die Firewall-Regel zieht nach.</summary>
    public event Action<int>? PortChanged;

    /// <summary>Ein neuer Schlüssel soll an den verbundenen Client; die Verbindung schickt ihn.</summary>
    public event Action<string>? KeyTransferRequested;

    /// <summary>Ein Client mit anderer Protokollversion hat sich gemeldet; vielleicht ist der Host der ältere.</summary>
    public event Action? ProtocolMismatch;

    /// <summary>Die Maus ist gerade auf dem Notebook; solange wird kein Update eingespielt.</summary>
    public bool IsRemote
    {
        get => Volatile.Read(ref _isRemote);
        set => Volatile.Write(ref _isRemote, value);
    }

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

    public int Port
    {
        get
        {
            lock (_lock)
            {
                return config.Port;
            }
        }
    }

    public string StatusText
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

    /// <summary>
    /// Null bei Erfolg, sonst der Grund der Ablehnung. Ohne Client gilt der Schlüssel ab der nächsten Verbindung. Mit
    /// Client geht er erst an ihn und wird gespeichert, wenn er ihn übernommen hat (<see cref="KeyTransferred"/>); sonst
    /// sperrte sich das Notebook aus, wenn es ihn nicht speichern kann.
    /// </summary>
    public string? SetKey(string key)
    {
        string? error = HostConfig.ValidateKey(key);
        if (error is not null)
        {
            return error;
        }

        bool transfer;
        lock (_lock)
        {
            if (_pendingKey is not null)
            {
                return "Der vorige Schlüssel wird noch an das Notebook übertragen.";
            }

            if (config.Key == key)
            {
                return null;
            }

            transfer = _clientAddress is not null;
            _keyStatus = null;
            if (transfer)
            {
                _pendingKey = key;
            }
            else
            {
                config.Key = key;
                config.Save();
            }
        }

        if (transfer)
        {
            Log.Write("Schlüssel geändert, übertrage ihn an das Notebook.");
            KeyTransferRequested?.Invoke(key);
            _ = FinishKeyTransferLaterAsync(key);
        }
        else
        {
            Log.Write("Schlüssel geändert, gilt ab der nächsten Verbindung.");
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>Antwort des Clients auf den neuen Schlüssel: null, wenn er ihn gespeichert hat, sonst der Grund.</summary>
    public void KeyTransferred(string? error)
    {
        string? key;
        lock (_lock)
        {
            key = _pendingKey;
        }

        if (key is null)
        {
            return;
        }

        if (error is null)
        {
            FinishKeyTransfer(key, commit: true, "Das Notebook hat den neuen Schlüssel übernommen.");
        }
        else
        {
            FinishKeyTransfer(key, commit: false, $"Das Notebook hat den Schlüssel nicht übernommen, er bleibt unverändert: {error}");
        }
    }

    /// <summary>
    /// Ohne Antwort trotzdem speichern: Ein älterer Client versteht die Nachricht nicht, und der Nutzer will den Schlüssel
    /// ändern. Dann muss er auf dem Notebook von Hand nachgetragen werden, wie vor der Übertragung.
    /// </summary>
    private async Task FinishKeyTransferLaterAsync(string key)
    {
        await Task.Delay(_keyTransferTimeout);
        FinishKeyTransfer(key, commit: true,
            "Das Notebook hat nicht geantwortet, vielleicht eine ältere Version. Den Schlüssel dort von Hand eintragen.");
    }

    /// <summary>Nur für die Übertragung von <paramref name="key"/>; ist sie schon beendet, bleibt alles, wie es ist.</summary>
    private void FinishKeyTransfer(string key, bool commit, string status)
    {
        lock (_lock)
        {
            if (_pendingKey != key)
            {
                return;
            }

            _pendingKey = null;
            _keyStatus = status;
            if (commit)
            {
                config.Key = key;
                config.Save();
            }
        }
        Log.Write(status);
        Changed?.Invoke();
    }

    /// <summary>
    /// Null bei Erfolg, sonst der Grund der Ablehnung. Der Listener zieht sofort um, eine bestehende Verbindung bleibt.
    /// Vorher kurz binden: Ein belegter Port fiele sonst erst im Protokoll auf, und das Notebook fände den Host nicht mehr.
    /// </summary>
    public string? SetPort(int port)
    {
        string? error = Ports.Validate(port);
        if (error is not null)
        {
            return error;
        }

        lock (_lock)
        {
            if (config.Port == port)
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

            config.Port = port;
            config.Save();
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

    public void ClientConnected(string address, string version)
    {
        lock (_lock)
        {
            _clientAddress = address;
            _clientVersion = version;
            _clientMonitors = [];
            _clientInfo = null;
            // Die Anmeldung mit dem Schlüssel hat geklappt; was beim vorigen Client daraus wurde, ist erledigt.
            _keyStatus = null;
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

    public void ClientDisconnected()
    {
        string? pendingKey;
        lock (_lock)
        {
            _clientAddress = null;
            _clientMonitors = [];
            _clientInfo = null;
            pendingKey = _pendingKey;
        }

        // Ob der Client den Schlüssel noch gespeichert hat, ist offen. Speichern wie ohne Antwort; verbindet er sich nicht
        // wieder, muss der Schlüssel dort von Hand nachgetragen werden.
        if (pendingKey is not null)
        {
            FinishKeyTransfer(pendingKey, commit: true,
                "Die Verbindung endete während der Übertragung. Verbindet sich das Notebook nicht wieder, den Schlüssel dort von Hand eintragen.");
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
                : new ClientStateInfo(_clientAddress, _clientVersion, _clientMonitors, layout?.OffsetX ?? 0, layout?.OffsetY ?? 0,
                    _clientInfo?.MachineName, _clientInfo?.OsVersion, _clientInfo?.UserName);
            return new HostStateMessage(Environment.MachineName, AppVersion.Current, Addresses(), config.Port, config.Key, _hostMonitors, client,
                _pendingKey, _keyStatus);
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
