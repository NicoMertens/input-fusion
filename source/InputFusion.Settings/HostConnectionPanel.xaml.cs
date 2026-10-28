using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NC2.InputFusion.Core.Config;
using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Net;

namespace NC2.InputFusion.Settings;

/// <summary>
/// Der Reiter Verbindung auf dem PC: das verbundene Notebook, Name und Adressen dieses PCs, dazu Port und Schlüssel,
/// die hier geändert und an den Agent geschickt werden.
/// </summary>
public partial class HostConnectionPanel : UserControl
{
    private SettingsConnection? _connection;
    private RequestErrors? _errors;
    private HostStateMessage? _state;
    private Boolean _updatingKey;
    private Boolean _updatingPort;
    // Nur der Agent kann einen Port ablehnen (belegt); die Meldung bleibt stehen, bis im Feld geändert wird.
    private Boolean _portRejected;

    public HostConnectionPanel()
    {
        InitializeComponent();
        ShowClient(null);
    }

    internal void Attach(SettingsConnection connection, RequestErrors errors)
    {
        _connection = connection;
        _errors = errors;
    }

    public void Show(HostStateMessage state)
    {
        _state = state;
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

    private void ShowPortError(String message)
    {
        _portRejected = true;
        PortMessage.Text = message;
        ThemeBrushes.SetMessageColor(PortMessage, error: true);
    }

    private void ShowKeyError(String message)
    {
        _portRejected = false;
        KeyMessage.Text = message;
        ThemeBrushes.SetMessageColor(KeyMessage, error: true);
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
        if (Ports.Validate(Port.Text, out Int32 port) is not null || _connection is not { } connection)
        {
            return;
        }

        _errors?.Sent(ShowPortError);
        await connection.SendAsync(new SetPortRequest(port));
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
        if (_connection is not { } connection)
        {
            return;
        }

        _errors?.Sent(ShowKeyError);
        await connection.SendAsync(new SetKeyRequest(Key.Text));
        // Der Agent antwortet mit dem neuen Stand; bis dahin gilt die Eingabe als übernommen.
        Keyboard.ClearFocus();
    }
}
