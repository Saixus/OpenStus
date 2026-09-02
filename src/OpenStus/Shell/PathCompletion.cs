using OpenStus.Files;

namespace OpenStus.Shell;

/// <summary>
/// Tab completion for the command line: expands the path token under the caret against the file
/// system, and cycles through the alternatives when Tab is pressed again.
/// </summary>
/// <remarks>
/// <para>
/// Only the token under the caret is touched, so completing the second word of
/// <c>copy read me.txt</c> leaves the command name alone. A token that opens with a double quote
/// runs to its closing quote, which is how a path containing spaces is completed as one unit; a
/// completion that itself contains a space is quoted on the way back out.
/// </para>
/// <para>
/// Environment references are expanded before matching - <c>%USERPROFILE%</c> everywhere,
/// <c>$HOME</c> and <c>${HOME}</c> as well, plus a leading <c>~</c> - and the expansion is what
/// lands in the edit field, because a half-expanded path is not something the user can carry on
/// typing into.
/// </para>
/// <para>
/// Cycling is stateful: the completer remembers the text and caret it produced, and a call that
/// arrives with exactly that text and caret is read as "the same Tab again" and advances to the
/// next match. Any other edit starts a fresh search. <see cref="Reset"/> forces that explicitly.
/// </para>
/// </remarks>
public sealed class PathCompletion
{
    /// <summary>A directory large enough that completing inside it is not useful anyway.</summary>
    private const int MaxMatches = 4096;

    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.PlatformDefault,
    };

    private static StringComparison NameComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private string[] _matches = [];
    private int _index = -1;
    private int _tokenStart;
    private int _tokenLength;
    private string _lastText = string.Empty;
    private int _lastCaret = -1;

    /// <summary>How many alternatives the current completion is cycling through.</summary>
    public int MatchCount => _matches.Length;

    /// <summary>Which alternative is currently substituted, or <c>-1</c> when idle.</summary>
    public int MatchIndex => _index;

    /// <summary>Forgets the current cycle, so the next call starts a fresh search.</summary>
    public void Reset()
    {
        _matches = [];
        _index = -1;
        _tokenStart = 0;
        _tokenLength = 0;
        _lastText = string.Empty;
        _lastCaret = -1;
    }

    /// <summary>
    /// Completes the path token under the caret.
    /// </summary>
    /// <param name="text">The whole command line.</param>
    /// <param name="caret">The caret position, from 0 to the length of <paramref name="text"/>.</param>
    /// <param name="baseDirectory">The directory relative tokens are resolved against.</param>
    /// <param name="newText">The command line with the token replaced, or the input unchanged.</param>
    /// <param name="newCaret">The caret position after the substituted token.</param>
    /// <returns><see langword="true"/> when something was substituted.</returns>
    public bool TryComplete(string? text, int caret, string? baseDirectory, out string newText, out int newCaret)
    {
        string source = text ?? string.Empty;
        int position = Math.Clamp(caret, 0, source.Length);

        newText = source;
        newCaret = position;

        bool sameAsLast = _matches.Length > 0
                          && position == _lastCaret
                          && string.Equals(source, _lastText, StringComparison.Ordinal);

        if (sameAsLast)
        {
            if (_matches.Length == 1)
            {
                return false; // only one candidate: cycling would be a no-op
            }

            _index = (_index + 1) % _matches.Length;
            return Substitute(source, out newText, out newCaret);
        }

        (int start, int length) = TokenAt(source, position);
        string token = source.Substring(start, length);

        _matches = [.. Matches(token, baseDirectory)];
        if (_matches.Length == 0)
        {
            Reset();
            return false;
        }

        _index = 0;
        _tokenStart = start;
        _tokenLength = length;
        return Substitute(source, out newText, out newCaret);
    }

    private bool Substitute(string source, out string newText, out int newCaret)
    {
        string replacement = _matches[_index];
        int end = Math.Min(source.Length, _tokenStart + _tokenLength);

        newText = string.Concat(source.AsSpan(0, _tokenStart), replacement, source.AsSpan(end));
        newCaret = _tokenStart + replacement.Length;

        _tokenLength = replacement.Length;
        _lastText = newText;
        _lastCaret = newCaret;
        return true;
    }

    /// <summary>
    /// The span of the path token the caret sits in.
    /// </summary>
    /// <param name="text">The whole command line.</param>
    /// <param name="caret">The caret position.</param>
    /// <returns>
    /// The token start and length. A caret in whitespace yields a zero-length token at the caret,
    /// which completes everything in the base directory.
    /// </returns>
    public static (int Start, int Length) TokenAt(string? text, int caret)
    {
        string source = text ?? string.Empty;
        int position = Math.Clamp(caret, 0, source.Length);

        int i = 0;
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                if (i >= position)
                {
                    break; // the caret sat in the whitespace before this run
                }

                i++;
                continue;
            }

            int start = i;
            bool quoted = false;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '"')
                {
                    quoted = !quoted;
                    i++;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(c))
                {
                    break;
                }

                i++;
            }

            if (position >= start && position <= i)
            {
                return (start, i - start);
            }
        }

        return (position, 0);
    }

    /// <summary>
    /// The completions for one token, already quoted where a space makes that necessary and already
    /// carrying a trailing separator when the match is a directory.
    /// </summary>
    /// <param name="token">The token as it appears on the command line, quotes included.</param>
    /// <param name="baseDirectory">The directory relative tokens are resolved against.</param>
    /// <returns>The replacement tokens, directories first then files, each group by name.</returns>
    public static IReadOnlyList<string> Matches(string? token, string? baseDirectory)
    {
        string raw = Unquote(token ?? string.Empty);
        string expanded = ExpandEnvironment(raw);

        (string prefixText, string namePrefix) = SplitDirectory(expanded);

        string searchDirectory = ResolveSearchDirectory(prefixText, baseDirectory);
        if (searchDirectory.Length == 0)
        {
            return [];
        }

        var directories = new List<string>();
        var files = new List<string>();

        try
        {
            var info = new DirectoryInfo(searchDirectory);
            if (!info.Exists)
            {
                return [];
            }

            foreach (FileSystemInfo entry in info.EnumerateFileSystemInfos("*", Options))
            {
                string name;
                bool isDirectory;
                try
                {
                    name = entry.Name;
                    isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue; // vanished between the enumeration and the stat
                }

                if (namePrefix.Length > 0 && !name.StartsWith(namePrefix, NameComparison))
                {
                    continue;
                }

                (isDirectory ? directories : files).Add(name);

                if (directories.Count + files.Count >= MaxMatches)
                {
                    break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return [];
        }

        var order = NaturalComparer.For(caseSensitive: false);
        directories.Sort(order);
        files.Sort(order);

        var result = new List<string>(directories.Count + files.Count);
        foreach (string name in directories)
        {
            result.Add(Quote(prefixText + name + Path.DirectorySeparatorChar));
        }

        foreach (string name in files)
        {
            result.Add(Quote(prefixText + name));
        }

        return result;
    }

    /// <summary>
    /// The longest prefix every completion shares - what a shell extends the token to when Tab
    /// finds several matches, so the next Tab has less to choose between. Quotes are ignored for
    /// the comparison and put back when the result needs them.
    /// </summary>
    /// <param name="matches">The completions, as <see cref="Matches"/> returns them.</param>
    /// <returns>The common prefix, quoted when it contains a space; empty when there is none.</returns>
    public static string CommonPrefix(IReadOnlyList<string> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return string.Empty;
        }

        string first = Unquote(matches[0]);
        int length = first.Length;

        for (int i = 1; i < matches.Count && length > 0; i++)
        {
            string other = Unquote(matches[i]);
            int common = 0;
            while (common < length && common < other.Length &&
                   string.Compare(first, common, other, common, 1, NameComparison) == 0)
            {
                common++;
            }

            length = common;
        }

        return Quote(first[..length]);
    }

    /// <summary>
    /// Expands <c>%VAR%</c>, <c>$VAR</c>, <c>${VAR}</c> and a leading <c>~</c>.
    /// </summary>
    /// <param name="text">The text to expand.</param>
    /// <returns>The expanded text; unknown names are left exactly as they were.</returns>
    public static string ExpandEnvironment(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string result = text;

        try
        {
            result = Environment.ExpandEnvironmentVariables(result);
        }
        catch (ArgumentException)
        {
            // Malformed %...% run; carry on with the text as typed.
        }

        result = ExpandDollarNames(result);

        if (result == "~" ||
            result.StartsWith("~/", StringComparison.Ordinal) ||
            (Path.DirectorySeparatorChar == '\\' && result.StartsWith("~\\", StringComparison.Ordinal)))
        {
            string home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify);

            if (home.Length > 0)
            {
                result = result.Length <= 1 ? home + Path.DirectorySeparatorChar : home + result[1..];
            }
        }

        return result;
    }

    private static string ExpandDollarNames(string text)
    {
        if (!text.Contains('$', StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '$' || i + 1 >= text.Length)
            {
                sb.Append(text[i]);
                continue;
            }

            int nameStart;
            int nameEnd;
            int after;

            if (text[i + 1] == '{')
            {
                int close = text.IndexOf('}', i + 2);
                if (close < 0)
                {
                    sb.Append(text[i]);
                    continue;
                }

                nameStart = i + 2;
                nameEnd = close;
                after = close + 1;
            }
            else
            {
                nameStart = i + 1;
                nameEnd = nameStart;
                while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
                {
                    nameEnd++;
                }

                if (nameEnd == nameStart)
                {
                    sb.Append(text[i]);
                    continue;
                }

                after = nameEnd;
            }

            string name = text[nameStart..nameEnd];
            string? value = SafeGetEnvironmentVariable(name);
            if (value is null)
            {
                sb.Append(text, i, after - i); // unknown name: leave it exactly as typed
            }
            else
            {
                sb.Append(value);
            }

            i = after - 1;
        }

        return sb.ToString();
    }

    private static string? SafeGetEnvironmentVariable(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name);
        }
        catch (Exception e) when (e is ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Splits a path into the part that stays verbatim and the name prefix being matched.</summary>
    private static (string Prefix, string NamePrefix) SplitDirectory(string path)
    {
        if (path.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (OperatingSystem.IsWindows() && path.Length == 2 && path[1] == ':' && char.IsLetter(path[0]))
        {
            // "C:" alone means "the current directory of drive C"; complete its root instead, which
            // is what the user meant and what they can carry on typing into.
            return (path + Path.DirectorySeparatorChar, string.Empty);
        }

        int cut = path.LastIndexOfAny(Separators);
        return cut < 0
            ? (string.Empty, path)
            : (path[..(cut + 1)], path[(cut + 1)..]);
    }

    private static string ResolveSearchDirectory(string prefixText, string? baseDirectory)
    {
        try
        {
            if (prefixText.Length == 0)
            {
                return string.IsNullOrWhiteSpace(baseDirectory)
                    ? Environment.CurrentDirectory
                    : Path.GetFullPath(baseDirectory);
            }

            if (Path.IsPathRooted(prefixText) || string.IsNullOrWhiteSpace(baseDirectory))
            {
                return Path.GetFullPath(prefixText);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, prefixText));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return string.Empty;
        }
    }

    private static string Unquote(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        // Quotes can appear anywhere in a shell token; for completion only their removal matters.
        return token.Replace("\"", string.Empty, StringComparison.Ordinal);
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? "\"" + path + "\"" : path;

    // Only the platform's own separators: a backslash is a perfectly ordinary character in a Unix
    // file name and must not split a token there.
    private static readonly char[] Separators =
        Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
            ? [Path.DirectorySeparatorChar]
            : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
}
