namespace OpenCommander.Core;

/// <summary>
/// One entry of a horizontal (F9) or popup menu.
/// </summary>
/// <remarks>
/// <see cref="Text"/> may carry a <c>'&amp;'</c> hotkey marker, exactly as
/// <see cref="Rendering.ScreenBuffer.WriteHotkey"/> understands it: the character after the marker is
/// the hotkey, and <c>"&amp;&amp;"</c> is a literal ampersand.
/// </remarks>
public sealed class MenuItem
{
    /// <summary>Creates an empty item.</summary>
    public MenuItem()
    {
    }

    /// <summary>Creates an item with a caption and, optionally, an accelerator hint and an action.</summary>
    /// <param name="text">The caption, possibly containing a <c>'&amp;'</c> hotkey marker.</param>
    /// <param name="rightText">The right-aligned accelerator hint, e.g. <c>"Ctrl+F3"</c>.</param>
    /// <param name="action">What to run when the item is chosen.</param>
    public MenuItem(string text, string? rightText = null, Action? action = null)
    {
        Text = text;
        RightText = rightText;
        Action = action;
    }

    /// <summary>The caption; may contain <c>'&amp;'</c> hotkey markers.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Right-aligned accelerator hint, for example <c>"Ctrl+F3"</c>.</summary>
    public string? RightText { get; set; }

    /// <summary>When set, the item is drawn as a horizontal rule and cannot be selected.</summary>
    public bool IsSeparator { get; set; }

    /// <summary>When clear, the item is drawn greyed out and cannot be chosen.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When set, the item is drawn with a check mark.</summary>
    public bool Checked { get; set; }

    /// <summary>Arbitrary caller data; the menu never looks at it.</summary>
    public object? Tag { get; set; }

    /// <summary>Invoked when the item is chosen. May be <see langword="null"/>.</summary>
    public Action? Action { get; set; }

    /// <summary>The sub-menu opened by this item, or <see langword="null"/> for a leaf.</summary>
    public IReadOnlyList<MenuItem>? SubItems { get; set; }

    /// <summary><see langword="true"/> when the item can be moved to and chosen.</summary>
    public bool IsSelectable => !IsSeparator && Enabled;

    /// <summary>The hotkey character, lowercased, or <see langword="null"/> when the caption has none.</summary>
    public char? Hotkey => Rendering.ScreenBuffer.HotkeyOf(Text);

    /// <summary>The display width of <see cref="Text"/>, ignoring the hotkey markers.</summary>
    public int TextLength => Rendering.ScreenBuffer.HotkeyTextLength(Text);

    /// <summary>Creates a separator item.</summary>
    /// <returns>A new, non-selectable separator.</returns>
    public static MenuItem Separator() => new() { IsSeparator = true };

    /// <inheritdoc/>
    public override string ToString() => IsSeparator ? "---" : Text;
}
