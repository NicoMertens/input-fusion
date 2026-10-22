using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Simulator;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 300;

    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<string> _pendingLog = new();
    private readonly SimulatorOptions _options;
    private readonly SimulatedDesktop _desktop;
    private readonly Ellipse _cursor;
    private readonly int _minX, _minY;
    private string _connection = "Nicht verbunden";

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
            return;
        }

        _desktop = new SimulatedDesktop(_options.Monitors);
        _desktop.Logged += line => _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");

        _minX = _options.Monitors.Min(m => m.Left);
        _minY = _options.Monitors.Min(m => m.Top);
        _cursor = DrawDesktop();

        CompositionTarget.Rendering += (_, _) => Refresh();
        Closed += (_, _) => _cts.Cancel();
        _ = Task.Run(() => RunNetworkAsync(_cts.Token));
    }

    private Ellipse DrawDesktop()
    {
        int width = _options.Monitors.Max(m => m.Right) - _minX;
        int height = _options.Monitors.Max(m => m.Bottom) - _minY;
        Desktop.Width = width;
        Desktop.Height = height;
        double unit = Math.Max(width, height) / 100.0;

        foreach (var (m, i) in _options.Monitors.Select((m, i) => (m, i)))
        {
            var label = new TextBlock
            {
                Text = $"Monitor {i + 1}\n{m.Width} × {m.Height}\n{m.Dpi} DPI ({m.Dpi * 100 / 96} %)",
                Foreground = Brushes.Gray,
                FontSize = unit * 2.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            var monitor = new Border
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

        var cursor = new Ellipse
        {
            Width = unit * 1.6,
            Height = unit * 1.6,
            Fill = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)),
            Visibility = Visibility.Hidden,
        };
        Desktop.Children.Add(cursor);
        return cursor;
    }

    private async Task RunNetworkAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                _connection = $"Verbinde zu {_options.Host}:{Ports.Input} …";
                await tcp.ConnectAsync(_options.Host, Ports.Input, ct);
                using var conn = new MessageConnection(tcp);
                _connection = $"Verbunden mit {_options.Host}:{Ports.Input}";
                _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {_connection}");

                if (_options.DelayMs > 0)
                    await ReceiveDelayedAsync(conn, ct);
                else
                    await foreach (var (type, payload) in conn.ReceiveAsync(ct))
                    {
                        _desktop.Apply(type, payload);
                        if (_desktop.TryTakeExitRequest())
                            await conn.SendAsync(MessageType.SwitchBackRequest, default, ct);
                    }

                _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  Verbindung vom Host beendet");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                _connection = $"Kein Host unter {_options.Host}:{Ports.Input}, neuer Versuch …";
            }

            await Task.Delay(1000, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>Hält jede Nachricht um die eingestellte Latenz zurück, ohne den Durchsatz zu drosseln.</summary>
    private async Task ReceiveDelayedAsync(MessageConnection conn, CancellationToken ct)
    {
        var delayed = Channel.CreateUnbounded<(DateTime Due, MessageType Type, byte[] Payload)>();
        var apply = Task.Run(async () =>
        {
            await foreach (var (due, type, payload) in delayed.Reader.ReadAllAsync(ct))
            {
                var wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);
                _desktop.Apply(type, payload);
                if (_desktop.TryTakeExitRequest())
                    await conn.SendAsync(MessageType.SwitchBackRequest, default, ct);
            }
        }, ct);

        await foreach (var (type, payload) in conn.ReceiveAsync(ct))
            delayed.Writer.TryWrite((DateTime.UtcNow.AddMilliseconds(_options.DelayMs), type, payload));

        delayed.Writer.Complete();
        await apply;
    }

    private void Refresh()
    {
        var (x, y, active, pressed, moves) = _desktop.Snapshot();

        _cursor.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        Canvas.SetLeft(_cursor, x - _minX - _cursor.Width / 2);
        Canvas.SetTop(_cursor, y - _minY - _cursor.Height / 2);

        Status.Text = active ? "Simulator hat die Steuerung" : "Host hat die Steuerung";
        Details.Text = $"{_connection}\nCursor ({x}, {y}) · {moves} Bewegungen"
            + (_options.DelayMs > 0 ? $" · Latenz {_options.DelayMs} ms" : "");
        Pressed.Text = pressed.Length == 0 ? "–" : string.Join(", ", pressed);

        while (_pendingLog.TryDequeue(out var line))
        {
            Log.Items.Insert(0, line);
            if (Log.Items.Count > MaxLogLines)
                Log.Items.RemoveAt(Log.Items.Count - 1);
        }
    }
}
