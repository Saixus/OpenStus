namespace OpenCommander.Input;

/// <summary>Discriminates the payload carried by an <see cref="InputEvent"/>.</summary>
public enum InputKind
{
    /// <summary>No event (the value of a default-constructed <see cref="InputEvent"/>).</summary>
    None,

    /// <summary>A key press; read <see cref="InputEvent.Key"/>.</summary>
    Key,

    /// <summary>A mouse event; read <see cref="InputEvent.Mouse"/>.</summary>
    Mouse,

    /// <summary>The console was resized; re-query the terminal size and repaint fully.</summary>
    Resize,

    /// <summary>
    /// The held modifier set changed with no key press attached; read
    /// <see cref="InputEvent.Modifiers"/>. This is what makes the key bar update live while Shift,
    /// Ctrl or Alt is merely held down.
    /// </summary>
    ModifiersChanged,
}

/// <summary>
/// A tagged union of everything an <see cref="IInputBackend"/> can report.
/// </summary>
public readonly struct InputEvent
{
    private readonly KeyEvent _key;
    private readonly MouseEvent _mouse;
    private readonly KeyMods _mods;

    private InputEvent(InputKind kind, KeyEvent key, MouseEvent mouse, KeyMods mods)
    {
        Kind = kind;
        _key = key;
        _mouse = mouse;
        _mods = mods;
    }

    /// <summary>Which payload this event carries.</summary>
    public InputKind Kind { get; }

    /// <summary>The key press. Only meaningful when <see cref="Kind"/> is <see cref="InputKind.Key"/>.</summary>
    public KeyEvent Key => _key;

    /// <summary>The mouse event. Only meaningful when <see cref="Kind"/> is <see cref="InputKind.Mouse"/>.</summary>
    public MouseEvent Mouse => _mouse;

    /// <summary>
    /// The modifier set. Valid for <see cref="InputKind.ModifiersChanged"/>; for a key or mouse
    /// event it mirrors that event's own modifiers.
    /// </summary>
    public KeyMods Modifiers => _mods;

    /// <summary><see langword="true"/> when this is the default, empty event.</summary>
    public bool IsNone => Kind == InputKind.None;

    /// <summary>The empty event.</summary>
    public static InputEvent None => default;

    /// <summary>Wraps a key press.</summary>
    /// <param name="k">The key press.</param>
    /// <returns>A <see cref="InputKind.Key"/> event.</returns>
    public static InputEvent FromKey(KeyEvent k) => new(InputKind.Key, k, default, k.Mods);

    /// <summary>Wraps a mouse event.</summary>
    /// <param name="m">The mouse event.</param>
    /// <returns>A <see cref="InputKind.Mouse"/> event.</returns>
    public static InputEvent FromMouse(MouseEvent m) => new(InputKind.Mouse, default, m, m.Mods);

    /// <summary>Creates a console resize notification.</summary>
    /// <returns>A <see cref="InputKind.Resize"/> event.</returns>
    public static InputEvent Resized() => new(InputKind.Resize, default, default, KeyMods.None);

    /// <summary>Creates a live modifier-state change notification.</summary>
    /// <param name="m">The new modifier set.</param>
    /// <returns>A <see cref="InputKind.ModifiersChanged"/> event.</returns>
    public static InputEvent ModifiersChangedTo(KeyMods m) =>
        new(InputKind.ModifiersChanged, default, default, m);

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        InputKind.Key => $"Key({_key.ToDisplayString()})",
        InputKind.Mouse => $"Mouse({_mouse})",
        InputKind.Resize => "Resize",
        InputKind.ModifiersChanged => $"Mods({_mods})",
        _ => "None",
    };
}
