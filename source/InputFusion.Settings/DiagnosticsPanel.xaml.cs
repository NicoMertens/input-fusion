using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NC2.InputFusion.Core.Ipc;

namespace NC2.InputFusion.Settings;

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
    private String? _logPath;

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
        Connection.Text = DescribeConnection(diagnostics);
        RoundTrip.Text = DescribeRoundTrip(diagnostics);
        Rates.Text = diagnostics is { Connected: true } ? DescribeRates(diagnostics, now) : "–";
        LastError.Text = DescribeLastError(diagnostics);
        ShowLog(diagnostics);
        _last = diagnostics;
        _lastAt = now;
    }

    private void ShowLog(DiagnosticsMessage? diagnostics)
    {
        String log = diagnostics is null ? "" : String.Join(Environment.NewLine, diagnostics.Log);
        if (LogLines.Text != log)
        {
            // Wer zurückgeblättert hat, liest gerade; nur wer am Ende steht, läuft mit.
            Boolean atEnd = LogLines.VerticalOffset + LogLines.ViewportHeight >= LogLines.ExtentHeight - 1;
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
    }

    private static String DescribeConnection(DiagnosticsMessage? diagnostics) =>
        diagnostics is null ? "–"
        : diagnostics.ConnectedSince is { } since ? $"verbunden seit {since.ToLocalTime():HH:mm:ss}, {Describe(DateTimeOffset.Now - since)}"
        : "nicht verbunden";

    private static String DescribeRoundTrip(DiagnosticsMessage? diagnostics) =>
        diagnostics?.RoundTripMs is { } ms ? $"{ms.ToString("0.0", CultureInfo.CurrentCulture)} ms"
        : diagnostics is { Connected: true } ? "noch keine Antwort; Gegenseiten vor Version 0.8.0 antworten nicht"
        : "–";

    private static String DescribeLastError(DiagnosticsMessage? diagnostics) =>
        diagnostics is null ? "–"
        : diagnostics.LastError is { } error ? $"{diagnostics.LastErrorAt?.ToLocalTime():HH:mm:ss}  {error}"
        : "keine";

    private void Request()
    {
        if (_connection is { } connection)
        {
            _ = connection.SendAsync(new GetDiagnosticsRequest());
        }
    }

    /// <summary>Aus der Differenz zur vorigen Antwort; nach einer Neuverbindung beginnen die Zähler neu.</summary>
    private String DescribeRates(DiagnosticsMessage current, DateTime now)
    {
        String totals = $"(gesamt {current.SentFrames:N0} gesendet, {current.ReceivedFrames:N0} empfangen)";
        Double seconds = (now - _lastAt).TotalSeconds;
        if (_last is not { Connected: true } last || last.ConnectedSince != current.ConnectedSince || seconds <= 0)
        {
            return totals;
        }

        Double sent = (current.SentFrames - last.SentFrames) / seconds;
        Double received = (current.ReceivedFrames - last.ReceivedFrames) / seconds;
        return $"{sent:0} gesendet, {received:0} empfangen {totals}";
    }

    private static String Describe(TimeSpan duration) =>
        duration.TotalMinutes < 1 ? "gerade eben"
        : duration.TotalHours < 1 ? $"{(Int32)duration.TotalMinutes} min"
        : duration.TotalDays < 1 ? $"{(Int32)duration.TotalHours} h {duration.Minutes} min"
        : $"{(Int32)duration.TotalDays} d {duration.Hours} h";

    private void OnOpenLog(Object sender, RoutedEventArgs e)
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
