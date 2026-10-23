namespace InputFusion.Host;

/// <summary>Was die Hooks beobachten, bevor der Controller daraus Netzwerknachrichten macht.</summary>
public abstract record InputEvent;

/// <summary>
/// Zielposition in Bildschirmkoordinaten, wie der Hook sie liefert, und die Bewegung als Abstand zur
/// Cursorposition vor dem Ereignis. Der Abstand stimmt auch, wenn der Hook die Bewegung verschluckt.
/// </summary>
public sealed record MouseMoveEvent(int X, int Y, int Dx, int Dy) : InputEvent;

/// <summary>
/// Rohe Zählimpulse der Maus aus Raw Input, ohne Zeigerbeschleunigung. Kommt nur beim Kapern, weil
/// verschluckte Bewegungen im Hook ihre Pixel-Bruchteile verlieren (siehe Fallstricke).
/// </summary>
public sealed record RawMouseMoveEvent(int Dx, int Dy) : InputEvent;

public sealed record MouseButtonEvent(byte Button, bool Down) : InputEvent;

public sealed record MouseWheelEvent(short Delta, bool Horizontal) : InputEvent;

public sealed record KeyInputEvent(ushort VirtualKey, ushort ScanCode, bool Down, bool Extended) : InputEvent;
