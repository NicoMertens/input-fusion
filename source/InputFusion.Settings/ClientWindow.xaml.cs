using System.Windows;
using NC2.InputFusion.Core.Ipc;

namespace NC2.InputFusion.Settings;

/// <summary>
/// Die Einstellungen auf dem Notebook: hält die Verbindung zum Dienst und verteilt dessen Zustand auf die Reiter.
/// </summary>
public partial class ClientWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SettingsConnection _connection;

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
            else if (message is DiagnosticsMessage diagnostics)
            {
                Diagnostics.Show(diagnostics);
            }
        };
        ConnectionTab.Attach(_connection);
        Diagnostics.Attach(_connection);
        _connection.ErrorReceived += ConnectionTab.ShowError;
        _connection.ConnectedChanged += OnConnectedChanged;
        OnConnectedChanged(false);
        Closed += (_, _) => _cts.Cancel();
        _ = _connection.RunAsync(_cts.Token);
    }

    private void OnConnectedChanged(Boolean connected)
    {
        if (!connected)
        {
            Status.Text = "Der InputFusion-Dienst läuft nicht. Die Einstellungen verbinden sich, sobald er gestartet ist …";
            MonitorsTab.Show(null);
            Diagnostics.Show(null);
        }
        ConnectionTab.ShowConnected(connected);
    }

    private void OnState(ClientStateMessage state)
    {
        Status.Text = $"Verbunden mit dem InputFusion-Dienst {state.Version}.";
        ConnectionTab.Show(state);
        MonitorsTab.Show(state);
    }
}
