namespace InputFusion.Core.Net;

/// <summary>
/// Feste TCP-Ports. Bewusst nicht 15100/15101 des Vorbilds, damit InputFusion neben
/// Mouse Without Borders laufen kann.
/// </summary>
public static class Ports
{
    /// <summary>Nachrichten, Maus und Tastatur.</summary>
    public const int Input = 15111;
}
