namespace InputFusion.Host;

/// <summary>Was die Hooks beobachten, bevor der Controller daraus Netzwerknachrichten macht.</summary>
public abstract record InputEvent;

/// <summary>Absolute Cursorposition in Bildschirmkoordinaten, wie der Hook sie liefert.</summary>
public sealed record MouseMoveEvent(int X, int Y) : InputEvent;

public sealed record MouseButtonEvent(byte Button, bool Down) : InputEvent;

public sealed record MouseWheelEvent(short Delta, bool Horizontal) : InputEvent;

public sealed record KeyInputEvent(ushort VirtualKey, ushort ScanCode, bool Down, bool Extended) : InputEvent;
