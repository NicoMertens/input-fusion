using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Simulator;

public partial class MainWindow : Window
{
    private const Int32 MaxLogLines = 300;

    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<String> _pendingLog = new();
    private readonly SimulatorOptions _options;
    private readonly SimulatedDesktop _desktop;
    private readonly Ellipse _cursor;
    private readonly Int32 _minX, _minY;
    private String _connection = "Nicht verbunden";

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

    private async Task RunNetworkAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using TcpClient tcp = new TcpClient();
                _connection = $"Verbinde zu {_options.Host}:{_options.Port} …";
                await tcp.ConnectAsync(_options.Host, _options.Port, ct);
                using MessageConnection conn = MessageConnection.FromTcp(tcp);
                HandshakeResult handshake = await Handshake.ClientAsync(conn, _options.Key, AppVersion.Current, ct);
                if (handshake.Outcome != HandshakeOutcome.Accepted)
                {
                    _connection = handshake.Outcome == HandshakeOutcome.ProtocolMismatch
                        ? $"Host spricht Protokoll {handshake.PeerProtocol} (Version {handshake.PeerVersion}), der Simulator {ProtocolVersion.Current}. Neuer Versuch …"
                        : $"{_options.Host}:{_options.Port} hat die Anmeldung abgelehnt, stimmt der Schlüssel? Neuer Versuch …";
                    await Task.Delay(3000, ct);
                    continue;
                }
                await conn.SendAsync(MessageType.Monitors, MonitorList.ToPayload(_options.Monitors), ct);
                await conn.SendAsync(MessageType.ClientInfo, new ClientInfo($"{Environment.MachineName} (Simulator)", null, Environment.UserName).ToPayload(), ct);
                _connection = $"Verbunden mit {_options.Host}:{_options.Port}";
                _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {_connection}");

                // Heartbeat zum Host, sonst trennt er nach dem Timeout; schweigt der Host, neu verbinden.
                using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task monitor = Task.Run(async () =>
                {
                    if (await conn.MonitorAsync(session.Token))
                    {
                        _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  Host antwortet nicht mehr");
                        session.Cancel();
                    }
                }, CancellationToken.None);
                try
                {
                    if (_options.DelayMs > 0)
                    {
                        await ReceiveDelayedAsync(conn, session.Token);
                    }
                    else
                    {
                        await foreach ((MessageType type, Byte[] payload) in conn.ReceiveAsync(session.Token))
                        {
                            _desktop.Apply(type, payload);
                            if (_desktop.TryTakeExitRequest() is { } exit)
                            {
                                await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), session.Token);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                }
                finally
                {
                    session.Cancel();
                    await monitor;
                }

                _desktop.Disconnected();
                _pendingLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  Verbindung vom Host beendet");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                _connection = $"Kein Host unter {_options.Host}:{_options.Port}, neuer Versuch …";
            }

            await Task.Delay(1000, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>Hält jede Nachricht um die eingestellte Latenz zurück, ohne den Durchsatz zu drosseln.</summary>
    private async Task ReceiveDelayedAsync(MessageConnection conn, CancellationToken ct)
    {
        Channel<(DateTime Due, MessageType Type, Byte[] Payload)> delayed = Channel.CreateUnbounded<(DateTime Due, MessageType Type, Byte[] Payload)>();
        Task apply = Task.Run(async () =>
        {
            await foreach ((DateTime due, MessageType type, Byte[] payload) in delayed.Reader.ReadAllAsync(ct))
            {
                TimeSpan wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                _desktop.Apply(type, payload);
                if (_desktop.TryTakeExitRequest() is { } exit)
                {
                    await conn.SendAsync(MessageType.SwitchBackRequest, exit.ToPayload(), ct);
                }
            }
        }, ct);

        await foreach ((MessageType type, Byte[] payload) in conn.ReceiveAsync(ct))
        {
            delayed.Writer.TryWrite((DateTime.UtcNow.AddMilliseconds(_options.DelayMs), type, payload));
        }

        delayed.Writer.Complete();
        await apply;
    }

    private void Refresh()
    {
        (Int32 x, Int32 y, Boolean active, String[] pressed, Int64 moves) = _desktop.Snapshot();

        _cursor.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        Canvas.SetLeft(_cursor, x - _minX - _cursor.Width / 2);
        Canvas.SetTop(_cursor, y - _minY - _cursor.Height / 2);

        Status.Text = active ? "Simulator hat die Steuerung" : "Host hat die Steuerung";
        Details.Text = $"{_connection}\nCursor ({x}, {y}) · {moves} Bewegungen"
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
