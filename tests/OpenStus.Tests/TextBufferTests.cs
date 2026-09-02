using System.Text;
using OpenStus.Editor;
using OpenStus.Text;

namespace OpenStus.Tests;

public class TextBufferContentTests
{
    [Fact]
    public void ANewBufferHasOneEmptyLineAndIsClean()
    {
        var buffer = new TextBuffer();

        Assert.Equal(1, buffer.LineCount);
        Assert.Equal(string.Empty, buffer[0]);
        Assert.False(buffer.IsModified);
        Assert.False(buffer.CanUndo);
        Assert.False(buffer.CanRedo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("one")]
    [InlineData("a\nb")]
    [InlineData("a\nb\n")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\rb\r")]
    [InlineData("a\r\nb\nc\rd")]
    [InlineData("\n\n\n")]
    public void LoadingAndReadingBackReproducesTheTextExactly(string text)
    {
        Assert.Equal(text, TextBuffer.FromText(text).GetText());
    }

    [Fact]
    public void ATrailingTerminatorBecomesAnEmptyFinalLine()
    {
        var buffer = TextBuffer.FromText("a\nb\n");

        Assert.Equal(3, buffer.LineCount);
        Assert.Equal("a", buffer[0]);
        Assert.Equal("b", buffer[1]);
        Assert.Equal(string.Empty, buffer[2]);
        Assert.Equal(string.Empty, buffer.GetLineEnding(2));
    }

    [Theory]
    [InlineData("a\r\nb", LineEndingStyle.Crlf)]
    [InlineData("a\nb", LineEndingStyle.Lf)]
    [InlineData("a\rb", LineEndingStyle.Cr)]
    [InlineData("a\r\nb\nc", LineEndingStyle.Mixed)]
    [InlineData("single", LineEndingStyle.None)]
    public void TheLineEndingStyleIsDerivedFromWhatIsActuallyThere(string text, LineEndingStyle expected)
    {
        Assert.Equal(expected, TextBuffer.FromText(text).LineEnding);
    }

    [Fact]
    public void MixedTerminatorsSurviveAnEditToAnUnrelatedLine()
    {
        var buffer = TextBuffer.FromText("a\r\nb\nc\rd");

        buffer.ReplaceLine(0, "A");

        Assert.Equal("A\r\nb\nc\rd", buffer.GetText());
        Assert.Equal(LineEndingStyle.Mixed, buffer.LineEnding);
    }

    [Fact]
    public void NewLinesUseTheDominantConventionOfAMixedFile()
    {
        var buffer = TextBuffer.FromText("a\r\nb\r\nc\nd");

        Assert.Equal(LineEndingStyle.Crlf, buffer.NewLineStyle);
    }

    [Fact]
    public void OutOfRangeReadsAreEmptyRatherThanThrowing()
    {
        var buffer = TextBuffer.FromText("a");

        Assert.Equal(string.Empty, buffer.GetLine(-1));
        Assert.Equal(string.Empty, buffer.GetLine(99));
        Assert.Equal(0, buffer.LineLength(99));
        Assert.Equal(string.Empty, buffer.GetLineEnding(99));
    }

    [Fact]
    public void ClampKeepsPositionsInsideTheDocument()
    {
        var buffer = TextBuffer.FromText("ab\ncdef");

        Assert.Equal((0, 2), buffer.Clamp(0, 40));
        Assert.Equal((1, 4), buffer.Clamp(9, 40));
        Assert.Equal((0, 0), buffer.Clamp(-3, -3));
        Assert.Equal((1, 4), buffer.EndPosition);
    }

    [Fact]
    public void TheDefaultTabStopIsEightMatchingFarAndTheViewer()
    {
        Assert.Equal(8, TextBuffer.DefaultTabSize);
        Assert.Equal(8, new TextBuffer().TabSize);
    }
}

public class TextBufferEditTests
{
    private static TextBuffer Lf(string text)
    {
        var buffer = TextBuffer.FromText(text);
        buffer.NewLineStyle = LineEndingStyle.Lf;
        return buffer;
    }

    [Fact]
    public void InsertingWithinALine()
    {
        var buffer = Lf("hello");

        var end = buffer.Insert(0, 5, " world");

        Assert.Equal("hello world", buffer.GetText());
        Assert.Equal((0, 11), end);
        Assert.True(buffer.IsModified);
    }

    [Fact]
    public void InsertingTextContainingLineBreaksSplitsTheLine()
    {
        var buffer = Lf("ac");

        var end = buffer.Insert(0, 1, "1\n2\n");

        Assert.Equal("a1\n2\nc", buffer.GetText());
        Assert.Equal(3, buffer.LineCount);
        Assert.Equal((2, 0), end);
    }

    [Fact]
    public void PastedLineBreaksAreRewrittenToTheDocumentConvention()
    {
        var buffer = TextBuffer.FromText("a\r\nb");

        buffer.Insert(0, 1, "X\nY");

        Assert.Equal("aX\r\nY\r\nb", buffer.GetText());
        Assert.Equal(LineEndingStyle.Crlf, buffer.LineEnding);
    }

    [Fact]
    public void InsertingNothingIsANoOpAndLeavesTheBufferClean()
    {
        var buffer = Lf("abc");

        Assert.Equal((0, 1), buffer.Insert(0, 1, string.Empty));
        Assert.Equal((0, 1), buffer.Insert(0, 1, null));
        Assert.False(buffer.IsModified);
    }

    [Fact]
    public void InsertCharInOverwriteModeReplacesRatherThanPushesRight()
    {
        var buffer = Lf("abc");

        buffer.InsertChar(0, 1, 'X', overwrite: true);

        Assert.Equal("aXc", buffer.GetText());
    }

    [Fact]
    public void OverwritingPastTheEndOfALineStillAppends()
    {
        var buffer = Lf("ab");

        buffer.InsertChar(0, 2, 'c', overwrite: true);

        Assert.Equal("abc", buffer.GetText());
    }

    [Fact]
    public void InsertNewLineSplitsAndTheSecondHalfKeepsTheOriginalTerminator()
    {
        var buffer = Lf("abcd\nnext");

        var at = buffer.InsertNewLine(0, 2);

        Assert.Equal("ab\ncd\nnext", buffer.GetText());
        Assert.Equal((1, 0), at);
    }

    [Fact]
    public void SplittingTheLastLineKeepsTheFinalTerminatorEmpty()
    {
        var buffer = Lf("abcd");

        buffer.InsertNewLine(0, 2);

        Assert.Equal("ab\ncd", buffer.GetText());
        Assert.Equal(string.Empty, buffer.GetLineEnding(buffer.LineCount - 1));
    }

    [Fact]
    public void DeletingWithinOneLine()
    {
        var buffer = Lf("abcdef");

        var at = buffer.Delete(0, 1, 0, 4);

        Assert.Equal("aef", buffer.GetText());
        Assert.Equal((0, 1), at);
    }

    [Fact]
    public void DeletingAcrossLinesJoinsThem()
    {
        var buffer = Lf("abc\ndef\nghi");

        buffer.Delete(0, 2, 2, 1);

        Assert.Equal("abhi", buffer.GetText());
        Assert.Equal(1, buffer.LineCount);
    }

    [Fact]
    public void ARangeGivenBackwardsIsNormalisedNotIgnored()
    {
        var buffer = Lf("abcdef");

        buffer.Delete(0, 4, 0, 1);

        Assert.Equal("aef", buffer.GetText());
    }

    [Fact]
    public void DeletingAnEmptyRangeChangesNothing()
    {
        var buffer = Lf("abc");

        buffer.Delete(0, 1, 0, 1);

        Assert.False(buffer.IsModified);
    }

    [Fact]
    public void BackspaceAtColumnZeroJoinsWithThePreviousLine()
    {
        var buffer = Lf("ab\ncd");

        var at = buffer.Backspace(1, 0);

        Assert.Equal("abcd", buffer.GetText());
        Assert.Equal((0, 2), at);
    }

    [Fact]
    public void BackspaceAtTheVeryStartOfTheDocumentDoesNothing()
    {
        var buffer = Lf("ab");

        Assert.Equal((0, 0), buffer.Backspace(0, 0));
        Assert.False(buffer.IsModified);
    }

    [Fact]
    public void DeleteAtEndOfLineJoinsWithTheNextLine()
    {
        var buffer = Lf("ab\ncd");

        var at = buffer.DeleteCharAt(0, 2);

        Assert.Equal("abcd", buffer.GetText());
        Assert.Equal((0, 2), at);
    }

    [Fact]
    public void DeleteAtTheVeryEndOfTheDocumentDoesNothing()
    {
        var buffer = Lf("ab");

        Assert.Equal((0, 2), buffer.DeleteCharAt(0, 2));
        Assert.False(buffer.IsModified);
    }

    [Fact]
    public void JoinLinesMergesWithTheOneBelow()
    {
        var buffer = Lf("ab\ncd\nef");

        buffer.JoinLines(0);

        Assert.Equal("abcd\nef", buffer.GetText());
    }

    [Fact]
    public void JoiningTheLastLineIsANoOp()
    {
        var buffer = Lf("ab\ncd");

        buffer.JoinLines(1);

        Assert.Equal("ab\ncd", buffer.GetText());
        Assert.False(buffer.IsModified);
    }

    [Fact]
    public void DeleteLineInTheMiddle()
    {
        var buffer = Lf("a\nb\nc");

        buffer.DeleteLine(1);

        Assert.Equal("a\nc", buffer.GetText());
    }

    [Fact]
    public void DeletingTheLastLineTakesTheTerminatorAboveItWithIt()
    {
        var buffer = Lf("a\nb\nc");

        buffer.DeleteLine(2);

        Assert.Equal("a\nb", buffer.GetText());
        Assert.Equal(2, buffer.LineCount);
    }

    [Fact]
    public void DeletingTheOnlyLineLeavesAnEmptyDocumentRatherThanNoLines()
    {
        var buffer = Lf("only");

        buffer.DeleteLine(0);

        Assert.Equal(1, buffer.LineCount);
        Assert.Equal(string.Empty, buffer.GetText());
    }

    [Fact]
    public void ReplaceLineKeepsTheTerminator()
    {
        var buffer = Lf("a\nb");

        buffer.ReplaceLine(0, "hello");

        Assert.Equal("hello\nb", buffer.GetText());
    }

    [Fact]
    public void ReplacingALineWithItsOwnTextIsANoOp()
    {
        var buffer = Lf("a\nb");

        buffer.ReplaceLine(0, "a");

        Assert.False(buffer.IsModified);
    }

    [Fact]
    public void InsertLineInTheMiddleAndAtTheEnd()
    {
        var buffer = Lf("a\nc");

        buffer.InsertLine(1, "b");
        Assert.Equal("a\nb\nc", buffer.GetText());

        buffer.InsertLine(buffer.LineCount, "d");
        Assert.Equal("a\nb\nc\nd", buffer.GetText());
        Assert.Equal(string.Empty, buffer.GetLineEnding(buffer.LineCount - 1));
    }

    [Fact]
    public void GetRangeSpansLinesIncludingTheirRealTerminators()
    {
        var buffer = TextBuffer.FromText("abc\r\ndef\nghi");

        Assert.Equal("bc", buffer.GetRange(0, 1, 0, 3));
        Assert.Equal("bc\r\ndef\ng", buffer.GetRange(0, 1, 2, 1));
        Assert.Equal(string.Empty, buffer.GetRange(1, 2, 1, 2));
        Assert.Equal("bc", buffer.GetRange(0, 3, 0, 1));
    }
}

public class TextBufferUndoTests
{
    private static TextBuffer Lf(string text)
    {
        var buffer = TextBuffer.FromText(text);
        buffer.NewLineStyle = LineEndingStyle.Lf;
        return buffer;
    }

    [Fact]
    public void UndoAndRedoRestoreTheTextAndTheCaret()
    {
        var buffer = Lf("hello");
        buffer.Insert(0, 5, "!");

        Assert.True(buffer.Undo(out int line, out int column));
        Assert.Equal("hello", buffer.GetText());
        Assert.Equal(0, line);
        Assert.Equal(5, column);

        Assert.True(buffer.Redo(out line, out column));
        Assert.Equal("hello!", buffer.GetText());
        Assert.Equal(0, line);
        Assert.Equal(6, column);
    }

    [Fact]
    public void UndoOnAnUntouchedBufferReportsNothingToDo()
    {
        var buffer = Lf("x");

        Assert.False(buffer.Undo(out _, out _));
        Assert.False(buffer.Redo(out _, out _));
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("delete")]
    [InlineData("split")]
    [InlineData("join")]
    [InlineData("deleteline")]
    [InlineData("replaceline")]
    [InlineData("insertline")]
    [InlineData("appendline")]
    [InlineData("backspace")]
    [InlineData("deletechar")]
    public void EveryOperationIsExactlyReversible(string operation)
    {
        var buffer = Lf("alpha\nbeta\ngamma");
        string before = buffer.GetText();

        switch (operation)
        {
            case "insert": buffer.Insert(1, 2, "XY\nZ"); break;
            case "delete": buffer.Delete(0, 3, 2, 2); break;
            case "split": buffer.InsertNewLine(1, 2); break;
            case "join": buffer.JoinLines(0); break;
            case "deleteline": buffer.DeleteLine(2); break;
            case "replaceline": buffer.ReplaceLine(1, "new"); break;
            case "insertline": buffer.InsertLine(1, "new"); break;
            case "appendline": buffer.InsertLine(buffer.LineCount, "new"); break;
            case "backspace": buffer.Backspace(1, 0); break;
            case "deletechar": buffer.DeleteCharAt(1, 4); break;
        }

        string after = buffer.GetText();
        Assert.NotEqual(before, after);

        Assert.True(buffer.Undo(out _, out _));
        Assert.Equal(before, buffer.GetText());

        Assert.True(buffer.Redo(out _, out _));
        Assert.Equal(after, buffer.GetText());
    }

    [Fact]
    public void ATypingRunCollapsesIntoASingleUndoStep()
    {
        var buffer = Lf(string.Empty);

        var at = (Line: 0, Column: 0);
        foreach (char c in "hello")
        {
            at = buffer.InsertChar(at.Line, at.Column, c);
        }

        Assert.Equal("hello", buffer.GetText());
        Assert.Equal(1, buffer.UndoDepth);

        Assert.True(buffer.Undo(out _, out _));
        Assert.Equal(string.Empty, buffer.GetText());
    }

    [Fact]
    public void BreakUndoRunStartsAFreshStep()
    {
        var buffer = Lf(string.Empty);

        buffer.InsertChar(0, 0, 'a');
        buffer.BreakUndoRun();
        buffer.InsertChar(0, 1, 'b');

        Assert.Equal(2, buffer.UndoDepth);
        buffer.Undo(out _, out _);
        Assert.Equal("a", buffer.GetText());
    }

    [Fact]
    public void ATypingRunIsBrokenWhenTheCaretJumpsAway()
    {
        var buffer = Lf("abcd");

        buffer.InsertChar(0, 0, 'X');
        buffer.InsertChar(0, 4, 'Y');

        Assert.Equal(2, buffer.UndoDepth);
    }

    [Fact]
    public void ARunOfBackspacesCollapsesToo()
    {
        var buffer = Lf("hello");

        var at = (Line: 0, Column: 5);
        for (int i = 0; i < 3; i++)
        {
            at = buffer.Backspace(at.Line, at.Column);
        }

        Assert.Equal("he", buffer.GetText());
        Assert.Equal(1, buffer.UndoDepth);
        buffer.Undo(out _, out _);
        Assert.Equal("hello", buffer.GetText());
    }

    [Fact]
    public void AnExplicitGroupIsOneUndoStepHoweverManyEditsItContains()
    {
        var buffer = Lf("a\nb\nc");

        using (buffer.BeginGroup())
        {
            buffer.ReplaceLine(0, "1");
            buffer.ReplaceLine(1, "2");
            buffer.ReplaceLine(2, "3");
        }

        Assert.Equal("1\n2\n3", buffer.GetText());
        Assert.Equal(1, buffer.UndoDepth);

        buffer.Undo(out _, out _);
        Assert.Equal("a\nb\nc", buffer.GetText());

        buffer.Redo(out _, out _);
        Assert.Equal("1\n2\n3", buffer.GetText());
    }

    [Fact]
    public void GroupsNest()
    {
        var buffer = Lf("a\nb");

        using (buffer.BeginGroup())
        {
            buffer.ReplaceLine(0, "1");
            using (buffer.BeginGroup())
            {
                buffer.ReplaceLine(1, "2");
            }
        }

        Assert.Equal(1, buffer.UndoDepth);
    }

    [Fact]
    public void AnEmptyGroupLeavesNoUndoStepBehind()
    {
        var buffer = Lf("a");

        using (buffer.BeginGroup())
        {
        }

        Assert.Equal(0, buffer.UndoDepth);
        Assert.False(buffer.CanUndo);
    }

    [Fact]
    public void ANewEditThrowsTheRedoStackAway()
    {
        var buffer = Lf("a");
        buffer.Insert(0, 1, "b");
        buffer.Undo(out _, out _);
        Assert.True(buffer.CanRedo);

        buffer.BreakUndoRun();
        buffer.Insert(0, 1, "c");

        Assert.False(buffer.CanRedo);
        Assert.Equal("ac", buffer.GetText());
    }

    [Fact]
    public void TheUndoHistoryIsBounded()
    {
        var buffer = Lf(string.Empty);
        buffer.UndoLimit = 5;

        for (int i = 0; i < 40; i++)
        {
            buffer.BreakUndoRun();
            buffer.InsertChar(0, buffer.LineLength(0), 'x');
        }

        Assert.Equal(5, buffer.UndoDepth);
    }

    [Fact]
    public void DirtyTrackingFollowsSavesAndUndo()
    {
        var buffer = Lf("hello");
        Assert.False(buffer.IsModified);

        buffer.Insert(0, 5, "!");
        Assert.True(buffer.IsModified);

        buffer.MarkSaved();
        Assert.False(buffer.IsModified);

        buffer.Insert(0, 6, "?");
        Assert.True(buffer.IsModified);

        buffer.Undo(out _, out _);
        Assert.False(buffer.IsModified);

        buffer.Undo(out _, out _);
        Assert.True(buffer.IsModified);
    }

    [Fact]
    public void TypingAfterASaveDoesNotFoldIntoTheSavedStep()
    {
        var buffer = Lf(string.Empty);

        buffer.InsertChar(0, 0, 'a');
        buffer.MarkSaved();
        buffer.InsertChar(0, 1, 'b');

        Assert.True(buffer.IsModified);
        Assert.Equal(2, buffer.UndoDepth);
    }

    [Fact]
    public void ClearUndoDropsTheHistoryButKeepsTheContent()
    {
        var buffer = Lf("a");
        buffer.Insert(0, 1, "b");

        buffer.ClearUndo();

        Assert.Equal("ab", buffer.GetText());
        Assert.False(buffer.CanUndo);
        Assert.False(buffer.CanRedo);
    }
}

public class TextBufferIndentAndSearchTests
{
    private static TextBuffer Lf(string text)
    {
        var buffer = TextBuffer.FromText(text);
        buffer.NewLineStyle = LineEndingStyle.Lf;
        return buffer;
    }

    [Fact]
    public void InsertTabInsertsATabCharacterByDefault()
    {
        var buffer = Lf("ab");

        buffer.InsertTab(0, 1);

        Assert.Equal("a\tb", buffer.GetText());
    }

    [Fact]
    public void InsertTabExpandsToTheNextTabStopWhenAskedTo()
    {
        var buffer = Lf("ab");
        buffer.ExpandTabs = true;
        buffer.TabSize = 4;

        buffer.InsertTab(0, 1);

        Assert.Equal("a   b", buffer.GetText());
    }

    [Fact]
    public void IndentingABlockIsOneUndoStepAndSkipsEmptyLines()
    {
        var buffer = Lf("a\n\nb");

        buffer.IndentLines(0, 2);

        Assert.Equal("\ta\n\n\tb", buffer.GetText());
        Assert.Equal(1, buffer.UndoDepth);

        buffer.Undo(out _, out _);
        Assert.Equal("a\n\nb", buffer.GetText());
    }

    [Fact]
    public void UnindentRemovesATabWholeOrUpToOneTabStopOfSpaces()
    {
        var buffer = Lf("\ta\n      b\n  c\nd");
        buffer.TabSize = 4;

        buffer.UnindentLines(0, 3);

        Assert.Equal("a\n  b\nc\nd", buffer.GetText());
    }

    [Fact]
    public void FindMovesForwardsFromThePositionGiven()
    {
        var buffer = Lf("alpha\nbeta\nalpha");

        Assert.True(buffer.Find("alpha", 0, 0, ignoreCase: false, backwards: false, out int line, out int column));
        Assert.Equal((0, 0), (line, column));

        Assert.True(buffer.Find("alpha", 0, 1, ignoreCase: false, backwards: false, out line, out column));
        Assert.Equal((2, 0), (line, column));
    }

    [Fact]
    public void FindIsCaseInsensitiveWhenAsked()
    {
        var buffer = Lf("Hello");

        Assert.True(buffer.Find("hello", 0, 0, ignoreCase: true, backwards: false, out _, out _));
        Assert.False(buffer.Find("hello", 0, 0, ignoreCase: false, backwards: false, out _, out _));
    }

    [Fact]
    public void FindSearchesBackwards()
    {
        var buffer = Lf("alpha\nbeta\nalpha");

        Assert.True(buffer.Find("alpha", 2, 0, ignoreCase: false, backwards: true, out int line, out int column));
        Assert.Equal((0, 0), (line, column));
    }

    [Fact]
    public void FindBackwardsSeesAMatchStraddlingTheStartColumn()
    {
        var buffer = Lf("hello");

        // The match starts before column 3 but extends past it; only its start is constrained.
        Assert.True(buffer.Find("hello", 0, 3, ignoreCase: false, backwards: true, out int line, out int column));
        Assert.Equal((0, 0), (line, column));
    }

    [Fact]
    public void FindBackwardsConstrainsTheMatchStartNotItsEnd()
    {
        var buffer = Lf("abab");

        Assert.True(buffer.Find("ab", 0, 3, ignoreCase: false, backwards: true, out int line, out int column));
        Assert.Equal((0, 2), (line, column));

        // A match starting exactly at the column is at the position, not before it.
        Assert.True(buffer.Find("ab", 0, 2, ignoreCase: false, backwards: true, out line, out column));
        Assert.Equal((0, 0), (line, column));

        Assert.False(buffer.Find("ab", 0, 0, ignoreCase: false, backwards: true, out _, out _));
    }

    [Fact]
    public void AnEmptyNeedleNeverMatches()
    {
        var buffer = Lf("abc");

        Assert.False(buffer.Find(string.Empty, 0, 0, ignoreCase: true, backwards: false, out _, out _));
        Assert.Equal(0, buffer.ReplaceAll(string.Empty, "x", ignoreCase: true));
    }

    [Fact]
    public void ReplaceAllCountsAndIsOneUndoStep()
    {
        var buffer = Lf("aXa\nbXb\nnone");

        int count = buffer.ReplaceAll("X", "--", ignoreCase: false);

        Assert.Equal(2, count);
        Assert.Equal("a--a\nb--b\nnone", buffer.GetText());
        Assert.Equal(1, buffer.UndoDepth);

        buffer.Undo(out _, out _);
        Assert.Equal("aXa\nbXb\nnone", buffer.GetText());
    }
}

public class TextBufferTabAndWordHelperTests
{
    [Theory]
    [InlineData("a\tb", 4, "a   b")]
    [InlineData("\tx", 4, "    x")]
    [InlineData("ab\tc", 4, "ab  c")]
    [InlineData("abcd\te", 4, "abcd    e")]
    [InlineData("plain", 4, "plain")]
    public void TabsExpandToTheNextStop(string line, int tabSize, string expected)
    {
        Assert.Equal(expected, TextBuffer.ExpandTabsForDisplay(line, tabSize));
    }

    [Fact]
    public void OtherControlCharactersBecomeDotsSoOneCharacterIsOneCell()
    {
        Assert.Equal("a.b", TextBuffer.ExpandTabsForDisplay("a\u0001b", 4));
        Assert.Equal(3, TextBuffer.ToDisplayColumn("a\u0001b", 3, 4));
    }
    [Fact]
    public void DisplayColumnsAndCharacterColumnsConvertBothWays()
    {
        const string line = "a\tbc";

        Assert.Equal(0, TextBuffer.ToDisplayColumn(line, 0, 4));
        Assert.Equal(1, TextBuffer.ToDisplayColumn(line, 1, 4));
        Assert.Equal(4, TextBuffer.ToDisplayColumn(line, 2, 4));
        Assert.Equal(6, TextBuffer.ToDisplayColumn(line, 4, 4));

        Assert.Equal(0, TextBuffer.FromDisplayColumn(line, 0, 4));
        Assert.Equal(1, TextBuffer.FromDisplayColumn(line, 1, 4));
        Assert.Equal(1, TextBuffer.FromDisplayColumn(line, 3, 4));
        Assert.Equal(2, TextBuffer.FromDisplayColumn(line, 4, 4));
        Assert.Equal(4, TextBuffer.FromDisplayColumn(line, 40, 4));
    }

    [Fact]
    public void ACaretPastTheEndOfALineKeepsAdvancingOneColumnPerCharacter()
    {
        Assert.Equal(7, TextBuffer.ToDisplayColumn("abc", 7, 4));
    }

    [Theory]
    [InlineData("hello world", 11, 6)]
    [InlineData("hello world", 6, 0)]
    [InlineData("hello world", 0, 0)]
    [InlineData("  indented", 10, 2)]
    public void WordLeftStopsAtTheStartOfTheWordBefore(string line, int from, int expected)
    {
        Assert.Equal(expected, TextBuffer.WordLeft(line, from));
    }

    [Theory]
    [InlineData("hello world", 0, 6)]
    [InlineData("hello world", 6, 11)]
    [InlineData("hello world", 11, 11)]
    [InlineData("a  b", 0, 3)]
    public void WordRightStopsAtTheStartOfTheWordAfter(string line, int from, int expected)
    {
        Assert.Equal(expected, TextBuffer.WordRight(line, from));
    }

    [Fact]
    public void PunctuationIsItsOwnWord()
    {
        Assert.Equal(3, TextBuffer.WordRight("abc()def", 0));
        Assert.Equal(5, TextBuffer.WordRight("abc()def", 3));
    }

    [Fact]
    public void IndentWidthMeasuresLeadingWhitespaceInColumns()
    {
        Assert.Equal(4, TextBuffer.IndentWidth("\tx", 4));
        Assert.Equal(2, TextBuffer.IndentWidth("  x", 4));
        Assert.Equal(0, TextBuffer.IndentWidth("x", 4));
        Assert.Equal(0, TextBuffer.IndentWidth(string.Empty, 4));
    }

    [Fact]
    public void WordCharactersAreLettersDigitsAndUnderscore()
    {
        Assert.True(TextBuffer.IsWordChar('a'));
        Assert.True(TextBuffer.IsWordChar('7'));
        Assert.True(TextBuffer.IsWordChar('_'));
        Assert.False(TextBuffer.IsWordChar('-'));
        Assert.False(TextBuffer.IsWordChar(' '));
    }
}

public class TextBufferPersistenceTests
{
    [Fact]
    public void SavingWritesBackTheSameBytesItLoaded()
    {
        byte[] original = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes("a\r\nb\nc\rd")];

        var buffer = TextBuffer.FromBytes(original);

        Assert.True(buffer.HasBom);
        Assert.Equal(LineEndingStyle.Mixed, buffer.LineEnding);
        Assert.Equal(original, buffer.GetBytes());
    }

    [Fact]
    public void ABomlessFileStaysBomless()
    {
        byte[] original = Encoding.UTF8.GetBytes("plain\n");

        Assert.Equal(original, TextBuffer.FromBytes(original).GetBytes());
    }

    [Fact]
    public void AUtf16FileRoundTripsThroughAnEdit()
    {
        byte[] original = [.. new byte[] { 0xFF, 0xFE }, .. Encoding.Unicode.GetBytes("ab\r\ncd")];
        byte[] expected = [.. new byte[] { 0xFF, 0xFE }, .. Encoding.Unicode.GetBytes("abX\r\ncd")];

        var buffer = TextBuffer.FromBytes(original);
        Assert.Equal(1200, buffer.Encoding.CodePage);
        Assert.True(buffer.HasBom);
        Assert.Equal("ab", buffer[0]);

        buffer.Insert(0, 2, "X");

        Assert.Equal(expected, buffer.GetBytes());
    }

    [Fact]
    public void ANonUtf8FileKeepsItsBytesWhereTheFallbackIsSingleByte()
    {
        // 0xE9 alone is not valid UTF-8, so detection falls back. On Windows the fallback is a
        // single byte encoding and the bytes survive; on Unix it is UTF-8 by design and cannot.
        byte[] original = [(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'\n'];

        var buffer = TextBuffer.FromBytes(original);

        Assert.Equal(EncodingDetector.AnsiFallback.CodePage, buffer.Encoding.CodePage);
        if (buffer.Encoding.IsSingleByte)
        {
            Assert.Equal(original, buffer.GetBytes());
        }
    }

    [Fact]
    public void SaveAndLoadThroughARealFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oc-editor-{Guid.NewGuid():N}.txt");
        try
        {
            var buffer = TextBuffer.FromText("alpha\r\nbeta");
            buffer.Insert(1, 4, "!");
            buffer.Save(path);

            Assert.False(buffer.IsModified);
            Assert.Equal(path, buffer.FilePath);

            var reloaded = TextBuffer.Load(path);
            Assert.Equal("alpha\r\nbeta!", reloaded.GetText());
            Assert.Equal(LineEndingStyle.Crlf, reloaded.LineEnding);
            Assert.False(reloaded.IsModified);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavingAnUnnamedDocumentWithoutAPathIsAnError()
    {
        var buffer = new TextBuffer();

        Assert.Throws<InvalidOperationException>(() => buffer.Save());
    }
}

public class FileEditorComponentTests
{
    private static readonly OpenStus.Theming.Theme Palette = OpenStus.Theming.Theme.Classic();

    private static OpenStus.Input.InputEvent Key(
        ConsoleKey key,
        OpenStus.Input.KeyMods mods = OpenStus.Input.KeyMods.None) =>
        OpenStus.Input.InputEvent.FromKey(new OpenStus.Input.KeyEvent(key, '\0', mods));

    private static OpenStus.Input.InputEvent Char(char c) =>
        OpenStus.Input.InputEvent.FromKey(
            new OpenStus.Input.KeyEvent(ConsoleKey.None, c, OpenStus.Input.KeyMods.None));

    private static FileEditor Editor(string text, FakeUi? ui = null, IEditorClipboard? clipboard = null)
    {
        var buffer = TextBuffer.FromText(text);
        buffer.NewLineStyle = LineEndingStyle.Lf;
        return new FileEditor(Palette, ui ?? new FakeUi(), buffer, clipboard ?? new InMemoryClipboard());
    }

    [Fact]
    public void TheDocumentAndAStatusLineAreRendered()
    {
        var editor = Editor("alpha\nbeta");

        string[] rows = editor.RenderToText(50, 6).Split('\n');

        Assert.Equal("alpha", rows[0].Trim());
        Assert.Equal("beta", rows[1].Trim());
        Assert.Contains("Line 1/2", rows[5], StringComparison.Ordinal);
        Assert.Contains("Col 1", rows[5], StringComparison.Ordinal);
        Assert.Contains("INS", rows[5], StringComparison.Ordinal);
        Assert.Contains("LF", rows[5], StringComparison.Ordinal);
        Assert.DoesNotContain("*", rows[5], StringComparison.Ordinal);
    }

    [Fact]
    public void TypingInsertsAndMarksTheDocumentModified()
    {
        var editor = Editor("bc");

        editor.HandleInput(Char('a'));

        Assert.Equal("abc", editor.Buffer.GetText());
        Assert.True(editor.IsModified);
        Assert.Contains("*", editor.RenderToText(50, 6).Split('\n')[5], StringComparison.Ordinal);
    }

    [Fact]
    public void EnterSplitsAndBackspaceJoins()
    {
        var editor = Editor("abcd");
        editor.HandleInput(Key(ConsoleKey.RightArrow));
        editor.HandleInput(Key(ConsoleKey.RightArrow));

        editor.HandleInput(Key(ConsoleKey.Enter));
        Assert.Equal("ab\ncd", editor.Buffer.GetText());
        Assert.Equal(1, editor.Cursor.Line);

        editor.HandleInput(Key(ConsoleKey.Backspace));
        Assert.Equal("abcd", editor.Buffer.GetText());
        Assert.Equal((0, 2), (editor.Cursor.Line, editor.Cursor.Column));
    }

    [Fact]
    public void InsertTogglesOverwriteAndOverwriteReplaces()
    {
        var editor = Editor("abc");

        editor.HandleInput(Key(ConsoleKey.Insert));
        Assert.True(editor.Overwrite);
        Assert.Contains("OVR", editor.RenderToText(50, 6).Split('\n')[5], StringComparison.Ordinal);

        editor.HandleInput(Char('X'));
        Assert.Equal("Xbc", editor.Buffer.GetText());
    }

    [Fact]
    public void CtrlYDeletesTheCurrentLine()
    {
        var editor = Editor("a\nb\nc");
        editor.HandleInput(Key(ConsoleKey.DownArrow));

        editor.HandleInput(Key(ConsoleKey.Y, OpenStus.Input.KeyMods.Ctrl));

        Assert.Equal("a\nc", editor.Buffer.GetText());
    }

    [Fact]
    public void CtrlZUndoesAndCtrlShiftZRedoes()
    {
        var editor = Editor("x");
        editor.HandleInput(Char('a'));

        editor.HandleInput(Key(ConsoleKey.Z, OpenStus.Input.KeyMods.Ctrl));
        Assert.Equal("x", editor.Buffer.GetText());

        editor.HandleInput(Key(ConsoleKey.Z, OpenStus.Input.KeyMods.Ctrl | OpenStus.Input.KeyMods.Shift));
        Assert.Equal("ax", editor.Buffer.GetText());
    }

    [Fact]
    public void ShiftArrowSelectsAndTypingReplacesTheSelection()
    {
        var editor = Editor("abcd");
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));

        Assert.True(editor.Cursor.HasSelection);

        editor.HandleInput(Char('Z'));

        Assert.Equal("Zcd", editor.Buffer.GetText());
        Assert.False(editor.Cursor.HasSelection);
    }

    [Fact]
    public void ReplacingASelectionIsASingleUndoStep()
    {
        var editor = Editor("abcd");
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Char('Z'));

        editor.HandleInput(Key(ConsoleKey.Z, OpenStus.Input.KeyMods.Ctrl));

        Assert.Equal("abcd", editor.Buffer.GetText());
    }

    [Fact]
    public void CopyCutAndPasteGoThroughTheClipboardAbstraction()
    {
        var clipboard = new InMemoryClipboard();
        var editor = Editor("abcd", clipboard: clipboard);

        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.X, OpenStus.Input.KeyMods.Ctrl));

        Assert.Equal("ab", clipboard.GetText());
        Assert.Equal("cd", editor.Buffer.GetText());

        editor.HandleInput(Key(ConsoleKey.End));
        editor.HandleInput(Key(ConsoleKey.V, OpenStus.Input.KeyMods.Ctrl));

        Assert.Equal("cdab", editor.Buffer.GetText());
    }

    [Fact]
    public void CopyWithNoSelectionTakesTheWholeLine()
    {
        var clipboard = new InMemoryClipboard();
        var editor = Editor("abc\ndef", clipboard: clipboard);

        editor.HandleInput(Key(ConsoleKey.C, OpenStus.Input.KeyMods.Ctrl));

        Assert.Equal("abc\n", clipboard.GetText());
    }

    [Fact]
    public void TabIndentsAWholeSelectedBlockInOneStep()
    {
        var editor = Editor("a\nb\nc");
        editor.HandleInput(Key(ConsoleKey.DownArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.DownArrow, OpenStus.Input.KeyMods.Shift));

        editor.HandleInput(Key(ConsoleKey.Tab));
        Assert.Equal("\ta\n\tb\n\tc", editor.Buffer.GetText());

        editor.HandleInput(Key(ConsoleKey.Tab, OpenStus.Input.KeyMods.Shift));
        Assert.Equal("a\nb\nc", editor.Buffer.GetText());
    }

    [Fact]
    public void WordMotionMovesByWords()
    {
        var editor = Editor("hello world");

        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Ctrl));
        Assert.Equal(6, editor.Cursor.Column);

        editor.HandleInput(Key(ConsoleKey.LeftArrow, OpenStus.Input.KeyMods.Ctrl));
        Assert.Equal(0, editor.Cursor.Column);
    }

    [Fact]
    public void CtrlHomeAndCtrlEndJumpToTheEndsOfTheDocument()
    {
        var editor = Editor("a\nbb\nccc");

        editor.HandleInput(Key(ConsoleKey.End, OpenStus.Input.KeyMods.Ctrl));
        Assert.Equal((2, 3), (editor.Cursor.Line, editor.Cursor.Column));

        editor.HandleInput(Key(ConsoleKey.Home, OpenStus.Input.KeyMods.Ctrl));
        Assert.Equal((0, 0), (editor.Cursor.Line, editor.Cursor.Column));
    }

    [Fact]
    public void TheViewportFollowsTheCaret()
    {
        var text = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line{i:000}"));
        var editor = Editor(text);

        editor.RenderToText(40, 10);
        editor.HandleInput(Key(ConsoleKey.End, OpenStus.Input.KeyMods.Ctrl));

        string[] rows = editor.RenderToText(40, 10).Split('\n');

        // The last cell of each row is the scroll bar, drawn because the document does not fit.
        Assert.StartsWith("line099", rows[8], StringComparison.Ordinal);
        Assert.EndsWith("░", rows[8], StringComparison.Ordinal);
        Assert.True(editor.TopLine > 0);
    }

    [Fact]
    public void CtrlDownScrollsTheViewAndDragsTheCaretAlong()
    {
        var text = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line{i:000}"));
        var editor = Editor(text);
        editor.RenderToText(40, 10);

        editor.HandleInput(Key(ConsoleKey.DownArrow, OpenStus.Input.KeyMods.Ctrl));

        // The scroll sticks: the caret was pulled onto the new top row, so the next draw's
        // scroll-into-view has nothing to snap back.
        string[] rows = editor.RenderToText(40, 10).Split('\n');
        Assert.StartsWith("line001", rows[0], StringComparison.Ordinal);
        Assert.Equal(1, editor.TopLine);
        Assert.Equal(1, editor.Cursor.Line);

        editor.HandleInput(Key(ConsoleKey.UpArrow, OpenStus.Input.KeyMods.Ctrl));
        Assert.Equal(0, editor.TopLine);
    }

    [Fact]
    public void TheMouseWheelScrollsWithoutSnappingBackToTheCaret()
    {
        var text = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line{i:000}"));
        var editor = Editor(text);
        editor.RenderToText(40, 10);

        editor.HandleInput(OpenStus.Input.InputEvent.FromMouse(new OpenStus.Input.MouseEvent(
            OpenStus.Input.MouseKind.Wheel,
            0,
            0,
            OpenStus.Input.MouseButton.None,
            -1,
            OpenStus.Input.KeyMods.None)));

        Assert.Equal(3, editor.TopLine);
        Assert.Equal(3, editor.Cursor.Line);
        Assert.StartsWith("line003", editor.RenderToText(40, 10).Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SearchSelectsTheMatch()
    {
        var ui = new FakeUi();
        ui.Answers.Enqueue("beta");
        var editor = Editor("alpha\nbeta\n", ui);

        editor.HandleInput(Key(ConsoleKey.F7));

        Assert.Equal(1, editor.Cursor.Line);
        Assert.True(editor.Cursor.HasSelection);
        Assert.Equal((1, 0), editor.Cursor.SelectionStart);
        Assert.Equal((1, 4), editor.Cursor.SelectionEnd);
    }

    [Fact]
    public void AMissedSearchIsReported()
    {
        var ui = new FakeUi();
        ui.Answers.Enqueue("nowhere");
        var editor = Editor("alpha\n", ui);

        editor.HandleInput(Key(ConsoleKey.F7));

        Assert.Contains(ui.Shown, s => s.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void AFreshSearchFindsAMatchSittingExactlyUnderTheCaret()
    {
        var ui = new FakeUi();
        ui.Answers.Enqueue("alpha");
        var editor = Editor("alpha\n", ui);

        editor.HandleInput(Key(ConsoleKey.F7));

        Assert.True(editor.Cursor.HasSelection);
        Assert.Equal((0, 0), editor.Cursor.SelectionStart);
        Assert.Equal((0, 5), editor.Cursor.SelectionEnd);
        Assert.DoesNotContain(ui.Shown, s => s.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void ContinueSearchFindsAnOccurrenceFlushAgainstThePreviousMatch()
    {
        var ui = new FakeUi();
        ui.Answers.Enqueue("ab");
        var editor = Editor("abab\n", ui);

        editor.HandleInput(Key(ConsoleKey.F7));
        Assert.Equal((0, 0), editor.Cursor.SelectionStart);

        // Shift+F7 continues from the end of the previous match, so the adjacent one is next.
        editor.HandleInput(Key(ConsoleKey.F7, OpenStus.Input.KeyMods.Shift));
        Assert.Equal((0, 2), editor.Cursor.SelectionStart);
        Assert.Equal((0, 4), editor.Cursor.SelectionEnd);
    }

    [Fact]
    public void ReplaceAllRunsAndReportsTheCount()
    {
        var ui = new FakeUi { ConfirmAnswer = true };
        ui.Answers.Enqueue("a");
        ui.Answers.Enqueue("X");
        var editor = Editor("aba\nca", ui);

        editor.HandleInput(Key(ConsoleKey.F7, OpenStus.Input.KeyMods.Ctrl));

        Assert.Equal("XbX\ncX", editor.Buffer.GetText());
        Assert.Contains(ui.Shown, s => s.Contains("3 occurrences replaced", StringComparison.Ordinal));
    }

    [Fact]
    public void GoToLineMovesTheCaret()
    {
        var ui = new FakeUi();
        ui.Answers.Enqueue("3");
        var editor = Editor("a\nb\nc\nd", ui);

        editor.HandleInput(Key(ConsoleKey.F8, OpenStus.Input.KeyMods.Alt));

        Assert.Equal(2, editor.Cursor.Line);
    }

    [Fact]
    public void EscapeClosesImmediatelyWhenNothingHasChanged()
    {
        var editor = Editor("clean");

        Assert.False(editor.HandleInput(Key(ConsoleKey.Escape)));
        Assert.True(editor.IsClosed);
    }

    [Fact]
    public void EscapeOnAModifiedDocumentAsksAndHonoursTheAnswer()
    {
        var ui = new FakeUi { MessageAnswer = OpenStus.Core.DialogResult.Cancel };
        var editor = Editor("x", ui);
        editor.HandleInput(Char('a'));

        Assert.True(editor.HandleInput(Key(ConsoleKey.Escape)));
        Assert.False(editor.IsClosed);

        ui.MessageAnswer = OpenStus.Core.DialogResult.No;
        Assert.False(editor.HandleInput(Key(ConsoleKey.Escape)));
        Assert.True(editor.IsClosed);
    }

    [Fact]
    public void F2SavesToDiskAndClearsTheModifiedFlag()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oc-editor-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "hello\n");
            var editor = new FileEditor(Palette, new FakeUi(), path);

            editor.HandleInput(Char('!'));
            Assert.True(editor.IsModified);

            editor.HandleInput(Key(ConsoleKey.F2));

            Assert.False(editor.IsModified);
            Assert.Equal("!hello\n", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpeningANameThatDoesNotExistStartsAnEmptyDocument()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oc-editor-new-{Guid.NewGuid():N}.txt");
        var editor = new FileEditor(Palette, new FakeUi(), path);

        Assert.False(editor.IsClosed);
        Assert.Equal(1, editor.Buffer.LineCount);
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void ABinaryFileIsOnlyOpenedWhenTheUserConfirms()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oc-editor-bin-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [0x41, 0x00, 0x42]);

            var refusing = new FakeUi { ConfirmAnswer = false };
            Assert.True(new FileEditor(Palette, refusing, path).IsClosed);
            Assert.Null(FileEditor.TryOpen(Palette, new FakeUi { ConfirmAnswer = false }, path));

            var accepting = new FakeUi { ConfirmAnswer = true };
            Assert.False(new FileEditor(Palette, accepting, path).IsClosed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheEditorKeyBarChangesWithTheHeldModifier()
    {
        var editor = Editor("x");

        Assert.Equal("Save", editor.KeyBarFor(OpenStus.Input.KeyMods.None)![1]);
        Assert.Equal("Quit", editor.KeyBarFor(OpenStus.Input.KeyMods.None)![9]);
        Assert.Equal("SaveAs", editor.KeyBarFor(OpenStus.Input.KeyMods.Shift)![1]);
        Assert.Equal("Replac", editor.KeyBarFor(OpenStus.Input.KeyMods.Ctrl)![6]);
        Assert.Equal("GoTo", editor.KeyBarFor(OpenStus.Input.KeyMods.Alt)![7]);
    }

    [Fact]
    public void SelectedTextIsPaintedInTheSelectionColour()
    {
        var editor = Editor("abcdef");
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));
        editor.HandleInput(Key(ConsoleKey.RightArrow, OpenStus.Input.KeyMods.Shift));

        var screen = new OpenStus.Rendering.ScreenBuffer(40, 6);
        editor.Layout(new OpenStus.Rendering.Rect(0, 0, 40, 6));
        editor.Draw(screen);

        Assert.Equal(Palette.EditorSelected, screen.Get(0, 0).Style);
        Assert.Equal(Palette.EditorSelected, screen.Get(2, 0).Style);
        Assert.Equal(Palette.EditorText, screen.Get(4, 0).Style);
    }
}
