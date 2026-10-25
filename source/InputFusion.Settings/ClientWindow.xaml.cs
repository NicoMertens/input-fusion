using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using InputFusion.Core.Config;
using InputFusion.Core.Display;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;
using InputFusion.Core.Shell;

namespace InputFusion.Settings;

/// <summary>
/// Die Einstellungen auf dem Notebook: Verbindungsdaten des Dienstes, sein Zustand und eine Vorschau der eigenen
/// Monitore. Ändern dürfen nur erhöhte
/// Administratoren; der Dienst prüft das selbst und schickt den Schlüssel nur ihnen. Ohne Rechte startet die Oberfläche
/// auf Wunsch per UAC erhöht neu.
/// </summary>
public partial class ClientWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SettingsConnection _connection;
    private ClientStateMessage? _state;
    private bool _updatingFields;

    public ClientWindow()
    {
        InitializeComponent();
        _connection = new SettingsConnection(Dispatcher, ClientPipe.CreateClient, ClientPipe.VerifyServer);
        _connection.StateReceived += message =>
        {
            if (message is ClientStateMessage state)
            {
                OnState(state);
            }
        };
        _connection.ErrorReceived += OnError;
        _connection.ConnectedChanged += OnConnectedChanged;
        OnConnectedChanged(false);
        RenderMonitors();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _cts.Cancel();
        };
        _ = _connection.RunAsync(_cts.Token);
    }

    // Kommt auf dem Thread von SystemEvents an, nicht auf dem der Oberfläche.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RenderMonitors);

    /// <summary>
    /// Misst die Monitore selbst statt über den Dienst: Die Oberfläche läuft in der Sitzung des Benutzers und ist wie der
    /// Injector Per-Monitor-V2-aware, sieht also dieselben physischen Pixel, auch ohne laufenden Dienst.
    /// </summary>
    private void RenderMonitors()
    {
        Monitors.Children.Clear();
        IReadOnlyList<MonitorRect> monitors = MonitorEnumerator.GetMonitors();
        if (monitors.Count == 0)
        {
            return;
        }

        int originX = monitors.Min(m => m.Left);
        int originY = monitors.Min(m => m.Top);
        Monitors.Width = monitors.Max(m => m.Right) - originX;
        Monitors.Height = monitors.Max(m => m.Bottom) - originY;
        double unit = Math.Max(Monitors.Width, Monitors.Height) / 100.0;
        foreach ((MonitorRect m, string label) in MonitorBox.Numbered(monitors))
        {
            Border box = MonitorBox.Create(m, label, "ClientMonitorBrush", "ClientMonitorBorderBrush", unit);
            Canvas.SetLeft(box, m.Left - originX);
            Canvas.SetTop(box, m.Top - originY);
            Monitors.Children.Add(box);
        }
    }

    private void OnConnectedChanged(bool connected)
    {
        if (!connected)
        {
            _state = null;
            Status.Text = "Der InputFusion-Dienst läuft nicht. Die Einstellungen verbinden sich, sobald er gestartet ist …";
        }
        Form.IsEnabled = connected;
    }

    private void OnState(ClientStateMessage state)
    {
        _state = state;
        Status.Text = $"InputFusion Client {state.Version}: {state.Summary()}";

        // Was gerade bearbeitet wird, nicht überschreiben; sonst springt die Eingabe bei jeder Zustandsänderung zurück.
        if (!Form.IsKeyboardFocusWithin || !IsChanged())
        {
            _updatingFields = true;
            HostName.Text = state.Host ?? "";
            Ip.Text = state.Ip ?? "";
            Port.Text = state.Port.ToString(CultureInfo.InvariantCulture);
            Key.Text = state.Key ?? "";
            _updatingFields = false;
        }

        foreach (TextBox box in new[] { HostName, Ip, Port, Key })
        {
            box.IsReadOnly = !state.CanEdit;
        }

        Key.IsEnabled = state.CanEdit;
        Apply.Visibility = state.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        Elevate.Visibility = state.CanEdit ? Visibility.Collapsed : Visibility.Visible;
        UpdateMessage();
    }

    private void OnError(string message)
    {
        Message.Text = message;
        Message.Foreground = (Brush)FindResource("ErrorBrush");
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingFields)
        {
            UpdateMessage();
        }
    }

    private void UpdateMessage()
    {
        if (_state is null)
        {
            return;
        }

        if (!_state.CanEdit)
        {
            Apply.IsEnabled = false;
            Message.Text = "Ändern dürfen nur Administratoren; der Schlüssel ist nur für sie sichtbar.";
            Message.Foreground = (Brush)FindResource("HintBrush");
            return;
        }

        string? error = Validate();
        bool changed = IsChanged();
        Apply.IsEnabled = error is null && changed;
        Message.Text = error ?? (changed ? "Noch nicht übernommen." : "");
        Message.Foreground = (Brush)FindResource(error is null ? "HintBrush" : "ErrorBrush");
    }

    private string? Validate() =>
        int.TryParse(Port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            ? ClientConfigRules.Validate(HostName.Text, Ip.Text, port, Key.Text)
            : "Der Port muss eine Zahl sein.";

    private bool IsChanged() =>
        _state is not null
        && (HostName.Text != (_state.Host ?? "") || Ip.Text != (_state.Ip ?? "")
            || Port.Text != _state.Port.ToString(CultureInfo.InvariantCulture) || Key.Text != (_state.Key ?? ""));

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (Validate() is not null)
        {
            return;
        }

        int port = int.Parse(Port.Text, NumberStyles.None, CultureInfo.InvariantCulture);
        await _connection.SendAsync(new SetClientConfigRequest(HostName.Text, Ip.Text.Length == 0 ? null : Ip.Text, port, Key.Text));
        // Der Dienst antwortet mit dem neuen Stand; bis dahin gilt die Eingabe als übernommen.
        Keyboard.ClearFocus();
    }

    private void OnElevate(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).RestartElevated(SettingsLauncher.ClientArgument))
        {
            Close();
        }
    }
}
