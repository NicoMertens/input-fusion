using System.Windows;
using System.Windows.Controls;

namespace InputFusion.Settings;

/// <summary>
/// Schlüssel der Farben aus dem Fluent-Theme, die der Code setzt. Immer per <see cref="FrameworkElement.SetResourceReference"/>,
/// nicht als einmal gelesener Pinsel: Wechselt Windows zwischen Hell und Dunkel, ziehen sie so mit.
/// </summary>
internal static class ThemeBrushes
{
    public const String Hint = "TextFillColorSecondaryBrush";
    public const String Error = "SystemFillColorCriticalBrush";
    public const String Success = "SystemFillColorSuccessBrush";
    public const String Idle = "TextFillColorTertiaryBrush";
    public const String AccentFill = "AccentFillColorDefaultBrush";
    public const String TextOnAccent = "TextOnAccentFillColorPrimaryBrush";
    public const String SubtleFill = "SubtleFillColorSecondaryBrush";
    public const String CardBackground = "CardBackgroundFillColorDefaultBrush";
    public const String CardStroke = "CardStrokeColorDefaultBrush";

    /// <summary>Meldung unter einem Feld: Fehler rot, sonst als Hinweis.</summary>
    public static void SetMessageColor(TextBlock text, Boolean error) =>
        text.SetResourceReference(TextBlock.ForegroundProperty, error ? Error : Hint);
}
