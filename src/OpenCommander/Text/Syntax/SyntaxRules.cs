namespace OpenCommander.Text.Syntax;

/// <summary>What a run of characters is, for colouring.</summary>
public enum TokenKind : byte
{
    /// <summary>Ordinary text; never emitted as a span - it is whatever the spans leave uncovered.</summary>
    Text,

    /// <summary>A reserved word of the language, or a JSON object key.</summary>
    Keyword,

    /// <summary>A string or character literal, delimiters included.</summary>
    String,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A line or block comment, markers included.</summary>
    Comment,

    /// <summary>A preprocessor directive, <c>#</c> included.</summary>
    Preprocessor,
}

/// <summary>One coloured run inside a line, in character columns.</summary>
/// <param name="Start">First character of the run.</param>
/// <param name="Length">How many characters it covers; always positive.</param>
/// <param name="Kind">What the run is.</param>
public readonly record struct TokenSpan(int Start, int Length, TokenKind Kind);

/// <summary>
/// Which multi-line construct is open when a line ends - the whole state the tokenizer carries
/// from one line to the next. Two bytes, so caching one per line costs nothing.
/// </summary>
/// <param name="Mode">The open construct.</param>
/// <param name="Arg">
/// A small payload some modes carry - CSV's open quoted field remembers which column it is in,
/// so the colour cycle survives a field that spans lines. Zero for every other mode.
/// </param>
public readonly record struct SyntaxState(SyntaxMode Mode, byte Arg = 0)
{
    /// <summary>The state outside any multi-line construct; the entry state of line zero.</summary>
    public static SyntaxState None => default;
}

/// <summary>The multi-line construct a <see cref="SyntaxState"/> can be inside.</summary>
public enum SyntaxMode : byte
{
    /// <summary>Ordinary code.</summary>
    None,

    /// <summary>Inside a block comment (<c>/* */</c>, <c>&lt;# #&gt;</c>, <c>&lt;!-- --&gt;</c>).</summary>
    BlockComment,

    /// <summary>Inside a C# verbatim string (<c>@"..."</c>), where <c>""</c> is the only escape.</summary>
    VerbatimString,

    /// <summary>Inside a JavaScript template literal (<c>`...`</c>).</summary>
    TemplateString,

    /// <summary>Inside a Python <c>'''...'''</c> string.</summary>
    TripleSingleString,

    /// <summary>Inside a Python <c>"""..."""</c> string.</summary>
    TripleDoubleString,

    /// <summary>Inside a markup tag that has not reached its <c>&gt;</c> yet.</summary>
    InsideTag,

    /// <summary>Inside an XML <c>&lt;![CDATA[ ... ]]&gt;</c> section.</summary>
    RawText,

    /// <summary>Inside a Markdown fenced code block (<c>```</c>).</summary>
    FencedCode,

    /// <summary>Past a CSV file's header row.</summary>
    CsvBody,

    /// <summary>Inside a quoted CSV field of the header row that spans lines.</summary>
    CsvQuotedHeader,

    /// <summary>Inside a quoted CSV field of a body row that spans lines.</summary>
    CsvQuotedBody,
}

/// <summary>Which of the tokenizer's scanners a language uses.</summary>
public enum SyntaxFamily : byte
{
    /// <summary>The C-like scanner: comments, strings, numbers, keywords, preprocessor.</summary>
    Code,

    /// <summary>The markup scanner: tags, attributes, entities, CDATA, <c>&lt;!-- --&gt;</c>.</summary>
    Markup,

    /// <summary>The Markdown scanner: headings, fences, inline code, links, quotes.</summary>
    Markdown,

    /// <summary>The CSV scanner: a white header row, then columns cycling through the colours.</summary>
    Csv,
}

/// <summary>
/// Everything the tokenizer needs to know about one language, as data. There is no per-language
/// code: C#, SQL and Python differ only in which of these switches are on and which words are
/// reserved, which is what keeps a new language a twenty-line entry in
/// <see cref="SyntaxRegistry"/>.
/// </summary>
public sealed class SyntaxRules
{
    /// <summary>The language name, for tests and diagnostics.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Which scanner reads the language. The switches below configure the
    /// <see cref="SyntaxFamily.Code"/> scanner and are ignored by the other two, whose grammars
    /// are fixed.
    /// </summary>
    public SyntaxFamily Family { get; init; } = SyntaxFamily.Code;

    /// <summary>Prefixes that start a comment running to the end of the line (<c>"//"</c>, <c>"--"</c>, <c>"#"</c>).</summary>
    public string[] LineComments { get; init; } = [];

    /// <summary>The opening of a block comment, or <see langword="null"/> when the language has none.</summary>
    public string? BlockCommentOpen { get; init; }

    /// <summary>The closing of a block comment.</summary>
    public string? BlockCommentClose { get; init; }

    /// <summary>The reserved words. Built with the right comparer for <see cref="CaseSensitive"/>.</summary>
    public required HashSet<string> Keywords { get; init; }

    /// <summary>Whether keywords match case-sensitively; SQL famously does not.</summary>
    public bool CaseSensitive { get; init; } = true;

    /// <summary>Whether <c>"..."</c> is a string.</summary>
    public bool DoubleQuoteStrings { get; init; } = true;

    /// <summary>Whether <c>'...'</c> is a string or character literal.</summary>
    public bool SingleQuoteStrings { get; init; } = true;

    /// <summary>Whether a backslash escapes the next character inside a string.</summary>
    public bool BackslashEscapes { get; init; } = true;

    /// <summary>Whether a doubled quote escapes itself inside a string, SQL style (<c>''</c>).</summary>
    public bool DoubledQuoteEscapes { get; init; }

    /// <summary>Whether <c>@"..."</c> is a multi-line verbatim string, C# style.</summary>
    public bool VerbatimStrings { get; init; }

    /// <summary>Whether <c>`...`</c> is a multi-line template literal, JavaScript style.</summary>
    public bool TemplateLiterals { get; init; }

    /// <summary>Whether <c>'''...'''</c> and <c>"""..."""</c> are multi-line strings, Python style.</summary>
    public bool TripleQuotedStrings { get; init; }

    /// <summary>
    /// Whether a <c>#</c> as the first non-blank character starts a preprocessor directive. Never
    /// combined with a <c>#</c> line comment - a language has one or the other.
    /// </summary>
    public bool HashPreprocessor { get; init; }

    /// <summary>
    /// Whether a string immediately followed by a colon is an object key and coloured as a
    /// keyword - what makes JSON readable.
    /// </summary>
    public bool JsonKeys { get; init; }

    /// <summary>Builds a keyword set with the comparer matching <paramref name="caseSensitive"/>.</summary>
    /// <param name="caseSensitive">Whether the language's keywords are case-sensitive.</param>
    /// <param name="words">The reserved words.</param>
    /// <returns>The set.</returns>
    public static HashSet<string> Words(bool caseSensitive, params string[] words) =>
        new(words, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
}
