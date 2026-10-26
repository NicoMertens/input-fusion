using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using InputFusion.Core.Ipc;

namespace InputFusion.Settings;

/// <summary>
/// Der Reiter Diagnose, auf PC und Notebook gleich: Verbindung, Laufzeit, Nachrichten pro Sekunde, letzte Störung und die
/// letzten Meldungen des Protokolls. Fragt jede Sekunde nach, solange er sichtbar ist.
/// </summary>
public partial class DiagnosticsPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SettingsConnection? _connection;
    private DiagnosticsMessage? _last;
    private DateTime _lastAt;
    private string? _logPath;

    public DiagnosticsPanel()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Request();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Request();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Show(null);
    }

    internal void Attach(SettingsConnection connection) => _connection = connection;

    /// <summary>Null, solange keine Antwort da ist, etwa weil Agent oder Dienst nicht laufen.</summary>
    public void Show(DiagnosticsMessage? diagnostics)
    {
        DateTime now = DateTime.UtcNow;
        Connection.Text = diagnostics is null ? "–"
            : diagnostics.ConnectedSince is { } since ? $"verbunden seit {since.ToLocalTime():HH:mm:ss}, {Describe(DateTimeOffset.Now - since)}"
            : "nicht verbunden";
        RoundTrip.Text = diagnostics?.RoundTripMs is { } ms ? $"{ms.ToString("0.0", CultureInfo.CurrentCulture)} ms"
            : diagnostics is { Connected: true } ? "noch keine Antwort; Gegenseiten vor Version 0.8.0 antworten nicht"
            : "–";
        Rates.Text = diagnostics is { Connected: true } ? DescribeRates(diagnostics, now) : "–";
        LastError.Text = diagnostics is null ? "–"
            : diagnostics.LastError is { } error ? $"{diagnostics.LastErrorAt?.ToLocalTime():HH:mm:ss}  {error}"
            : "keine";

        string log = diagnostics is null ? "" : string.Join(Environment.NewLine, diagnostics.Log);
        if (LogLines.Text != log)
        {
            // Wer zurückgeblättert hat, liest gerade; nur wer am Ende steht, läuft mit.
            bool atEnd = LogLines.VerticalOffset + LogLines.ViewportHeight >= LogLines.ExtentHeight - 1;
            LogLines.Text = log;
            if (atEnd)
            {
                LogLines.ScrollToEnd();
            }
        }

        _logPath = diagnostics?.LogPath;
        OpenLog.Content = _logPath is null ? "Ereignisanzeige öffnen" : "Protokoll öffnen";
        OpenLog.Visibility = diagnostics is null ? Visibility.Collapsed : Visibility.Visible;
        LogHint.Text = diagnostics is null ? ""
            : _logPath is null ? "Das ganze Protokoll des Dienstes steht in der Ereignisanzeige unter Windows-Protokolle › Anwendung, Quelle InputFusion.Client."
            : $"Das ganze Protokoll steht in {_logPath}.";

        _last = diagnostics;
        _lastAt = now;
    }

    private void Request()
    {
        if (_connection is { } connection)
        {
            _ = connection.SendAsync(new GetDiagnosticsRequest());
        }
    }

    /// <summary>Aus der Differenz zur vorigen Antwort; nach einer Neuverbindung beginnen die Zähler neu.</summary>
    private string DescribeRates(DiagnosticsMessage current, DateTime now)
    {
        string totals = $"(gesamt {current.SentFrames:N0} gesendet, {current.ReceivedFrames:N0} empfangen)";
        double seconds = (now - _lastAt).TotalSeconds;
        if (_last is not { Connected: true } last || last.ConnectedSince != current.ConnectedSince || seconds <= 0)
        {
            return totals;
        }

        double sent = (current.SentFrames - last.SentFrames) / seconds;
        double received = (current.ReceivedFrames - last.ReceivedFrames) / seconds;
        return $"{sent:0} gesendet, {received:0} empfangen {totals}";
    }

    private static string Describe(TimeSpan duration) =>
        duration.TotalMinutes < 1 ? "gerade eben"
        : duration.TotalHours < 1 ? $"{(int)duration.TotalMinutes} min"
        : duration.TotalDays < 1 ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
        : $"{(int)duration.TotalDays} d {duration.Hours} h";

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_logPath ?? "eventvwr.msc") { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            LogHint.Text = $"Ließ sich nicht öffnen: {ex.Message}";
        }
    }
}
