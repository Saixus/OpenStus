namespace Dvopan.Input;

/// <summary>
/// A single key press: the translated <see cref="ConsoleKey"/>, the character it produced (when any)
/// and the modifier set that was held down.
/// </summary>
/// <param name="Key">
/// The virtual key, or <see cref="ConsoleKey.None"/> when the record carried only a character
/// (IME commit, injected input, dead-key composition).
/// </param>
/// <param name="Ch">The character produced by the key, or <c>'\0'</c> when the key produced none.</param>
/// <param name="Mods">The modifiers held down while the key was pressed.</param>
public readonly record struct KeyEvent(ConsoleKey Key, char Ch, KeyMods Mods)
{
    /// <summary>An empty key event (no key, no character, no modifiers).</summary>
    public static KeyEvent None => default;

    /// <summary>
    /// Tests the key against <paramref name="key"/> with an <em>exact</em> modifier match, so
    /// <c>Is(ConsoleKey.Enter)</c> is false when Shift is held.
    /// </summary>
    /// <param name="key">The key to compare against.</param>
    /// <param name="mods">The exact modifier set that must be held; defaults to none.</param>
    /// <returns><see langword="true"/> when both the key and the whole modifier set match.</returns>
    public bool Is(ConsoleKey key, KeyMods mods = KeyMods.None) => Key == key && Mods == mods;

    /// <summary>
    /// Tests the key against <paramref name="key"/> while ignoring the Shift flag. Useful for keys
    /// whose shifted form is irrelevant (for example the gray keypad keys on some layouts).
    /// </summary>
    /// <param name="key">The key to compare against.</param>
    /// <param name="mods">The Ctrl/Alt combination that must be held.</param>
    /// <returns><see langword="true"/> when the key and the Ctrl/Alt part of the modifiers match.</returns>
    public bool IsIgnoringShift(ConsoleKey key, KeyMods mods = KeyMods.None) =>
        Key == key && (Mods & ~KeyMods.Shift) == (mods & ~KeyMods.Shift);

    /// <summary>
    /// <see langword="true"/> when this event carries a printable character that should be inserted
    /// into an edit control: a non-control <see cref="Ch"/> with neither Ctrl nor Alt held.
    /// </summary>
    public bool IsPlainChar =>
        Ch != '\0' && !char.IsControl(Ch) && (Mods & (KeyMods.Ctrl | KeyMods.Alt)) == 0;

    /// <summary><see langword="true"/> when any modifier at all is held.</summary>
    public bool HasMods => Mods != KeyMods.None;

    /// <summary>
    /// Renders the key in the canonical key notation used by the key binding table and the help
    /// screen, for example <c>"Ctrl+Alt+F5"</c>, <c>"Shift+Ins"</c>, <c>"Gray+"</c>, <c>"Enter"</c>
    /// or <c>"A"</c>. Modifiers are always emitted in Ctrl, Alt, Shift order.
    /// </summary>
    /// <returns>The display string; never <see langword="null"/>.</returns>
    public string ToDisplayString() => KeyNames.Format(Key, Ch, Mods);

    /// <inheritdoc/>
    public override string ToString() => ToDisplayString();
}
