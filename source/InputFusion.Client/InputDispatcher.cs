using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Setzt empfangene Protokoll-Nachrichten in lokale Eingaben um. Geteilt zwischen dem Durchstich-Client
/// (Phase 1, direkt über TCP, siehe <see cref="SpikeClient"/>) und dem Injector (Phase 2, über die Pipe).
/// </summary>
public sealed class InputDispatcher(InputPlayer player)
{
    public void Dispatch(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.SwitchToClient:
                player.MoveAbsolute(0, SwitchToClient.FromPayload(payload).Y);
                break;
            case MessageType.MouseMoveRelative:
                var move = MouseMoveRelative.FromPayload(payload);
                player.MoveRelative(move.Dx, move.Dy);
                break;
            case MessageType.MouseMoveAbsolute:
                var abs = MouseMoveAbsolute.FromPayload(payload);
                player.MoveAbsolute(abs.X, abs.Y);
                break;
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
    }
}
