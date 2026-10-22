namespace InputFusion.Core.Protocol;

/// <summary>Typ-Byte im Frame-Kopf. Werte sind fest, damit sich das Protokoll über Versionen verträgt.</summary>
public enum MessageType : byte
{
    Heartbeat = 1,
    MouseMoveRelative = 2,
    MouseMoveAbsolute = 3,
    MouseButton = 4,
    MouseWheel = 5,
    Key = 6,
    SwitchToClient = 7,
    SwitchToHost = 8,
    Monitors = 9,

    /// <summary>Client → Host: Der Cursor hat die Kante des Clients erreicht, der Host soll zurück übernehmen.</summary>
    SwitchBackRequest = 10,
}
