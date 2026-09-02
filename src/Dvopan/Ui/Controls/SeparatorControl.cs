using Dvopan.Rendering;

namespace Dvopan.Ui.Controls;

/// <summary>
/// A horizontal rule across the whole dialog, drawn into the frame so it ends in the tee characters
/// of the owner's <see cref="Dialog.FrameStyle"/> (<c>╠═══╣</c> for the default double frame). An
/// optional caption is centred on it.
/// </summary>
/// <remarks>
/// Only <see cref="DialogControl.Bounds"/>.<c>Y</c> matters: the rule always spans the full client
/// width and reaches one cell further on each side, into the dialog frame.
/// </remarks>
public sealed class SeparatorControl : DialogControl
{
    /// <summary>Creates a separator.</summary>
    /// <param name="caption">An optional caption centred on the rule.</param>
    public SeparatorControl(string? caption = null)
    {
        Caption = caption;
        Bounds = new Rect(0, 0, 1, 1);
    }

    /// <summary>An optional caption centred on the rule.</summary>
    public string? Caption { get; set; }

    /// <summary>Separators are never focusable.</summary>
    public override bool CanFocus => false;

    /// <inheritdoc/>
    public override void Draw(ScreenBuffer buffer, Rect client, DialogPalette palette)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(palette);

        int y = client.Y + Bounds.Y;
        var frame = Owner?.FrameStyle ?? BoxStyle.Double;

        buffer.Set(client.X - 1, y, BoxChars.LeftTee(frame), palette.Box);
        buffer.HLine(client.X, y, client.Width, BoxChars.Horizontal(frame), palette.Box);
        buffer.Set(client.Right, y, BoxChars.RightTee(frame), palette.Box);

        if (string.IsNullOrEmpty(Caption) || client.Width < 5)
        {
            return;
        }

        string text = " " + Caption + " ";
        int width = Math.Min(text.Length, client.Width);
        int x = client.X + Math.Max(0, (client.Width - width) / 2);
        buffer.WriteFixed(x, y, width, text, palette.Title);
    }
}
