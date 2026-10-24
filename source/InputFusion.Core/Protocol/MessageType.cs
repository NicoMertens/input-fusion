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

    /// <summary>Client → Host: die Monitore des Clients, beim Verbinden und nach jeder Änderung.</summary>
    Monitors = 9,

    /// <summary>Client → Host: Der Cursor hat die Kante des Clients erreicht, der Host soll zurück übernehmen.</summary>
    SwitchBackRequest = 10,

    /// <summary>Host → Client: erster Schritt der Anmeldung mit dem gemeinsamen Schlüssel, siehe <c>Handshake</c>.</summary>
    Hello = 11,

    /// <summary>Client → Host: Antwort auf <see cref="Hello"/>.</summary>
    HelloResponse = 12,

    /// <summary>Host → Client: Der Client ist angemeldet; der Host weist seinerseits den Schlüssel nach.</summary>
    HelloAccept = 13,

    /// <summary>Host → Client: die Kantenabschnitte des Clients, über die die Maus zurück zum Host geht.</summary>
    ReturnEdges = 14,

    /// <summary>Host → Client: Auf dem PC wurde Win+L gedrückt; das Notebook sperrt sich mit.</summary>
    LockWorkstation = 15,

    /// <summary>Beide Richtungen nach der Anmeldung: Typ und Nutzlast verschlüsselt, siehe <c>FrameCipher</c>.</summary>
    Encrypted = 16,

    /// <summary>Einstellungen ↔ Agent über die Named Pipe, Nutzlast JSON, siehe <c>SettingsMessage</c>.</summary>
    Settings = 32,
}
