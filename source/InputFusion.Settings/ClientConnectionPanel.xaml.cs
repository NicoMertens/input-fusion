using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NC2.InputFusion.Core.Config;
using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Shell;

namespace NC2.InputFusion.Settings;

/// <summary>
/// Der Reiter Verbindung auf dem Notebook: der Zustand der Verbindung und die PCs, mit denen sich der Dienst verbindet.
/// Bearbeitet wird eine Kopie der Liste, übernommen als Ganzes. Ändern dürfen nur erhöhte Administratoren; der Dienst
/// prüft das selbst und schickt die Schlüssel nur ihnen. Ohne Rechte startet die Oberfläche auf Wunsch per UAC erhöht neu.
/// </summary>
public partial class ClientConnectionPanel : UserControl
{
    private SettingsConnection? _connection;
    private ClientStateMessage? _state;
    // Die Liste in Bearbeitung, je PC die Felder als Text; ein halb getippter Port ist noch keine Zahl.
    private List<HostDraft> _drafts = [];
    private Boolean _updatingFields;

    private sealed record HostDraft(String Host, String Ip, String Port, String Key)
    {
        public static HostDraft From(HostEntry entry) =>
            new(entry.Host, entry.Ip ?? "", entry.Port.ToString(CultureInfo.InvariantCulture), entry.Key);

        public HostEntry? ToEntry() =>
            Int32.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out Int32 port)
                ? new HostEntry(Host, Key, Ip.Length == 0 ? null : Ip, port)
                : null;
    }

    public ClientConnectionPanel()
    {
        InitializeComponent();
    }

    internal void Attach(SettingsConnection connection) => _connection = connection;

    public void ShowConnected(Boolean connected)
    {
        if (!connected)
        {
            _state = null;
            HostCard.Show(false, "Nicht mit dem PC verbunden", [(null, "Ohne Dienst gibt es keine Verbindung zum PC.")]);
        }
        Form.IsEnabled = connected;
    }

    public void Show(ClientStateMessage state)
    {
        _state = state;
        ShowHost(state);

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

    public void ShowError(String message)
    {
        Message.Text = message;
        ThemeBrushes.SetMessageColor(Message, error: true);
    }

    private void ShowHost(ClientStateMessage state)
    {
        if (state.Connected)
        {
            HostCard.Show(true, $"{state.Host} verbunden",
                [("Adresse", state.HostAddress ?? ""), ("Version", state.HostVersion ?? "")]);
            return;
        }

        String title = state.Hosts.Count == 0 ? "Kein PC eingetragen" : $"Nicht mit {state.HostNames()} verbunden";
        String hint = state.Problem ?? (state.Hosts.Count == 0
            ? "Unten mit „PC hinzufügen“ PC-Name, Port und Schlüssel eintragen."
            : "Der Dienst versucht es weiter, sobald der PC erreichbar ist.");
        HostCard.Show(false, title, [(null, hint)]);
    }

    /// <summary>Füllt die Liste neu und zeigt den Eintrag an <paramref name="select"/> in den Feldern.</summary>
    private void ShowDrafts(Int32 select)
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

    private String Label(HostDraft draft)
    {
        String name = draft.Host.Length == 0 ? "(neuer PC)" : draft.Host;
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

        Boolean editable = draft is not null && _state is { CanEdit: true };
        foreach (TextBox box in new[] { HostName, Ip, Port, Key })
        {
            box.IsReadOnly = !editable;
        }

        Key.IsEnabled = editable;
        RemoveHost.IsEnabled = editable;
        UpdateMessage();
    }

    private void OnHostSelected(Object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFields)
        {
            ShowSelected();
        }
    }

    private void OnAddHost(Object sender, RoutedEventArgs e)
    {
        _drafts.Add(new HostDraft("", "", Ports.Input.ToString(CultureInfo.InvariantCulture), ""));
        ShowDrafts(_drafts.Count - 1);
        HostName.Focus();
    }

    private void OnRemoveHost(Object sender, RoutedEventArgs e)
    {
        Int32 index = HostList.SelectedIndex;
        if (index >= 0)
        {
            _drafts.RemoveAt(index);
            ShowDrafts(index);
        }
    }

    private void OnInputChanged(Object sender, TextChangedEventArgs e)
    {
        Int32 index = HostList.SelectedIndex;
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

        String? error = Validate();
        Boolean changed = IsChanged();
        Apply.IsEnabled = error is null && changed;
        Message.Text = error ?? (changed ? "Noch nicht übernommen." : "");
        ThemeBrushes.SetMessageColor(Message, error is not null);
    }

    private String? Validate()
    {
        List<HostEntry> entries = [];
        for (Int32 i = 0; i < _drafts.Count; i++)
        {
            if (_drafts[i].ToEntry() is not { } entry)
            {
                return _drafts.Count == 1 ? "Der Port muss eine Zahl sein." : $"{i + 1}. PC: Der Port muss eine Zahl sein.";
            }
            entries.Add(entry);
        }
        return ClientConfigRules.Validate(entries);
    }

    private Boolean IsChanged() =>
        _state is not null && !_drafts.SequenceEqual(_state.Hosts.Select(HostDraft.From));

    private async void OnApply(Object sender, RoutedEventArgs e)
    {
        if (Validate() is not null || _connection is not { } connection)
        {
            return;
        }

        await connection.SendAsync(new SetClientConfigRequest(_drafts.Select(d => d.ToEntry()!).ToArray()));
        // Der Dienst antwortet mit dem neuen Stand; bis dahin gilt die Eingabe als übernommen.
        Keyboard.ClearFocus();
    }

    private void OnElevate(Object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).RestartElevated(SettingsLauncher.ClientArgument))
        {
            Window.GetWindow(this)?.Close();
        }
    }
}
