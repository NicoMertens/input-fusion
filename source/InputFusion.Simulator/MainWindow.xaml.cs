using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NC2.InputFusion.Simulator;

public partial class MainWindow : Window
{
    private const Int32 MaxLogLines = 300;

    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<String> _pendingLog = new();
    private readonly SimulatorOptions _options;
    private readonly SimulatedDesktop _desktop;
    private readonly Ellipse _cursor;
    private readonly SimulatorClient _client;
    private readonly Int32 _minX, _minY;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _options = SimulatorOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToList());
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            MessageBox.Show(ex.Message, "InputFusion Simulator", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            _options = null!;
            _desktop = null!;
            _cursor = null!;
            _client = null!;
            return;
        }

        _desktop = new SimulatedDesktop(_options.Monitors);
        _desktop.Logged += EnqueueLog;
        _client = new SimulatorClient(_options, _desktop, EnqueueLog);

        _minX = _options.Monitors.Min(m => m.Left);
        _minY = _options.Monitors.Min(m => m.Top);
        _cursor = DrawDesktop();

        CompositionTarget.Rendering += (_, _) => Refresh();
        Closed += (_, _) => _cts.Cancel();
        _ = Task.Run(() => _client.RunAsync(_cts.Token));
    }

    // Vom Netzwerk-Thread; die Liste füllt der UI-Thread je Frame.
    private void EnqueueLog(String line) => _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");

    private Ellipse DrawDesktop()
    {
        Int32 width = _options.Monitors.Max(m => m.Right) - _minX;
        Int32 height = _options.Monitors.Max(m => m.Bottom) - _minY;
        Desktop.Width = width;
        Desktop.Height = height;
        Double unit = Math.Max(width, height) / 100.0;

        foreach ((Core.Layout.MonitorRect m, Int32 i) in _options.Monitors.Select((m, i) => (m, i)))
        {
            TextBlock label = new TextBlock
            {
                Text = $"Monitor {i + 1}\n{m.Width} × {m.Height}\n{m.Dpi} DPI ({m.Dpi * 100 / 96} %)",
                Foreground = Brushes.Gray,
                FontSize = unit * 2.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            Border monitor = new Border
            {
                Width = m.Width,
                Height = m.Height,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)),
                BorderThickness = new Thickness(unit * 0.3),
                Child = label,
            };
            Canvas.SetLeft(monitor, m.Left - _minX);
            Canvas.SetTop(monitor, m.Top - _minY);
            Desktop.Children.Add(monitor);
        }

        Ellipse cursor = new Ellipse
        {
            Width = unit * 1.6,
            Height = unit * 1.6,
            Fill = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)),
            Visibility = Visibility.Hidden,
        };
        Desktop.Children.Add(cursor);
        return cursor;
    }

    private void Refresh()
    {
        (Int32 x, Int32 y, Boolean active, String[] pressed, Int64 moves) = _desktop.Snapshot();

        _cursor.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        Canvas.SetLeft(_cursor, x - _minX - _cursor.Width / 2);
        Canvas.SetTop(_cursor, y - _minY - _cursor.Height / 2);

        Status.Text = active ? "Simulator hat die Steuerung" : "Host hat die Steuerung";
        Details.Text = $"{_client.Connection}\nCursor ({x}, {y}) · {moves} Bewegungen"
            + (_options.DelayMs > 0 ? $" · Latenz {_options.DelayMs} ms" : "");
        Pressed.Text = pressed.Length == 0 ? "–" : String.Join(", ", pressed);

        while (_pendingLog.TryDequeue(out String? line))
        {
            Log.Items.Insert(0, line);
            if (Log.Items.Count > MaxLogLines)
            {
                Log.Items.RemoveAt(Log.Items.Count - 1);
            }
        }
    }
}
