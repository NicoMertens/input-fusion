using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InputFusion.Core.Layout;

namespace InputFusion.Settings;

/// <summary>Ein Monitor als beschriftetes Rechteck in Layout-Pixeln, für die Anordnung des PCs und die Vorschau des Notebooks.</summary>
internal static class MonitorBox
{
    /// <summary>Nummer von links nach rechts, Hauptmonitor und Auflösung; die Nummern von Windows sind hier nicht bekannt.</summary>
    public static IEnumerable<(MonitorRect Monitor, string Label)> Numbered(IEnumerable<MonitorRect> monitors) =>
        monitors.OrderBy(m => m.Left).ThenBy(m => m.Top)
            .Select((m, i) => (m, $"{i + 1}{(m.X == 0 && m.Y == 0 ? " · Hauptmonitor" : "")}\n{m.Width} × {m.Height}"));

    public static Border Create(MonitorRect m, string label, string fill, string border, double unit)
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
}
