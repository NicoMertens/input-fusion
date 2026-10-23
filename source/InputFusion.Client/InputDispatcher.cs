using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Setzt empfangene Protokoll-Nachrichten in lokale Eingaben um. Geteilt zwischen dem Durchstich-Client
/// (Phase 1, direkt über TCP, siehe <see cref="SpikeClient"/>) und dem Injector (Phase 2, über die Pipe).
/// Erkennt außerdem den Austritt an der linken Kante, damit der Host zurück übernimmt.
/// </summary>
public sealed class InputDispatcher(InputPlayer player)
{
    // Zwischen SwitchToClient und dem Austritt bzw. SwitchToHost. Nur dann wird ein Austritt gemeldet,
    // damit Bewegungen, die nach dem Austritt noch unterwegs sind, ihn nicht ein zweites Mal auslösen.
    private bool _active;

    /// <summary>Spielt die Nachricht ab. Gibt true zurück, wenn der Cursor dabei die linke Kante erreicht
    /// hat; der Aufrufer meldet dann <see cref="MessageType.SwitchBackRequest"/> an den Host.</summary>
    public bool Dispatch(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.SwitchToClient:
                _active = true;
                player.MoveAbsolute(0, SwitchToClient.FromPayload(payload).Y);
                break;
            case MessageType.SwitchToHost:
                _active = false;
                break;
            case MessageType.MouseMoveRelative:
                var move = MouseMoveRelative.FromPayload(payload);
                player.MoveRelative(move.Dx, move.Dy);
                return move.Dx < 0 && TryExit();
            case MessageType.MouseMoveAbsolute:
                var abs = MouseMoveAbsolute.FromPayload(payload);
                player.MoveAbsolute(abs.X, abs.Y);
                return TryExit();
            case MessageType.MouseButton:
                player.Button(MouseButton.FromPayload(payload));
                break;
            case MessageType.MouseWheel:
                player.Wheel(MouseWheel.FromPayload(payload));
                break;
            case MessageType.Key:
                player.Key(KeyEvent.FromPayload(payload));
                break;
        }
        return false;
    }

    /// <summary>
    /// Steht der Cursor am linken Rand des virtuellen Desktops, ist der Austritt fällig. Relativ nur bei
    /// Bewegung nach links geprüft (siehe Aufrufer): Der Eintritt setzt den Cursor genau auf den Rand,
    /// eine Bewegung nach rechts darf dort nicht sofort wieder austreten.
    /// </summary>
    private bool TryExit()
    {
        if (!_active || !PInvoke.GetCursorPos(out var cursor))
            return false;
        if (cursor.X > PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN))
            return false;
        _active = false;
        return true;
    }
}
