using System.Text;
using OpenCommander.Editor;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Text.Syntax;
using OpenCommander.Theming;
using OpenCommander.Viewer;

namespace OpenCommander.Tests;

/// <summary>The line tokenizer: spans, keywords and the state carried between lines.</summary>
public class SyntaxTokenizerTests
{
    private static readonly SyntaxRules CSharp = SyntaxRegistry.ForPath("a.cs")!;
    private static readonly SyntaxRules Json = SyntaxRegistry.ForPath("a.json")!;
    private static readonly SyntaxRules Sql = SyntaxRegistry.ForPath("a.sql")!;
    private static readonly SyntaxRules JavaScript = SyntaxRegistry.ForPath("a.js")!;
    private static readonly SyntaxRules Python = SyntaxRegistry.ForPath("a.py")!;

    private static List<TokenSpan> Tokens(string line, SyntaxRules rules, SyntaxState state = default)
    {
        var tokens = new List<TokenSpan>();
        SyntaxTokenizer.TokenizeLine(line, rules, state, tokens);
        return tokens;
    }

    private static string Slice(string line, TokenSpan t) => line.Substring(t.Start, t.Length);

    [Fact]
    public void KeywordsStringsNumbersAndCommentsAreAllSpanned()
    {
        const string Line = "if (x == 42) return \"done\"; // fine";
        List<TokenSpan> tokens = Tokens(Line, CSharp);

        Assert.Collection(
            tokens,
            t => { Assert.Equal(TokenKind.Keyword, t.Kind); Assert.Equal("if", Slice(Line, t)); },
            t => { Assert.Equal(TokenKind.Number, t.Kind); Assert.Equal("42", Slice(Line, t)); },
            t => { Assert.Equal(TokenKind.Keyword, t.Kind); Assert.Equal("return", Slice(Line, t)); },
            t => { Assert.Equal(TokenKind.String, t.Kind); Assert.Equal("\"done\"", Slice(Line, t)); },
            t => { Assert.Equal(TokenKind.Comment, t.Kind); Assert.Equal("// fine", Slice(Line, t)); });
    }

    [Fact]
    public void AnIdentifierMerelyContainingAKeywordIsNotOne()
    {
        const string Line = "int interval = classify(iffy);";
        List<TokenSpan> tokens = Tokens(Line, CSharp);

        Assert.Single(tokens, t => t.Kind == TokenKind.Keyword);
        Assert.Equal("int", Slice(Line, tokens[0]));
    }

    [Fact]
    public void ABlockCommentCarriesItsStateAcrossLines()
    {
        var tokens = new List<TokenSpan>();

        SyntaxState after1 = SyntaxTokenizer.TokenizeLine("code(); /* begins", CSharp, default, tokens);
        Assert.Equal(SyntaxMode.BlockComment, after1.Mode);

        tokens.Clear();
        SyntaxState after2 = SyntaxTokenizer.TokenizeLine("all comment here", CSharp, after1, tokens);
        Assert.Equal(SyntaxMode.BlockComment, after2.Mode);
        TokenSpan whole = Assert.Single(tokens);
        Assert.Equal(TokenKind.Comment, whole.Kind);
        Assert.Equal(0, whole.Start);
        Assert.Equal("all comment here".Length, whole.Length);

        tokens.Clear();
        SyntaxState after3 = SyntaxTokenizer.TokenizeLine("done */ int x;", CSharp, after2, tokens);
        Assert.Equal(SyntaxMode.None, after3.Mode);
        Assert.Equal(TokenKind.Comment, tokens[0].Kind);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword); // int, after the close
    }

    [Fact]
    public void AVerbatimStringSwallowsBackslashesAndDoubledQuotes()
    {
        const string Line = "var p = @\"C:\\temp\\\"\"quoted\"\"\";";
        List<TokenSpan> tokens = Tokens(Line, CSharp);

        TokenSpan s = Assert.Single(tokens, t => t.Kind == TokenKind.String);
        Assert.StartsWith("@\"C:\\temp\\", Slice(Line, s), StringComparison.Ordinal);
        Assert.EndsWith("\"", Slice(Line, s), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenVerbatimStringCarriesAcrossLines()
    {
        SyntaxState state = SyntaxTokenizer.ScanLine("var s = @\"first", CSharp, default);
        Assert.Equal(SyntaxMode.VerbatimString, state.Mode);

        var tokens = new List<TokenSpan>();
        state = SyntaxTokenizer.TokenizeLine("still \"\" inside", CSharp, state, tokens);
        Assert.Equal(SyntaxMode.VerbatimString, state.Mode);
        Assert.Equal(TokenKind.String, Assert.Single(tokens).Kind);

        state = SyntaxTokenizer.ScanLine("over\" + rest;", CSharp, state);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void PreprocessorDirectivesClaimTheLineOnlyFromTheMargin()
    {
        List<TokenSpan> margin = Tokens("  #region Setup", CSharp);
        TokenSpan directive = Assert.Single(margin);
        Assert.Equal(TokenKind.Preprocessor, directive.Kind);

        // '#' in the middle of a line is not a directive in C#.
        List<TokenSpan> middle = Tokens("var tag = x; # not one", CSharp);
        Assert.DoesNotContain(middle, t => t.Kind == TokenKind.Preprocessor);
    }

    [Fact]
    public void JsonKeysAreKeywordsAndValuesAreStrings()
    {
        const string Line = "  \"name\": \"value\", \"n\": 42, \"on\": true";
        List<TokenSpan> tokens = Tokens(Line, Json);

        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal("\"name\"", Slice(Line, tokens[0]));
        Assert.Equal(TokenKind.String, tokens[1].Kind);
        Assert.Equal("\"value\"", Slice(Line, tokens[1]));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Number && Slice(Line, t) == "42");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && Slice(Line, t) == "true");
    }

    [Fact]
    public void SqlIsCaseInsensitiveAndUsesDoubledQuoteEscapes()
    {
        const string Line = "SELECT name FROM t WHERE x = 'it''s' -- tail";
        List<TokenSpan> tokens = Tokens(Line, Sql);

        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && Slice(Line, t) == "SELECT");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && Slice(Line, t) == "FROM");
        TokenSpan s = Assert.Single(tokens, t => t.Kind == TokenKind.String);
        Assert.Equal("'it''s'", Slice(Line, s));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Comment && Slice(Line, t) == "-- tail");
    }

    [Fact]
    public void AJavaScriptTemplateLiteralCarriesAcrossLines()
    {
        SyntaxState state = SyntaxTokenizer.ScanLine("const s = `first", JavaScript, default);
        Assert.Equal(SyntaxMode.TemplateString, state.Mode);

        state = SyntaxTokenizer.ScanLine("middle \\` still", JavaScript, state);
        Assert.Equal(SyntaxMode.TemplateString, state.Mode);

        state = SyntaxTokenizer.ScanLine("end`;", JavaScript, state);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void APythonTripleQuotedStringCarriesAcrossLines()
    {
        SyntaxState state = SyntaxTokenizer.ScanLine("doc = \"\"\"begin", Python, default);
        Assert.Equal(SyntaxMode.TripleDoubleString, state.Mode);

        state = SyntaxTokenizer.ScanLine("has 'quotes' and \" inside", Python, state);
        Assert.Equal(SyntaxMode.TripleDoubleString, state.Mode);

        state = SyntaxTokenizer.ScanLine("over\"\"\"", Python, state);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void AnUnterminatedPlainStringDoesNotLeakIntoTheNextLine()
    {
        SyntaxState state = SyntaxTokenizer.ScanLine("var s = \"broken", CSharp, default);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void NumbersStopAtRangesAndMemberCalls()
    {
        const string Ranges = "var s = arr[0..Count]; var t = 1.ToString();";
        List<TokenSpan> tokens = Tokens(Ranges, CSharp);

        Assert.All(
            tokens.Where(t => t.Kind == TokenKind.Number),
            t => Assert.True(Slice(Ranges, t) is "0" or "1"));

        // Real fractions, exponents, separators, hex and suffixes stay one span.
        foreach ((string line, string expected) in new[]
                 {
                     ("x = 1.5e-3;", "1.5e-3"),
                     ("x = 1_000_000;", "1_000_000"),
                     ("x = 0xFF_EC;", "0xFF_EC"),
                     ("x = 1.5f;", "1.5f"),
                     ("x = 42UL;", "42UL"),
                 })
        {
            TokenSpan n = Assert.Single(Tokens(line, CSharp), t => t.Kind == TokenKind.Number);
            Assert.Equal(expected, Slice(line, n));
        }
    }

    [Fact]
    public void PowerShellStringsUseDoublingNotBackslashes()
    {
        SyntaxRules ps = SyntaxRegistry.ForPath("a.ps1")!;
        const string Line = "Set-Location \"C:\\temp\\\" ; $x = 1";
        List<TokenSpan> tokens = Tokens(Line, ps);

        TokenSpan s = Assert.Single(tokens, t => t.Kind == TokenKind.String);
        Assert.Equal("\"C:\\temp\\\"", Slice(Line, s));
    }

    [Fact]
    public void AnApostropheInProseDoesNotOpenAString()
    {
        SyntaxRules yaml = SyntaxRegistry.ForPath("a.yaml")!;
        const string Line = "note: don't panic  # comment";
        List<TokenSpan> tokens = Tokens(Line, yaml);

        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.String);
        TokenSpan c = Assert.Single(tokens, t => t.Kind == TokenKind.Comment);
        Assert.Equal("# comment", Slice(Line, c));
    }

    [Fact]
    public void AHashInsideAWordIsNotACommentButOneAfterSpaceIs()
    {
        SyntaxRules sh = SyntaxRegistry.ForPath("a.sh")!;

        Assert.DoesNotContain(
            Tokens("wget http://host/page#frag", sh),
            t => t.Kind == TokenKind.Comment);

        TokenSpan c = Assert.Single(Tokens("run  # note", sh), t => t.Kind == TokenKind.Comment);
        Assert.Equal(TokenKind.Comment, c.Kind);
    }

    [Fact]
    public void AnInterpolatedVerbatimStringIsVerbatimInBothSpellings()
    {
        foreach (string opener in new[] { "@$\"", "$@\"" })
        {
            string line = "var w = " + opener + "C:\\a\\";
            SyntaxState state = SyntaxTokenizer.ScanLine(line, CSharp, default);
            Assert.Equal(SyntaxMode.VerbatimString, state.Mode);
        }
    }

    [Fact]
    public void AConstructClosingPastTheCapDoesNotBleedIntoTheRestOfTheFile()
    {
        string line = "/* " + new string('x', SyntaxTokenizer.MaxScanLength) + " */ int x;";
        SyntaxState state = SyntaxTokenizer.ScanLine(line, CSharp, default);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void AnOverlongLineIsCappedWithoutCarryingBogusState()
    {
        string line = "x = 1; " + new string('y', SyntaxTokenizer.MaxScanLength) + " /* never seen";
        SyntaxState state = SyntaxTokenizer.ScanLine(line, CSharp, default);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void MarkupColoursTagsAttributesCommentsAndEntities()
    {
        SyntaxRules xml = SyntaxRegistry.ForPath("a.xml")!;
        const string Line = "<item id=\"7\">a &amp; b</item> <!-- note -->";
        List<TokenSpan> tokens = Tokens(Line, xml);

        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && Slice(Line, t) == "<item");
        Assert.Contains(tokens, t => t.Kind == TokenKind.String && Slice(Line, t) == "\"7\"");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Number && Slice(Line, t) == "&amp;");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && Slice(Line, t) == "</item");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Comment && Slice(Line, t) == "<!-- note -->");
    }

    [Fact]
    public void MarkupCarriesTagsCommentsAndCDataAcrossLines()
    {
        SyntaxRules xml = SyntaxRegistry.ForPath("a.xml")!;

        // A tag broken across lines: the attribute on the continuation line is still a string.
        SyntaxState state = SyntaxTokenizer.ScanLine("<a href=\"x\"", xml, default);
        Assert.Equal(SyntaxMode.InsideTag, state.Mode);
        var tokens = new List<TokenSpan>();
        state = SyntaxTokenizer.TokenizeLine("   title=\"t\">tail", xml, state, tokens);
        Assert.Equal(SyntaxMode.None, state.Mode);
        Assert.Contains(tokens, t => t.Kind == TokenKind.String && t.Length == 3);

        // Comments and CDATA sections span lines through their own states.
        state = SyntaxTokenizer.ScanLine("<!-- open", xml, default);
        Assert.Equal(SyntaxMode.BlockComment, state.Mode);
        state = SyntaxTokenizer.ScanLine("still --> <x/>", xml, state);
        Assert.Equal(SyntaxMode.None, state.Mode);

        state = SyntaxTokenizer.ScanLine("<![CDATA[ raw <notatag>", xml, default);
        Assert.Equal(SyntaxMode.RawText, state.Mode);
        state = SyntaxTokenizer.ScanLine("done ]]>", xml, state);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void MarkupColoursDeclarationsAsPreprocessor()
    {
        SyntaxRules xml = SyntaxRegistry.ForPath("a.xml")!;
        const string Line = "<?xml version=\"1.0\"?><!DOCTYPE html>";
        List<TokenSpan> tokens = Tokens(Line, xml);

        Assert.Equal(2, tokens.Count(t => t.Kind == TokenKind.Preprocessor));
    }

    [Fact]
    public void MarkdownColoursHeadingsFencesCodeLinksAndQuotes()
    {
        SyntaxRules md = SyntaxRegistry.ForPath("a.md")!;

        TokenSpan heading = Assert.Single(Tokens("# Title", md));
        Assert.Equal(TokenKind.Keyword, heading.Kind);

        TokenSpan quote = Assert.Single(Tokens("> quoted text", md));
        Assert.Equal(TokenKind.Comment, quote.Kind);

        const string Inline = "Use `dotnet build` here";
        TokenSpan code = Assert.Single(Tokens(Inline, md));
        Assert.Equal(TokenKind.Preprocessor, code.Kind);
        Assert.Equal("`dotnet build`", Slice(Inline, code));

        const string Link = "See [the docs](https://example.org) now";
        List<TokenSpan> link = Tokens(Link, md);
        Assert.Contains(link, t => t.Kind == TokenKind.Keyword && Slice(Link, t) == "[the docs]");
        Assert.Contains(link, t => t.Kind == TokenKind.String && Slice(Link, t) == "(https://example.org)");
    }

    [Fact]
    public void MarkdownFencedCodeBlocksCarryTheirStateUntilTheClosingFence()
    {
        SyntaxRules md = SyntaxRegistry.ForPath("a.md")!;

        SyntaxState state = SyntaxTokenizer.ScanLine("```csharp", md, default);
        Assert.Equal(SyntaxMode.FencedCode, state.Mode);

        var tokens = new List<TokenSpan>();
        state = SyntaxTokenizer.TokenizeLine("# not a heading in here", md, state, tokens);
        Assert.Equal(SyntaxMode.FencedCode, state.Mode);
        Assert.Equal(TokenKind.Preprocessor, Assert.Single(tokens).Kind);

        state = SyntaxTokenizer.ScanLine("```", md, state);
        Assert.Equal(SyntaxMode.None, state.Mode);
    }

    [Fact]
    public void TheRegistryKnowsTheAdvertisedExtensionsAndIgnoresTheRest()
    {
        Assert.NotNull(SyntaxRegistry.ForPath(@"C:\src\Program.cs"));
        Assert.NotNull(SyntaxRegistry.ForPath("data.JSON")); // case-insensitive
        Assert.NotNull(SyntaxRegistry.ForPath("query.sql"));
        Assert.NotNull(SyntaxRegistry.ForPath("app.ts"));
        Assert.NotNull(SyntaxRegistry.ForPath(".gitignore"));
        Assert.Null(SyntaxRegistry.ForPath("readme.txt"));
        Assert.Null(SyntaxRegistry.ForPath("noextension"));
        Assert.Null(SyntaxRegistry.ForPath(null));
        Assert.Null(SyntaxRegistry.ForPath(string.Empty));
    }

    [Fact]
    public void EveryShippedLanguageTokenizesACodeSampleWithoutThrowing()
    {
        const string Sample = "if (x == 'a\\'b') { /* c */ return \"d\"; } # e -- f `g` @\"h\" '''i''' 1.5e-3";

        foreach (SyntaxRules rules in SyntaxRegistry.All)
        {
            var tokens = new List<TokenSpan>();
            SyntaxState state = default;
            for (int pass = 0; pass < 3; pass++)
            {
                state = SyntaxTokenizer.TokenizeLine(Sample, rules, state, tokens);
            }

            // Spans never overlap and never leave the line.
            int last = -1;
            foreach (TokenSpan t in tokens)
            {
                Assert.True(t.Length > 0);
                Assert.True(t.Start + t.Length <= Sample.Length);
            }

            _ = last;
        }
    }
}

/// <summary>The per-line state cache the editor highlights through.</summary>
public class LineStateCacheTests
{
    private static readonly SyntaxRules CSharp = SyntaxRegistry.ForPath("a.cs")!;

    [Fact]
    public void StatesChainThroughABlockComment()
    {
        TextBuffer buffer = TextBuffer.FromText("/* open\ninside\nclosed */\nint x;");
        var cache = new LineStateCache();

        Assert.Equal(SyntaxMode.None, cache.EntryState(buffer, CSharp, 0).Mode);
        Assert.Equal(SyntaxMode.BlockComment, cache.EntryState(buffer, CSharp, 1).Mode);
        Assert.Equal(SyntaxMode.BlockComment, cache.EntryState(buffer, CSharp, 2).Mode);
        Assert.Equal(SyntaxMode.None, cache.EntryState(buffer, CSharp, 3).Mode);
    }

    [Fact]
    public void AnEditAboveInvalidatesTheStatesBelowIt()
    {
        TextBuffer buffer = TextBuffer.FromText("int a;\nint b;\nint c;");
        var cache = new LineStateCache();
        Assert.Equal(SyntaxMode.None, cache.EntryState(buffer, CSharp, 2).Mode);

        // Opening a block comment on line 0 must flow into the lines below.
        buffer.Insert(0, 6, " /*");
        Assert.Equal(SyntaxMode.BlockComment, cache.EntryState(buffer, CSharp, 1).Mode);
        Assert.Equal(SyntaxMode.BlockComment, cache.EntryState(buffer, CSharp, 2).Mode);

        // And closing it again must flow too - undo exercises ApplyBackward.
        Assert.True(buffer.Undo(out _, out _));
        Assert.Equal(SyntaxMode.None, cache.EntryState(buffer, CSharp, 2).Mode);
    }
}

/// <summary>The two surfaces actually colouring what they draw.</summary>
public class SyntaxDrawingTests
{
    private static FileEditor Editor(Theme theme, string path, string text)
    {
        TextBuffer buffer = TextBuffer.FromText(text);
        buffer.FilePath = path;

        var editor = new FileEditor(theme, new FakeUi(), buffer);
        editor.Layout(new Rect(0, 0, 60, 10));
        return editor;
    }

    [Fact]
    public void TheEditorColoursKeywordsStringsAndComments()
    {
        var theme = Theme.FarDefault();
        FileEditor editor = Editor(theme, @"C:\demo\sample.cs", "return \"text\"; // note");

        var screen = new ScreenBuffer(60, 10);
        editor.Draw(screen);

        // Cell (0,0) carries the inverted caret, so the spans are asserted next to it.
        Assert.Equal(theme.SyntaxKeyword, screen.Get(1, 0).Style);   // 'return'
        Assert.Equal(theme.SyntaxString, screen.Get(8, 0).Style);    // '"text"'
        Assert.Equal(theme.SyntaxComment, screen.Get(16, 0).Style);  // '// note'
        Assert.Equal(theme.EditorText, screen.Get(6, 0).Style);      // the space between
    }

    [Fact]
    public void TheEditorDrawsPlainWhenHighlightingIsOffOrTheTypeIsUnknown()
    {
        var theme = Theme.FarDefault();
        var screen = new ScreenBuffer(60, 10);

        // Column 1: cell (0,0) carries the inverted caret.
        FileEditor off = Editor(theme, @"C:\demo\sample.cs", "return 1;");
        off.SyntaxHighlight = false;
        off.Draw(screen);
        Assert.Equal(theme.EditorText, screen.Get(1, 0).Style);

        FileEditor unknown = Editor(theme, @"C:\demo\notes.txt", "return 1;");
        unknown.Draw(screen);
        Assert.Equal(theme.EditorText, screen.Get(1, 0).Style);
    }

    [Fact]
    public void TheEditorSelectionCoversTheColouring()
    {
        var theme = Theme.FarDefault();
        FileEditor editor = Editor(theme, @"C:\demo\sample.cs", "return x;");

        // Shift+End selects the whole first line.
        editor.HandleInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.End, '\0', KeyMods.Shift)));

        var screen = new ScreenBuffer(60, 10);
        editor.Draw(screen);
        Assert.Equal(theme.EditorSelected, screen.Get(0, 0).Style);
    }

    [Fact]
    public void TheViewerColoursTextAcrossAMultiLineComment()
    {
        byte[] content = Encoding.UTF8.GetBytes("/* one\ntwo\nthree */\nreturn \"x\"; // done\n");
        using var file = new TempFile(content, ".cs");

        var theme = Theme.FarDefault();
        using var viewer = new FileViewer(theme, new FakeUi(), file.Path);
        viewer.Layout(new Rect(0, 0, 60, 10));

        var screen = new ScreenBuffer(60, 10);
        viewer.Draw(screen);

        // Body rows start under the title row; the block comment covers the first three lines.
        Assert.Equal(theme.SyntaxComment, screen.Get(0, 1).Style);
        Assert.Equal(theme.SyntaxComment, screen.Get(0, 2).Style);
        Assert.Equal(theme.SyntaxComment, screen.Get(0, 3).Style);
        Assert.Equal(theme.SyntaxKeyword, screen.Get(0, 4).Style); // return
    }
}
