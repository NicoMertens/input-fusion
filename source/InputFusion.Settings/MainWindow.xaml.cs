using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private Boolean _updatingKey;
    private Boolean _updatingPort;
    // Nur der Agent kann einen Port ablehnen (belegt); die Meldung bleibt stehen, bis im Feld geändert wird.
    private Boolean _portRejected;
    // Wohin eine Ablehnung des Agents gehört: unter das Feld der letzten Anfrage.
    private TextBlock? _lastRequestMessage;

    // Verschieben der Client-Gruppe: Ausgangspunkt der Maus und Lage beim Anfassen, dazu die eingerastete Vorschau.
    private Boolean _dragging;
    private Point _dragStart;
    private (Int32 X, Int32 Y) _dragStartOffset;
    private (Int32 X, Int32 Y) _previewOffset;

    // Linke obere Ecke der Zeichenfläche in Layout-Koordinaten. Bleibt beim Ziehen stehen, sonst wanderte die Ansicht mit.
    private Double _originX, _originY;

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
            else if (message is DiagnosticsMessage diagnostics)
            {
                Diagnostics.Show(diagnostics);
            }
        };
        Diagnostics.Attach(_connection);
        _connection.ErrorReceived += OnError;
        _connection.ConnectedChanged += OnConnectedChanged;
        OnConnectedChanged(false);
        ShowClient(null);
        Closed += (_, _) => _cts.Cancel();
        _ = _connection.RunAsync(_cts.Token);
    }

    private void OnConnectedChanged(Boolean connected)
    {
        Status.Text = connected
            ? "Verbunden mit dem InputFusion-Agent auf diesem PC."
            : "Der InputFusion-Agent läuft nicht. Die Einstellungen verbinden sich, sobald er gestartet ist …";
        Tabs.IsEnabled = connected;
        if (!connected)
        {
            Diagnostics.Show(null);
        }
    }

    private void OnState(HostStateMessage state)
    {
        _state = state;
        Status.Text = $"Verbunden mit dem InputFusion-Agent {state.Version}.";
        MachineName.Text = state.MachineName;
        Addresses.Text = state.Addresses.Count == 0 ? "keine" : String.Join(Environment.NewLine, state.Addresses);

        String port = state.Port.ToString(CultureInfo.InvariantCulture);
        if (Port.Text == port || !Port.IsKeyboardFocusWithin)
        {
            _updatingPort = true;
            Port.Text = port;
            _updatingPort = false;
        }
        UpdatePortState();

        if (Key.Text == CurrentKey || !KeyField.IsKeyboardFocusWithin)
        {
            _updatingKey = true;
            Key.Text = CurrentKey;
            _updatingKey = false;
        }
        UpdateKeyState();

        ShowClient(state.Client);

        if (!_dragging)
        {
            RenderArrangement(recenter: true);
        }
    }

    private void ShowClient(ClientStateInfo? client)
    {
        if (client is null)
        {
            ClientCard.Show(false, "Kein Notebook verbunden", [(null, "Sobald sich das Notebook anmeldet, erscheint es hier.")]);
            return;
        }

        List<(String?, String)> details =
        [
            ("Adresse", client.Address),
            ("Version", client.Version),
            ("Monitore", client.Monitors.Count == 0 ? "noch nicht gemeldet" : client.Monitors.Count.ToString(CultureInfo.CurrentCulture)),
        ];
        // Ohne Namen hat der Client noch nichts gemeldet oder ist zu alt dafür; „niemand angemeldet“ wäre dann falsch.
        if (client.MachineName is not null)
        {
            details.Add(("Windows", client.OsVersion ?? ""));
            details.Add(("Angemeldet", client.UserName ?? "niemand"));
        }
        ClientCard.Show(true, $"{client.MachineName ?? "Notebook"} verbunden", details);
    }

    private void OnError(String message)
    {
        TextBlock target = _lastRequestMessage ?? LayoutMessage;
        _portRejected = target == PortMessage;
        target.Text = message;
        ThemeBrushes.SetMessageColor(target, error: true);
    }

    private void OnPortChanged(Object sender, TextChangedEventArgs e)
    {
        if (!_updatingPort)
        {
            _portRejected = false;
            UpdatePortState();
        }
    }

    private void UpdatePortState()
    {
        String? error = Ports.Validate(Port.Text, out Int32 port);
        Boolean changed = _state is not null && port != _state.Port;
        ApplyPort.IsEnabled = error is null && changed;
        if (_portRejected)
        {
            return;
        }

        if (error is not null)
        {
            PortMessage.Text = error;
            ThemeBrushes.SetMessageColor(PortMessage, error: true);
        }
        else
        {
            PortMessage.Text = changed
                ? "Noch nicht übernommen."
                : $"Standard {Ports.Input}. Auf dem Notebook muss derselbe Port stehen.";
            ThemeBrushes.SetMessageColor(PortMessage, error: false);
        }
    }

    private async void OnApplyPort(Object sender, RoutedEventArgs e)
    {
        if (Ports.Validate(Port.Text, out Int32 port) is not null)
        {
            return;
        }

        _lastRequestMessage = PortMessage;
        await _connection.SendAsync(new SetPortRequest(port));
        Keyboard.ClearFocus();
    }

    // Key hält den Schlüssel, HiddenKey zeigt ihn verdeckt. Beide gleichen sich in beide Richtungen ab; der Vergleich
    // vorher beendet das Hin und Her.
    private void OnKeyChanged(Object sender, TextChangedEventArgs e)
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

    private void OnHiddenKeyChanged(Object sender, RoutedEventArgs e)
    {
        if (Key.Text != HiddenKey.Password)
        {
            Key.Text = HiddenKey.Password;
        }
    }

    private void OnShowKey(Object sender, RoutedEventArgs e)
    {
        Boolean show = ShowKey.IsChecked == true;
        Boolean focused = KeyField.IsKeyboardFocusWithin;
        Key.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HiddenKey.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        // Symbole aus Segoe Fluent Icons: durchgestrichenes Auge (Hide), Auge (View).
        ShowKey.Content = show ? "" : "";
        ShowKey.ToolTip = show ? "Schlüssel verbergen" : "Schlüssel anzeigen";
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

    // Während der Übertragung an das Notebook der neue Schlüssel, sonst sprängen Feld und Meldung kurz auf den alten zurück.
    private String CurrentKey => _state?.PendingKey ?? _state?.Key ?? "";

    private void UpdateKeyState()
    {
        String? error = HostConfig.ValidateKey(Key.Text);
        Boolean pending = _state?.PendingKey is not null;
        Boolean changed = _state is not null && Key.Text != CurrentKey;
        ApplyKey.IsEnabled = error is null && changed && !pending;
        if (error is not null)
        {
            KeyMessage.Text = error;
            ThemeBrushes.SetMessageColor(KeyMessage, error: true);
        }
        else
        {
            KeyMessage.Text = changed ? "Noch nicht übernommen."
                : pending ? "Wird an das Notebook übertragen …"
                : _state?.KeyStatus ?? $"Mindestens {HostConfig.MinKeyLength} Zeichen; vorbelegt mit einem Zufallswert, änderbar.";
            ThemeBrushes.SetMessageColor(KeyMessage, error: false);
        }
    }

    private void OnGenerateKey(Object sender, RoutedEventArgs e)
    {
        Key.Text = HostConfig.GenerateKey();
        FocusKey();
    }

    private void OnCopyKey(Object sender, RoutedEventArgs e) => Clipboard.SetText(Key.Text);

    private async void OnApplyKey(Object sender, RoutedEventArgs e)
    {
        _lastRequestMessage = KeyMessage;
        await _connection.SendAsync(new SetKeyRequest(Key.Text));
        // Der Agent antwortet mit dem neuen Stand; bis dahin gilt die Eingabe als übernommen.
        Keyboard.ClearFocus();
    }

    private IReadOnlyList<MonitorRect> HostMonitors => _state?.HostMonitors ?? [];

    private IReadOnlyList<MonitorRect> ClientMonitors => _state?.Client?.Monitors ?? [];

    private (Int32 X, Int32 Y) CurrentOffset =>
        _dragging ? _previewOffset : (_state?.Client?.OffsetX ?? 0, _state?.Client?.OffsetY ?? 0);

    private void RenderArrangement(Boolean recenter)
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
            Int32 margin = client.Count == 0
                ? host.Max(m => Math.Max(m.Width, m.Height)) / 3
                : Math.Max(client.Max(m => m.Right) - client.Min(m => m.Left), client.Max(m => m.Bottom) - client.Min(m => m.Top)) / 2;
            _originX = host.Min(m => m.Left) - margin;
            _originY = host.Min(m => m.Top) - margin;
            Arrangement.Width = host.Max(m => m.Right) + margin - _originX;
            Arrangement.Height = host.Max(m => m.Bottom) + margin - _originY;
        }
        Double unit = Math.Max(Arrangement.Width, Arrangement.Height) / 100.0;

        foreach ((MonitorRect m, String label) in MonitorBox.Numbered(host))
        {
            AddMonitor(m, label, "HostMonitorBrush", "HostMonitorBorderBrush", unit, draggable: false);
        }

        if (client.Count == 0)
        {
            return;
        }

        (Int32 ox, Int32 oy) = CurrentOffset;
        MonitorLayout layout = new MonitorLayout(host, client, ox, oy);
        for (Int32 i = 0; i < client.Count; i++)
        {
            MonitorRect m = layout.Place(client[i]);
            AddMonitor(m, $"Notebook {i + 1}\n{m.Width} × {m.Height}", "ClientMonitorBrush", "ClientMonitorBorderBrush", unit, draggable: true);
        }
        foreach (EdgeTransition t in layout.Transitions)
        {
            Arrangement.Children.Add(MonitorBox.CreateTransition(t, unit, _originX, _originY));
        }
    }

    private void AddMonitor(MonitorRect m, String label, String fill, String border, Double unit, Boolean draggable)
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

    private void OnClientMouseDown(Object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(Arrangement);
        _dragStartOffset = (_state?.Client?.OffsetX ?? 0, _state?.Client?.OffsetY ?? 0);
        _previewOffset = _dragStartOffset;
        Arrangement.CaptureMouse();
        LayoutMessage.Text = "";
        e.Handled = true;
    }

    private void OnArrangementMouseMove(Object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point p = e.GetPosition(Arrangement);
        Int32 x = _dragStartOffset.X + (Int32)Math.Round(p.X - _dragStart.X);
        Int32 y = _dragStartOffset.Y + (Int32)Math.Round(p.Y - _dragStart.Y);
        (Int32 X, Int32 Y) snapped = MonitorLayout.Snap(HostMonitors, ClientMonitors, x, y);
        if (snapped == _previewOffset)
        {
            return;
        }

        _previewOffset = snapped;
        RenderArrangement(recenter: false);
    }

    private async void OnArrangementMouseUp(Object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        (Int32 X, Int32 Y) offset = _previewOffset;
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
        ThemeBrushes.SetMessageColor(LayoutMessage, error: false);
        await _connection.SendAsync(new SetClientOffsetRequest(offset.X, offset.Y));
    }

    private void OnArrangementLostCapture(Object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RenderArrangement(recenter: false);
    }
}
