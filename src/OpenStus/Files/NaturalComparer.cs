namespace OpenStus.Files;

/// <summary>
/// Compares names the way a person reads them: runs of digits are compared by value, so
/// <c>"file2"</c> sorts before <c>"file10"</c>.
/// </summary>
/// <remarks>
/// The contract sketch spelled this as a static class implementing <see cref="IComparer{T}"/>,
/// which C# does not allow; it ships as a sealed class with cached instances plus a static
/// <see cref="Compare(string, string, bool)"/> so both call styles work.
/// </remarks>
public sealed class NaturalComparer : IComparer<string>
{
    /// <summary>Creates a comparer.</summary>
    /// <param name="caseSensitive">When set, letters compare by ordinal instead of case-insensitively.</param>
    public NaturalComparer(bool caseSensitive = false) => CaseSensitive = caseSensitive;

    /// <summary>The shared case-insensitive comparer.</summary>
    public static NaturalComparer OrdinalIgnoreCase { get; } = new(caseSensitive: false);

    /// <summary>The shared case-sensitive comparer.</summary>
    public static NaturalComparer Ordinal { get; } = new(caseSensitive: true);

    /// <summary>Whether this instance compares letters case sensitively.</summary>
    public bool CaseSensitive { get; }

    /// <summary>Returns the shared instance for the requested case sensitivity.</summary>
    /// <param name="caseSensitive">Whether letters should compare case sensitively.</param>
    /// <returns>The shared comparer.</returns>
    public static NaturalComparer For(bool caseSensitive) => caseSensitive ? Ordinal : OrdinalIgnoreCase;

    /// <inheritdoc/>
    public int Compare(string? x, string? y) => Compare(x, y, CaseSensitive);

    /// <summary>
    /// Compares two names with digit runs taken as numbers.
    /// </summary>
    /// <param name="x">The first name; <see langword="null"/> sorts first.</param>
    /// <param name="y">The second name.</param>
    /// <param name="caseSensitive">Whether letters compare by ordinal.</param>
    /// <returns>A negative value, zero, or a positive value in the usual comparer sense.</returns>
    public static int Compare(string? x, string? y, bool caseSensitive)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // Two names that only differ in how many zeros pad a number are ordered by that padding,
        // but only once nothing more important has separated them.
        int zeroTieBreak = 0;
        int i = 0;
        int j = 0;

        while (i < x.Length && j < y.Length)
        {
            char cx = x[i];
            char cy = y[j];

            if (char.IsAsciiDigit(cx) && char.IsAsciiDigit(cy))
            {
                int zerosX = i;
                int zerosY = j;

                while (i < x.Length - 1 && x[i] == '0' && char.IsAsciiDigit(x[i + 1]))
                {
                    i++;
                }

                while (j < y.Length - 1 && y[j] == '0' && char.IsAsciiDigit(y[j + 1]))
                {
                    j++;
                }

                zerosX = i - zerosX;
                zerosY = j - zerosY;

                int endX = i;
                int endY = j;
                while (endX < x.Length && char.IsAsciiDigit(x[endX]))
                {
                    endX++;
                }

                while (endY < y.Length && char.IsAsciiDigit(y[endY]))
                {
                    endY++;
                }

                int lenX = endX - i;
                int lenY = endY - j;

                if (lenX != lenY)
                {
                    return lenX < lenY ? -1 : 1;
                }

                int digits = string.CompareOrdinal(x, i, y, j, lenX);
                if (digits != 0)
                {
                    return digits < 0 ? -1 : 1;
                }

                if (zeroTieBreak == 0)
                {
                    zeroTieBreak = zerosX.CompareTo(zerosY);
                }

                i = endX;
                j = endY;
                continue;
            }

            int c = CompareChar(cx, cy, caseSensitive);
            if (c != 0)
            {
                return c;
            }

            i++;
            j++;
        }

        if (i < x.Length)
        {
            return 1;
        }

        if (j < y.Length)
        {
            return -1;
        }

        return zeroTieBreak;
    }

    private static int CompareChar(char a, char b, bool caseSensitive)
    {
        if (a == b)
        {
            return 0;
        }

        if (!caseSensitive)
        {
            char ua = char.ToUpperInvariant(a);
            char ub = char.ToUpperInvariant(b);
            if (ua == ub)
            {
                return 0;
            }

            return ua < ub ? -1 : 1;
        }

        return a < b ? -1 : 1;
    }
}
