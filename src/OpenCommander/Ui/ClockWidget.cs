using System.Globalization;
using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Ui;

/// <summary>
/// The clock Far draws in the top right corner, over whatever panel frame happens to be underneath.
/// </summary>
/// <remarks>
/// The format is fixed to the invariant culture on purpose: a twelve hour clock with an unpadded
/// hour is exactly seven or eight columns wide, so the panel title never has to be re-centred when
/// the time ticks from <c>"9:59 AM"</c> to <c>"10:00 AM"</c> and back the next day.
/// </remarks>
public sealed class ClockWidget
{
    /// <summary>The .NET format string used for the time, e.g. <c>"10:06 AM"</c>.</summary>
    public const string TimeFormat = "h:mm tt";

    /// <summary>
    /// Where the time comes from. Defaults to <see cref="DateTime.Now"/>; tests replace it to get a
    /// deterministic frame.
    /// </summary>
    public Func<DateTime> TimeSource { get; set; } = static () => DateTime.Now;

    /// <summary>The text that would be drawn right now.</summary>
    public string Text => Format(TimeSource());

    /// <summary>
    /// Draws the current time right-aligned on row 0.
    /// </summary>
    /// <param name="buf">The back buffer.</param>
    /// <param name="theme">The palette; the clock uses <see cref="Theme.Clock"/>.</param>
    public void Draw(ScreenBuffer buf, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(theme);

        Draw(buf, theme, 0);
    }

    /// <summary>
    /// Draws the current time right-aligned on an explicit row, for the viewer and editor which
    /// keep their own top line.
    /// </summary>
    /// <param name="buf">The back buffer.</param>
    /// <param name="theme">The palette; the clock uses <see cref="Theme.Clock"/>.</param>
    /// <param name="y">The screen row; rows outside the buffer are ignored.</param>
    public void Draw(ScreenBuffer buf, Theme theme, int y)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(theme);

        if ((uint)y >= (uint)buf.Height)
        {
            return;
        }

        string text = Text;
        if (text.Length == 0 || text.Length > buf.Width)
        {
            return;
        }

        buf.Write(buf.Width - text.Length, y, text, theme.Clock);
    }

    /// <summary>Formats a time the way the clock shows it.</summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The text, e.g. <c>"10:06 AM"</c>.</returns>
    public static string Format(DateTime time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);
}
