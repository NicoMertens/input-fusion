using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Host;

/// <summary>
/// Welche Tasten und Maustasten gerade gedrückt sind, auf dem PC und beim Client. Daraus gehen beim Wechsel die
/// gehaltenen Modifier an den Client, und beim Verlassen wird losgelassen, was dort noch gedrückt ist.
/// </summary>
internal sealed class PressedInput
{
    private const UInt16 VkL = 0x4C;
    private const UInt16 VkLeftWin = 0x5B;
    private const UInt16 VkRightWin = 0x5C;

    // Alle Tasten, die der Hook gerade als gedrückt sieht (down ohne up). Damit lässt sich beim Wechsel
    // der Modifier-Zustand des Hosts an den Client mitschicken. Wird nur auf dem Hook-Thread berührt.
    private readonly Dictionary<UInt16, KeyInputEvent> _physicalDown = new();
    // Maustasten, die der Hook gerade als gedrückt sieht, ebenfalls nur auf dem Hook-Thread.
    private readonly HashSet<Byte> _physicalButtonsDown = new();
    private Boolean _ctrlDown, _altDown;

    // Tasten, die als "down" an den Client gingen und dort noch gedrückt sind. Beim Verlassen werden sie
    // losgelassen, sonst bleiben sie auf der Gegenseite kleben. Hook-Thread und Empfang greifen zu.
    private readonly Lock _clientLock = new();
    private readonly Dictionary<UInt16, KeyEvent> _clientDown = new();
    // Dasselbe für Maustasten, unter derselben Sperre.
    private readonly HashSet<Byte> _clientButtonsDown = new();

    public Boolean CtrlAltDown => _ctrlDown && _altDown;

    /// <summary>
    /// Solange eine Maustaste gehalten wird, wechselt die Maus nicht auf den Client: Das Loslassen käme dort an, und
    /// auf dem PC bliebe die Taste gedrückt.
    /// </summary>
    public Boolean AnyButtonDown => _physicalButtonsDown.Count > 0;

    public Boolean IsLockCombination(UInt16 virtualKey) =>
        virtualKey == VkL && (_physicalDown.ContainsKey(VkLeftWin) || _physicalDown.ContainsKey(VkRightWin));

    public void Track(KeyInputEvent key)
    {
        if (key.VirtualKey is 0x11 or 0xA2 or 0xA3)
        {
            _ctrlDown = key.Down;
        }

        if (key.VirtualKey is 0x12 or 0xA4 or 0xA5)
        {
            _altDown = key.Down;
        }

        if (key.Down)
        {
            _physicalDown[key.VirtualKey] = key;
        }
        else
        {
            _physicalDown.Remove(key.VirtualKey);
        }
    }

    public void Track(MouseButtonEvent button)
    {
        if (button.Down)
        {
            _physicalButtonsDown.Add(button.Button);
        }
        else
        {
            _physicalButtonsDown.Remove(button.Button);
        }
    }

    /// <summary>
    /// Auf dem Sperrbildschirm des PCs sieht der Hook die Tasten von Win+L nicht mehr los gehen; ohne Vergessen
    /// sperrte nach dem Entsperren schon das nächste L.
    /// </summary>
    public void ForgetLockCombination()
    {
        _physicalDown.Remove(VkLeftWin);
        _physicalDown.Remove(VkRightWin);
        _physicalDown.Remove(VkL);
    }

    /// <summary>Die Taste als Nachricht an den Client, als dort gedrückt oder losgelassen vermerkt.</summary>
    public KeyEvent ForwardKey(KeyInputEvent key)
    {
        KeyEvent ev = new KeyEvent(key.VirtualKey, key.ScanCode, key.Down ? ButtonState.Down : ButtonState.Up, key.Extended);
        lock (_clientLock)
        {
            if (key.Down)
            {
                _clientDown[key.VirtualKey] = ev;
            }
            else
            {
                _clientDown.Remove(key.VirtualKey);
            }
        }
        return ev;
    }

    public MouseButton ForwardButton(MouseButtonEvent button)
    {
        lock (_clientLock)
        {
            if (button.Down)
            {
                _clientButtonsDown.Add(button.Button);
            }
            else
            {
                _clientButtonsDown.Remove(button.Button);
            }
        }
        return new MouseButton(button.Button, button.Down ? ButtonState.Down : ButtonState.Up);
    }

    /// <summary>
    /// Die Modifier, die beim Übertreten gehalten werden (z. B. Umschalt beim Ziehen über die Kante, oder das
    /// Strg+Alt des F11-Hotkeys), als dort gedrückt vermerkt; der Client bekommt sie, damit sein Zustand stimmt.
    /// </summary>
    public List<KeyEvent> ForwardHeldModifiers()
    {
        List<KeyEvent> downs = new();
        foreach ((UInt16 vk, KeyInputEvent k) in _physicalDown)
        {
            if (!IsModifier(vk))
            {
                continue;
            }

            KeyEvent down = new KeyEvent(k.VirtualKey, k.ScanCode, ButtonState.Down, k.Extended);
            lock (_clientLock)
            {
                _clientDown[vk] = down;
            }

            downs.Add(down);
        }
        return downs;
    }

    /// <summary>Alles, was beim Client noch gedrückt ist, als Loslassen; danach gilt dort nichts mehr als gedrückt.</summary>
    public (KeyEvent[] Keys, Byte[] Buttons) ReleaseOnClient()
    {
        lock (_clientLock)
        {
            KeyEvent[] ups = _clientDown.Values.Select(ev => ev with { State = ButtonState.Up }).ToArray();
            Byte[] buttons = _clientButtonsDown.ToArray();
            _clientDown.Clear();
            _clientButtonsDown.Clear();
            return (ups, buttons);
        }
    }

    private static Boolean IsModifier(UInt16 vk) => vk is
        0x10 or 0xA0 or 0xA1 or   // Umschalt (Shift)
        0x11 or 0xA2 or 0xA3 or   // Strg (Control)
        0x12 or 0xA4 or 0xA5 or   // Alt (Menu)
        0x5B or 0x5C;             // Windows
}
