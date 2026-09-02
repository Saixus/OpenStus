namespace OpenStus.Input;

/// <summary>The kind of mouse activity an event describes.</summary>
public enum MouseKind
{
    /// <summary>A button transitioned from released to pressed.</summary>
    Down,

    /// <summary>A button transitioned from pressed to released.</summary>
    Up,

    /// <summary>The pointer moved. <see cref="MouseEvent.Button"/> carries the button held during a drag.</summary>
    Move,

    /// <summary>The vertical wheel was rotated; see <see cref="MouseEvent.Wheel"/>.</summary>
    Wheel,

    /// <summary>A double click was reported by the console.</summary>
    DoubleClick,
}

/// <summary>Which mouse button an event refers to.</summary>
public enum MouseButton
{
    /// <summary>No button (a plain move, or a wheel rotation with no button held).</summary>
    None,

    /// <summary>The left (primary) button.</summary>
    Left,

    /// <summary>The right (secondary) button.</summary>
    Right,

    /// <summary>The middle button.</summary>
    Middle,
}

/// <summary>
/// A mouse event in console cell coordinates.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="X">Zero-based column of the cell under the pointer.</param>
/// <param name="Y">Zero-based row of the cell under the pointer.</param>
/// <param name="Button">The button involved, or <see cref="MouseButton.None"/>.</param>
/// <param name="Wheel">
/// Wheel notches: positive is a scroll away from the user (content scrolls up), negative is towards
/// the user. Always zero unless <paramref name="Kind"/> is <see cref="MouseKind.Wheel"/>.
/// </param>
/// <param name="Mods">The modifiers held while the event was produced.</param>
/// <remarks>
/// Coordinates are screen-buffer relative. In the alternate screen buffer (which the terminal always
/// switches to) the buffer and the window coincide, so these are also viewport coordinates and can
/// be compared directly against <c>Rect</c> values from the rendering layer.
/// </remarks>
public readonly record struct MouseEvent(
    MouseKind Kind,
    int X,
    int Y,
    MouseButton Button,
    int Wheel,
    KeyMods Mods)
{
    /// <summary><see langword="true"/> when this is a press or a double click of any button.</summary>
    public bool IsPress => Kind is MouseKind.Down or MouseKind.DoubleClick;

    /// <inheritdoc/>
    public override string ToString() =>
        Kind == MouseKind.Wheel
            ? $"Wheel {Wheel:+0;-0;0} @ {X},{Y}"
            : $"{Kind} {Button} @ {X},{Y}";
}
