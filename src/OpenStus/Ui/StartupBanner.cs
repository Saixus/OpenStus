using System.Text;
using OpenStus.Core;

namespace OpenStus.Ui;

/// <summary>
/// The notice printed on the user screen before the panels come up, which is what <c>Ctrl+O</c>
/// then reveals: a text portrait of Vasyl Stus, whom the program is named after, beside the
/// version, the licence and the credit for the photograph the portrait is drawn from.
/// </summary>
/// <remarks>
/// It goes to the primary buffer before the alternate screen is entered, so it costs no room on
/// the panels screen and survives underneath it exactly the way command output does. The terminal
/// writes it once the console has been switched to UTF-8, which is what lets the portrait use the
/// block shades; the notice beside it stays ASCII, having no reason not to.
/// </remarks>
public static class StartupBanner
{
    /// <summary>The blank columns between the portrait and the text beside it.</summary>
    public const int Gutter = 2;

    /// <summary>
    /// The portrait, one string per row, in the five block shades from blank through
    /// <c>U+2591</c>..<c>U+2593</c> to <c>U+2588</c>, where a denser shade is a <em>lighter</em>
    /// part of the photograph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blocks rather than the punctuation an ASCII portrait would use, because punctuation fills
    /// only part of its cell and a different part for every glyph - a comma sits on the baseline, a
    /// tilde floats at mid height - so with the leading a terminal puts between rows the picture
    /// bands into stripes instead of reading as tone. A block fills its whole cell, which makes the
    /// result independent of the font's weight and spacing. Five even shades beat sixteen uneven
    /// ones; dithering to fake more was tried and only added a visible checkerboard at this size.
    /// </para>
    /// <para>
    /// Density tracks lightness, not darkness, on purpose. On a light-on-dark terminal density
    /// reads as brightness, so the other way round prints a photographic negative - and a face in
    /// negative is markedly harder to recognise, which is exactly what the first attempt looked
    /// like. Keeping the tones true costs a lit block where the wall behind him is, and buys a face.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Portrait { get; } =
    [
        "▓▓▓▓▓▓▓████▓▒▒░░░░░░░░░    ░░░▒▓▓████▓▓▓▓▓▓▓",
        "▓▓▓▓▓▓██▓▒░     ░░               ░▒▓█▓▓▓▓▓▓▓",
        "▓▓▓▓▓▓▓░                            ░▓▓▓▓▓▓▓",
        "▓▓▓▓▓▒                 ░░░░░░░       ░▓▓▓▓▓▓",
        "▓▓▓▓▒              ░▒▒▒▒▒▒▒▒▒▒▒░     ░▓▓▓▓▓▓",
        "▓▓▓▒           ░░▒▓▓▓▓▓▓▓▓▒▒▒▒▒▒░     ▓▓▓▓▓▓",
        "▓▓▓         ░▒▒▓▓▓▓▓▓▒▒▒▒▒▒▒▒▒▒▒░     ▒█▓▓▓▓",
        "▓▓▓     ░░▒▓▓▓▓▓▓▓▒▒▒▒▒▒▒▒▒▒░░░▒▒░░░  ░▓▓▓▓▓",
        "▓▓▓░   ░▒▓▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒░░░░░░░░▒░░ ░░▓▓▓▓▓",
        "▓▓█▓░░ ░▓▓▒░░░░░░▒▒▒▒▓▒░░░▒▒░▒▒▒▒▒▒░░▒▒▓▒▓██",
        "▓▓██▓░░░▓▒░░▒▒▒░░░▒▒▓▓▒▒░░▒░░▒▒▒▒▒▒▒░▒▓▓▒▒██",
        "▓▓█▓▓▒░░▒▓▓▒░▒░░░▒▒▒▓▓▒▒▒▒▒▓▓▓▓▓▓▓▒▓▒▒▒▒▒▓█▓",
        "▓▓▓▓▒▓▒░▒▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▓▓▓▓▓▓▓▓▒▓▒▒▒▒▓█▓▓",
        "▓▓▓▓▒▓▓▒▒▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▓▓▓▓▓▓▓▒▒▒▒▒▓████",
        "▓▓▓▓▓▓▒▒▒▓▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▓▓▓▓▒▒▒▒▒▒▓▓█▓▓▓",
        "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▒▒▒▒▓▓▓▓▓▒▒▒▒████▓▓▓",
        "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▓▓▓▓▒▒▒▒▒▒▓▓▓▓▓▒▒███▓▓▓▓",
        "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▒▒▒▒▒▒▒▒▓▓▒▒▒▒▓██████▓",
        "▓▓▓▓▓▓▓▓██▓▓▓▓▓▓▓▒▓▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▓██▓▓▓▓▓",
        "▓▓▓▓█▓▓████▓▒▒▓▒▒▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▒▒▒▓██▓▓▓▓▓",
        "▓▓████▓███▓▒▒▒▒▒▒▒▒▓▓▓▓▓▓▓▓▓▒▒▒▒▒▒▒▒▓▓▓▓▓▓▓▓",
        "▓▓▓▓▓▓▓▓▓▒░░▒▒▒▒▒▒▒▒▒▓▓▒▒▒▒▒▒▒▒▒▒▒▒░░▓▓▓▓▓██",
    ];

    /// <summary>The lines printed beside the portrait.</summary>
    public static IReadOnlyList<string> Notice { get; } =
    [
        CommandLineArgs.VersionText,
        "An open source dual-pane",
        "console file manager.",
        string.Empty,
        "Named after Vasyl Stus",
        "(1938-1985), Ukrainian poet,",
        "translator and dissident. The",
        "Soviet regime banned his work;",
        "arrested in 1972 and again in",
        "1980, he spent thirteen years",
        "in camps and exile and died in",
        "the Perm-36 camp for political",
        "prisoners. Reburied in Kyiv in",
        "1989; Hero of Ukraine in 2005.",
        string.Empty,
        "Portrait from his 1980 arrest",
        "photograph; public domain, via",
        "Wikimedia Commons.",
        string.Empty,
        "Copyright (c) 2026",
        "Dmytro Soyenko, MIT.",
        "github.com/Saixus/OpenStus",
    ];

    /// <summary>The width of the widest portrait row.</summary>
    public static int PortraitWidth => Portrait.Max(static line => line.Length);

    /// <summary>The width of the widest notice line.</summary>
    public static int NoticeWidth => Notice.Max(static line => line.Length);

    /// <summary>
    /// Lays the banner out for a terminal of the given width: the portrait and the notice side by
    /// side when both fit, the notice alone when only it does, and nothing at all on a terminal too
    /// narrow even for that - a banner wrapped by the terminal reads as damage rather than as a
    /// greeting.
    /// </summary>
    /// <param name="width">The terminal width in columns.</param>
    /// <returns>
    /// The banner, every line terminated and a blank line above and below it, or
    /// <see langword="null"/> when it does not fit.
    /// </returns>
    public static string? Render(int width)
    {
        // Strictly wider, never equal: a line that fills the last column makes the Windows console
        // wrap eagerly, and the newline after it then costs a second row. The banner is printed
        // before the terminal is switched to VT, where that wrap is deferred, so the spare column
        // is the only thing keeping those rows from double spacing.
        bool sideBySide = width > PortraitWidth + Gutter + NoticeWidth;

        if (!sideBySide && width <= NoticeWidth)
        {
            return null;
        }

        var sb = new StringBuilder(2048);
        sb.Append(Environment.NewLine);

        if (sideBySide)
        {
            int rows = Math.Max(Portrait.Count, Notice.Count);
            for (int i = 0; i < rows; i++)
            {
                string art = i < Portrait.Count ? Portrait[i] : string.Empty;
                string text = i < Notice.Count ? Notice[i] : string.Empty;

                sb.Append(art);

                // Nothing is padded when the line to the right is empty: a banner that leaves
                // trailing blanks behind smears the colour of whatever the terminal had there.
                if (text.Length != 0)
                {
                    sb.Append(' ', (PortraitWidth - art.Length) + Gutter).Append(text);
                }

                sb.Append(Environment.NewLine);
            }
        }
        else
        {
            foreach (string line in Notice)
            {
                sb.Append(line).Append(Environment.NewLine);
            }
        }

        sb.Append(Environment.NewLine);
        return sb.ToString();
    }
}
