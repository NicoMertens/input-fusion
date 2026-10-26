using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InputFusion.Settings;

/// <summary>
/// Zeigt, ob die Gegenseite verbunden ist: grün mit Titel und Angaben, sonst grau. Beide Einstellungsfenster nutzen
/// dieselbe Karte, der Host für das Notebook, das Notebook für den PC.
/// </summary>
public partial class ConnectionCard : UserControl
{
    public ConnectionCard()
    {
        InitializeComponent();
        Show(false, "", []);
    }

    /// <summary>Symbol der Gegenseite als Zeichen aus Segoe Fluent Icons.</summary>
    public string Glyph
    {
        get => Icon.Text;
        set => Icon.Text = value;
    }

    /// <summary>Angaben ohne Bezeichnung erscheinen als Hinweis über beide Spalten.</summary>
    public void Show(bool connected, string title, IEnumerable<(string? Label, string Value)> details)
    {
        Card.Background = (Brush)FindResource(connected ? "ConnectedCardBrush" : "IdleCardBrush");
        Card.BorderBrush = (Brush)FindResource(connected ? "ConnectedCardBorderBrush" : "IdleCardBorderBrush");
        Dot.Fill = (Brush)FindResource(connected ? "ConnectedBrush" : "IdleBrush");
        Icon.Foreground = (Brush)FindResource(connected ? "ClientMonitorBrush" : "IdleBrush");
        Title.Text = title;
        Details.Children.Clear();
        Details.RowDefinitions.Clear();
        foreach ((string? label, string value) in details)
        {
            AddDetail(label, value);
        }
    }

    private void AddDetail(string? label, string value)
    {
        int row = Details.RowDefinitions.Count;
        Details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock text = new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(text, row);
        if (label is null)
        {
            text.Foreground = (Brush)FindResource("HintBrush");
            Grid.SetColumnSpan(text, 2);
        }
        else
        {
            TextBlock caption = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 2, 16, 2),
                Foreground = (Brush)FindResource("HintBrush"),
            };
            Grid.SetRow(caption, row);
            Details.Children.Add(caption);
            Grid.SetColumn(text, 1);
        }
        Details.Children.Add(text);
    }
}
