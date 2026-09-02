namespace OpenStus.Text;

/// <summary>
/// The line terminator convention a text file uses.
/// </summary>
public enum LineEndingStyle
{
    /// <summary>The text contains no line terminator at all (a single line, or an empty file).</summary>
    None,

    /// <summary>Windows: carriage return followed by line feed.</summary>
    Crlf,

    /// <summary>Unix: line feed only.</summary>
    Lf,

    /// <summary>Classic Mac OS: carriage return only.</summary>
    Cr,

    /// <summary>More than one convention occurs in the same text.</summary>
    Mixed,
}

/// <summary>
/// Detection, naming and conversion of line terminators.
/// </summary>
/// <remarks>
/// Nothing here ever rewrites a terminator behind the user's back. The editor stores the terminator
/// of every individual line, so a file that arrives mixed is saved back mixed, byte for byte; this
/// class only classifies what it sees and supplies the sequence used for lines the user creates.
/// </remarks>
public static class LineEndings
{
    /// <summary>The Windows terminator, <c>"\r\n"</c>.</summary>
    public const string Crlf = "\r\n";

    /// <summary>The Unix terminator, <c>"\n"</c>.</summary>
    public const string Lf = "\n";

    /// <summary>The classic Mac OS terminator, <c>"\r"</c>.</summary>
    public const string Cr = "\r";

    /// <summary>The convention native to the running operating system.</summary>
    public static LineEndingStyle Platform =>
        OperatingSystem.IsWindows() ? LineEndingStyle.Crlf : LineEndingStyle.Lf;

    /// <summary>
    /// Counts each terminator convention occurring in <paramref name="text"/>. A CR immediately
    /// followed by an LF counts once, as a CRLF, and never as a lone CR plus a lone LF.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="crlf">Receives the number of <c>"\r\n"</c> pairs.</param>
    /// <param name="lf">Receives the number of solitary <c>"\n"</c>.</param>
    /// <param name="cr">Receives the number of solitary <c>"\r"</c>.</param>
    public static void Count(ReadOnlySpan<char> text, out int crlf, out int lf, out int cr)
    {
        crlf = 0;
        lf = 0;
        cr = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (c == '\n')
            {
                lf++;
            }
        }
    }

    /// <summary>
    /// Classifies the terminators used by <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>
    /// <see cref="LineEndingStyle.None"/> when there is no terminator, the single convention used
    /// when there is exactly one, or <see cref="LineEndingStyle.Mixed"/>.
    /// </returns>
    public static LineEndingStyle Detect(ReadOnlySpan<char> text)
    {
        Count(text, out int crlf, out int lf, out int cr);
        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);

        return kinds switch
        {
            0 => LineEndingStyle.None,
            1 => crlf > 0 ? LineEndingStyle.Crlf : lf > 0 ? LineEndingStyle.Lf : LineEndingStyle.Cr,
            _ => LineEndingStyle.Mixed,
        };
    }

    /// <summary>Classifies the terminators used by a string.</summary>
    /// <param name="text">The text to scan; <see langword="null"/> yields <see cref="LineEndingStyle.None"/>.</param>
    /// <returns>The detected style.</returns>
    public static LineEndingStyle Detect(string? text) =>
        string.IsNullOrEmpty(text) ? LineEndingStyle.None : Detect(text.AsSpan());

    /// <summary>
    /// The convention that occurs most often in <paramref name="text"/>, which is what new lines
    /// should use when the text as a whole is <see cref="LineEndingStyle.Mixed"/>.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>
    /// The most frequent convention, or <see cref="Platform"/> when the text has no terminator.
    /// Ties are broken in CRLF, LF, CR order.
    /// </returns>
    public static LineEndingStyle Dominant(ReadOnlySpan<char> text)
    {
        Count(text, out int crlf, out int lf, out int cr);
        if (crlf == 0 && lf == 0 && cr == 0)
        {
            return Platform;
        }

        if (crlf >= lf && crlf >= cr)
        {
            return LineEndingStyle.Crlf;
        }

        return lf >= cr ? LineEndingStyle.Lf : LineEndingStyle.Cr;
    }

    /// <summary>Classifies one terminator string.</summary>
    /// <param name="ending">The terminator, typically <c>""</c>, <c>"\n"</c>, <c>"\r\n"</c> or <c>"\r"</c>.</param>
    /// <returns>The matching style, or <see cref="LineEndingStyle.None"/> for an empty terminator.</returns>
    public static LineEndingStyle Of(string? ending) => ending switch
    {
        Crlf => LineEndingStyle.Crlf,
        Lf => LineEndingStyle.Lf,
        Cr => LineEndingStyle.Cr,
        _ => LineEndingStyle.None,
    };

    /// <summary>
    /// The character sequence for a style. <see cref="LineEndingStyle.None"/> and
    /// <see cref="LineEndingStyle.Mixed"/> both fall back to <see cref="Platform"/>, because a new
    /// line has to be terminated somehow.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <returns>The terminator characters; never <see langword="null"/> and never empty.</returns>
    public static string Sequence(LineEndingStyle style) => style switch
    {
        LineEndingStyle.Crlf => Crlf,
        LineEndingStyle.Lf => Lf,
        LineEndingStyle.Cr => Cr,
        _ => Platform == LineEndingStyle.Crlf ? Crlf : Lf,
    };

    /// <summary>The short name shown on the viewer and editor status lines.</summary>
    /// <param name="style">The style.</param>
    /// <returns><c>"CRLF"</c>, <c>"LF"</c>, <c>"CR"</c>, <c>"Mixed"</c> or <c>"None"</c>.</returns>
    public static string Name(LineEndingStyle style) => style switch
    {
        LineEndingStyle.Crlf => "CRLF",
        LineEndingStyle.Lf => "LF",
        LineEndingStyle.Cr => "CR",
        LineEndingStyle.Mixed => "Mixed",
        _ => "None",
    };

    /// <summary>
    /// Folds two observed styles into one, which is how a whole-file style is accumulated from the
    /// per-line terminators.
    /// </summary>
    /// <param name="a">The style so far.</param>
    /// <param name="b">The style just seen.</param>
    /// <returns>
    /// The other operand when either is <see cref="LineEndingStyle.None"/>, that same style when
    /// both agree, otherwise <see cref="LineEndingStyle.Mixed"/>.
    /// </returns>
    public static LineEndingStyle Combine(LineEndingStyle a, LineEndingStyle b)
    {
        if (a == LineEndingStyle.None)
        {
            return b;
        }

        if (b == LineEndingStyle.None || a == b)
        {
            return a;
        }

        return LineEndingStyle.Mixed;
    }

    /// <summary>
    /// Splits text on every convention at once, dropping the terminators.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>
    /// The lines. Text ending in a terminator yields a trailing empty element, so that joining the
    /// result back reproduces the input exactly.
    /// </returns>
    public static string[] Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                lines.Add(text[start..i]);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }

        lines.Add(text[start..]);
        return [.. lines];
    }

    /// <summary>Joins lines with one terminator convention.</summary>
    /// <param name="lines">The line texts, without terminators.</param>
    /// <param name="style">The convention to insert between them.</param>
    /// <returns>The joined text; no terminator is appended after the last line.</returns>
    public static string Join(IEnumerable<string> lines, LineEndingStyle style) =>
        string.Join(Sequence(style), lines);

    /// <summary>Rewrites every terminator in <paramref name="text"/> to one convention.</summary>
    /// <param name="text">The text to convert.</param>
    /// <param name="style">The convention to convert to.</param>
    /// <returns>The converted text.</returns>
    public static string Normalize(string? text, LineEndingStyle style) =>
        string.IsNullOrEmpty(text) ? string.Empty : string.Join(Sequence(style), Split(text));
}
