using System.Windows.Controls;
using Microsoft.Win32;
using NC2.InputFusion.Core.Display;
using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Layout;

namespace NC2.InputFusion.Settings;

/// <summary>
/// Der Reiter Monitore auf dem Notebook. Mit Verbindung die Anordnung, die der Host schickt, wie im Reiter Anordnung
/// des PCs, nur ohne Verschieben. Sonst die eigenen Monitore, selbst gemessen statt über den Dienst: Die Oberfläche
/// läuft in der Sitzung des Benutzers und ist wie der Injector Per-Monitor-V2-aware, sieht also dieselben physischen
/// Pixel, auch ohne laufenden Dienst.
/// </summary>
public partial class MonitorsPanel : UserControl
{
    private ClientStateMessage? _state;

    public MonitorsPanel()
    {
        InitializeComponent();
        // Ein Reiter wird beim Wechsel entladen; nur solange er geladen ist, auf Änderungen der Anzeige hören.
        Loaded += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Render();
        };
        Unloaded += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Render();
    }

    /// <summary>Null ohne Verbindung zum Dienst.</summary>
    public void Show(ClientStateMessage? state)
    {
        _state = state;
        Render();
    }

    // Kommt auf dem Thread von SystemEvents an, nicht auf dem der Oberfläche.
    private void OnDisplaySettingsChanged(Object? sender, EventArgs e) => Dispatcher.BeginInvoke(Render);

    private void Render()
    {
        Monitors.Children.Clear();
        MonitorLayout? layout = _state is { Connected: true } ? _state.Arrangement?.ToLayout() : null;
        MonitorsHint.Text = layout is null
            ? "Die Monitore dieses Notebooks, wie Windows sie anordnet. Wo sie neben denen des PCs liegen, wird auf "
              + "dem PC eingestellt, in dessen Einstellungen im Reiter Anordnung; mit Verbindung erscheint es hier."
            : "Die Anordnung, mit der der PC rechnet: seine Monitore grau, die dieses Notebooks blau. Orange markiert "
              + "die Übergänge. Ändern lässt sie sich nur auf dem PC, in dessen Einstellungen im Reiter Anordnung.";

        List<(MonitorRect Monitor, String Label, Boolean Own)> boxes = layout is null
            ? MonitorBox.Numbered(MonitorEnumerator.GetMonitors()).Select(b => (b.Monitor, b.Label, true)).ToList()
            : [
                .. MonitorBox.Numbered(layout.HostMonitors).Select(b => (b.Monitor, $"PC {b.Label}", false)),
                .. layout.ClientMonitors.Select((m, i) => (layout.Place(m), $"Notebook {i + 1}\n{m.Width} × {m.Height}", true)),
            ];
        if (boxes.Count == 0)
        {
            return;
        }

        Int32 originX = boxes.Min(b => b.Monitor.Left);
        Int32 originY = boxes.Min(b => b.Monitor.Top);
        Monitors.Width = boxes.Max(b => b.Monitor.Right) - originX;
        Monitors.Height = boxes.Max(b => b.Monitor.Bottom) - originY;
        Double unit = Math.Max(Monitors.Width, Monitors.Height) / 100.0;
        foreach ((MonitorRect m, String label, Boolean own) in boxes)
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
}
