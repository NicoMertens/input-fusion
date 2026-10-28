using Windows.Win32;
using NC2.InputFusion.Core.Layout;
using NC2.InputFusion.Core.Protocol;
using System.Drawing;

namespace NC2.InputFusion.Client;

/// <summary>
/// Setzt empfangene Protokoll-Nachrichten in lokale Eingaben um. Geteilt zwischen dem Durchstich-Client
/// (Phase 1, direkt über TCP, siehe <see cref="SpikeClient"/>) und dem Injector (Phase 2, über die Pipe).
/// Erkennt außerdem den Austritt über eine der Rückkehrkanten, die der Host aus dem Layout schickt.
/// </summary>
public sealed class InputDispatcher(InputPlayer player)
{
    // Zwischen SwitchToClient und dem Austritt bzw. SwitchToHost. Nur dann wird ein Austritt gemeldet,
    // damit Bewegungen, die nach dem Austritt noch unterwegs sind, ihn nicht ein zweites Mal auslösen.
    private Boolean _active;
    private ReturnEdge[] _returnEdges = [];

    /// <summary>Spielt die Nachricht ab. Erreicht der Cursor dabei eine Rückkehrkante, kommt der Austritt zurück;
    /// der Aufrufer meldet ihn dann als <see cref="MessageType.SwitchBackRequest"/> an den Host.</summary>
    public SwitchBackRequest? Dispatch(MessageType type, Byte[] payload)
    {
        switch (type)
        {
            case MessageType.ReturnEdges:
                _returnEdges = ReturnEdgeList.FromPayload(payload);
                break;
            case MessageType.SwitchToClient:
                _active = true;
                SwitchToClient entry = SwitchToClient.FromPayload(payload);
                player.SetCursor(entry.X, entry.Y);
                break;
            case MessageType.SwitchToHost:
                _active = false;
                break;
            case MessageType.LockWorkstation:
                // Eingespieltes Win+L sperrt nicht, Windows ignoriert es; LockWorkStation schon, auch als SYSTEM in der Sitzung.
                _active = false;
                player.ReleaseAll();
                PInvoke.LockWorkStation();
                break;
            case MessageType.MouseMoveRelative:
                MouseMoveRelative move = MouseMoveRelative.FromPayload(payload);
                player.MoveRelative(move.Dx, move.Dy);
                return TryExit(move.Dx, move.Dy);
            case MessageType.MouseMoveAbsolute:
                MouseMoveAbsolute abs = MouseMoveAbsolute.FromPayload(payload);
                PInvoke.GetCursorPos(out Point before);
                player.MoveAbsolute(abs.X, abs.Y);
                PInvoke.GetCursorPos(out Point after);
                return TryExit(after.X - before.X, after.Y - before.Y);
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
        return null;
    }

    /// <summary>Windows hält den Cursor an der Monitorkante fest; steht er auf einer Rückkehrkante und drückt nach
    /// außen, ist der Austritt fällig.</summary>
    private SwitchBackRequest? TryExit(Int32 dx, Int32 dy)
    {
        if (!_active || !PInvoke.GetCursorPos(out Point cursor))
        {
            return null;
        }

        if (ReturnEdge.Find(_returnEdges, cursor.X, cursor.Y, dx, dy) is not { } edge)
        {
            return null;
        }

        _active = false;
        return new SwitchBackRequest(edge.Side, cursor.X, cursor.Y);
    }
}
