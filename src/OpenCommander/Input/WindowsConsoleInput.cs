using System.Diagnostics.CodeAnalysis;
using OpenCommander.Input.Native;

namespace OpenCommander.Input;

/// <summary>
/// The Windows input backend. Reads raw <c>INPUT_RECORD</c>s with <c>ReadConsoleInputW</c>, which is
/// the only way to observe a modifier key being pressed or released on its own - exactly what the
/// live key bar needs, and something <see cref="Console.ReadKey(bool)"/> structurally cannot provide
/// because it discards key-up records and filters out VK_SHIFT/VK_CONTROL/VK_MENU.
/// </summary>
/// <remarks>
/// <para>
/// The constructor switches the console input handle to raw mode:
/// <c>ENABLE_EXTENDED_FLAGS | ENABLE_MOUSE_INPUT | ENABLE_WINDOW_INPUT</c> on, and
/// <c>ENABLE_QUICK_EDIT_MODE | ENABLE_PROCESSED_INPUT | ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT |
/// ENABLE_VIRTUAL_TERMINAL_INPUT</c> off. The original mode is restored by <see cref="Dispose"/>.
/// </para>
/// <para>
/// <c>ENABLE_EXTENDED_FLAGS</c> is mandatory when clearing quick edit: without it Windows ignores the
/// quick-edit bit entirely and mouse drags keep selecting text instead of reaching the application.
/// </para>
/// <para>
/// Because <c>ENABLE_PROCESSED_INPUT</c> is cleared, Ctrl+C arrives as an ordinary key event and
/// <see cref="Console.CancelKeyPress"/> no longer fires. The application owns Ctrl+C.
/// </para>
/// </remarks>
public sealed class WindowsConsoleInput : IInputBackend
{
    /// <summary>How many records are pulled out of the console queue per native call.</summary>
    private const int RecordBatch = 64;

    /// <summary>Upper bound on how many events one auto-repeat record may expand into.</summary>
    private const int MaxRepeat = 32;

    private readonly IntPtr _handle;
    private readonly uint _savedMode;
    private readonly bool _mouseEnabled;
    private readonly INPUT_RECORD[] _records = new INPUT_RECORD[RecordBatch];
    private readonly Queue<InputEvent> _queue = new();

    private KeyMods _mods;
    private uint _prevButtons;
    private bool _disposed;

    private WindowsConsoleInput(IntPtr handle, uint savedMode, bool enableMouse)
    {
        _handle = handle;
        _savedMode = savedMode;
        _mouseEnabled = enableMouse;
    }

    /// <inheritdoc/>
    public KeyMods CurrentModifiers => _mods;

    /// <inheritdoc/>
    public bool SupportsMouse => _mouseEnabled;

    /// <inheritdoc/>
    public bool SupportsModifierTracking => true;

    /// <summary>
    /// Attempts to attach to the real Windows console and switch it to raw mode.
    /// </summary>
    /// <param name="enableMouse">Whether to request mouse reporting.</param>
    /// <param name="backend">Receives the backend on success.</param>
    /// <returns>
    /// <see langword="false"/> when the process is not running on Windows, has no console, or has its
    /// standard input redirected - the caller should then fall back to
    /// <see cref="PortableConsoleInput"/>.
    /// </returns>
    public static bool TryCreate(bool enableMouse, [NotNullWhen(true)] out WindowsConsoleInput? backend)
    {
        backend = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (Console.IsInputRedirected)
            {
                return false;
            }

            var handle = ConsoleNative.GetStdHandle(ConsoleNative.STD_INPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == ConsoleNative.InvalidHandleValue)
            {
                return false;
            }

            // GetConsoleMode failing (ERROR_INVALID_HANDLE) is the reliable "not a real console" test.
            if (!ConsoleNative.GetConsoleMode(handle, out var saved))
            {
                return false;
            }

            var mode = saved;
            mode |= ConsoleNative.ENABLE_EXTENDED_FLAGS | ConsoleNative.ENABLE_WINDOW_INPUT;
            if (enableMouse)
            {
                mode |= ConsoleNative.ENABLE_MOUSE_INPUT;
            }
            else
            {
                mode &= ~ConsoleNative.ENABLE_MOUSE_INPUT;
            }

            mode &= ~(ConsoleNative.ENABLE_PROCESSED_INPUT
                    | ConsoleNative.ENABLE_LINE_INPUT
                    | ConsoleNative.ENABLE_ECHO_INPUT
                    | ConsoleNative.ENABLE_QUICK_EDIT_MODE
                    | ConsoleNative.ENABLE_INSERT_MODE
                    | ConsoleNative.ENABLE_VIRTUAL_TERMINAL_INPUT);

            if (!ConsoleNative.SetConsoleMode(handle, mode))
            {
                return false;
            }

            // The Enter that launched the app is usually still sitting in the queue.
            ConsoleNative.FlushConsoleInputBuffer(handle);

            backend = new WindowsConsoleInput(handle, saved, enableMouse);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryRead(out InputEvent ev)
    {
        ev = InputEvent.None;
        if (_disposed)
        {
            return false;
        }

        // A batch can consist entirely of records we filter out (key-up, focus, menu), so keep
        // pumping while the console still has something for us.
        while (_queue.Count == 0 && Pump())
        {
            // Intentionally empty: Pump() enqueues.
        }

        if (_queue.Count == 0)
        {
            return false;
        }

        ev = _queue.Dequeue();
        return true;
    }

    /// <summary>
    /// Reads one batch of console records without blocking.
    /// </summary>
    /// <returns><see langword="true"/> when at least one record was read.</returns>
    private bool Pump()
    {
        if (!ConsoleNative.GetNumberOfConsoleInputEvents(_handle, out var pending) || pending == 0)
        {
            return false;
        }

        var want = Math.Min(pending, (uint)_records.Length);
        if (!ConsoleNative.ReadConsoleInput(_handle, _records, want, out var got) || got == 0)
        {
            return false;
        }

        for (var i = 0; i < got && i < _records.Length; i++)
        {
            Dispatch(ref _records[i]);
        }

        return true;
    }

    private void Dispatch(ref INPUT_RECORD r)
    {
        switch (r.EventType)
        {
            case ConsoleNative.KEY_EVENT:
                DispatchKey(ref r.KeyEvent);
                break;

            case ConsoleNative.MOUSE_EVENT:
                DispatchMouse(ref r.MouseEvent);
                break;

            case ConsoleNative.WINDOW_BUFFER_SIZE_EVENT:
                // Resizes arrive in bursts while dragging; one pending notification is enough
                // because the consumer re-queries the terminal size anyway.
                if (!QueueHasPendingResize())
                {
                    _queue.Enqueue(InputEvent.Resized());
                }

                break;

            default:
                // MENU_EVENT and FOCUS_EVENT are documented as "ignore".
                break;
        }
    }

    private void DispatchKey(ref KEY_EVENT_RECORD k)
    {
        var state = k.dwControlKeyState;
        UpdateModifiers(MapControlKeyState(state));

        int vk = k.wVirtualKeyCode;

        // Modifier and lock keys only move CurrentModifiers; they are never delivered as key presses.
        if (vk is ConsoleNative.VK_SHIFT or ConsoleNative.VK_CONTROL or ConsoleNative.VK_MENU
              or ConsoleNative.VK_CAPITAL or ConsoleNative.VK_NUMLOCK or ConsoleNative.VK_SCROLL
              or ConsoleNative.VK_LWIN or ConsoleNative.VK_RWIN)
        {
            return;
        }

        if (k.bKeyDown == 0)
        {
            return;
        }

        var ch = (char)k.UnicodeChar;

        // AltGr is reported as LeftCtrl+RightAlt. When it produced a character the keystroke is text,
        // not a Ctrl+Alt chord.
        var isAltGrText = (state & ConsoleNative.LEFT_CTRL_PRESSED) != 0
                       && (state & ConsoleNative.RIGHT_ALT_PRESSED) != 0
                       && ch != '\0';
        var mods = isAltGrText ? KeyMods.None : MapControlKeyState(state);

        var key = MapVirtualKey(vk);
        if (key == ConsoleKey.None && ch == '\0')
        {
            return;
        }

        var ev = InputEvent.FromKey(new KeyEvent(key, ch, mods));
        var repeat = k.wRepeatCount < 1 ? 1 : Math.Min((int)k.wRepeatCount, MaxRepeat);
        for (var i = 0; i < repeat; i++)
        {
            _queue.Enqueue(ev);
        }
    }

    private void DispatchMouse(ref MOUSE_EVENT_RECORD m)
    {
        var mods = MapControlKeyState(m.dwControlKeyState);
        UpdateModifiers(mods);

        if (!_mouseEnabled)
        {
            return;
        }

        int x = m.dwMousePosition.X;
        int y = m.dwMousePosition.Y;
        var buttons = m.dwButtonState & ConsoleNative.BUTTON_MASK;
        var flags = m.dwEventFlags;

        if ((flags & ConsoleNative.MOUSE_WHEELED) != 0)
        {
            // The delta lives in the HIGH word and is signed; +-120 per notch.
            var raw = (short)(m.dwButtonState >> 16);
            var notches = raw / ConsoleNative.WHEEL_DELTA;
            if (notches == 0 && raw != 0)
            {
                notches = raw > 0 ? 1 : -1;
            }

            if (notches != 0)
            {
                _queue.Enqueue(InputEvent.FromMouse(
                    new MouseEvent(MouseKind.Wheel, x, y, MouseButton.None, notches, mods)));
            }

            _prevButtons = buttons;
            return;
        }

        if ((flags & ConsoleNative.MOUSE_HWHEELED) != 0)
        {
            // Horizontal wheel: nothing in the UI consumes it.
            _prevButtons = buttons;
            return;
        }

        if ((flags & ConsoleNative.DOUBLE_CLICK) != 0)
        {
            _queue.Enqueue(InputEvent.FromMouse(
                new MouseEvent(MouseKind.DoubleClick, x, y, ToButton(buttons), 0, mods)));
            _prevButtons = buttons;
            return;
        }

        if ((flags & ConsoleNative.MOUSE_MOVED) != 0)
        {
            // Button carries the button held during a drag, or None for a bare move.
            _queue.Enqueue(InputEvent.FromMouse(
                new MouseEvent(MouseKind.Move, x, y, ToButton(buttons), 0, mods)));
            _prevButtons = buttons;
            return;
        }

        // flags == 0: a press or a release. dwButtonState is the CURRENT set, not the transition,
        // so diff it against what we saw last time to find out which button changed and how.
        var changed = buttons ^ _prevButtons;
        _prevButtons = buttons;
        if (changed == 0)
        {
            return;
        }

        EmitTransition(changed, buttons, ConsoleNative.FROM_LEFT_1ST_BUTTON_PRESSED, MouseButton.Left, x, y, mods);
        EmitTransition(changed, buttons, ConsoleNative.RIGHTMOST_BUTTON_PRESSED, MouseButton.Right, x, y, mods);
        EmitTransition(changed, buttons, ConsoleNative.FROM_LEFT_2ND_BUTTON_PRESSED, MouseButton.Middle, x, y, mods);
    }

    private void EmitTransition(uint changed, uint buttons, uint mask, MouseButton button, int x, int y, KeyMods mods)
    {
        if ((changed & mask) == 0)
        {
            return;
        }

        var kind = (buttons & mask) != 0 ? MouseKind.Down : MouseKind.Up;
        _queue.Enqueue(InputEvent.FromMouse(new MouseEvent(kind, x, y, button, 0, mods)));
    }

    private void UpdateModifiers(KeyMods mods)
    {
        if (mods == _mods)
        {
            return;
        }

        _mods = mods;
        _queue.Enqueue(InputEvent.ModifiersChangedTo(mods));
    }

    private bool QueueHasPendingResize()
    {
        if (_queue.Count == 0)
        {
            return false;
        }

        foreach (var e in _queue)
        {
            if (e.Kind == InputKind.Resize)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps a Win32 <c>dwControlKeyState</c> bit set to <see cref="KeyMods"/>, folding the left and
    /// right variants of each modifier together.
    /// </summary>
    /// <param name="controlKeyState">The raw <c>dwControlKeyState</c> value.</param>
    /// <returns>The modifier set.</returns>
    public static KeyMods MapControlKeyState(uint controlKeyState)
    {
        var mods = KeyMods.None;
        if ((controlKeyState & ConsoleNative.SHIFT_PRESSED) != 0)
        {
            mods |= KeyMods.Shift;
        }

        if ((controlKeyState & (ConsoleNative.LEFT_CTRL_PRESSED | ConsoleNative.RIGHT_CTRL_PRESSED)) != 0)
        {
            mods |= KeyMods.Ctrl;
        }

        if ((controlKeyState & (ConsoleNative.LEFT_ALT_PRESSED | ConsoleNative.RIGHT_ALT_PRESSED)) != 0)
        {
            mods |= KeyMods.Alt;
        }

        return mods;
    }

    /// <summary>
    /// Maps a Win32 virtual key code to a <see cref="ConsoleKey"/>.
    /// </summary>
    /// <param name="virtualKey">The <c>wVirtualKeyCode</c> from a key record.</param>
    /// <returns>
    /// The console key, or <see cref="ConsoleKey.None"/> for codes with no <see cref="ConsoleKey"/>
    /// member (the modifier and lock keys, IME keys, injected input).
    /// </returns>
    /// <remarks>
    /// <see cref="ConsoleKey"/> values are deliberately identical to the Win32 VK codes over the
    /// overlapping range, so most of this is an identity map - but it must be an explicit allow-list,
    /// because casting an unknown VK straight to the enum produces an undefined value that then
    /// breaks every downstream <c>switch</c>.
    /// </remarks>
    public static ConsoleKey MapVirtualKey(int virtualKey)
    {
        // Identity ranges.
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ConsoleKey.D0 + (virtualKey - 0x30);
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ConsoleKey.A + (virtualKey - 0x41);
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return ConsoleKey.NumPad0 + (virtualKey - 0x60);
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return ConsoleKey.F1 + (virtualKey - 0x70);
        }

        return virtualKey switch
        {
            0x08 => ConsoleKey.Backspace,
            0x09 => ConsoleKey.Tab,
            0x0C => ConsoleKey.Clear,
            0x0D => ConsoleKey.Enter,
            0x13 => ConsoleKey.Pause,
            0x1B => ConsoleKey.Escape,
            0x20 => ConsoleKey.Spacebar,
            0x21 => ConsoleKey.PageUp,
            0x22 => ConsoleKey.PageDown,
            0x23 => ConsoleKey.End,
            0x24 => ConsoleKey.Home,
            0x25 => ConsoleKey.LeftArrow,
            0x26 => ConsoleKey.UpArrow,
            0x27 => ConsoleKey.RightArrow,
            0x28 => ConsoleKey.DownArrow,
            0x29 => ConsoleKey.Select,
            0x2A => ConsoleKey.Print,
            0x2B => ConsoleKey.Execute,
            0x2C => ConsoleKey.PrintScreen,
            0x2D => ConsoleKey.Insert,
            0x2E => ConsoleKey.Delete,
            0x2F => ConsoleKey.Help,
            0x5B => ConsoleKey.LeftWindows,
            0x5C => ConsoleKey.RightWindows,
            0x5D => ConsoleKey.Applications,
            0x5F => ConsoleKey.Sleep,
            0x6A => ConsoleKey.Multiply,
            0x6B => ConsoleKey.Add,
            0x6C => ConsoleKey.Separator,
            0x6D => ConsoleKey.Subtract,
            0x6E => ConsoleKey.Decimal,
            0x6F => ConsoleKey.Divide,
            0xA6 => ConsoleKey.BrowserBack,
            0xA7 => ConsoleKey.BrowserForward,
            0xA8 => ConsoleKey.BrowserRefresh,
            0xA9 => ConsoleKey.BrowserStop,
            0xAA => ConsoleKey.BrowserSearch,
            0xAB => ConsoleKey.BrowserFavorites,
            0xAC => ConsoleKey.BrowserHome,
            0xAD => ConsoleKey.VolumeMute,
            0xAE => ConsoleKey.VolumeDown,
            0xAF => ConsoleKey.VolumeUp,
            0xB0 => ConsoleKey.MediaNext,
            0xB1 => ConsoleKey.MediaPrevious,
            0xB2 => ConsoleKey.MediaStop,
            0xB3 => ConsoleKey.MediaPlay,
            0xB4 => ConsoleKey.LaunchMail,
            0xB5 => ConsoleKey.LaunchMediaSelect,
            0xB6 => ConsoleKey.LaunchApp1,
            0xB7 => ConsoleKey.LaunchApp2,
            0xBA => ConsoleKey.Oem1,
            0xBB => ConsoleKey.OemPlus,
            0xBC => ConsoleKey.OemComma,
            0xBD => ConsoleKey.OemMinus,
            0xBE => ConsoleKey.OemPeriod,
            0xBF => ConsoleKey.Oem2,
            0xC0 => ConsoleKey.Oem3,
            0xDB => ConsoleKey.Oem4,
            0xDC => ConsoleKey.Oem5,
            0xDD => ConsoleKey.Oem6,
            0xDE => ConsoleKey.Oem7,
            0xDF => ConsoleKey.Oem8,
            0xE2 => ConsoleKey.Oem102,
            0xF6 => ConsoleKey.Attention,
            0xF7 => ConsoleKey.CrSel,
            0xF8 => ConsoleKey.ExSel,
            0xF9 => ConsoleKey.EraseEndOfFile,
            0xFA => ConsoleKey.Play,
            0xFB => ConsoleKey.Zoom,
            0xFC => ConsoleKey.NoName,
            0xFD => ConsoleKey.Pa1,
            0xFE => ConsoleKey.OemClear,
            _ => ConsoleKey.None,
        };
    }

    /// <summary>
    /// Picks the reported button from a Win32 button bit set, preferring left over right over middle.
    /// </summary>
    /// <param name="buttons">The low word of <c>dwButtonState</c>.</param>
    /// <returns>The button, or <see cref="MouseButton.None"/> when nothing is pressed.</returns>
    public static MouseButton ToButton(uint buttons)
    {
        if ((buttons & ConsoleNative.FROM_LEFT_1ST_BUTTON_PRESSED) != 0)
        {
            return MouseButton.Left;
        }

        if ((buttons & ConsoleNative.RIGHTMOST_BUTTON_PRESSED) != 0)
        {
            return MouseButton.Right;
        }

        if ((buttons & ConsoleNative.FROM_LEFT_2ND_BUTTON_PRESSED) != 0)
        {
            return MouseButton.Middle;
        }

        return MouseButton.None;
    }

    /// <summary>Restores the console input mode captured at construction. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Clear();
        try
        {
            ConsoleNative.SetConsoleMode(_handle, _savedMode);
        }
        catch
        {
            // Shutdown must never throw: the console may already be gone.
        }
    }
}
