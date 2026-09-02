using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Rendering;

namespace Dvopan.Ui.Controls;

/// <summary>
/// A push button, rendered Far style as <c>[ Ok ]</c> with the hotkey letter highlighted. The
/// focused button is painted in the selected-button colours.
/// </summary>
/// <remarks>
/// Pressing the button runs <see cref="OnClick"/> and then, unless <see cref="Result"/> is
/// <see cref="DialogResult.None"/>, closes the owning dialog with that result. A button with
/// <see cref="DialogResult.None"/> and a handler is therefore a plain action button that leaves the
/// dialog open.
/// </remarks>
public sealed class ButtonControl : DialogControl
{
    /// <summary>The two frame characters plus the two spaces around the caption.</summary>
    public const int Decoration = 4;

    /// <summary>Creates a button.</summary>
    /// <param name="text">The caption; may contain one <c>'&amp;'</c> hotkey marker.</param>
    /// <param name="result">The result the dialog closes with, or <see cref="DialogResult.None"/> to stay open.</param>
    /// <param name="onClick">An optional handler run before the dialog closes.</param>
    public ButtonControl(string text, DialogResult result = DialogResult.None, Action? onClick = null)
    {
        Text = text ?? string.Empty;
        Result = result;
        OnClick = onClick;
        Bounds = new Rect(0, 0, ScreenBuffer.HotkeyTextLength(Text) + Decoration, 1);
    }

    /// <summary>The caption; may contain one <c>'&amp;'</c> hotkey marker.</summary>
    public string Text { get; set; }

    /// <summary>The result the owning dialog closes with when this button is pressed.</summary>
    public DialogResult Result { get; set; }

    /// <summary>Run when the button is pressed, before the dialog closes.</summary>
    public Action? OnClick { get; set; }

    /// <summary>Marks this button as the one Enter presses from anywhere in the dialog.</summary>
    public bool IsDefault { get; set; }

    /// <inheritdoc/>
    public override char? Hotkey => ScreenBuffer.HotkeyOf(Text);

    /// <inheritdoc/>
    public override int PreferredWidth => ScreenBuffer.HotkeyTextLength(Text) + Decoration;

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
            : palette.Button;
        var hot = !Enabled ? palette.EditDisabled
            : focused ? palette.ButtonSelectedHighlight
            : palette.ButtonHighlight;

        buffer.Fill(new Rect(r.X, r.Y, r.Width, 1), ' ', style);

        int len = ScreenBuffer.HotkeyTextLength(Text);
        int width = Math.Min(r.Width, len + Decoration);
        int x = r.X + Math.Max(0, (r.Width - width) / 2);

        buffer.Set(x, r.Y, '[', style);
        buffer.Set(x + width - 1, r.Y, ']', style);

        if (width > Decoration)
        {
            buffer.WriteHotkey(x + 2, r.Y, Text, style, hot);
        }
    }

    /// <inheritdoc/>
    public override bool HandleKey(KeyEvent key)
    {
        if (key.Is(ConsoleKey.Enter) || key.Is(ConsoleKey.Spacebar))
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

        OnClick?.Invoke();

        if (Result != DialogResult.None)
        {
            Owner?.Close(Result);
        }

        return true;
    }
}
