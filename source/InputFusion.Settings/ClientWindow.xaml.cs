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
using InputFusion.Core.Net;
using InputFusion.Core.Shell;

namespace InputFusion.Settings;

/// <summary>
/// Die Einstellungen auf dem Notebook: die PCs, mit denen sich der Dienst verbindet, sein Zustand und eine Vorschau
/// der eigenen Monitore. Bearbeitet wird eine Kopie der Liste, übernommen als Ganzes. Ändern dürfen nur erhöhte
/// Administratoren; der Dienst prüft das selbst und schickt die Schlüssel nur ihnen. Ohne Rechte startet die Oberfläche
/// auf Wunsch per UAC erhöht neu.
/// </summary>
public partial class ClientWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SettingsConnection _connection;
    private ClientStateMessage? _state;
    // Die Liste in Bearbeitung, je PC die Felder als Text; ein halb getippter Port ist noch keine Zahl.
    private List<HostDraft> _drafts = [];
    private bool _updatingFields;

    private sealed record HostDraft(string Host, string Ip, string Port, string Key)
    {
        public static HostDraft From(HostEntry entry) =>
            new(entry.Host, entry.Ip ?? "", entry.Port.ToString(CultureInfo.InvariantCulture), entry.Key);

        public HostEntry? ToEntry() =>
            int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                ? new HostEntry(Host, Key, Ip.Length == 0 ? null : Ip, port)
                : null;
    }

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
        Diagnostics.Attach(_connection);
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
    /// Mit Verbindung die Anordnung, die der Host schickt, wie im Reiter Anordnung des PCs, nur ohne Verschieben.
    /// Sonst die eigenen Monitore, selbst gemessen statt über den Dienst: Die Oberfläche läuft in der Sitzung des
    /// Benutzers und ist wie der Injector Per-Monitor-V2-aware, sieht also dieselben physischen Pixel, auch ohne
    /// laufenden Dienst.
    /// </summary>
    private void RenderMonitors()
    {
        Monitors.Children.Clear();
        MonitorLayout? layout = _state is { Connected: true } ? _state.Arrangement?.ToLayout() : null;
        MonitorsHint.Text = layout is null
            ? "Die Monitore dieses Notebooks, wie Windows sie anordnet. Wo sie neben denen des PCs liegen, wird auf "
              + "dem PC eingestellt, in dessen Einstellungen im Reiter Anordnung; mit Verbindung erscheint es hier."
            : "Die Anordnung, mit der der PC rechnet: seine Monitore grau, die dieses Notebooks blau. Orange markiert "
              + "die Übergänge. Ändern lässt sie sich nur auf dem PC, in dessen Einstellungen im Reiter Anordnung.";

        List<(MonitorRect Monitor, string Label, bool Own)> boxes = layout is null
            ? MonitorBox.Numbered(MonitorEnumerator.GetMonitors()).Select(b => (b.Monitor, b.Label, true)).ToList()
            : [
                .. MonitorBox.Numbered(layout.HostMonitors).Select(b => (b.Monitor, $"PC {b.Label}", false)),
                .. layout.ClientMonitors.Select((m, i) => (layout.Place(m), $"Notebook {i + 1}\n{m.Width} × {m.Height}", true)),
            ];
        if (boxes.Count == 0)
        {
            return;
        }

        int originX = boxes.Min(b => b.Monitor.Left);
        int originY = boxes.Min(b => b.Monitor.Top);
        Monitors.Width = boxes.Max(b => b.Monitor.Right) - originX;
        Monitors.Height = boxes.Max(b => b.Monitor.Bottom) - originY;
        double unit = Math.Max(Monitors.Width, Monitors.Height) / 100.0;
        foreach ((MonitorRect m, string label, bool own) in boxes)
        {
            Border box = own
                ? MonitorBox.Create(m, label, "ClientMonitorBrush", "ClientMonitorBorderBrush", unit)
                : MonitorBox.Create(m, label, "HostMonitorBrush", "HostMonitorBorderBrush", unit);
            Canvas.SetLeft(box, m.Left - originX);
            Canvas.SetTop(box, m.Top - originY);
            Monitors.Children.Add(box);
        }

        foreach (EdgeTransition t in layout?.Transitions ?? [])
        {
            Monitors.Children.Add(MonitorBox.CreateTransition(t, unit, originX, originY));
        }
    }

    private void OnConnectedChanged(bool connected)
    {
        if (!connected)
        {
            _state = null;
            Status.Text = "Der InputFusion-Dienst läuft nicht. Die Einstellungen verbinden sich, sobald er gestartet ist …";
            HostCard.Show(false, "Nicht mit dem PC verbunden", [(null, "Ohne Dienst gibt es keine Verbindung zum PC.")]);
            RenderMonitors();
            Diagnostics.Show(null);
        }
        Form.IsEnabled = connected;
    }

    private void OnState(ClientStateMessage state)
    {
        _state = state;
        Status.Text = $"Verbunden mit dem InputFusion-Dienst {state.Version}.";
        ShowHost(state);
        RenderMonitors();

        // Was gerade bearbeitet wird, nicht überschreiben; sonst springt die Eingabe bei jeder Zustandsänderung zurück.
        if (!Form.IsKeyboardFocusWithin || !IsChanged())
        {
            _drafts = state.Hosts.Select(HostDraft.From).ToList();
        }

        AddHost.Visibility = state.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        RemoveHost.Visibility = AddHost.Visibility;
        ShowDrafts(Math.Max(HostList.SelectedIndex, 0));
        Apply.Visibility = state.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        Elevate.Visibility = state.CanEdit ? Visibility.Collapsed : Visibility.Visible;
        UpdateMessage();
    }

    private void ShowHost(ClientStateMessage state)
    {
        if (state.Connected)
        {
            HostCard.Show(true, $"{state.Host} verbunden",
                [("Adresse", state.HostAddress ?? ""), ("Version", state.HostVersion ?? "")]);
            return;
        }

        string title = state.Hosts.Count == 0 ? "Kein PC eingetragen" : $"Nicht mit {state.HostNames()} verbunden";
        string hint = state.Problem ?? (state.Hosts.Count == 0
            ? "Unten mit „PC hinzufügen“ PC-Name, Port und Schlüssel eintragen."
            : "Der Dienst versucht es weiter, sobald der PC erreichbar ist.");
        HostCard.Show(false, title, [(null, hint)]);
    }

    private void OnError(string message)
    {
        Message.Text = message;
        ThemeBrushes.SetMessageColor(Message, error: true);
    }

    /// <summary>Füllt die Liste neu und zeigt den Eintrag an <paramref name="select"/> in den Feldern.</summary>
    private void ShowDrafts(int select)
    {
        _updatingFields = true;
        HostList.Items.Clear();
        foreach (HostDraft draft in _drafts)
        {
            HostList.Items.Add(Label(draft));
        }

        HostList.SelectedIndex = _drafts.Count == 0 ? -1 : Math.Min(select, _drafts.Count - 1);
        _updatingFields = false;
        ShowSelected();
    }

    private string Label(HostDraft draft)
    {
        string name = draft.Host.Length == 0 ? "(neuer PC)" : draft.Host;
        return _state is { Connected: true } && _state.Host == draft.Host ? $"{name}  –  verbunden" : name;
    }

    private void ShowSelected()
    {
        HostDraft? draft = HostList.SelectedIndex >= 0 ? _drafts[HostList.SelectedIndex] : null;
        _updatingFields = true;
        HostName.Text = draft?.Host ?? "";
        Ip.Text = draft?.Ip ?? "";
        Port.Text = draft?.Port ?? "";
        Key.Text = draft?.Key ?? "";
        _updatingFields = false;

        bool editable = draft is not null && _state is { CanEdit: true };
        foreach (TextBox box in new[] { HostName, Ip, Port, Key })
        {
            box.IsReadOnly = !editable;
        }

        Key.IsEnabled = editable;
        RemoveHost.IsEnabled = editable;
        UpdateMessage();
    }

    private void OnHostSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFields)
        {
            ShowSelected();
        }
    }

    private void OnAddHost(object sender, RoutedEventArgs e)
    {
        _drafts.Add(new HostDraft("", "", Ports.Input.ToString(CultureInfo.InvariantCulture), ""));
        ShowDrafts(_drafts.Count - 1);
        HostName.Focus();
    }

    private void OnRemoveHost(object sender, RoutedEventArgs e)
    {
        int index = HostList.SelectedIndex;
        if (index >= 0)
        {
            _drafts.RemoveAt(index);
            ShowDrafts(index);
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        int index = HostList.SelectedIndex;
        if (_updatingFields || index < 0)
        {
            return;
        }

        _drafts[index] = new HostDraft(HostName.Text, Ip.Text, Port.Text, Key.Text);
        // Nur den Eintrag ersetzen: Neu füllen würde die Auswahl kurz aufheben und die Felder leeren.
        _updatingFields = true;
        HostList.Items[index] = Label(_drafts[index]);
        HostList.SelectedIndex = index;
        _updatingFields = false;
        UpdateMessage();
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
            Message.Text = "Ändern dürfen nur Administratoren; die Schlüssel sind nur für sie sichtbar.";
            ThemeBrushes.SetMessageColor(Message, error: false);
            return;
        }

        string? error = Validate();
        bool changed = IsChanged();
        Apply.IsEnabled = error is null && changed;
        Message.Text = error ?? (changed ? "Noch nicht übernommen." : "");
        ThemeBrushes.SetMessageColor(Message, error is not null);
    }

    private string? Validate()
    {
        List<HostEntry> entries = [];
        for (int i = 0; i < _drafts.Count; i++)
        {
            if (_drafts[i].ToEntry() is not { } entry)
            {
                return _drafts.Count == 1 ? "Der Port muss eine Zahl sein." : $"{i + 1}. PC: Der Port muss eine Zahl sein.";
            }
            entries.Add(entry);
        }
        return ClientConfigRules.Validate(entries);
    }

    private bool IsChanged() =>
        _state is not null && !_drafts.SequenceEqual(_state.Hosts.Select(HostDraft.From));

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (Validate() is not null)
        {
            return;
        }

        await _connection.SendAsync(new SetClientConfigRequest(_drafts.Select(d => d.ToEntry()!).ToArray()));
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
