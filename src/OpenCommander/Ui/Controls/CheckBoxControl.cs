using OpenCommander.Input;
using OpenCommander.Rendering;

namespace OpenCommander.Ui.Controls;

/// <summary>
/// A two-state check box drawn as <c>[x] Caption</c>, toggled with Space, Enter, a click or its
/// Alt+hotkey.
/// </summary>
public sealed class CheckBoxControl : DialogControl
{
    /// <summary>The three box characters plus the separating space.</summary>
    public const int Decoration = 4;

    /// <summary>Creates a check box.</summary>
    /// <param name="text">The caption; may contain one <c>'&amp;'</c> hotkey marker.</param>
    /// <param name="checked">The initial state.</param>
    public CheckBoxControl(string text, bool @checked = false)
    {
        Text = text ?? string.Empty;
        Checked = @checked;
        Bounds = new Rect(0, 0, ScreenBuffer.HotkeyTextLength(Text) + Decoration, 1);
    }

    /// <summary>The caption; may contain one <c>'&amp;'</c> hotkey marker.</summary>
    public string Text { get; set; }

    /// <summary>The current state.</summary>
    public bool Checked { get; set; }

    /// <summary>The glyph drawn inside the brackets when checked.</summary>
    public char CheckGlyph { get; set; } = 'x';

    /// <summary>Raised whenever the state changes.</summary>
    public Action<bool>? CheckedChanged { get; set; }

    /// <inheritdoc/>
    public override char? Hotkey => ScreenBuffer.HotkeyOf(Text);

    /// <inheritdoc/>
    public override int PreferredWidth => ScreenBuffer.HotkeyTextLength(Text) + Decoration;

    /// <inheritdoc/>
    public override bool WantsCursor => HasFocus && Enabled;

    /// <inheritdoc/>
    public override (int X, int Y) CursorOffset => (1, 0);

    /// <inheritdoc/>
    public override void Draw(ScreenBuffer buffer, Rect client, DialogPalette palette)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(palette);

        var r = ScreenBounds(client);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        bool focused = HasFocus;
        var style = !Enabled ? palette.EditDisabled
            : focused ? palette.ButtonSelected
            : palette.Text;
        var hot = !Enabled ? palette.EditDisabled
            : focused ? palette.ButtonSelectedHighlight
            : palette.Highlight;

        buffer.Fill(new Rect(r.X, r.Y, r.Width, 1), ' ', style);
        buffer.Set(r.X, r.Y, '[', style);
        buffer.Set(r.X + 1, r.Y, Checked ? CheckGlyph : ' ', style);
        buffer.Set(r.X + 2, r.Y, ']', style);

        if (r.Width > Decoration)
        {
            buffer.WriteHotkey(r.X + 4, r.Y, Text, style, hot);
        }
    }

    /// <inheritdoc/>
    public override bool HandleKey(KeyEvent key)
    {
        if (key.Is(ConsoleKey.Spacebar) || key.Is(ConsoleKey.Enter))
        {
            Activate();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse, Rect client)
    {
        if (!mouse.IsPress || mouse.Button != MouseButton.Left)
        {
            return false;
        }

        Activate();
        return true;
    }

    /// <inheritdoc/>
    public override bool Activate()
    {
        if (!Enabled || !Visible)
        {
            return false;
        }

        Checked = !Checked;
        CheckedChanged?.Invoke(Checked);
        return true;
    }
}
