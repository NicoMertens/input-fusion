using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using InputFusion.Core.Config;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;

namespace InputFusion.Settings;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SettingsConnection _connection;
    private HostStateMessage? _state;
    private bool _updatingKey;
    private bool _lastRequestWasKey;

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
        _connection = new SettingsConnection(Dispatcher);
        _connection.StateReceived += OnState;
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
        MachineName.Text = state.MachineName;
        Addresses.Text = state.Addresses.Count == 0 ? "keine" : string.Join(Environment.NewLine, state.Addresses);
        Port.Text = $"{state.Port} (TCP)";

        if (Key.Text == state.Key || !Key.IsKeyboardFocusWithin)
        {
            _updatingKey = true;
            Key.Text = state.Key;
            _updatingKey = false;
        }
        UpdateKeyState();

        ClientInfo.Text = state.Client switch
        {
            null => "Kein Notebook verbunden.",
            { Monitors.Count: 0 } client => $"Notebook verbunden: {client.Address}, Monitore noch nicht gemeldet.",
            var client => $"Notebook verbunden: {client.Address}, {client.Monitors.Count} Monitor(e).",
        };

        if (!_dragging)
            RenderArrangement(recenter: true);
    }

    private void OnError(string message)
    {
        if (_lastRequestWasKey)
        {
            KeyMessage.Text = message;
            KeyMessage.Foreground = (Brush)FindResource("ErrorBrush");
        }
        else
        {
            LayoutMessage.Text = message;
            LayoutMessage.Foreground = (Brush)FindResource("ErrorBrush");
        }
    }

    private void OnKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingKey)
            UpdateKeyState();
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
        Key.Focus();
    }

    private void OnCopyKey(object sender, RoutedEventArgs e) => Clipboard.SetText(Key.Text);

    private async void OnApplyKey(object sender, RoutedEventArgs e)
    {
        _lastRequestWasKey = true;
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
        var host = HostMonitors;
        var client = ClientMonitors;
        if (host.Count == 0)
            return;

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

        // Von links nach rechts nummeriert; die Nummern von Windows kennt der Agent nicht.
        var ordered = host.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            string primary = m.X == 0 && m.Y == 0 ? " · Hauptmonitor" : "";
            AddMonitor(m, $"{i + 1}{primary}\n{m.Width} × {m.Height}", "HostMonitorBrush", "HostMonitorBorderBrush", unit, draggable: false);
        }

        if (client.Count == 0)
            return;

        var (ox, oy) = CurrentOffset;
        var layout = new MonitorLayout(host, client, ox, oy);
        for (int i = 0; i < client.Count; i++)
        {
            var m = layout.Place(client[i]);
            AddMonitor(m, $"Notebook {i + 1}\n{m.Width} × {m.Height}", "ClientMonitorBrush", "ClientMonitorBorderBrush", unit, draggable: true);
        }
        foreach (var t in layout.Transitions)
            AddTransition(t, unit);
    }

    private void AddMonitor(MonitorRect m, string label, string fill, string border, double unit, bool draggable)
    {
        var box = new Border
        {
            Width = m.Width,
            Height = m.Height,
            Background = (Brush)FindResource(fill),
            BorderBrush = (Brush)FindResource(border),
            BorderThickness = new Thickness(unit * 0.5),
            // Die Beschriftung passt sich dem Monitor an; eine Schriftgröße für alle wäre auf kleinen Monitoren abgeschnitten.
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(Math.Min(m.Width, m.Height) * 0.15),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                },
            },
        };
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
        var h = t.HostMonitor;
        var (x1, y1, x2, y2) = t.Side switch
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
            return;
        var p = e.GetPosition(Arrangement);
        int x = _dragStartOffset.X + (int)Math.Round(p.X - _dragStart.X);
        int y = _dragStartOffset.Y + (int)Math.Round(p.Y - _dragStart.Y);
        var snapped = MonitorLayout.Snap(HostMonitors, ClientMonitors, x, y);
        if (snapped == _previewOffset)
            return;
        _previewOffset = snapped;
        RenderArrangement(recenter: false);
    }

    private async void OnArrangementMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        var offset = _previewOffset;
        Arrangement.ReleaseMouseCapture();
        if (offset == _dragStartOffset || _state?.Client is not { } client)
            return;

        // Bis der Agent bestätigt, die neue Lage schon zeigen, sonst springt die Gruppe kurz zurück.
        _state = _state with { Client = client with { OffsetX = offset.X, OffsetY = offset.Y } };
        RenderArrangement(recenter: false);
        _lastRequestWasKey = false;
        LayoutMessage.Text = "Gespeichert für diese Kombination aus Monitoren.";
        LayoutMessage.Foreground = (Brush)FindResource("HintBrush");
        await _connection.SendAsync(new SetClientOffsetRequest(offset.X, offset.Y));
    }

    private void OnArrangementLostCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        RenderArrangement(recenter: false);
    }
}
