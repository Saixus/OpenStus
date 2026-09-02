using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Dvopan.Files;

/// <summary>
/// The file mask engine behind Gray+/Gray- selection, the filter, and the find dialog.
/// </summary>
/// <remarks>
/// <para>
/// A mask supports <c>*</c> (any run of characters), <c>?</c> (exactly one character) and character
/// classes - <c>[abc]</c>, <c>[a-z]</c>, and <c>[!a-z]</c> or <c>[^a-z]</c> to negate. A mask list
/// separates masks with commas or semicolons, and an item starting with <c>!</c> excludes rather
/// than includes, so <c>"*.cs,!Generated*"</c> means "C# sources, but nothing generated". The
/// classic bar spelling works too: everything after a <c>|</c> is an exclude list, so
/// <c>"*.cs|*.g.cs"</c> means "C# sources except the generated ones", and an empty include half -
/// <c>"|*.bak"</c> - includes everything the excludes leave alone.
/// </para>
/// <para>
/// Matching is case-insensitive on Windows and case-sensitive elsewhere, following the file systems
/// themselves; the explicit overloads override that. Masks compile to a <see cref="Regex"/> once and
/// are cached, because selection runs the same mask over every entry in a directory.
/// </para>
/// </remarks>
public static class FileMask
{
    private const int CacheLimit = 512;

    private static readonly ConcurrentDictionary<(string Mask, bool IgnoreCase), Regex> Cache = new();

    private static readonly char[] Separators = [',', ';'];

    /// <summary>Whether masks ignore case by default on this platform.</summary>
    public static bool DefaultIgnoreCase { get; } = OperatingSystem.IsWindows();

    /// <summary>
    /// Matches <paramref name="name"/> against a single mask, using the platform's default case
    /// sensitivity.
    /// </summary>
    /// <param name="name">The file name to test.</param>
    /// <param name="mask">The mask.</param>
    /// <returns><see langword="true"/> when the name matches.</returns>
    public static bool IsMatch(string? name, string? mask) => IsMatch(name, mask, DefaultIgnoreCase);

    /// <summary>
    /// Matches <paramref name="name"/> against a single mask.
    /// </summary>
    /// <param name="name">The file name to test.</param>
    /// <param name="mask">The mask. An empty mask matches nothing.</param>
    /// <param name="ignoreCase">Whether to compare case-insensitively.</param>
    /// <returns><see langword="true"/> when the name matches.</returns>
    public static bool IsMatch(string? name, string? mask, bool ignoreCase)
    {
        if (name is null || string.IsNullOrEmpty(mask))
        {
            return false;
        }

        string trimmed = mask.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // "*.*" is the DOS spelling of "everything", and it keeps that meaning here - it matches
        // names without a dot too.
        if (trimmed is "*" or "*.*")
        {
            return true;
        }

        return GetRegex(trimmed, ignoreCase).IsMatch(name);
    }

    /// <summary>
    /// Matches <paramref name="name"/> against a mask list, using the platform's default case
    /// sensitivity.
    /// </summary>
    /// <param name="name">The file name to test.</param>
    /// <param name="maskList">The comma or semicolon separated list, optionally followed by
    /// <c>|</c> and an exclude list.</param>
    /// <returns><see langword="true"/> when the name matches.</returns>
    public static bool IsMatchAny(string? name, string? maskList) =>
        IsMatchAny(name, maskList, DefaultIgnoreCase);

    /// <summary>
    /// Matches <paramref name="name"/> against a mask list.
    /// </summary>
    /// <remarks>
    /// An empty list matches everything - there is nothing to filter by. An excluded name is
    /// rejected whatever else matches, and a list holding only exclusions matches every name they do
    /// not cover. Exclusions come in two spellings that mix freely: a <c>!</c> prefix on an item,
    /// and - the classic orthodox-file-manager syntax - everything after the first <c>|</c>.
    /// </remarks>
    /// <param name="name">The file name to test.</param>
    /// <param name="maskList">The comma or semicolon separated list, optionally followed by
    /// <c>|</c> and an exclude list.</param>
    /// <param name="ignoreCase">Whether to compare case-insensitively.</param>
    /// <returns><see langword="true"/> when the name matches.</returns>
    public static bool IsMatchAny(string? name, string? maskList, bool ignoreCase)
    {
        if (name is null)
        {
            return false;
        }

        // The bar spelling puts excludes after a '|' - "*.cs|*.g.cs" - so the list splits into an
        // include half and an exclude half before anything else. Only the first '|' separates; there is
        // no third section, and a '|' inside a [...] character class is one of the class's own
        // members, not the separator.
        string? includeHalf = maskList;
        string? excludeHalf = null;
        int bar = IndexOfSeparatorBar(maskList);
        if (bar >= 0)
        {
            includeHalf = maskList![..bar];
            excludeHalf = maskList[(bar + 1)..];
        }

        // Everything in the exclude half excludes, whether or not it also carries the '!' prefix.
        foreach (string item in SplitList(excludeHalf))
        {
            string exclude = item[0] == '!' ? item[1..].Trim() : item;
            if (exclude.Length != 0 && IsMatch(name, exclude, ignoreCase))
            {
                return false;
            }
        }

        IReadOnlyList<string> items = SplitList(includeHalf);
        if (items.Count == 0)
        {
            // An empty include half reads "everything except" - as in "|*.bak" - and an empty
            // list has nothing to filter by at all.
            return true;
        }

        bool sawInclude = false;
        bool included = false;

        foreach (string item in items)
        {
            if (item[0] == '!')
            {
                string exclude = item[1..].Trim();
                if (exclude.Length != 0 && IsMatch(name, exclude, ignoreCase))
                {
                    return false;
                }

                continue;
            }

            sawInclude = true;
            if (!included && IsMatch(name, item, ignoreCase))
            {
                included = true;
            }
        }

        return !sawInclude || included;
    }

    /// <summary>
    /// The index of the <c>|</c> that separates the include half from the exclude half, ignoring
    /// any <c>|</c> that sits inside a <c>[...]</c> character class, or <c>-1</c>.
    /// </summary>
    /// <param name="maskList">The list to scan.</param>
    /// <returns>The index, or <c>-1</c> when the list has no separator.</returns>
    private static int IndexOfSeparatorBar(string? maskList)
    {
        if (maskList is null)
        {
            return -1;
        }

        bool inClass = false;
        for (int i = 0; i < maskList.Length; i++)
        {
            switch (maskList[i])
            {
                case '[' when !inClass:
                    inClass = true;
                    break;

                case ']' when inClass:
                    inClass = false;
                    break;

                case '|' when !inClass:
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits a mask list into its items, dropping empty ones and trimming the whitespace people
    /// leave after a comma.
    /// </summary>
    /// <param name="maskList">The list to split.</param>
    /// <returns>The items, in order; empty when there are none.</returns>
    public static IReadOnlyList<string> SplitList(string? maskList)
    {
        if (string.IsNullOrWhiteSpace(maskList))
        {
            return [];
        }

        var items = new List<string>();
        foreach (string part in maskList.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string item = part.Trim();
            if (item.Length != 0)
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Translates a mask into the anchored regular expression used to match it. Exposed so that
    /// callers with their own <see cref="Regex"/> needs - the find dialog, for one - can reuse the
    /// same wildcard rules.
    /// </summary>
    /// <param name="mask">The mask to translate.</param>
    /// <returns>The pattern, anchored at both ends.</returns>
    public static string ToRegexPattern(string mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        var sb = new StringBuilder(mask.Length + 8);
        sb.Append('^');

        int i = 0;
        while (i < mask.Length)
        {
            char c = mask[i];
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    i++;
                    break;

                case '?':
                    sb.Append('.');
                    i++;
                    break;

                case '[':
                    i = AppendCharacterClass(sb, mask, i);
                    break;

                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }

    /// <summary>Empties the compiled mask cache. Only the tests need this.</summary>
    public static void ClearCache() => Cache.Clear();

    private static int AppendCharacterClass(StringBuilder sb, string mask, int open)
    {
        int close = mask.IndexOf(']', open + 1);

        // "[]abc]" puts a literal ']' first, exactly as a shell glob does.
        if (close == open + 1 || (close == open + 2 && (mask[open + 1] == '!' || mask[open + 1] == '^')))
        {
            close = mask.IndexOf(']', close + 1);
        }

        if (close < 0)
        {
            // Unterminated: the bracket is just a bracket.
            sb.Append("\\[");
            return open + 1;
        }

        int body = open + 1;
        bool negated = body < close && (mask[body] == '!' || mask[body] == '^');
        if (negated)
        {
            body++;
        }

        if (body >= close)
        {
            // "[]" or "[!]" carry no members; treat the whole thing literally.
            sb.Append(Regex.Escape(mask[open..(close + 1)]));
            return close + 1;
        }

        sb.Append('[');
        if (negated)
        {
            sb.Append('^');
        }

        for (int k = body; k < close; k++)
        {
            char c = mask[k];
            if (c is '\\' or ']' or '[' or '^')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        sb.Append(']');
        return close + 1;
    }

    private static Regex GetRegex(string mask, bool ignoreCase)
    {
        (string, bool) key = (mask, ignoreCase);
        if (Cache.TryGetValue(key, out Regex? cached))
        {
            return cached;
        }

        // A file manager can be pointed at a lot of one-off masks over a long session; the cache is
        // a speed-up, not a leak.
        if (Cache.Count >= CacheLimit)
        {
            Cache.Clear();
        }

        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        Regex regex;
        try
        {
            regex = new Regex(ToRegexPattern(mask), options);
        }
        catch (ArgumentException)
        {
            // A mask no translation could rescue matches itself and nothing else.
            regex = new Regex("^" + Regex.Escape(mask) + "$", options);
        }

        Cache[key] = regex;
        return regex;
    }
}
