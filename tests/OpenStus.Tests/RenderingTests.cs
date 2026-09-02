using OpenStus.Rendering;

namespace OpenStus.Tests;

public class RectTests
{
    [Fact]
    public void EdgesAndEmptiness()
    {
        var r = new Rect(3, 4, 10, 5);
        Assert.Equal(13, r.Right);
        Assert.Equal(9, r.Bottom);
        Assert.False(r.IsEmpty);
        Assert.True(new Rect(0, 0, 0, 5).IsEmpty);
        Assert.True(new Rect(0, 0, 5, -1).IsEmpty);
    }

    [Fact]
    public void ContainsUsesExclusiveRightAndBottom()
    {
        var r = new Rect(2, 2, 3, 3);
        Assert.True(r.Contains(2, 2));
        Assert.True(r.Contains(4, 4));
        Assert.False(r.Contains(5, 4));
        Assert.False(r.Contains(4, 5));
        Assert.False(r.Contains(1, 2));
    }

    [Fact]
    public void InflateOffsetAndFromLtrb()
    {
        var r = new Rect(5, 5, 4, 4);
        Assert.Equal(new Rect(4, 3, 6, 8), r.Inflate(1, 2));
        Assert.Equal(new Rect(6, 3, 4, 4), r.Offset(1, -2));
        Assert.Equal(new Rect(1, 2, 9, 8), Rect.FromLTRB(1, 2, 10, 10));
    }
}

public class CellStyleTests
{
    [Fact]
    public void DefaultIsGrayOnBlack()
    {
        Assert.Equal(ConsoleColor.Gray, CellStyle.Default.Fg);
        Assert.Equal(ConsoleColor.Black, CellStyle.Default.Bg);
    }

    [Fact]
    public void WithFgAndWithBgAreNonDestructive()
    {
        var s = new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
        Assert.Equal(new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue), s.WithFg(ConsoleColor.Yellow));
        Assert.Equal(new CellStyle(ConsoleColor.Cyan, ConsoleColor.Black), s.WithBg(ConsoleColor.Black));
        Assert.Equal(new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue), s);
    }
}

public class ScreenBufferClippingTests
{
    private static readonly CellStyle S = new(ConsoleColor.Cyan, ConsoleColor.DarkBlue);

    [Fact]
    public void SetOutsideTheBufferIsIgnored()
    {
        var b = new ScreenBuffer(4, 3);
        b.Set(-1, 0, 'x', S);
        b.Set(0, -1, 'x', S);
        b.Set(4, 0, 'x', S);
        b.Set(0, 3, 'x', S);
        b.Set(int.MaxValue, int.MinValue, 'x', S);
        Assert.Equal("", b.RenderPlainText().Replace("\n", string.Empty));
    }

    [Fact]
    public void GetOutsideTheBufferReturnsABlankDefaultCell()
    {
        var b = new ScreenBuffer(4, 3);
        var c = b.Get(99, 99);
        Assert.Equal(' ', c.Ch);
        Assert.Equal(CellStyle.Default, c.Style);
    }

    [Fact]
    public void WriteClipsOnTheRightAndReportsCellsWritten()
    {
        var b = new ScreenBuffer(5, 2);
        Assert.Equal(2, b.Write(3, 0, "abc", S));
        Assert.Equal('a', b.Get(3, 0).Ch);
        Assert.Equal('b', b.Get(4, 0).Ch);
    }

    [Fact]
    public void WriteClipsOnTheLeftAndKeepsTheColumnAlignment()
    {
        var b = new ScreenBuffer(5, 3);
        Assert.Equal(4, b.Write(-2, 1, "abcdef", S));
        Assert.Equal('c', b.Get(0, 1).Ch);
        Assert.Equal('f', b.Get(3, 1).Ch);
    }

    [Fact]
    public void WriteOnAnInvisibleRowWritesNothing()
    {
        var b = new ScreenBuffer(5, 2);
        Assert.Equal(0, b.Write(0, 9, "abc", S));
        Assert.Equal(0, b.Write(0, -1, "abc", S));
        Assert.Equal(0, b.Write(0, 0, string.Empty, S));
    }

    [Fact]
    public void FillHLineAndVLineClipToTheBuffer()
    {
        var b = new ScreenBuffer(4, 3);
        b.Fill(new Rect(-3, -3, 20, 20), '#', S);
        Assert.Equal("####\n####\n####", b.RenderPlainText());

        b.HLine(-5, 0, 40, '-', S);
        b.VLine(0, -5, 40, '|', S);
        Assert.Equal('|', b.Get(0, 0).Ch);
        Assert.Equal('-', b.Get(3, 0).Ch);
    }

    [Fact]
    public void ConstructorClampsDegenerateSizes()
    {
        var b = new ScreenBuffer(0, -4);
        Assert.Equal(1, b.Width);
        Assert.Equal(1, b.Height);
    }

    [Fact]
    public void ResizePreservesTheOverlappingRegion()
    {
        var b = new ScreenBuffer(4, 2);
        b.Write(0, 0, "abcd", S);
        b.Resize(6, 3);
        Assert.Equal(6, b.Width);
        Assert.Equal(3, b.Height);
        Assert.Equal('d', b.Get(3, 0).Ch);
        Assert.Equal(' ', b.Get(5, 0).Ch);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var b = new ScreenBuffer(3, 1);
        b.Write(0, 0, "abc", S);
        var copy = b.Clone();
        b.Set(0, 0, 'z', S);
        Assert.Equal('a', copy.Get(0, 0).Ch);
        Assert.Equal('z', b.Get(0, 0).Ch);
    }

    [Fact]
    public void FillStyleRecoloursWithoutTouchingCharacters()
    {
        var b = new ScreenBuffer(4, 1);
        b.Write(0, 0, "abcd", S);
        var other = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkRed);
        b.FillStyle(new Rect(1, 0, 2, 1), other);
        Assert.Equal('b', b.Get(1, 0).Ch);
        Assert.Equal(other, b.Get(1, 0).Style);
        Assert.Equal(S, b.Get(0, 0).Style);
        Assert.Equal(S, b.Get(3, 0).Style);
    }

    [Fact]
    public void DrawShadowDarkensTheRightTwoColumnsAndBottomRow()
    {
        var b = new ScreenBuffer(10, 6);
        b.Clear(new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        var box = new Rect(1, 1, 4, 3); // right = 5, bottom = 4
        b.Fill(box, ' ', new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));
        b.DrawShadow(box);

        var shadow = new CellStyle(ConsoleColor.DarkGray, ConsoleColor.Black);
        Assert.Equal(shadow, b.Get(5, 2).Style);
        Assert.Equal(shadow, b.Get(6, 2).Style);
        Assert.Equal(shadow, b.Get(3, 4).Style);
        Assert.Equal(shadow, b.Get(6, 4).Style);

        // Not shadowed: the row above the box's top-right corner, and the box itself.
        Assert.NotEqual(shadow, b.Get(5, 1).Style);
        Assert.NotEqual(shadow, b.Get(1, 4).Style);
        Assert.NotEqual(shadow, b.Get(2, 2).Style);
    }
}

public class WriteFixedTests
{
    private static readonly CellStyle S = new(ConsoleColor.Cyan, ConsoleColor.DarkBlue);

    private static string Row(ScreenBuffer b, int y)
    {
        var chars = new char[b.Width];
        for (int x = 0; x < b.Width; x++)
        {
            chars[x] = b.Get(x, y).Ch;
        }

        return new string(chars);
    }

    [Theory]
    [InlineData("ab", HAlign.Left, "ab....")]
    [InlineData("ab", HAlign.Right, "....ab")]
    [InlineData("ab", HAlign.Center, "..ab..")]
    [InlineData("abc", HAlign.Center, ".abc..")] // odd padding goes to the right
    [InlineData("", HAlign.Left, "......")]
    [InlineData(null, HAlign.Left, "......")]
    [InlineData("abcdef", HAlign.Center, "abcdef")]
    public void PadsToExactlyWidthCells(string? text, HAlign align, string expected)
    {
        var b = new ScreenBuffer(6, 1);
        b.WriteFixed(0, 0, 6, text, S, align, truncateLeft: false, pad: '.');
        Assert.Equal(expected, Row(b, 0));
    }

    [Fact]
    public void TruncatesOnTheRightWithAnEllipsisAtTheCut()
    {
        var b = new ScreenBuffer(5, 1);
        b.WriteFixed(0, 0, 5, "abcdefgh", S);
        Assert.Equal("abcd" + ScreenBuffer.Ellipsis, Row(b, 0));
    }

    [Fact]
    public void TruncatesOnTheLeftKeepingTheTail()
    {
        var b = new ScreenBuffer(5, 1);
        b.WriteFixed(0, 0, 5, "abcdefgh", S, truncateLeft: true);
        Assert.Equal(ScreenBuffer.Ellipsis + "efgh", Row(b, 0));
    }

    [Fact]
    public void LongPathsTruncateFromTheLeft()
    {
        var b = new ScreenBuffer(20, 1);
        b.WriteFixed(0, 0, 20, @"C:\Windows\System32\drivers\etc", S, HAlign.Left, truncateLeft: true);
        Assert.Equal(ScreenBuffer.Ellipsis + @"ystem32\drivers\etc", Row(b, 0));
    }

    [Fact]
    public void WidthOfOneDegradesToJustTheEllipsis()
    {
        var b = new ScreenBuffer(3, 1);
        b.WriteFixed(1, 0, 1, "abc", S);
        Assert.Equal(ScreenBuffer.Ellipsis, b.Get(1, 0).Ch);
        Assert.Equal(' ', b.Get(0, 0).Ch);
        Assert.Equal(' ', b.Get(2, 0).Ch);
    }

    [Fact]
    public void NonPositiveWidthPaintsNothing()
    {
        var b = new ScreenBuffer(4, 1);
        b.Write(0, 0, "wxyz", S);
        b.WriteFixed(0, 0, 0, "abc", S);
        b.WriteFixed(0, 0, -3, "abc", S);
        Assert.Equal("wxyz", Row(b, 0));
    }

    [Fact]
    public void PaintsExactlyWidthCellsAndNoMore()
    {
        var b = new ScreenBuffer(8, 1);
        b.Write(0, 0, "########", S);
        b.WriteFixed(2, 0, 4, "x", S, HAlign.Center);
        Assert.Equal("## x  ##", Row(b, 0));
    }

    [Fact]
    public void ClipsAtTheBufferEdgeWithoutThrowing()
    {
        var b = new ScreenBuffer(4, 1);
        b.WriteFixed(2, 0, 10, "abcdefgh", S);
        Assert.Equal("  ab", Row(b, 0));
    }
}

public class WriteHotkeyTests
{
    private static readonly CellStyle Normal = new(ConsoleColor.Black, ConsoleColor.Gray);
    private static readonly CellStyle Hot = new(ConsoleColor.DarkRed, ConsoleColor.Gray);

    private static string Row(ScreenBuffer b, int y)
    {
        var chars = new char[b.Width];
        for (int x = 0; x < b.Width; x++)
        {
            chars[x] = b.Get(x, y).Ch;
        }

        return new string(chars);
    }

    [Fact]
    public void MarkerSelectsTheFollowingCharacter()
    {
        var b = new ScreenBuffer(8, 1);
        b.WriteHotkey(0, 0, "&File", Normal, Hot);
        Assert.Equal("File   ", Row(b, 0)[..7]);
        Assert.Equal(Hot, b.Get(0, 0).Style);
        Assert.Equal(Normal, b.Get(1, 0).Style);
    }

    [Fact]
    public void MarkerInTheMiddleOfTheWord()
    {
        var b = new ScreenBuffer(8, 1);
        b.WriteHotkey(0, 0, "E&xit", Normal, Hot);
        Assert.Equal("Exit", Row(b, 0)[..4]);
        Assert.Equal(Normal, b.Get(0, 0).Style);
        Assert.Equal(Hot, b.Get(1, 0).Style);
        Assert.Equal(Normal, b.Get(2, 0).Style);
    }

    [Fact]
    public void DoubleAmpersandIsALiteralAmpersand()
    {
        var b = new ScreenBuffer(8, 1);
        b.WriteHotkey(0, 0, "A && B", Normal, Hot);
        Assert.Equal("A & B", Row(b, 0)[..5]);
        for (int x = 0; x < 5; x++)
        {
            Assert.Equal(Normal, b.Get(x, 0).Style);
        }
    }

    [Fact]
    public void OnlyTheFirstMarkerIsHighlighted()
    {
        var b = new ScreenBuffer(8, 1);
        b.WriteHotkey(0, 0, "&a&b", Normal, Hot);
        Assert.Equal("ab", Row(b, 0)[..2]);
        Assert.Equal(Hot, b.Get(0, 0).Style);
        Assert.Equal(Normal, b.Get(1, 0).Style);
    }

    [Fact]
    public void TrailingLoneMarkerIsDropped()
    {
        var b = new ScreenBuffer(8, 1);
        b.WriteHotkey(0, 0, "ab&", Normal, Hot);
        Assert.Equal("ab      ", Row(b, 0));
    }

    [Fact]
    public void EmptyTextDrawsNothing()
    {
        var b = new ScreenBuffer(4, 1);
        b.Write(0, 0, "####", Normal);
        b.WriteHotkey(0, 0, string.Empty, Normal, Hot);
        Assert.Equal("####", Row(b, 0));
    }

    [Theory]
    [InlineData("&File", 4)]
    [InlineData("E&xit", 4)]
    [InlineData("A && B", 5)]
    [InlineData("No marker", 9)]
    [InlineData("trailing&", 8)]
    [InlineData("", 0)]
    [InlineData("&&", 1)]
    public void HotkeyTextLengthIgnoresMarkers(string text, int expected) =>
        Assert.Equal(expected, ScreenBuffer.HotkeyTextLength(text));

    [Theory]
    [InlineData("&File", 'f')]
    [InlineData("E&xit", 'x')]
    [InlineData("Re&Move", 'm')]
    [InlineData("A && B", null)]
    [InlineData("plain", null)]
    [InlineData("trailing&", null)]
    [InlineData("&&&z", 'z')]
    public void HotkeyOfFindsTheFirstRealMarker(string text, char? expected) =>
        Assert.Equal(expected, ScreenBuffer.HotkeyOf(text));
}

public class BoxDrawingTests
{
    private static readonly CellStyle S = new(ConsoleColor.Cyan, ConsoleColor.DarkBlue);

    [Fact]
    public void SingleBoxUsesTheCanonicalGlyphs()
    {
        var b = new ScreenBuffer(6, 4);
        b.DrawBox(new Rect(0, 0, 4, 3), BoxStyle.Single, S);
        Assert.Equal('\u250c', b.Get(0, 0).Ch);
        Assert.Equal('\u2510', b.Get(3, 0).Ch);
        Assert.Equal('\u2514', b.Get(0, 2).Ch);
        Assert.Equal('\u2518', b.Get(3, 2).Ch);
        Assert.Equal('\u2500', b.Get(1, 0).Ch);
        Assert.Equal('\u2502', b.Get(0, 1).Ch);
        Assert.Equal(' ', b.Get(1, 1).Ch); // interior untouched
        Assert.Equal(S, b.Get(0, 0).Style);
    }

    [Fact]
    public void DoubleBoxUsesTheDoubleGlyphs()
    {
        var b = new ScreenBuffer(6, 4);
        b.DrawBox(new Rect(0, 0, 4, 3), BoxStyle.Double, S);
        Assert.Equal('\u2554', b.Get(0, 0).Ch);
        Assert.Equal('\u2557', b.Get(3, 0).Ch);
        Assert.Equal('\u255a', b.Get(0, 2).Ch);
        Assert.Equal('\u255d', b.Get(3, 2).Ch);
        Assert.Equal('\u2550', b.Get(1, 0).Ch);
        Assert.Equal('\u2551', b.Get(0, 1).Ch);
    }

    [Fact]
    public void MixedStylesPairDoubleHorizontalsWithSingleVerticals()
    {
        Assert.Equal('\u2550', BoxChars.Horizontal(BoxStyle.SingleH));
        Assert.Equal('\u2502', BoxChars.Vertical(BoxStyle.SingleH));
        Assert.Equal('\u2552', BoxChars.TopLeft(BoxStyle.SingleH));
        Assert.Equal('\u255b', BoxChars.BottomRight(BoxStyle.SingleH));

        Assert.Equal('\u2500', BoxChars.Horizontal(BoxStyle.SingleV));
        Assert.Equal('\u2551', BoxChars.Vertical(BoxStyle.SingleV));
        Assert.Equal('\u2553', BoxChars.TopLeft(BoxStyle.SingleV));
        Assert.Equal('\u255c', BoxChars.BottomRight(BoxStyle.SingleV));
    }

    [Fact]
    public void TeesAndCrossMatchTheContract()
    {
        Assert.Equal('\u251c', BoxChars.LeftTee(BoxStyle.Single));
        Assert.Equal('\u2524', BoxChars.RightTee(BoxStyle.Single));
        Assert.Equal('\u252c', BoxChars.TopTee(BoxStyle.Single));
        Assert.Equal('\u2534', BoxChars.BottomTee(BoxStyle.Single));
        Assert.Equal('\u253c', BoxChars.Cross(BoxStyle.Single));

        Assert.Equal('\u2560', BoxChars.LeftTee(BoxStyle.Double));
        Assert.Equal('\u2563', BoxChars.RightTee(BoxStyle.Double));
        Assert.Equal('\u2566', BoxChars.TopTee(BoxStyle.Double));
        Assert.Equal('\u2569', BoxChars.BottomTee(BoxStyle.Double));
        Assert.Equal('\u256c', BoxChars.Cross(BoxStyle.Double));
    }

    [Fact]
    public void BoxStyleNoneIsAllSpaces()
    {
        Assert.Equal(' ', BoxChars.TopLeft(BoxStyle.None));
        Assert.Equal(' ', BoxChars.Horizontal(BoxStyle.None));
        Assert.Equal(' ', BoxChars.Cross(BoxStyle.None));
    }

    [Fact]
    public void ScrollBarGlyphsAreTheFarOnes()
    {
        Assert.Equal('\u2591', BoxChars.ScrollBarTrack);
        Assert.Equal('\u2588', BoxChars.ScrollBarThumb);
        Assert.Equal('\u25b2', BoxChars.ScrollUpArrow);
        Assert.Equal('\u25bc', BoxChars.ScrollDownArrow);
    }

    [Fact]
    public void DegenerateRectanglesDegradeToLines()
    {
        var b = new ScreenBuffer(6, 4);
        b.DrawBox(new Rect(0, 0, 1, 3), BoxStyle.Single, S);
        Assert.Equal('\u2502', b.Get(0, 0).Ch);
        Assert.Equal('\u2502', b.Get(0, 2).Ch);

        b.DrawBox(new Rect(2, 0, 3, 1), BoxStyle.Single, S);
        Assert.Equal('\u2500', b.Get(2, 0).Ch);
        Assert.Equal('\u2500', b.Get(4, 0).Ch);

        b.DrawBox(new Rect(0, 0, 0, 0), BoxStyle.Single, S); // must not throw
    }

    [Fact]
    public void BoxClipsWhenItHangsOffTheBuffer()
    {
        // Both vertical edges fall outside a 4x3 buffer; only the two horizontal runs survive.
        var b = new ScreenBuffer(4, 3);
        b.DrawBox(new Rect(-1, 0, 6, 3), BoxStyle.Single, S);
        Assert.Equal('\u2500', b.Get(0, 0).Ch);
        Assert.Equal('\u2500', b.Get(3, 0).Ch);
        Assert.Equal('\u2500', b.Get(3, 2).Ch);
        Assert.Equal(' ', b.Get(0, 1).Ch);
    }
}

public class RenderOutputTests
{
    [Fact]
    public void RenderPlainTextJoinsRowsAndTrimsTrailingSpaces()
    {
        var b = new ScreenBuffer(6, 3);
        b.Write(0, 0, "hi", CellStyle.Default);
        b.Write(2, 2, "yo", CellStyle.Default);
        Assert.Equal("hi\n\n  yo", b.RenderPlainText());
    }

    [Fact]
    public void RenderPlainTextTreatsNulCharactersAsSpaces()
    {
        var b = new ScreenBuffer(3, 1);
        b.Set(0, 0, '\0', CellStyle.Default);
        b.Set(1, 0, 'x', CellStyle.Default);
        Assert.Equal(" x", b.RenderPlainText());
    }

    [Fact]
    public void RenderPlainTextIsDeterministic()
    {
        var b = new ScreenBuffer(8, 2);
        b.DrawBox(new Rect(0, 0, 8, 2), BoxStyle.Single, CellStyle.Default);
        Assert.Equal(b.RenderPlainText(), b.RenderPlainText());
    }

    [Fact]
    public void RenderAnsiEmitsSgrAndResetsAtEndOfLine()
    {
        var b = new ScreenBuffer(4, 1);
        b.Set(0, 0, 'A', new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));
        Assert.Equal("\u001b[96;44mA\u001b[0m", b.RenderAnsi());
    }

    [Fact]
    public void RenderAnsiEmitsOneSequencePerStyleRun()
    {
        var b = new ScreenBuffer(4, 1);
        var a = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        var c = new CellStyle(ConsoleColor.Black, ConsoleColor.Cyan);
        b.Set(0, 0, 'x', a);
        b.Set(1, 0, 'y', a);
        b.Set(2, 0, 'z', c);
        Assert.Equal("\u001b[93;44mxy\u001b[30;106mz\u001b[0m", b.RenderAnsi());
    }

    [Theory]
    [InlineData(ConsoleColor.Black, 30, 40)]
    [InlineData(ConsoleColor.DarkRed, 31, 41)]
    [InlineData(ConsoleColor.DarkGreen, 32, 42)]
    [InlineData(ConsoleColor.DarkYellow, 33, 43)]
    [InlineData(ConsoleColor.DarkBlue, 34, 44)]
    [InlineData(ConsoleColor.DarkMagenta, 35, 45)]
    [InlineData(ConsoleColor.DarkCyan, 36, 46)]
    [InlineData(ConsoleColor.Gray, 37, 47)]
    [InlineData(ConsoleColor.DarkGray, 90, 100)]
    [InlineData(ConsoleColor.Red, 91, 101)]
    [InlineData(ConsoleColor.Green, 92, 102)]
    [InlineData(ConsoleColor.Yellow, 93, 103)]
    [InlineData(ConsoleColor.Blue, 94, 104)]
    [InlineData(ConsoleColor.Magenta, 95, 105)]
    [InlineData(ConsoleColor.Cyan, 96, 106)]
    [InlineData(ConsoleColor.White, 97, 107)]
    public void RenderAnsiMapsAllSixteenColours(ConsoleColor color, int fgCode, int bgCode)
    {
        var fg = new ScreenBuffer(1, 1);
        fg.Set(0, 0, 'q', new CellStyle(color, ConsoleColor.Black));
        Assert.Equal($"\u001b[{fgCode};40mq\u001b[0m", fg.RenderAnsi());

        var bg = new ScreenBuffer(1, 1);
        bg.Set(0, 0, 'q', new CellStyle(ConsoleColor.Black, color));
        Assert.Equal($"\u001b[30;{bgCode}mq\u001b[0m", bg.RenderAnsi());
    }

    [Fact]
    public void RenderAnsiLeavesBlankRowsEmpty()
    {
        var b = new ScreenBuffer(4, 3);
        b.Set(0, 1, 'm', CellStyle.Default);
        Assert.Equal("\n\u001b[37;40mm\u001b[0m\n", b.RenderAnsi());
    }

    [Fact]
    public void RenderAnsiRowCountMatchesRenderPlainText()
    {
        var b = new ScreenBuffer(10, 5);
        b.DrawBox(new Rect(1, 1, 8, 3), BoxStyle.Double, new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));
        Assert.Equal(
            b.RenderPlainText().Split('\n').Length,
            b.RenderAnsi().Split('\n').Length);
    }
}

public class TerminalTests
{
    [Fact]
    public void ForcedSizeCreatesAHeadlessTerminal()
    {
        using var t = Terminal.Create(100, 30);
        Assert.True(t.IsHeadless);
        Assert.Equal(100, t.Width);
        Assert.Equal(30, t.Height);
        Assert.Equal(100, t.Buffer.Width);
        Assert.Equal(30, t.Buffer.Height);
    }

    [Fact]
    public void ForcingOnlyOneDimensionFallsBackToTheDefaultForTheOther()
    {
        using var wide = Terminal.Create(forcedWidth: 200);
        Assert.Equal(200, wide.Width);
        Assert.Equal(40, wide.Height);

        using var tall = Terminal.Create(forcedHeight: 60);
        Assert.Equal(120, tall.Width);
        Assert.Equal(60, tall.Height);
    }

    [Fact]
    public void HeadlessRenderIsSafeAndSyncSizeIsAStableNoOp()
    {
        using var t = Terminal.Create(40, 10);
        t.Buffer.Write(0, 0, "hello", CellStyle.Default);
        t.Render();
        t.Invalidate();
        t.SetCursor(3, 3, visible: true);
        t.Render();
        Assert.False(t.SyncSize());
        Assert.False(t.SyncSize());
        Assert.Equal("hello", t.Buffer.RenderPlainText().Split('\n')[0]);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var t = Terminal.Create(20, 5);
        t.Dispose();
        t.Dispose();
    }

    [Fact]
    public void DegenerateForcedSizesAreClamped()
    {
        using var t = Terminal.Create(0, -5);
        Assert.Equal(1, t.Width);
        Assert.Equal(1, t.Height);
    }
}

/// <summary>
/// Exercises the diff renderer through <see cref="Terminal.BuildFrameText"/>, which produces
/// exactly the escape sequence <see cref="Terminal.Render"/> would write.
/// </summary>
public class TerminalDiffTests
{
    private const string Begin = "\u001b[?2026h\u001b[?25l";
    private const string End = "\u001b[?2026l";
    private static readonly string DefaultSgr = "\u001b[37;40m";

    private static int Count(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void TheFirstFrameIsAFullRepaintWrappedInASynchronizedUpdate()
    {
        using var t = Terminal.Create(4, 1);
        t.Buffer.Write(0, 0, "ab", CellStyle.Default);

        string frame = t.BuildFrameText();
        Assert.Equal(Begin + "\u001b[1;1H" + DefaultSgr + "ab  " + End, frame);
    }

    [Fact]
    public void AnUnchangedFrameWritesNothingAtAll()
    {
        using var t = Terminal.Create(8, 2);
        t.Buffer.Write(0, 0, "hello", CellStyle.Default);
        Assert.NotEqual(string.Empty, t.BuildFrameText());
        Assert.Equal(string.Empty, t.BuildFrameText());
        Assert.Equal(string.Empty, t.BuildFrameText());
    }

    [Fact]
    public void OnlyTheChangedRunIsRepainted()
    {
        using var t = Terminal.Create(8, 1);
        t.Buffer.Write(0, 0, "abcdefgh", CellStyle.Default);
        _ = t.BuildFrameText();

        t.Buffer.Set(4, 0, 'X', CellStyle.Default);
        string frame = t.BuildFrameText();

        Assert.Contains("\u001b[1;5H", frame, StringComparison.Ordinal); // 1-based CUP to column 5
        Assert.Contains("X", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("abcd", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[1;1H", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void AStyleRunEmitsExactlyOneSgrSequence()
    {
        using var t = Terminal.Create(10, 1);
        var a = new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
        var b = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        t.Buffer.Clear(a);
        t.Buffer.Write(4, 0, "XY", b);

        string frame = t.BuildFrameText();
        Assert.Equal(2, Count(frame, "\u001b[96;44m")); // cyan run, break for yellow, cyan again
        Assert.Equal(1, Count(frame, "\u001b[93;44m"));
    }

    [Fact]
    public void ShortCleanGapsAreOverwrittenInsteadOfJumped()
    {
        using var t = Terminal.Create(12, 1);
        t.Buffer.Clear(CellStyle.Default);
        _ = t.BuildFrameText();

        t.Buffer.Set(0, 0, 'A', CellStyle.Default);
        t.Buffer.Set(3, 0, 'B', CellStyle.Default);
        string frame = t.BuildFrameText();

        // One cursor move, not two: the two-cell gap is cheaper to repaint than to skip.
        Assert.Equal(1, Count(frame, "\u001b[1;"));
        Assert.Contains("A  B", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void LongCleanGapsCauseASecondCursorMove()
    {
        using var t = Terminal.Create(40, 1);
        t.Buffer.Clear(CellStyle.Default);
        _ = t.BuildFrameText();

        t.Buffer.Set(0, 0, 'A', CellStyle.Default);
        t.Buffer.Set(30, 0, 'B', CellStyle.Default);
        string frame = t.BuildFrameText();

        Assert.Contains("\u001b[1;1H", frame, StringComparison.Ordinal);
        Assert.Contains("\u001b[1;31H", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidateForcesAFullRepaint()
    {
        using var t = Terminal.Create(4, 1);
        t.Buffer.Write(0, 0, "ab", CellStyle.Default);
        _ = t.BuildFrameText();
        Assert.Equal(string.Empty, t.BuildFrameText());

        t.Invalidate();
        Assert.Equal(Begin + "\u001b[1;1H" + DefaultSgr + "ab  " + End, t.BuildFrameText());
    }

    [Fact]
    public void EachRowGetsItsOwnCursorPositioning()
    {
        using var t = Terminal.Create(3, 3);
        t.Buffer.Write(0, 0, "abc", CellStyle.Default);
        string frame = t.BuildFrameText();

        Assert.Contains("\u001b[1;1H", frame, StringComparison.Ordinal);
        Assert.Contains("\u001b[2;1H", frame, StringComparison.Ordinal);
        Assert.Contains("\u001b[3;1H", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void AVisibleCursorIsPlacedAndShownAtTheEndOfTheFrame()
    {
        using var t = Terminal.Create(6, 2);
        t.Buffer.Write(0, 0, "hi", CellStyle.Default);
        t.SetCursor(2, 1, visible: true);

        string frame = t.BuildFrameText();
        Assert.EndsWith("\u001b[2;3H\u001b[?25h" + End, frame, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutOfRangeCursorIsClampedIntoTheBuffer()
    {
        using var t = Terminal.Create(6, 2);
        t.SetCursor(99, 99, visible: true);
        Assert.EndsWith("\u001b[2;6H\u001b[?25h" + End, t.BuildFrameText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AHiddenCursorProducesNoTrailingCursorSequence()
    {
        using var t = Terminal.Create(4, 1);
        t.Buffer.Write(0, 0, "ab", CellStyle.Default);
        t.SetCursor(0, 0, visible: false);
        Assert.DoesNotContain("\u001b[?25h", t.BuildFrameText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResizingTheBufferForcesAFullRepaintWithoutCorruptingTheDiff()
    {
        using var t = Terminal.Create(4, 1);
        t.Buffer.Write(0, 0, "abcd", CellStyle.Default);
        _ = t.BuildFrameText();

        t.Buffer.Resize(6, 2);
        string frame = t.BuildFrameText();
        Assert.Contains("\u001b[1;1H", frame, StringComparison.Ordinal);
        Assert.Contains("abcd", frame, StringComparison.Ordinal);
        Assert.Contains("\u001b[2;1H", frame, StringComparison.Ordinal);

        Assert.Equal(string.Empty, t.BuildFrameText());
    }
}
