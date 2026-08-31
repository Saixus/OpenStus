namespace OpenCommander.Text.Syntax;

/// <summary>
/// The line tokenizer behind the editor's and the viewer's syntax colouring.
/// </summary>
/// <remarks>
/// <para>
/// One call handles one line: it takes the state the previous line ended in, appends the coloured
/// spans of this line to a caller-owned list, and returns the state this line ends in. Only
/// non-<see cref="TokenKind.Text"/> spans are emitted - plain text is whatever the spans leave
/// uncovered, so a line with nothing to colour costs one scan and no allocations.
/// </para>
/// <para>
/// The tokenizer is deliberately permissive. It exists to make a file readable, not to validate
/// it: a malformed number or an unterminated string still gets a colour, and exotica such as
/// regex literals or string interpolation holes are simply left as part of whatever they sit in.
/// One known consequence: a JavaScript regex ending in an escaped slash (<c>/\//</c>) forms a
/// <c>//</c> the tokenizer reads as a line comment, greying the rest of that line - damage never
/// crosses the line. Lines longer than <see cref="MaxScanLength"/> are coloured up to the cap and
/// left plain past it, so one minified megabyte of JSON cannot stall a repaint; the cap applies to
/// the spans only, while the carried state still reflects the whole line, so a construct closing
/// past the cap does not bleed into the rest of the file.
/// </para>
/// </remarks>
public static class SyntaxTokenizer
{
    /// <summary>How many characters of one line are tokenized before giving up on the rest.</summary>
    public const int MaxScanLength = 20_000;

    /// <summary>
    /// Tokenizes one line.
    /// </summary>
    /// <param name="line">The line, without its terminator.</param>
    /// <param name="rules">The language.</param>
    /// <param name="state">The state the previous line ended in.</param>
    /// <param name="tokens">Receives the coloured spans, appended in order; may be <see langword="null"/> for a state-only scan.</param>
    /// <returns>The state this line ends in.</returns>
    public static SyntaxState TokenizeLine(string line, SyntaxRules rules, SyntaxState state, List<TokenSpan>? tokens)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(rules);

        int n = Math.Min(line.Length, MaxScanLength);
        int i = 0;

        // First finish whatever the previous line left open.
        switch (state.Mode)
        {
            case SyntaxMode.BlockComment:
                i = CloseBlockComment(line, n, rules, tokens, ref state);
                break;

            case SyntaxMode.VerbatimString:
                i = CloseVerbatimString(line, n, tokens, ref state);
                break;

            case SyntaxMode.TemplateString:
                i = CloseTemplateString(line, n, tokens, ref state);
                break;

            case SyntaxMode.TripleSingleString:
            case SyntaxMode.TripleDoubleString:
                i = CloseTripleString(line, n, tokens, ref state);
                break;

            default:
                break;
        }

        if (state.Mode != SyntaxMode.None)
        {
            return state; // the whole line is still inside the construct
        }

        while (i < n)
        {
            char c = line[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // A preprocessor directive claims the rest of the line, but only from the left margin.
            if (rules.HashPreprocessor && c == '#' && FirstNonBlankIs(line, i))
            {
                Emit(tokens, i, n - i, TokenKind.Preprocessor);
                return SyntaxState.None;
            }

            if (StartsLineComment(line, i, n, rules))
            {
                Emit(tokens, i, n - i, TokenKind.Comment);
                return SyntaxState.None;
            }

            if (rules.BlockCommentOpen is string open && Matches(line, i, n, open))
            {
                int start = i;
                i += open.Length;
                state = new SyntaxState(SyntaxMode.BlockComment);
                int after = CloseBlockCommentFrom(line, n, i, rules, ref state);
                Emit(tokens, start, after - start, TokenKind.Comment);
                i = after;

                if (state.Mode != SyntaxMode.None)
                {
                    return state;
                }

                continue;
            }

            if (rules.VerbatimStrings && c == '@' && (Matches(line, i, n, "@\"") || Matches(line, i, n, "@$\"")))
            {
                // Both spellings of an interpolated verbatim string open the same construct;
                // '$@"' needs no case of its own because the '$' reads as an identifier first.
                int start = i;
                i += line[i + 1] == '$' ? 3 : 2;
                state = new SyntaxState(SyntaxMode.VerbatimString);
                int after = CloseVerbatimStringFrom(line, n, i, ref state);
                Emit(tokens, start, after - start, TokenKind.String);
                i = after;

                if (state.Mode != SyntaxMode.None)
                {
                    return state;
                }

                continue;
            }

            if (rules.TripleQuotedStrings && (c == '\'' || c == '"') && Matches(line, i, n, c == '\'' ? "'''" : "\"\"\""))
            {
                int start = i;
                i += 3;
                state = new SyntaxState(c == '\'' ? SyntaxMode.TripleSingleString : SyntaxMode.TripleDoubleString);
                int after = CloseTripleStringFrom(line, n, i, ref state);
                Emit(tokens, start, after - start, TokenKind.String);
                i = after;

                if (state.Mode != SyntaxMode.None)
                {
                    return state;
                }

                continue;
            }

            if (rules.TemplateLiterals && c == '`')
            {
                int start = i;
                i++;
                state = new SyntaxState(SyntaxMode.TemplateString);
                int after = CloseTemplateStringFrom(line, n, i, ref state);
                Emit(tokens, start, after - start, TokenKind.String);
                i = after;

                if (state.Mode != SyntaxMode.None)
                {
                    return state;
                }

                continue;
            }

            // A single quote only opens a string at a word boundary: the apostrophe in a YAML
            // "don't" or a shell echo must not swallow the rest of the line.
            if ((c == '"' && rules.DoubleQuoteStrings) ||
                (c == '\'' && rules.SingleQuoteStrings && (i == 0 || !IsIdentifierPart(line[i - 1]))))
            {
                int start = i;
                i = ScanSingleLineString(line, n, i, c, rules);
                TokenKind kind = rules.JsonKeys && c == '"' && IsJsonKey(line, i) ? TokenKind.Keyword : TokenKind.String;
                Emit(tokens, start, i - start, kind);
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < n && char.IsAsciiDigit(line[i + 1])))
            {
                int start = i;
                i = ScanNumber(line, n, i);
                Emit(tokens, start, i - start, TokenKind.Number);
                continue;
            }

            if (IsIdentifierStart(c))
            {
                int start = i;
                while (i < n && IsIdentifierPart(line[i]))
                {
                    i++;
                }

                if (rules.Keywords.Contains(line[start..i]))
                {
                    Emit(tokens, start, i - start, TokenKind.Keyword);
                }

                continue;
            }

            i++;
        }

        return SyntaxState.None;
    }

    /// <summary>Scans a line only for its exit state, without collecting spans.</summary>
    /// <param name="line">The line, without its terminator.</param>
    /// <param name="rules">The language.</param>
    /// <param name="state">The state the previous line ended in.</param>
    /// <returns>The state this line ends in.</returns>
    public static SyntaxState ScanLine(string line, SyntaxRules rules, SyntaxState state) =>
        TokenizeLine(line, rules, state, tokens: null);

    // ---------------------------------------------------------------- multi-line closers

    private static int CloseBlockComment(string line, int n, SyntaxRules rules, List<TokenSpan>? tokens, ref SyntaxState state)
    {
        int after = CloseBlockCommentFrom(line, n, 0, rules, ref state);
        Emit(tokens, 0, after, TokenKind.Comment);
        return after;
    }

    private static int CloseBlockCommentFrom(string line, int n, int from, SyntaxRules rules, ref SyntaxState state)
    {
        // The search deliberately runs past the span cap: the emitted colour stops at n, but the
        // carried state must reflect the whole line, or a close past the cap would paint the rest
        // of the file as comment.
        string close = rules.BlockCommentClose ?? "*/";
        int at = line.IndexOf(close, Math.Min(from, line.Length), StringComparison.Ordinal);
        if (at < 0)
        {
            return n; // still open; state stays BlockComment
        }

        state = SyntaxState.None;
        return Math.Min(at + close.Length, Math.Max(n, from));
    }

    private static int CloseVerbatimString(string line, int n, List<TokenSpan>? tokens, ref SyntaxState state)
    {
        int after = CloseVerbatimStringFrom(line, n, 0, ref state);
        Emit(tokens, 0, after, TokenKind.String);
        return after;
    }

    private static int CloseVerbatimStringFrom(string line, int n, int from, ref SyntaxState state)
    {
        // Scans the whole line, not just the capped part: the colour stops at n but the state
        // must be right for the lines below.
        int i = from;
        while (i < line.Length)
        {
            if (line[i] != '"')
            {
                i++;
                continue;
            }

            if (i + 1 < line.Length && line[i + 1] == '"')
            {
                i += 2; // "" is a literal quote
                continue;
            }

            state = SyntaxState.None;
            return Math.Min(i + 1, Math.Max(n, from));
        }

        return n; // still open
    }

    private static int CloseTemplateString(string line, int n, List<TokenSpan>? tokens, ref SyntaxState state)
    {
        int after = CloseTemplateStringFrom(line, n, 0, ref state);
        Emit(tokens, 0, after, TokenKind.String);
        return after;
    }

    private static int CloseTemplateStringFrom(string line, int n, int from, ref SyntaxState state)
    {
        // Scans the whole line for the same reason as the verbatim closer: state over spans.
        int i = from;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                i += 2;
                continue;
            }

            if (c == '`')
            {
                state = SyntaxState.None;
                return Math.Min(i + 1, Math.Max(n, from));
            }

            i++;
        }

        return n; // still open
    }

    private static int CloseTripleString(string line, int n, List<TokenSpan>? tokens, ref SyntaxState state)
    {
        int after = CloseTripleStringFrom(line, n, 0, ref state);
        Emit(tokens, 0, after, TokenKind.String);
        return after;
    }

    private static int CloseTripleStringFrom(string line, int n, int from, ref SyntaxState state)
    {
        // As with the block-comment closer: search past the span cap so the state stays honest.
        string close = state.Mode == SyntaxMode.TripleSingleString ? "'''" : "\"\"\"";
        int at = line.IndexOf(close, Math.Min(from, line.Length), StringComparison.Ordinal);
        if (at < 0)
        {
            return n; // still open
        }

        state = SyntaxState.None;
        return Math.Min(at + close.Length, Math.Max(n, from));
    }

    // ---------------------------------------------------------------- single-line pieces

    private static int ScanSingleLineString(string line, int n, int at, char quote, SyntaxRules rules)
    {
        int i = at + 1;
        while (i < n)
        {
            char c = line[i];

            if (c == '\\' && rules.BackslashEscapes && i + 1 < n)
            {
                i += 2;
                continue;
            }

            if (c == quote)
            {
                if (rules.DoubledQuoteEscapes && i + 1 < n && line[i + 1] == quote)
                {
                    i += 2; // SQL's '' is a literal quote
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return n; // unterminated: coloured to the end, but the state does not carry over
    }

    private static int ScanNumber(string line, int n, int at)
    {
        // Permissive about the digits themselves - hex, binary, exponents, separators and short
        // type suffixes all read as one number - but strict about the dot: it only continues the
        // number when a digit follows, so a range operator ("0..n") or a member call on a literal
        // ("1.ToString()") stops the number where the number actually ends.
        int i = at;

        bool prefixed = i + 1 < n && line[i] == '0' && (line[i + 1] is 'x' or 'X' or 'b' or 'B');
        if (prefixed)
        {
            i += 2;
            while (i < n && (char.IsAsciiHexDigit(line[i]) || line[i] == '_'))
            {
                i++;
            }

            return i;
        }

        while (i < n)
        {
            char c = line[i];

            if (char.IsAsciiDigit(c) || c == '_')
            {
                i++;
                continue;
            }

            if (c == '.' && i + 1 < n && char.IsAsciiDigit(line[i + 1]))
            {
                i++;
                continue;
            }

            if ((c is 'e' or 'E') && i + 1 < n &&
                (char.IsAsciiDigit(line[i + 1]) ||
                 (line[i + 1] is '+' or '-' && i + 2 < n && char.IsAsciiDigit(line[i + 2]))))
            {
                i += line[i + 1] is '+' or '-' ? 2 : 1;
                continue;
            }

            break;
        }

        // A short trailing type suffix directly after the digits - 1.5f, 10m, 42UL - but only
        // when nothing identifier-like follows it, so "5times" leaves "times" a word of its own.
        int end = i;
        while (end < n && end - i < 2 && char.IsAsciiLetter(line[end]))
        {
            end++;
        }

        if (end > i && (end >= n || !IsIdentifierPart(line[end])))
        {
            i = end;
        }

        return i;
    }

    private static bool IsJsonKey(string line, int afterString)
    {
        int i = afterString;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        return i < line.Length && line[i] == ':';
    }

    private static bool StartsLineComment(string line, int at, int n, SyntaxRules rules)
    {
        foreach (string prefix in rules.LineComments)
        {
            if (!Matches(line, at, n, prefix))
            {
                continue;
            }

            // A bare '#' comments only from a word boundary after whitespace: a URL fragment
            // ("page#frag") or bash's "${#var}" must not grey out the rest of the line. The
            // multi-character markers ("//", "--") keep firing anywhere, as their languages do.
            if (prefix.Length == 1 && prefix[0] == '#' &&
                at > 0 && !char.IsWhiteSpace(line[at - 1]))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool Matches(string line, int at, int n, string what) =>
        at + what.Length <= n &&
        string.CompareOrdinal(line, at, what, 0, what.Length) == 0;

    private static bool FirstNonBlankIs(string line, int at)
    {
        for (int i = 0; i < at; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static void Emit(List<TokenSpan>? tokens, int start, int length, TokenKind kind)
    {
        if (tokens is not null && length > 0)
        {
            tokens.Add(new TokenSpan(start, length, kind));
        }
    }
}
