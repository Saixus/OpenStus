using System.Globalization;

namespace OpenCommander.Files;

/// <summary>
/// Formats byte counts the way the panel columns, the status line and the totals line want them.
/// </summary>
/// <remarks>
/// Every format is culture invariant on purpose. The panel columns are laid out in character cells
/// and a locale that writes <c>"1,5 K"</c> instead of <c>"1.5 K"</c>, or that groups digits with a
/// non-breaking space, would break the alignment the layout depends on.
/// </remarks>
public static class SizeFormatter
{
    /// <summary>The widest string <see cref="Short"/> can produce, for column sizing.</summary>
    public const int ShortMaxWidth = 7;

    /// <summary>The character <see cref="Grouped"/> puts between digit groups: a plain space.</summary>
    public const char GroupSeparator = ' ';

    private const long Step = 1024;

    private static readonly char[] Units = ['B', 'K', 'M', 'G', 'T', 'P'];

    /// <summary>
    /// The compact Far-style size: <c>"0 B"</c>, <c>"999 B"</c>, <c>"1.5 K"</c>, <c>"52.0 G"</c>.
    /// Never wider than <see cref="ShortMaxWidth"/> characters.
    /// </summary>
    /// <remarks>
    /// Below a kilobyte the exact byte count is shown. Above it the value carries one decimal, which
    /// is dropped once the mantissa reaches four digits (<c>"1023 K"</c>) so the result still fits
    /// the column.
    /// </remarks>
    /// <param name="bytes">The byte count; negative values are formatted with a leading minus.</param>
    /// <returns>The formatted size.</returns>
    public static string Short(long bytes)
    {
        if (bytes < 0)
        {
            return bytes == long.MinValue
                ? "-8192 P"
                : "-" + Short(-bytes);
        }

        if (bytes < Step)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{bytes} {Units[0]}");
        }

        double value = bytes;
        int unit = 0;
        while (value >= Step && unit < Units.Length - 1)
        {
            value /= Step;
            unit++;
        }

        // One decimal while it fits; four mantissa digits leave no room for it. The threshold is
        // just below 1000 because a value like 999.97 would otherwise round up to "1000.0" and
        // spill out of the column.
        string number = value >= 999.95d
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F1", CultureInfo.InvariantCulture);

        return string.Create(CultureInfo.InvariantCulture, $"{number} {Units[unit]}");
    }

    /// <summary>
    /// The exact byte count with its digits grouped by spaces: <c>"1 234 567"</c>. This is what the
    /// panel status line and the totals line show.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>The grouped number.</returns>
    public static string Grouped(long bytes) => Group(bytes, GroupSeparator);

    /// <summary>
    /// The exact byte count with its digits grouped by commas: <c>"1,234,567"</c>.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>The grouped number.</returns>
    public static string Commas(long bytes) => Group(bytes, ',');

    private static string Group(long value, char separator)
    {
        bool negative = value < 0;

        // Negating long.MinValue overflows, so do the arithmetic unsigned.
        ulong magnitude = negative ? unchecked((ulong)-(value + 1)) + 1UL : (ulong)value;

        string digits = magnitude.ToString(CultureInfo.InvariantCulture);
        int groups = (digits.Length - 1) / 3;
        if (groups == 0 && !negative)
        {
            return digits;
        }

        int length = digits.Length + groups + (negative ? 1 : 0);

        return string.Create(length, (digits, separator, negative), static (span, state) =>
        {
            (string src, char sep, bool neg) = state;

            int w = span.Length - 1;
            int written = 0;
            for (int r = src.Length - 1; r >= 0; r--)
            {
                if (written == 3)
                {
                    span[w--] = sep;
                    written = 0;
                }

                span[w--] = src[r];
                written++;
            }

            if (neg)
            {
                span[w] = '-';
            }
        });
    }
}
