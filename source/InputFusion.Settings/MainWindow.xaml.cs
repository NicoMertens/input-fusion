using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using InputFusion.Core.Config;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;
using InputFusion.Core.Net;

namespace InputFusion.Settings;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SettingsConnection _connection;
    private HostStateMessage? _state;
    private bool _updatingKey;
    private bool _updatingPort;
    // Nur der Agent kann einen Port ablehnen (belegt); die Meldung bleibt stehen, bis im Feld geändert wird.
    private bool _portRejected;
    // Wohin eine Ablehnung des Agents gehört: unter das Feld der letzten Anfrage.
    private TextBlock? _lastRequestMessage;

    // Verschieben der Client-Gruppe: Ausgangspunkt der Maus und Lage beim Anfassen, dazu die eingerastete Vorschau.
    private bool _dragging;
    private Point _dragStart;
    private (int X, int Y) _dragStartOffset;
    private (int X, int Y) _previewOffset;

    // Linke obere Ecke der Zeichenfläche in Layout-Koordinaten. Bleibt beim Ziehen stehen, sonst wanderte die Ansicht mit.
    private double _originX, _originY;

    public MainWindow()
    {
        InitializeComponent();
        _connection = new SettingsConnection(Dispatcher, SettingsPipe.CreateClient);
        _connection.StateReceived += message =>
        {
            if (message is HostStateMessage state)
            {
                OnState(state);
            }
        };
        _connection.ErrorReceived += OnError;
        _connection.ConnectedChanged += OnConnectedChanged;
        OnConnectedChanged(false);
        Closed += (_, _) => _cts.Cancel();
        _ = _connection.RunAsync(_cts.Token);
    }

    private void OnConnectedChanged(bool connected)
    {
        Status.Text = connected
            ? "Verbunden mit dem InputFusion-Agent auf diesem PC."
            : "Der InputFusion-Agent läuft nicht. Die Einstellungen verbinden sich, sobald er gestartet ist …";
        Tabs.IsEnabled = connected;
    }

    private void OnState(HostStateMessage state)
    {
        _state = state;
        Status.Text = $"Verbunden mit dem InputFusion-Agent {state.Version} auf diesem PC.";
        MachineName.Text = state.MachineName;
        Addresses.Text = state.Addresses.Count == 0 ? "keine" : string.Join(Environment.NewLine, state.Addresses);

        string port = state.Port.ToString(CultureInfo.InvariantCulture);
        if (Port.Text == port || !Port.IsKeyboardFocusWithin)
        {
            _updatingPort = true;
            Port.Text = port;
            _updatingPort = false;
        }
        UpdatePortState();

        if (Key.Text == state.Key || !KeyField.IsKeyboardFocusWithin)
        {
            _updatingKey = true;
            Key.Text = state.Key;
            _updatingKey = false;
        }
        UpdateKeyState();

        ClientInfo.Text = state.Client is { } client ? DescribeClient(client) : "Kein Notebook verbunden.";

        if (!_dragging)
        {
            RenderArrangement(recenter: true);
        }
    }

    private static string DescribeClient(ClientStateInfo client)
    {
        string name = client.MachineName is null ? "Notebook" : $"Notebook {client.MachineName}";
        string monitors = client.Monitors.Count == 0 ? "Monitore noch nicht gemeldet" : $"{client.Monitors.Count} Monitor(e)";
        string text = $"{name} verbunden: {client.Address}, Version {client.Version}, {monitors}.";
        // Ohne Namen hat der Client noch nichts gemeldet oder ist zu alt dafür; „niemand angemeldet“ wäre dann falsch.
        if (client.MachineName is null)
        {
            return text;
        }

        return $"{text}\n{client.OsVersion}\n{(client.UserName is null ? "Niemand angemeldet." : $"Angemeldet: {client.UserName}")}";
    }

    private void OnError(string message)
    {
        TextBlock target = _lastRequestMessage ?? LayoutMessage;
        _portRejected = target == PortMessage;
        target.Text = message;
        target.Foreground = (Brush)FindResource("ErrorBrush");
    }

    private void OnPortChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingPort)
        {
            _portRejected = false;
            UpdatePortState();
        }
    }

    private void UpdatePortState()
    {
        string? error = Ports.Validate(Port.Text, out int port);
        bool changed = _state is not null && port != _state.Port;
        ApplyPort.IsEnabled = error is null && changed;
        if (_portRejected)
        {
            return;
        }

        if (error is not null)
        {
            PortMessage.Text = error;
            PortMessage.Foreground = (Brush)FindResource("ErrorBrush");
        }
        else
        {
            PortMessage.Text = changed
                ? "Noch nicht übernommen."
                : $"Standard {Ports.Input}. Auf dem Notebook muss derselbe Port stehen.";
            PortMessage.Foreground = (Brush)FindResource("HintBrush");
        }
    }

    private async void OnApplyPort(object sender, RoutedEventArgs e)
    {
        if (Ports.Validate(Port.Text, out int port) is not null)
        {
            return;
        }

        _lastRequestMessage = PortMessage;
        await _connection.SendAsync(new SetPortRequest(port));
        Keyboard.ClearFocus();
    }

    // Key hält den Schlüssel, HiddenKey zeigt ihn verdeckt. Beide gleichen sich in beide Richtungen ab; der Vergleich
    // vorher beendet das Hin und Her.
    private void OnKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (HiddenKey.Password != Key.Text)
        {
            HiddenKey.Password = Key.Text;
        }

        if (!_updatingKey)
        {
            UpdateKeyState();
        }
    }

    private void OnHiddenKeyChanged(object sender, RoutedEventArgs e)
    {
        if (Key.Text != HiddenKey.Password)
        {
            Key.Text = HiddenKey.Password;
        }
    }

    private void OnShowKey(object sender, RoutedEventArgs e)
    {
        bool show = ShowKey.IsChecked == true;
        bool focused = KeyField.IsKeyboardFocusWithin;
        Key.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HiddenKey.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ShowKey.Content = show ? "Verbergen" : "Anzeigen";
        if (focused)
        {
            FocusKey();
        }
    }

    private void FocusKey()
    {
        if (Key.IsVisible)
        {
            Key.Focus();
            Key.CaretIndex = Key.Text.Length;
        }
        else
        {
            HiddenKey.Focus();
        }
    }

    private void UpdateKeyState()
    {
        string? error = HostConfig.ValidateKey(Key.Text);
        bool changed = _state is not null && Key.Text != _state.Key;
        ApplyKey.IsEnabled = error is null && changed;
        if (error is not null)
        {
            KeyMessage.Text = error;
            KeyMessage.Foreground = (Brush)FindResource("ErrorBrush");
        }
        else
        {
            KeyMessage.Text = changed
                ? "Noch nicht übernommen."
                : $"Mindestens {HostConfig.MinKeyLength} Zeichen; vorbelegt mit einem Zufallswert, änderbar.";
            KeyMessage.Foreground = (Brush)FindResource("HintBrush");
        }
    }

    private void OnGenerateKey(object sender, RoutedEventArgs e)
    {
        Key.Text = HostConfig.GenerateKey();
        FocusKey();
    }

    private void OnCopyKey(object sender, RoutedEventArgs e) => Clipboard.SetText(Key.Text);

    private async void OnApplyKey(object sender, RoutedEventArgs e)
    {
        _lastRequestMessage = KeyMessage;
        await _connection.SendAsync(new SetKeyRequest(Key.Text));
        // Der Agent antwortet mit dem neuen Stand; bis dahin gilt die Eingabe als übernommen.
        Keyboard.ClearFocus();
    }

    private IReadOnlyList<MonitorRect> HostMonitors => _state?.HostMonitors ?? [];

    private IReadOnlyList<MonitorRect> ClientMonitors => _state?.Client?.Monitors ?? [];

    private (int X, int Y) CurrentOffset =>
        _dragging ? _previewOffset : (_state?.Client?.OffsetX ?? 0, _state?.Client?.OffsetY ?? 0);

    private void RenderArrangement(bool recenter)
    {
        Arrangement.Children.Clear();
        IReadOnlyList<MonitorRect> host = HostMonitors;
        IReadOnlyList<MonitorRect> client = ClientMonitors;
        if (host.Count == 0)
        {
            return;
        }

        LayoutHint.Text = client.Count == 0
            ? "Kein Notebook verbunden: zu sehen sind nur die eigenen Monitore. Sobald ein Notebook verbunden ist, "
              + "erscheinen seine Monitore blau und lassen sich an jede Kante der eigenen Monitore ziehen."
            : "Die blauen Monitore des Notebooks an eine Kante der eigenen Monitore ziehen. Orange markiert die "
              + "Übergänge: Dort wechselt die Maus auf das Notebook und zurück.";

        if (recenter)
        {
            // Platz um die eigenen Monitore, damit sich das Notebook an jede Seite ziehen lässt. Die halbe Größe des
            // Notebooks genügt: Es liegt immer an, der Rest darf über den Rand ragen und wird abgeschnitten.
            int margin = client.Count == 0
                ? host.Max(m => Math.Max(m.Width, m.Height)) / 3
                : Math.Max(client.Max(m => m.Right) - client.Min(m => m.Left), client.Max(m => m.Bottom) - client.Min(m => m.Top)) / 2;
            _originX = host.Min(m => m.Left) - margin;
            _originY = host.Min(m => m.Top) - margin;
            Arrangement.Width = host.Max(m => m.Right) + margin - _originX;
            Arrangement.Height = host.Max(m => m.Bottom) + margin - _originY;
        }
        double unit = Math.Max(Arrangement.Width, Arrangement.Height) / 100.0;

        foreach ((MonitorRect m, string label) in MonitorBox.Numbered(host))
        {
            AddMonitor(m, label, "HostMonitorBrush", "HostMonitorBorderBrush", unit, draggable: false);
        }

        if (client.Count == 0)
        {
            return;
        }

        (int ox, int oy) = CurrentOffset;
        MonitorLayout layout = new MonitorLayout(host, client, ox, oy);
        for (int i = 0; i < client.Count; i++)
        {
            MonitorRect m = layout.Place(client[i]);
            AddMonitor(m, $"Notebook {i + 1}\n{m.Width} × {m.Height}", "ClientMonitorBrush", "ClientMonitorBorderBrush", unit, draggable: true);
        }
        foreach (EdgeTransition t in layout.Transitions)
        {
            AddTransition(t, unit);
        }
    }

    private void AddMonitor(MonitorRect m, string label, string fill, string border, double unit, bool draggable)
    {
        Border box = MonitorBox.Create(m, label, fill, border, unit);
        if (draggable)
        {
            box.Cursor = Cursors.SizeAll;
            box.MouseLeftButtonDown += OnClientMouseDown;
        }
        Canvas.SetLeft(box, m.Left - _originX);
        Canvas.SetTop(box, m.Top - _originY);
        Arrangement.Children.Add(box);
    }

    private void AddTransition(EdgeTransition t, double unit)
    {
        MonitorRect h = t.HostMonitor;
        (int x1, int y1, int x2, int y2) = t.Side switch
        {
            EdgeSide.Right => (h.Right, t.From, h.Right, t.To),
            EdgeSide.Left => (h.Left, t.From, h.Left, t.To),
            EdgeSide.Bottom => (t.From, h.Bottom, t.To, h.Bottom),
            _ => (t.From, h.Top, t.To, h.Top),
        };
        Arrangement.Children.Add(new Line
        {
            X1 = x1 - _originX,
            Y1 = y1 - _originY,
            X2 = x2 - _originX,
            Y2 = y2 - _originY,
            Stroke = (Brush)FindResource("TransitionBrush"),
            StrokeThickness = unit * 1.2,
            IsHitTestVisible = false,
        });
    }

    private void OnClientMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(Arrangement);
        _dragStartOffset = (_state?.Client?.OffsetX ?? 0, _state?.Client?.OffsetY ?? 0);
        _previewOffset = _dragStartOffset;
        Arrangement.CaptureMouse();
        LayoutMessage.Text = "";
        e.Handled = true;
    }

    private void OnArrangementMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point p = e.GetPosition(Arrangement);
        int x = _dragStartOffset.X + (int)Math.Round(p.X - _dragStart.X);
        int y = _dragStartOffset.Y + (int)Math.Round(p.Y - _dragStart.Y);
        (int X, int Y) snapped = MonitorLayout.Snap(HostMonitors, ClientMonitors, x, y);
        if (snapped == _previewOffset)
        {
            return;
        }

        _previewOffset = snapped;
        RenderArrangement(recenter: false);
    }

    private async void OnArrangementMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        (int X, int Y) offset = _previewOffset;
        Arrangement.ReleaseMouseCapture();
        if (offset == _dragStartOffset || _state?.Client is not { } client)
        {
            return;
        }

        // Bis der Agent bestätigt, die neue Lage schon zeigen, sonst springt die Gruppe kurz zurück.
        _state = _state with { Client = client with { OffsetX = offset.X, OffsetY = offset.Y } };
        RenderArrangement(recenter: false);
        _lastRequestMessage = LayoutMessage;
        LayoutMessage.Text = "Gespeichert für diese Kombination aus Monitoren.";
        LayoutMessage.Foreground = (Brush)FindResource("HintBrush");
        await _connection.SendAsync(new SetClientOffsetRequest(offset.X, offset.Y));
    }

    private void OnArrangementLostCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RenderArrangement(recenter: false);
    }
}
