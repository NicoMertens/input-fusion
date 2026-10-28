using System.Windows;
using NC2.InputFusion.Core.Ipc;

namespace NC2.InputFusion.Settings;

/// <summary>Die Einstellungen auf dem PC: hält die Verbindung zum Agent und verteilt dessen Zustand auf die Reiter.</summary>
public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SettingsConnection _connection;

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
        // Vor der ersten eigenen Anfrage kann der Agent nur eine Lage abgelehnt haben.
        RequestErrors errors = new RequestErrors(ArrangementTab.ShowError);
        ConnectionTab.Attach(_connection, errors);
        ArrangementTab.Attach(_connection, errors);
        Diagnostics.Attach(_connection);
        _connection.ErrorReceived += errors.Show;
        _connection.ConnectedChanged += OnConnectedChanged;
        OnConnectedChanged(false);
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
        Status.Text = $"Verbunden mit dem InputFusion-Agent {state.Version}.";
        ConnectionTab.Show(state);
        ArrangementTab.Show(state);
    }
}
