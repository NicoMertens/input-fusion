namespace NC2.InputFusion.Settings;

/// <summary>
/// Wohin eine Ablehnung des Agents gehört: unter das Feld der letzten Anfrage, auch wenn die aus einem anderen Reiter
/// kam. Vor der ersten Anfrage an die Stelle, die beim Anlegen genannt wurde.
/// </summary>
internal sealed class RequestErrors(Action<String> fallback)
{
    private Action<String>? _last;

    public void Sent(Action<String> show) => _last = show;

    public void Show(String message) => (_last ?? fallback)(message);
}
