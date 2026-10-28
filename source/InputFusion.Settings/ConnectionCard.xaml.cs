using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace NC2.InputFusion.Settings;

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
    public String Glyph
    {
        get => Icon.Text;
        set => Icon.Text = value;
    }

    /// <summary>Angaben ohne Bezeichnung erscheinen als Hinweis über beide Spalten.</summary>
    public void Show(Boolean connected, String title, IEnumerable<(String? Label, String Value)> details)
    {
        // Die Karte bleibt neutral; den Zustand zeigen Symbol in Akzentfarbe und grüner Punkt. Ein grün getönter Hintergrund
        // (SystemFillColorSuccessBackgroundBrush) wirkt im dunklen Modus schlammig.
        Card.SetResourceReference(Border.BackgroundProperty, ThemeBrushes.CardBackground);
        Card.SetResourceReference(Border.BorderBrushProperty, ThemeBrushes.CardStroke);
        IconPlate.SetResourceReference(Border.BackgroundProperty, connected ? ThemeBrushes.AccentFill : ThemeBrushes.SubtleFill);
        Dot.SetResourceReference(Shape.FillProperty, connected ? ThemeBrushes.Success : ThemeBrushes.Idle);
        Icon.SetResourceReference(TextBlock.ForegroundProperty, connected ? ThemeBrushes.TextOnAccent : ThemeBrushes.Idle);
        Title.Text = title;
        Details.Children.Clear();
        Details.RowDefinitions.Clear();
        foreach ((String? label, String value) in details)
        {
            AddDetail(label, value);
        }
    }

    private void AddDetail(String? label, String value)
    {
        Int32 row = Details.RowDefinitions.Count;
        Details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock text = new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(text, row);
        if (label is null)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, ThemeBrushes.Hint);
            Grid.SetColumnSpan(text, 2);
        }
        else
        {
            TextBlock caption = new TextBlock { Text = label, Margin = new Thickness(0, 2, 16, 2) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, ThemeBrushes.Hint);
            Grid.SetRow(caption, row);
            Details.Children.Add(caption);
            Grid.SetColumn(text, 1);
        }
        Details.Children.Add(text);
    }
}
