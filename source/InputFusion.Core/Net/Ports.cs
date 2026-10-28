using System.Globalization;

namespace NC2.InputFusion.Core.Net;

/// <summary>
/// TCP-Port zwischen Host und Client. Bewusst nicht 15100/15101 des Vorbilds, damit InputFusion neben
/// Mouse Without Borders laufen kann. Host und Client lesen ihn aus ihrer Konfiguration; beide müssen gleich sein.
/// </summary>
public static class Ports
{
    /// <summary>Standard für Nachrichten, Maus und Tastatur, wenn die Konfiguration keinen Port nennt.</summary>
    public const Int32 Input = 15111;

    /// <summary>Null, wenn der Port taugt, sonst der Grund.</summary>
    public static String? Validate(Int32 port) =>
        port is >= 1 and <= 65535 ? null : $"Der Port muss zwischen 1 und 65535 liegen, nicht {port}.";

    /// <summary>Für Eingaben als Text, etwa im Setup oder auf der Kommandozeile.</summary>
    public static String? Validate(String text, out Int32 port)
    {
        if (!Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port))
        {
            return $"„{text}“ ist keine Portnummer.";
        }

        return Validate(port);
    }
}
