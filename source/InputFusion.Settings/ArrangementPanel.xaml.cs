using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Layout;

namespace NC2.InputFusion.Settings;

/// <summary>
/// Der Reiter Anordnung auf dem PC: die eigenen Monitore und die des Notebooks, die sich als Gruppe an jede Kante
/// ziehen lassen. Die neue Lage geht beim Loslassen an den Agent.
/// </summary>
public partial class ArrangementPanel : UserControl
{
    private SettingsConnection? _connection;
    private RequestErrors? _errors;
    private HostStateMessage? _state;

    // Verschieben der Client-Gruppe: Ausgangspunkt der Maus und Lage beim Anfassen, dazu die eingerastete Vorschau.
    private Boolean _dragging;
    private Point _dragStart;
    private (Int32 X, Int32 Y) _dragStartOffset;
    private (Int32 X, Int32 Y) _previewOffset;

    // Linke obere Ecke der Zeichenfläche in Layout-Koordinaten. Bleibt beim Ziehen stehen, sonst wanderte die Ansicht mit.
    private Double _originX, _originY;

    public ArrangementPanel()
    {
        InitializeComponent();
    }

    internal void Attach(SettingsConnection connection, RequestErrors errors)
    {
        _connection = connection;
        _errors = errors;
    }

    public void Show(HostStateMessage state)
    {
        _state = state;
        if (!_dragging)
        {
            RenderArrangement(recenter: true);
        }
    }

    internal void ShowError(String message)
    {
        LayoutMessage.Text = message;
        ThemeBrushes.SetMessageColor(LayoutMessage, error: true);
    }

    private IReadOnlyList<MonitorRect> HostMonitors => _state?.HostMonitors ?? [];

    private IReadOnlyList<MonitorRect> ClientMonitors => _state?.Client?.Monitors ?? [];

    private (Int32 X, Int32 Y) CurrentOffset =>
        _dragging ? _previewOffset : (_state?.Client?.OffsetX ?? 0, _state?.Client?.OffsetY ?? 0);

    private void RenderArrangement(Boolean recenter)
    {
        Arrangement.Children.Clear();
        IReadOnlyList<MonitorRect> host = HostMonitors;
        IReadOnlyList<MonitorRect> client = ClientMonitors;
        if (host.Count == 0)
        {
            return;
        }

        LayoutHint.Text = client.Count == 0
            ? "Kein Notebook verbunden: zu sehen sind nur die eigenen Monitore. Sobald ein Notebook verbunden ist, "
              + "erscheinen seine Monitore blau und lassen sich an jede Kante der eigenen Monitore ziehen."
            : "Die blauen Monitore des Notebooks an eine Kante der eigenen Monitore ziehen. Orange markiert die "
              + "Übergänge: Dort wechselt die Maus auf das Notebook und zurück.";
        if (recenter)
        {
            Recenter(host, client);
        }
        Double unit = Math.Max(Arrangement.Width, Arrangement.Height) / 100.0;

        foreach ((MonitorRect m, String label) in MonitorBox.Numbered(host))
        {
            AddMonitor(m, label, "HostMonitorBrush", "HostMonitorBorderBrush", unit, draggable: false);
        }

        if (client.Count > 0)
        {
            RenderClient(host, client, unit);
        }
    }

    /// <summary>
    /// Platz um die eigenen Monitore, damit sich das Notebook an jede Seite ziehen lässt. Die halbe Größe des Notebooks
    /// genügt: Es liegt immer an, der Rest darf über den Rand ragen und wird abgeschnitten.
    /// </summary>
    private void Recenter(IReadOnlyList<MonitorRect> host, IReadOnlyList<MonitorRect> client)
    {
        Int32 margin = client.Count == 0
            ? host.Max(m => Math.Max(m.Width, m.Height)) / 3
            : Math.Max(client.Max(m => m.Right) - client.Min(m => m.Left), client.Max(m => m.Bottom) - client.Min(m => m.Top)) / 2;
        _originX = host.Min(m => m.Left) - margin;
        _originY = host.Min(m => m.Top) - margin;
        Arrangement.Width = host.Max(m => m.Right) + margin - _originX;
        Arrangement.Height = host.Max(m => m.Bottom) + margin - _originY;
    }

    private void RenderClient(IReadOnlyList<MonitorRect> host, IReadOnlyList<MonitorRect> client, Double unit)
    {
        (Int32 ox, Int32 oy) = CurrentOffset;
        MonitorLayout layout = new MonitorLayout(host, client, ox, oy);
        for (Int32 i = 0; i < client.Count; i++)
        {
            MonitorRect m = layout.Place(client[i]);
            AddMonitor(m, $"Notebook {i + 1}\n{m.Width} × {m.Height}", "ClientMonitorBrush", "ClientMonitorBorderBrush", unit, draggable: true);
        }
        foreach (EdgeTransition t in layout.Transitions)
        {
            Arrangement.Children.Add(MonitorBox.CreateTransition(t, unit, _originX, _originY));
        }
    }

    private void AddMonitor(MonitorRect m, String label, String fill, String border, Double unit, Boolean draggable)
    {
        Border box = MonitorBox.Create(m, label, fill, border, unit);
        if (draggable)
        {
            box.Cursor = Cursors.SizeAll;
            box.MouseLeftButtonDown += OnClientMouseDown;
        }
        Canvas.SetLeft(box, m.Left - _originX);
        Canvas.SetTop(box, m.Top - _originY);
        Arrangement.Children.Add(box);
    }

    private void OnClientMouseDown(Object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(Arrangement);
        _dragStartOffset = (_state?.Client?.OffsetX ?? 0, _state?.Client?.OffsetY ?? 0);
        _previewOffset = _dragStartOffset;
        Arrangement.CaptureMouse();
        LayoutMessage.Text = "";
        e.Handled = true;
    }

    private void OnArrangementMouseMove(Object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point p = e.GetPosition(Arrangement);
        Int32 x = _dragStartOffset.X + (Int32)Math.Round(p.X - _dragStart.X);
        Int32 y = _dragStartOffset.Y + (Int32)Math.Round(p.Y - _dragStart.Y);
        (Int32 X, Int32 Y) snapped = MonitorLayout.Snap(HostMonitors, ClientMonitors, x, y);
        if (snapped == _previewOffset)
        {
            return;
        }

        _previewOffset = snapped;
        RenderArrangement(recenter: false);
    }

    private async void OnArrangementMouseUp(Object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        (Int32 X, Int32 Y) offset = _previewOffset;
        Arrangement.ReleaseMouseCapture();
        if (offset == _dragStartOffset || _state?.Client is not { } client)
        {
            return;
        }

        // Bis der Agent bestätigt, die neue Lage schon zeigen, sonst springt die Gruppe kurz zurück.
        _state = _state with { Client = client with { OffsetX = offset.X, OffsetY = offset.Y } };
        RenderArrangement(recenter: false);
        _errors?.Sent(ShowError);
        LayoutMessage.Text = "Gespeichert für diese Kombination aus Monitoren.";
        ThemeBrushes.SetMessageColor(LayoutMessage, error: false);
        if (_connection is { } connection)
        {
            await connection.SendAsync(new SetClientOffsetRequest(offset.X, offset.Y));
        }
    }

    private void OnArrangementLostCapture(Object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RenderArrangement(recenter: false);
    }
}
