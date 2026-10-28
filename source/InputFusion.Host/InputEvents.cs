namespace NC2.InputFusion.Host;

/// <summary>Was die Hooks beobachten, bevor der Controller daraus Netzwerknachrichten macht.</summary>
public abstract record InputEvent;

/// <summary>
/// Zielposition in Bildschirmkoordinaten, wie der Hook sie liefert, und die Bewegung als Abstand zur
/// Cursorposition vor dem Ereignis. Der Abstand stimmt auch, wenn der Hook die Bewegung verschluckt.
/// </summary>
public sealed record MouseMoveEvent(Int32 X, Int32 Y, Int32 Dx, Int32 Dy) : InputEvent;

/// <summary>
/// Rohe Zählimpulse der Maus aus Raw Input, ohne Zeigerbeschleunigung. Kommt nur beim Kapern, weil
/// verschluckte Bewegungen im Hook ihre Pixel-Bruchteile verlieren (siehe Fallstricke).
/// </summary>
public sealed record RawMouseMoveEvent(Int32 Dx, Int32 Dy) : InputEvent;

public sealed record MouseButtonEvent(Byte Button, Boolean Down) : InputEvent;

public sealed record MouseWheelEvent(Int16 Delta, Boolean Horizontal) : InputEvent;

public sealed record KeyInputEvent(UInt16 VirtualKey, UInt16 ScanCode, Boolean Down, Boolean Extended) : InputEvent;
