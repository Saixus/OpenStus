namespace OpenStus.Shell;

/// <summary>What a run of the command line is, for colouring.</summary>
public enum CommandTokenKind : byte
{
    /// <summary>Plain text: arguments, operators, whitespace.</summary>
    Text,

    /// <summary>The command itself - the first word, and the first word after <c>|</c>, <c>&amp;&amp;</c> or <c>||</c>.</summary>
    Command,

    /// <summary>An option: a word starting with <c>-</c>, <c>--</c> or <c>/</c>.</summary>
    Option,

    /// <summary>A quoted string, quotes included.</summary>
    String,

    /// <summary>A variable: <c>$name</c>, <c>${name}</c>, <c>$env:name</c> or <c>%NAME%</c>.</summary>
    Variable,
}

/// <summary>One coloured run of the command line.</summary>
/// <param name="Start">First character of the run.</param>
/// <param name="Length">How many characters it covers; always positive.</param>
/// <param name="Kind">What the run is.</param>
public readonly record struct CommandToken(int Start, int Length, CommandTokenKind Kind);

/// <summary>
/// The tiny tokenizer behind the command line's colouring - the same idea as PSReadLine's or
/// fish's: the command stands out, strings and options and variables each get a colour, and
/// everything else stays plain. It has no opinion about which shell will run the line, so it
/// understands the spellings of both cmd and PowerShell at once.
/// </summary>
public static class CommandLineSyntax
{
    /// <summary>
    /// Tokenizes a command line, appending only the coloured runs - plain text is whatever the
    /// runs leave uncovered.
    /// </summary>
    /// <param name="text">The command line.</param>
    /// <param name="tokens">Receives the runs, in order.</param>
    public static void Tokenize(string? text, List<CommandToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int n = text.Length;
        int i = 0;
        bool expectCommand = true;

        while (i < n)
        {
            char c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // A pipe or a chain hands the next word the command colour again - except the '&' of
            // a redirection such as "2>&1", which is not a chain and whose "1" is not a command.
            if (c is '|' or '&')
            {
                bool redirect = c == '&' && i > 0 && text[i - 1] is '>' or '<';
                i += i + 1 < n && text[i + 1] == c ? 2 : 1;
                if (!redirect)
                {
                    expectCommand = true;
                }

                continue;
            }

            if (c is '<' or '>')
            {
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                int start = i;
                int close = text.IndexOf(c, i + 1);
                i = close < 0 ? n : close + 1;

                // A quoted first word is still the command: "C:\Program Files\tool.exe" args.
                tokens.Add(new CommandToken(start, i - start, expectCommand ? CommandTokenKind.Command : CommandTokenKind.String));
                expectCommand = false;
                continue;
            }

            if (c == '$' || (c == '%' && i + 1 < n && IsVariableChar(text[i + 1])))
            {
                int start = i;
                i = ScanVariable(text, i);
                tokens.Add(new CommandToken(start, i - start, CommandTokenKind.Variable));
                expectCommand = false;
                continue;
            }

            int wordStart = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] is not ('|' or '&' or '<' or '>'))
            {
                i++;
            }

            if (expectCommand)
            {
                tokens.Add(new CommandToken(wordStart, i - wordStart, CommandTokenKind.Command));
                expectCommand = false;
            }
            else if (IsOption(text, wordStart, i))
            {
                tokens.Add(new CommandToken(wordStart, i - wordStart, CommandTokenKind.Option));
            }
        }
    }

    private static bool IsOption(string text, int start, int end)
    {
        char c = text[start];
        if (c == '-')
        {
            return end - start > 1; // a lone dash is an argument, "-v" and "--verbose" are options
        }

        // "/s" is an option on Windows; "/usr/bin" is a path. A slash followed by a short word
        // with no further slash is the former.
        if (c == '/' && end - start > 1 && end - start <= 12)
        {
            for (int i = start + 1; i < end; i++)
            {
                if (text[i] is '/' or '\\' or '.')
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static int ScanVariable(string text, int at)
    {
        int n = text.Length;
        int i = at + 1;

        if (text[at] == '%')
        {
            // %NAME%: closed by the next percent sign, or just "%NAME" when it is missing.
            while (i < n && IsVariableChar(text[i]))
            {
                i++;
            }

            return i < n && text[i] == '%' ? i + 1 : i;
        }

        if (i < n && text[i] == '{')
        {
            int close = text.IndexOf('}', i + 1);
            return close < 0 ? n : close + 1;
        }

        // $name, and PowerShell's $env:NAME / $script:name scopes.
        while (i < n && (IsVariableChar(text[i]) || text[i] == ':'))
        {
            i++;
        }

        return i;
    }

    private static bool IsVariableChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
