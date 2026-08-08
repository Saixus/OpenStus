using OpenCommander.Rendering;

namespace OpenCommander.Ui.Controls;

/// <summary>
/// A static caption. Never takes the focus; when it declares a <c>'&amp;'</c> hotkey and is wired to
/// a <see cref="LinkedControl"/>, Alt+hotkey moves the focus there - the classic "label for a field"
/// pattern.
/// </summary>
public sealed class LabelControl : DialogControl
{
    /// <summary>Creates a label.</summary>
    /// <param name="text">The caption; may contain one <c>'&amp;'</c> hotkey marker.</param>
    /// <param name="align">How the caption sits inside <see cref="DialogControl.Bounds"/>.</param>
    public LabelControl(string text, HAlign align = HAlign.Left)
    {
        Text = text ?? string.Empty;
        Align = align;
    }

    /// <summary>The caption; may contain one <c>'&amp;'</c> hotkey marker.</summary>
    public string Text { get; set; }

    /// <summary>How the caption sits inside the control rectangle.</summary>
    public HAlign Align { get; set; }

    /// <summary>The control Alt+hotkey should move the focus to.</summary>
    public DialogControl? LinkedControl { get; set; }

    /// <summary>
    /// When clear, <c>'&amp;'</c> is drawn literally and the label declares no hotkey. Message box
    /// body lines use this so an ampersand in a file name cannot swallow a character or hijack a
    /// keystroke.
    /// </summary>
    public bool ParseHotkey { get; set; } = true;

    /// <summary>Labels are never focusable.</summary>
    public override bool CanFocus => false;

    /// <inheritdoc/>
    public override char? Hotkey => ParseHotkey ? ScreenBuffer.HotkeyOf(Text) : null;

    /// <inheritdoc/>
    public override int PreferredWidth =>
        ParseHotkey ? ScreenBuffer.HotkeyTextLength(Text) : Text.Length;

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

        var style = Enabled ? palette.Text : palette.EditDisabled;
        var hot = Enabled ? palette.Highlight : palette.EditDisabled;

        if (!ParseHotkey)
        {
            buffer.WriteFixed(r.X, r.Y, r.Width, Text, style, Align);
            return;
        }

        int len = ScreenBuffer.HotkeyTextLength(Text);
        int offset = Align switch
        {
            HAlign.Right => Math.Max(0, r.Width - len),
            HAlign.Center => Math.Max(0, (r.Width - len) / 2),
            _ => 0,
        };

        buffer.Fill(new Rect(r.X, r.Y, r.Width, 1), ' ', style);

        if (len <= r.Width)
        {
            buffer.WriteHotkey(r.X + offset, r.Y, Text, style, hot);
        }
        else
        {
            // Too long for the field: drop the markers and let WriteFixed put in the ellipsis.
            buffer.WriteFixed(r.X, r.Y, r.Width, StripMarkers(Text), style);
        }
    }

    /// <inheritdoc/>
    public override bool Activate()
    {
        if (LinkedControl is null || Owner is null)
        {
            return false;
        }

        return Owner.SetFocus(LinkedControl) || LinkedControl.Activate();
    }

    /// <summary>Removes the <c>'&amp;'</c> hotkey markers from a caption.</summary>
    /// <param name="text">The marked caption.</param>
    /// <returns>The plain display text.</returns>
    public static string StripMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('&', StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                sb.Append(text[i]);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                sb.Append('&');
                i++;
            }
        }

        return sb.ToString();
    }
}
