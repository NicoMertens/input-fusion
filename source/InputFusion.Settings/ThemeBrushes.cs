using System.Windows;
using System.Windows.Controls;

namespace InputFusion.Settings;

/// <summary>
/// Schlüssel der Farben aus dem Fluent-Theme, die der Code setzt. Immer per <see cref="FrameworkElement.SetResourceReference"/>,
/// nicht als einmal gelesener Pinsel: Wechselt Windows zwischen Hell und Dunkel, ziehen sie so mit.
/// </summary>
internal static class ThemeBrushes
{
    public const string Hint = "TextFillColorSecondaryBrush";
    public const string Error = "SystemFillColorCriticalBrush";
    public const string Success = "SystemFillColorSuccessBrush";
    public const string Idle = "TextFillColorTertiaryBrush";
    public const string AccentFill = "AccentFillColorDefaultBrush";
    public const string TextOnAccent = "TextOnAccentFillColorPrimaryBrush";
    public const string SubtleFill = "SubtleFillColorSecondaryBrush";
    public const string CardBackground = "CardBackgroundFillColorDefaultBrush";
    public const string CardStroke = "CardStrokeColorDefaultBrush";

    /// <summary>Meldung unter einem Feld: Fehler rot, sonst als Hinweis.</summary>
    public static void SetMessageColor(TextBlock text, bool error) =>
        text.SetResourceReference(TextBlock.ForegroundProperty, error ? Error : Hint);
}
