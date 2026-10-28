using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using InputFusion.Core.Layout;

namespace InputFusion.Settings;

/// <summary>Ein Monitor als beschriftetes Rechteck in Layout-Pixeln, für die Anordnung auf beiden Seiten und die Vorschau des Notebooks.</summary>
internal static class MonitorBox
{
    /// <summary>Nummer von links nach rechts, Hauptmonitor und Auflösung; die Nummern von Windows sind hier nicht bekannt.</summary>
    public static IEnumerable<(MonitorRect Monitor, String Label)> Numbered(IEnumerable<MonitorRect> monitors) =>
        monitors.OrderBy(m => m.Left).ThenBy(m => m.Top)
            .Select((m, i) => (m, $"{i + 1}{(m.X == 0 && m.Y == 0 ? " · Hauptmonitor" : "")}\n{m.Width} × {m.Height}"));

    public static Border Create(MonitorRect m, String label, String fill, String border, Double unit)
    {
        return new Border
        {
            Width = m.Width,
            Height = m.Height,
            Background = (Brush)Application.Current.FindResource(fill),
            BorderBrush = (Brush)Application.Current.FindResource(border),
            BorderThickness = new Thickness(unit * 0.5),
            // Die Beschriftung passt sich dem Monitor an; eine Schriftgröße für alle wäre auf kleinen Monitoren abgeschnitten.
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(Math.Min(m.Width, m.Height) * 0.15),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    /// <summary>Ein Übergang als orange Linie auf der Kante des Host-Monitors, verschoben um den Ursprung der Zeichenfläche.</summary>
    public static Line CreateTransition(EdgeTransition t, Double unit, Double originX, Double originY)
    {
        MonitorRect h = t.HostMonitor;
        (Int32 x1, Int32 y1, Int32 x2, Int32 y2) = t.Side switch
        {
            EdgeSide.Right => (h.Right, t.From, h.Right, t.To),
            EdgeSide.Left => (h.Left, t.From, h.Left, t.To),
            EdgeSide.Bottom => (t.From, h.Bottom, t.To, h.Bottom),
            _ => (t.From, h.Top, t.To, h.Top),
        };
        return new Line
        {
            X1 = x1 - originX,
            Y1 = y1 - originY,
            X2 = x2 - originX,
            Y2 = y2 - originY,
            Stroke = (Brush)Application.Current.FindResource("TransitionBrush"),
            StrokeThickness = unit * 1.2,
            IsHitTestVisible = false,
        };
    }
}
