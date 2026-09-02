using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Shell;
using Dvopan.Theming;
using Dvopan.Ui;

namespace Dvopan.Tests;

/// <summary>
/// A context that records what the command line asks the application to do. Every member the
/// command line never touches throws, so an accidental new dependency shows up as a failing test
/// rather than as a silent coupling.
/// </summary>
internal sealed class RecordingContext : IAppContext
{
    public List<string> Commands { get; } = [];

    public Theme Theme { get; } = Theme.Classic();

    public Settings Settings { get; } = new();

    public Terminal Terminal => throw new NotSupportedException();

    public IUiServices Ui => throw new NotSupportedException();

    public IFilePanel ActivePanel => throw new NotSupportedException();

    public IFilePanel PassivePanel => throw new NotSupportedException();

    public IFilePanel LeftPanel => throw new NotSupportedException();

    public IFilePanel RightPanel => throw new NotSupportedException();

    public void SwapPanels() => throw new NotSupportedException();

    public void SwitchPanel() => throw new NotSupportedException();

    public void RequestQuit() => throw new NotSupportedException();

    public void Redraw() => throw new NotSupportedException();

    public void RefreshBothPanels() => throw new NotSupportedException();

    public void RunShellCommand(string command) => Commands.Add(command);

    public void InsertIntoCommandLine(string text) => throw new NotSupportedException();
}

internal static class Keys
{
    public static KeyEvent Char(char c) => new(ConsoleKey.None, c, KeyMods.None);

    public static KeyEvent Key(ConsoleKey key, KeyMods mods = KeyMods.None) => new(key, '\0', mods);

    public static void Type(CommandLine line, IAppContext ctx, string text)
    {
        foreach (char c in text)
        {
            Assert.True(line.HandleKey(Char(c), ctx), $"typing '{c}' should have been consumed");
        }
    }
}

public class CommandLineEditingTests
{
    private static (CommandLine Line, RecordingContext Ctx) New()
    {
        var ctx = new RecordingContext();
        return (new CommandLine(ctx.Theme, new CommandHistory()), ctx);
    }

    [Fact]
    public void TypingAppendsAtTheCaret()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "dir");

        Assert.Equal("dir", line.Text);
        Assert.Equal(3, line.Caret);
        Assert.False(line.IsEmpty);
    }

    [Fact]
    public void HomeEndAndTheArrowsMoveTheCaret()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "abc");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Home), ctx));
        Assert.Equal(0, line.Caret);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.RightArrow), ctx));
        Assert.Equal(1, line.Caret);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.End), ctx));
        Assert.Equal(3, line.Caret);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx));
        Assert.Equal(2, line.Caret);
    }

    [Fact]
    public void TheCaretNeverLeavesTheText()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "ab");

        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx);
        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx);
        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx);
        Assert.Equal(0, line.Caret);

        line.HandleKey(Keys.Key(ConsoleKey.RightArrow), ctx);
        line.HandleKey(Keys.Key(ConsoleKey.RightArrow), ctx);
        line.HandleKey(Keys.Key(ConsoleKey.RightArrow), ctx);
        Assert.Equal(2, line.Caret);
    }

    [Fact]
    public void InsertingHappensAtTheCaretNotTheEnd()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "ac");
        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx);
        Keys.Type(line, ctx, "b");

        Assert.Equal("abc", line.Text);
        Assert.Equal(2, line.Caret);
    }

    [Fact]
    public void BackspaceDeletesBeforeTheCaretAndDeleteDeletesUnderIt()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "abcd");

        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx);
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Backspace), ctx));
        Assert.Equal("abd", line.Text);
        Assert.Equal(2, line.Caret);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Delete), ctx));
        Assert.Equal("ab", line.Text);
        Assert.Equal(2, line.Caret);

        // Delete at the end of the line is consumed but does nothing.
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Delete), ctx));
        Assert.Equal("ab", line.Text);
    }

    [Fact]
    public void BackspaceAtColumnZeroIsConsumedButHarmless()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "ab");
        line.HandleKey(Keys.Key(ConsoleKey.Home), ctx);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Backspace), ctx));
        Assert.Equal("ab", line.Text);
    }

    [Fact]
    public void CtrlLeftAndCtrlRightMoveByWords()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "one two three");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.LeftArrow, KeyMods.Ctrl), ctx));
        Assert.Equal(8, line.Caret);

        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow, KeyMods.Ctrl), ctx);
        Assert.Equal(4, line.Caret);

        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow, KeyMods.Ctrl), ctx);
        Assert.Equal(0, line.Caret);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.RightArrow, KeyMods.Ctrl), ctx));
        Assert.Equal(4, line.Caret);
    }

    [Fact]
    public void PathSeparatorsCountAsWordBreaks()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, @"copy C:\Work\file.txt");

        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow, KeyMods.Ctrl), ctx);
        Assert.Equal("txt", line.Text[line.Caret..]);
    }

    [Fact]
    public void CtrlYClearsTheLine()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "something");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Y, KeyMods.Ctrl), ctx));
        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(0, line.Caret);
        Assert.True(line.IsEmpty);
    }

    [Fact]
    public void EscapeClearsANonEmptyLine()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "oops");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Escape), ctx));
        Assert.Equal(string.Empty, line.Text);
    }

    [Fact]
    public void InsertPutsTextAtTheCaret()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "copy ");
        line.Insert("file.txt");

        Assert.Equal("copy file.txt", line.Text);
        Assert.Equal(13, line.Caret);

        line.Insert(null);
        line.Insert(string.Empty);
        Assert.Equal("copy file.txt", line.Text);
    }

    [Fact]
    public void SettingTextPutsTheCaretAtTheEnd()
    {
        (CommandLine line, _) = New();
        line.Text = "hello";

        Assert.Equal(5, line.Caret);

        line.Text = null!;
        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(0, line.Caret);
    }

    [Fact]
    public void EnterRunsTheCommandRemembersItAndClearsTheLine()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "git status");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Enter), ctx));
        Assert.Equal(new[] { "git status" }, ctx.Commands);
        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(new[] { "git status" }, line.History.All);
    }

    [Fact]
    public void ANullThemeOrHistoryIsRejectedUpFront()
    {
        Assert.Throws<ArgumentNullException>(static () => new CommandLine(null!, new CommandHistory()));
        Assert.Throws<ArgumentNullException>(static () => new CommandLine(Theme.Classic(), null!));
    }
}

public class CommandLineRoutingTests
{
    private static (CommandLine Line, RecordingContext Ctx) New()
    {
        var ctx = new RecordingContext();
        return (new CommandLine(ctx.Theme, new CommandHistory()), ctx);
    }

    [Theory]
    [InlineData(ConsoleKey.UpArrow)]
    [InlineData(ConsoleKey.DownArrow)]
    [InlineData(ConsoleKey.LeftArrow)]
    [InlineData(ConsoleKey.RightArrow)]
    [InlineData(ConsoleKey.PageUp)]
    [InlineData(ConsoleKey.PageDown)]
    [InlineData(ConsoleKey.Home)]
    [InlineData(ConsoleKey.End)]
    [InlineData(ConsoleKey.Insert)]
    [InlineData(ConsoleKey.Tab)]
    [InlineData(ConsoleKey.Enter)]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.Backspace)]
    [InlineData(ConsoleKey.Delete)]
    [InlineData(ConsoleKey.F1)]
    [InlineData(ConsoleKey.F3)]
    [InlineData(ConsoleKey.F5)]
    [InlineData(ConsoleKey.F8)]
    [InlineData(ConsoleKey.F10)]
    [InlineData(ConsoleKey.F12)]
    public void AnEmptyLineHandsEveryNavigationKeyToThePanel(ConsoleKey key)
    {
        (CommandLine line, RecordingContext ctx) = New();

        Assert.True(line.IsEmpty);
        Assert.False(line.HandleKey(Keys.Key(key), ctx), $"{key} should have gone to the panel");
    }

    [Theory]
    [InlineData(ConsoleKey.LeftArrow)]
    [InlineData(ConsoleKey.RightArrow)]
    [InlineData(ConsoleKey.Home)]
    [InlineData(ConsoleKey.End)]
    [InlineData(ConsoleKey.Backspace)]
    [InlineData(ConsoleKey.Delete)]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.Enter)]
    [InlineData(ConsoleKey.Tab)]
    public void ANonEmptyLineTakesTheEditingKeysBack(ConsoleKey key)
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "text");

        Assert.True(line.HandleKey(Keys.Key(key), ctx), $"{key} should have been consumed");
    }

    [Theory]
    [InlineData(ConsoleKey.PageUp)]
    [InlineData(ConsoleKey.PageDown)]
    [InlineData(ConsoleKey.Insert)]
    [InlineData(ConsoleKey.F3)]
    [InlineData(ConsoleKey.F5)]
    [InlineData(ConsoleKey.F10)]
    public void SomeKeysBelongToThePanelEvenWithTextOnTheLine(ConsoleKey key)
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "text");

        Assert.False(line.HandleKey(Keys.Key(key), ctx), $"{key} should have gone to the panel");
    }

    /// <summary>
    /// Shift plus a motion key is the panel's select-and-move family, so the command line
    /// must not treat the chord as an unshifted caret motion - or, worse, a history recall - even
    /// with a command half typed.
    /// </summary>
    [Theory]
    [InlineData(ConsoleKey.UpArrow)]
    [InlineData(ConsoleKey.DownArrow)]
    [InlineData(ConsoleKey.LeftArrow)]
    [InlineData(ConsoleKey.RightArrow)]
    [InlineData(ConsoleKey.Home)]
    [InlineData(ConsoleKey.End)]
    public void ShiftedMotionKeysReachThePanelWithACommandHalfTyped(ConsoleKey key)
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "git ");

        Assert.False(line.HandleKey(Keys.Key(key, KeyMods.Shift), ctx), $"Shift+{key} should have gone to the panel");
        Assert.Equal("git ", line.Text); // and it must not have edited or replaced the line
    }

    [Fact]
    public void ShiftEnterIsNotEnter()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "dir");

        Assert.False(line.HandleKey(Keys.Key(ConsoleKey.Enter, KeyMods.Shift), ctx));
        Assert.Empty(ctx.Commands);
        Assert.Equal("dir", line.Text);
    }

    /// <summary>
    /// The grey keypad keys are the panel's selection commands. The Windows backend delivers them
    /// carrying a printable character, so the command line has to hand them over explicitly rather
    /// than letting the trailing "plain character" case insert them as text.
    /// </summary>
    [Theory]
    [InlineData(ConsoleKey.Add, '+', KeyMods.None)]
    [InlineData(ConsoleKey.Add, '+', KeyMods.Shift)]
    [InlineData(ConsoleKey.Subtract, '-', KeyMods.None)]
    [InlineData(ConsoleKey.Subtract, '-', KeyMods.Shift)]
    [InlineData(ConsoleKey.Multiply, '*', KeyMods.None)]
    [InlineData(ConsoleKey.Multiply, '*', KeyMods.Shift)]
    public void TheGreyKeypadKeysAlwaysReachThePanelOnAnEmptyLine(ConsoleKey key, char ch, KeyMods mods)
    {
        (CommandLine line, RecordingContext ctx) = New();

        Assert.True(line.IsEmpty);
        Assert.False(line.HandleKey(new KeyEvent(key, ch, mods), ctx), $"{key} should have gone to the panel");
        Assert.Equal(string.Empty, line.Text);
    }

    [Theory]
    [InlineData(ConsoleKey.Add, '+', KeyMods.None)]
    [InlineData(ConsoleKey.Add, '+', KeyMods.Shift)]
    [InlineData(ConsoleKey.Subtract, '-', KeyMods.None)]
    [InlineData(ConsoleKey.Subtract, '-', KeyMods.Shift)]
    [InlineData(ConsoleKey.Multiply, '*', KeyMods.None)]
    [InlineData(ConsoleKey.Multiply, '*', KeyMods.Shift)]
    public void TheGreyKeypadKeysReachThePanelWithACommandHalfTyped(ConsoleKey key, char ch, KeyMods mods)
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "copy ");

        Assert.False(line.HandleKey(new KeyEvent(key, ch, mods), ctx), $"{key} should have gone to the panel");
        Assert.Equal("copy ", line.Text); // and it must not have been typed into the line
    }

    /// <summary>
    /// Gray/ is not one of them: the panel binds nothing to it and a slash has to stay typeable into
    /// a path. The same goes for the main keyboard row's "+", which arrives as OemPlus.
    /// </summary>
    [Theory]
    [InlineData(ConsoleKey.Divide, '/')]
    [InlineData(ConsoleKey.OemPlus, '+')]
    [InlineData(ConsoleKey.OemMinus, '-')]
    public void TheOtherPunctuationKeysStillTypeThemselves(ConsoleKey key, char ch)
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "cd ");

        Assert.True(line.HandleKey(new KeyEvent(key, ch, KeyMods.None), ctx));
        Assert.Equal("cd " + ch, line.Text);
    }

    [Theory]
    [InlineData(ConsoleKey.U)]
    [InlineData(ConsoleKey.Enter)]
    [InlineData(ConsoleKey.F)]
    [InlineData(ConsoleKey.J)]
    public void UnclaimedCtrlChordsGoToThePanel(ConsoleKey key)
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "text");

        Assert.False(line.HandleKey(Keys.Key(key, KeyMods.Ctrl), ctx));
    }

    [Fact]
    public void AltChordsAlwaysGoToThePanelSoQuickSearchStillWorks()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "text");

        Assert.False(line.HandleKey(new KeyEvent(ConsoleKey.S, 's', KeyMods.Alt), ctx));
        Assert.Equal("text", line.Text);
    }

    [Fact]
    public void CtrlYWorksEvenOnAnEmptyLine()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Y, KeyMods.Ctrl), ctx));
    }

    [Fact]
    public void EnterOnAnEmptyLineRunsNothing()
    {
        (CommandLine line, RecordingContext ctx) = New();

        Assert.False(line.HandleKey(Keys.Key(ConsoleKey.Enter), ctx));
        Assert.Empty(ctx.Commands);
    }
}

public class CommandLineHistoryTests
{
    private static (CommandLine Line, RecordingContext Ctx) New(params string[] seed)
    {
        var ctx = new RecordingContext();
        var history = new CommandHistory();
        foreach (string s in seed)
        {
            history.Add(s);
        }

        return (new CommandLine(ctx.Theme, history), ctx);
    }

    [Fact]
    public void CtrlEWalksBackwardsThroughTheHistory()
    {
        (CommandLine line, RecordingContext ctx) = New("first", "second", "third");
        Keys.Type(line, ctx, "x");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx));
        Assert.Equal("third", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        Assert.Equal("second", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        Assert.Equal("first", line.Text);

        // Already at the oldest entry: the line stays put.
        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        Assert.Equal("first", line.Text);
    }

    [Fact]
    public void CtrlXComesBackAndRestoresTheHalfTypedLine()
    {
        (CommandLine line, RecordingContext ctx) = New("alpha", "beta");
        Keys.Type(line, ctx, "half typed");

        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        Assert.Equal("alpha", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.X, KeyMods.Ctrl), ctx);
        Assert.Equal("beta", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.X, KeyMods.Ctrl), ctx);
        Assert.Equal("half typed", line.Text);
        Assert.Equal(10, line.Caret);
    }

    /// <summary>
    /// With text on the line Up and Down walk the history like a shell's, but only through the
    /// entries that start like what was typed, and Down past the newest match brings the typed
    /// text back.
    /// </summary>
    [Fact]
    public void UpAndDownWalkOnlyTheEntriesStartingLikeTheTypedText()
    {
        (CommandLine line, RecordingContext ctx) = New("git status", "dir", "git push", "Git pull");
        Keys.Type(line, ctx, "git");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx));
        Assert.Equal("Git pull", line.Text); // newest match first, case-insensitively

        line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx);
        Assert.Equal("git push", line.Text); // "dir" is skipped

        line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx);
        Assert.Equal("git status", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx);
        Assert.Equal("git status", line.Text); // nothing older matches

        line.HandleKey(Keys.Key(ConsoleKey.DownArrow), ctx);
        line.HandleKey(Keys.Key(ConsoleKey.DownArrow), ctx);
        Assert.Equal("Git pull", line.Text);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.DownArrow), ctx));
        Assert.Equal("git", line.Text); // the half-typed line comes back
        Assert.Equal(3, line.Caret);
    }

    [Fact]
    public void TheGhostSuggestionIsTheNewestLongerMatchAndRightAcceptsIt()
    {
        (CommandLine line, RecordingContext ctx) = New("dotnet build", "dotnet test --no-build", "dir");

        Keys.Type(line, ctx, "dot");
        Assert.Equal("dotnet test --no-build", line.Suggestion);

        // Ctrl+Right takes one word of it, Right the rest.
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.RightArrow, KeyMods.Ctrl), ctx));
        Assert.Equal("dotnet", line.Text);
        Assert.Equal("dotnet test --no-build", line.Suggestion);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.RightArrow), ctx));
        Assert.Equal("dotnet test --no-build", line.Text);
        Assert.Equal(line.Text.Length, line.Caret);
        Assert.Null(line.Suggestion); // nothing longer remains
    }

    [Fact]
    public void EndAcceptsTheSuggestionOnlyWhenTheCaretIsAlreadyAtTheEnd()
    {
        (CommandLine line, RecordingContext ctx) = New("make clean");
        Keys.Type(line, ctx, "make");

        line.HandleKey(Keys.Key(ConsoleKey.Home), ctx);
        Assert.Null(line.Suggestion); // only offered with the caret at the end

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.End), ctx));
        Assert.Equal("make", line.Text); // the first End only moves the caret

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.End), ctx));
        Assert.Equal("make clean", line.Text);
    }

    [Fact]
    public void NoSuggestionWithoutTextOrDuringARecall()
    {
        (CommandLine line, RecordingContext ctx) = New("git status", "git push");

        Assert.Null(line.Suggestion);

        Keys.Type(line, ctx, "git");
        line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx);
        Assert.Equal("git push", line.Text);
        Assert.Null(line.Suggestion); // a recalled entry is not a prefix to complete
    }

    [Fact]
    public void CtrlBackspaceAndCtrlDeleteRemoveAWord()
    {
        (CommandLine line, RecordingContext ctx) = New();
        Keys.Type(line, ctx, "git commit -m");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Backspace, KeyMods.Ctrl), ctx));
        Assert.Equal("git commit ", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.Home), ctx);
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Delete, KeyMods.Ctrl), ctx));
        Assert.Equal("commit ", line.Text);
        Assert.Equal(0, line.Caret);
    }

    /// <summary>
    /// While Ctrl+O hides the panels the shell recalls through <see cref="CommandLine.RecallHistory"/>,
    /// which steps regardless of what is on the line because there is no panel cursor to move.
    /// </summary>
    [Fact]
    public void RecallHistoryStepsRegardlessOfTheLineForTheHiddenPanelsState()
    {
        (CommandLine line, RecordingContext ctx) = New("one", "two");
        Keys.Type(line, ctx, "half");

        Assert.True(line.RecallHistory(previous: true));
        Assert.Equal("two", line.Text);

        Assert.True(line.RecallHistory(previous: true));
        Assert.Equal("one", line.Text);

        Assert.True(line.RecallHistory(previous: false));
        Assert.Equal("two", line.Text);

        // Stepping past the newest entry restores the half-typed line, exactly like a shell.
        Assert.True(line.RecallHistory(previous: false));
        Assert.Equal("half", line.Text);
    }

    [Fact]
    public void EditingAfterARecallStartsTheWalkOver()
    {
        (CommandLine line, RecordingContext ctx) = New("one", "two");

        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        Assert.Equal("two", line.Text);

        Keys.Type(line, ctx, "!");
        Assert.Equal("two!", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.E, KeyMods.Ctrl), ctx);
        Assert.Equal("two", line.Text);
    }

    [Fact]
    public void UpOnAnEmptyLineStillBelongsToThePanel()
    {
        (CommandLine line, RecordingContext ctx) = New("one");

        Assert.False(line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx));
        Assert.Equal(string.Empty, line.Text);
    }
}

public class CommandLinePasteTests
{
    private static (CommandLine Line, RecordingContext Ctx, MemoryClipboard Clipboard) New()
    {
        var ctx = new RecordingContext();
        var clipboard = new MemoryClipboard();
        var line = new CommandLine(ctx.Theme, new CommandHistory()) { Clipboard = clipboard };
        return (line, ctx, clipboard);
    }

    [Fact]
    public void ShiftInsPastesAtTheCaret()
    {
        (CommandLine line, RecordingContext ctx, MemoryClipboard clipboard) = New();
        clipboard.SetText("file.txt");
        Keys.Type(line, ctx, "copy ");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Insert, KeyMods.Shift), ctx));
        Assert.Equal("copy file.txt", line.Text);
        Assert.Equal(13, line.Caret);
    }

    [Fact]
    public void CtrlVPastesToo()
    {
        (CommandLine line, RecordingContext ctx, MemoryClipboard clipboard) = New();
        clipboard.SetText("dir");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.V, KeyMods.Ctrl), ctx));
        Assert.Equal("dir", line.Text);
        Assert.Equal(3, line.Caret);
    }

    [Fact]
    public void PastingInTheMiddleKeepsTheTail()
    {
        (CommandLine line, RecordingContext ctx, MemoryClipboard clipboard) = New();
        clipboard.SetText("b");
        Keys.Type(line, ctx, "ac");
        line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Insert, KeyMods.Shift), ctx));
        Assert.Equal("abc", line.Text);
        Assert.Equal(2, line.Caret);
    }

    /// <summary>
    /// The command line is a single-line field: a multi-line clipboard contributes only its first
    /// line, so a copied block of script cannot smuggle an Enter into the prompt.
    /// </summary>
    [Fact]
    public void OnlyTheFirstLineOfAMultiLineClipboardIsTaken()
    {
        (CommandLine line, RecordingContext ctx, MemoryClipboard clipboard) = New();
        clipboard.SetText("first line\r\nsecond line\nthird");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Insert, KeyMods.Shift), ctx));
        Assert.Equal("first line", line.Text);
        Assert.Empty(ctx.Commands);
    }

    [Fact]
    public void AnEmptyClipboardIsConsumedButHarmless()
    {
        (CommandLine line, RecordingContext ctx, _) = New();

        // The chord is still the command line's even when there is nothing to paste; handing it to
        // the panel instead would make Shift+Ins mean two different things depending on the
        // clipboard.
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Insert, KeyMods.Shift), ctx));
        Assert.Equal(string.Empty, line.Text);
    }

    [Fact]
    public void PlainInsertStillBelongsToThePanel()
    {
        (CommandLine line, RecordingContext ctx, MemoryClipboard clipboard) = New();
        clipboard.SetText("x");

        Assert.False(line.HandleKey(Keys.Key(ConsoleKey.Insert), ctx));
        Assert.Equal(string.Empty, line.Text);
    }
}

public class CommandHistoryTests
{
    [Fact]
    public void EntriesAreNewestFirst()
    {
        var history = new CommandHistory();
        history.Add("one");
        history.Add("two");

        Assert.Equal(new[] { "two", "one" }, history.All);
    }

    [Fact]
    public void ReRunningACommandMovesItToTheFrontInsteadOfDuplicatingIt()
    {
        var history = new CommandHistory();
        history.Add("a");
        history.Add("b");
        history.Add("a");

        Assert.Equal(new[] { "a", "b" }, history.All);
    }

    [Fact]
    public void BlankCommandsAreNotRemembered()
    {
        var history = new CommandHistory();
        history.Add(null);
        history.Add(string.Empty);
        history.Add("   ");

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void CommandsAreTrimmed()
    {
        var history = new CommandHistory();
        history.Add("  dir  ");

        Assert.Equal(new[] { "dir" }, history.All);
    }

    [Fact]
    public void TheListIsBounded()
    {
        var history = new CommandHistory();
        for (int i = 0; i < CommandHistory.MaxEntries + 50; i++)
        {
            history.Add($"command {i}");
        }

        Assert.Equal(CommandHistory.MaxEntries, history.Count);
        Assert.Equal($"command {CommandHistory.MaxEntries + 49}", history.All[0]);
    }

    [Fact]
    public void TheCursorWalksBothWaysAndStops()
    {
        var history = new CommandHistory();
        history.Add("old");
        history.Add("new");

        Assert.Equal("new", history.Previous());
        Assert.Equal("old", history.Previous());
        Assert.Null(history.Previous());

        Assert.Equal("new", history.Next());
        Assert.Null(history.Next());
        Assert.Null(history.Next());
        Assert.Equal(-1, history.Cursor);
    }

    [Fact]
    public void AddingResetsTheCursor()
    {
        var history = new CommandHistory();
        history.Add("one");
        history.Previous();
        history.Add("two");

        Assert.Equal(-1, history.Cursor);
        Assert.Equal("two", history.Previous());
    }

    [Fact]
    public void ItRoundTripsThroughAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oc-history-{Guid.NewGuid():N}", CommandHistory.FileName);
        try
        {
            var saved = new CommandHistory(path);
            saved.Add("first");
            saved.Add("second");
            Assert.True(saved.Save());

            CommandHistory loaded = CommandHistory.LoadFrom(path);
            Assert.Equal(new[] { "second", "first" }, loaded.All);
            Assert.Equal(-1, loaded.Cursor);
        }
        finally
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void AMissingOrCorruptFileLoadsAsAnEmptyHistory()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"oc-history-{Guid.NewGuid():N}.json");
        Assert.Equal(0, CommandHistory.LoadFrom(missing).Count);

        string corrupt = Path.Combine(Path.GetTempPath(), $"oc-history-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(corrupt, "{ this is not json");
            Assert.Equal(0, CommandHistory.LoadFrom(corrupt).Count);
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Fact]
    public void AnInMemoryHistoryNeverWritesAnything()
    {
        var history = new CommandHistory();
        history.Add("x");

        Assert.Null(history.FilePath);
        Assert.False(history.Save());
    }

    [Fact]
    public void TheDefaultPathSitsBesideTheSettingsFile()
    {
        Assert.Equal(
            Path.GetDirectoryName(Settings.SettingsFilePath),
            Path.GetDirectoryName(CommandHistory.DefaultFilePath));

        Assert.Equal(CommandHistory.FileName, Path.GetFileName(CommandHistory.DefaultFilePath));
    }
}

public class CommandLineDrawTests
{
    private static string Row(ScreenBuffer buf, int y) => KeyBarDrawTests.Row(buf, y);

    [Fact]
    public void ThePromptIsTheDirectoryFollowedByAGreaterThanSign()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory());
        var buf = new ScreenBuffer(40, 1);

        line.Text = "dir";
        line.Draw(buf, 0, @"C:\Work");

        Assert.Equal(@"C:\Work>dir" + new string(' ', 29), Row(buf, 0));
        Assert.Equal(@"C:\Work", line.Prefix);
    }

    [Fact]
    public void TheCaretLandsAfterThePrompt()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory());
        var buf = new ScreenBuffer(40, 3);

        line.Text = "ab";
        line.Draw(buf, 2, "C:");

        Assert.Equal(2, line.CaretY);
        Assert.Equal(5, line.CaretX); // "C:>" is three columns, then two of text
    }

    [Fact]
    public void ALongDirectoryIsTruncatedFromTheLeft()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory());
        var buf = new ScreenBuffer(30, 1);

        line.Draw(buf, 0, @"C:\a-very-long-path\that-will-not-fit\at-all");

        string row = Row(buf, 0);
        Assert.Equal(ScreenBuffer.Ellipsis, row[0]);
        Assert.Contains("at-all>", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextScrollsSoTheCaretStaysVisible()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory());
        var buf = new ScreenBuffer(24, 1);

        line.Prefix = "C:";
        line.Text = new string('x', 40) + "END";
        line.Draw(buf, 0);

        // The window ends one column past the text so the caret has somewhere to sit.
        string row = Row(buf, 0);
        Assert.EndsWith("END ", row, StringComparison.Ordinal);
        Assert.Equal(23, line.CaretX);
    }

    [Fact]
    public void ThePromptAndTheTextUseTheirOwnStyles()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory());
        var buf = new ScreenBuffer(40, 1);

        line.Text = "dir x";
        line.Draw(buf, 0, "C:");

        Assert.Equal(ctx.Theme.CommandLinePrefix, buf.Get(0, 0).Style);
        Assert.Equal(ctx.Theme.CommandLinePrefix, buf.Get(2, 0).Style);
        Assert.Equal(ctx.Theme.CommandLineCommand, buf.Get(3, 0).Style); // the command word
        Assert.Equal(ctx.Theme.CommandLineText, buf.Get(7, 0).Style);    // a plain argument
    }

    [Fact]
    public void DrawingOutsideTheBufferIsIgnored()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory());
        var buf = new ScreenBuffer(20, 1);

        line.Draw(buf, 9);
        Assert.Equal(new string(' ', 20), Row(buf, 0));
        Assert.Throws<ArgumentNullException>(() => line.Draw(null!, 0));
    }
}

public class TryParseCdTests
{
    private static readonly string Base =
        OperatingSystem.IsWindows() ? @"C:\Work\Projects" : "/work/projects";

    [Fact]
    public void ARelativeArgumentResolvesAgainstTheCurrentDirectory()
    {
        Assert.True(CommandExecutor.TryParseCd("cd sub", Base, out string target));
        Assert.Equal(Path.Combine(Base, "sub"), target);
    }

    [Fact]
    public void DotDotWalksUp()
    {
        Assert.True(CommandExecutor.TryParseCd("cd ..", Base, out string target));
        Assert.Equal(Path.GetDirectoryName(Base), target);
    }

    [Fact]
    public void ChdirIsTheSameCommand()
    {
        Assert.True(CommandExecutor.TryParseCd("chdir sub", Base, out string a));
        Assert.True(CommandExecutor.TryParseCd("CD sub", Base, out string b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void ExtraWhitespaceDoesNotMatter()
    {
        Assert.True(CommandExecutor.TryParseCd("   cd    sub   ", Base, out string target));
        Assert.Equal(Path.Combine(Base, "sub"), target);
    }

    [Fact]
    public void AQuotedArgumentKeepsItsSpaces()
    {
        Assert.True(CommandExecutor.TryParseCd("cd \"two words\"", Base, out string target));
        Assert.Equal(Path.Combine(Base, "two words"), target);
    }

    [Fact]
    public void ABareCdGoesHome()
    {
        Assert.True(CommandExecutor.TryParseCd("cd", Base, out string target));
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify)
                .TrimEnd(Path.DirectorySeparatorChar),
            target.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void AnythingElseIsNotACd()
    {
        Assert.False(CommandExecutor.TryParseCd("cdrom", Base, out _));
        Assert.False(CommandExecutor.TryParseCd("git status", Base, out _));
        Assert.False(CommandExecutor.TryParseCd(string.Empty, Base, out _));
        Assert.False(CommandExecutor.TryParseCd("   ", Base, out _));
    }

    /// <summary>The no-space cmd spellings all work, exactly as in cmd.exe.</summary>
    [Fact]
    public void TheCompactSpellingsAreStillACd()
    {
        Assert.True(CommandExecutor.TryParseCd("cd..", Base, out string up));
        Assert.Equal(Path.GetDirectoryName(Base), up);

        Assert.True(CommandExecutor.TryParseCd("cd" + Path.DirectorySeparatorChar, Base, out string root));
        Assert.Equal(Path.GetPathRoot(Base), root);

        Assert.True(CommandExecutor.TryParseCd("cd/", Base, out string slashRoot));
        Assert.Equal(Path.GetPathRoot(Base), slashRoot);
    }

    /// <summary>
    /// A cd chained to another command belongs to the shell: claiming it internally would silently
    /// drop everything after the operator.
    /// </summary>
    [Fact]
    public void ACompoundCommandFallsThroughToTheShell()
    {
        Assert.False(CommandExecutor.TryParseCd("cd sub && dotnet build", Base, out _));
        Assert.False(CommandExecutor.TryParseCd("cd sub | sort", Base, out _));
        Assert.False(CommandExecutor.TryParseCd("cd sub > out.txt", Base, out _));

        // Inside quotes the operator is just a character in the folder name.
        Assert.True(CommandExecutor.TryParseCd("cd \"a & b\"", Base, out string quoted));
        Assert.Equal(Path.Combine(Base, "a & b"), quoted);
    }

    [Fact]
    public void TheTargetIsEmptyWhenTheCommandIsNotACd()
    {
        Assert.False(CommandExecutor.TryParseCd("echo hi", Base, out string target));
        Assert.Equal(string.Empty, target);
    }

    [Fact]
    public void ARunThatIsACdReportsTheSentinelWithoutSpawningAShell()
    {
        using var terminal = Terminal.Create(80, 25);

        int result = CommandExecutor.Run("cd ..", Base, terminal, out string? target);

        Assert.Equal(CommandExecutor.DirectoryChanged, result);
        Assert.Equal(Path.GetDirectoryName(Base), target);
    }

    [Fact]
    public void ABlankCommandLineDoesNothing()
    {
        using var terminal = Terminal.Create(80, 25);
        Assert.Equal(0, CommandExecutor.Run("   ", Base, terminal, out string? target));
        Assert.Null(target);
    }

    [Fact]
    public void ABareDriveLetterChangesDrive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // there are no drive letters to change to
        }

        Assert.True(CommandExecutor.TryParseCd("C:", Base, out string target));
        Assert.Equal(@"C:\", target);

        Assert.True(CommandExecutor.TryParseCd(@"C:\", Base, out string rooted));
        Assert.Equal(@"C:\", rooted);
    }

    [Fact]
    public void TheShellInvocationMatchesThePlatform()
    {
        var info = CommandExecutor.BuildStartInfo("echo hi", Base);

        Assert.False(info.UseShellExecute);
        Assert.Equal(Base, info.WorkingDirectory);

        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith("cmd.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("/c echo hi", info.Arguments);
        }
        else
        {
            Assert.Equal(new[] { "-c", "echo hi" }, info.ArgumentList);
        }
    }
}

public class PathCompletionTests : IDisposable
{
    private readonly string _root;

    public PathCompletionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"oc-complete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "album"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "two words"));
        File.WriteAllText(Path.Combine(_root, "alfa.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "beta.txt"), "x");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void DirectoriesComeFirstAndCarryATrailingSeparator()
    {
        IReadOnlyList<string> matches = PathCompletion.Matches("al", _root);
        char s = Path.DirectorySeparatorChar;

        Assert.Equal(new[] { "album" + s, "alpha" + s, "alfa.txt" }, matches);
    }

    [Fact]
    public void AnEmptyTokenListsTheWholeDirectory()
    {
        Assert.Equal(5, PathCompletion.Matches(string.Empty, _root).Count);
    }

    [Fact]
    public void AMatchContainingASpaceComesBackQuoted()
    {
        IReadOnlyList<string> matches = PathCompletion.Matches("two", _root);
        Assert.Equal(new[] { "\"two words" + Path.DirectorySeparatorChar + "\"" }, matches);
    }

    [Fact]
    public void NothingMatchingYieldsNothing()
    {
        Assert.Empty(PathCompletion.Matches("zzz", _root));
        Assert.Empty(PathCompletion.Matches("x", Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void RepeatedTabCyclesThroughTheAlternatives()
    {
        var completion = new PathCompletion();

        Assert.True(completion.TryComplete("al", 2, _root, out string first, out int caret1));
        Assert.Equal("album" + Path.DirectorySeparatorChar, first);
        Assert.Equal(first.Length, caret1);

        Assert.True(completion.TryComplete(first, caret1, _root, out string second, out int caret2));
        Assert.Equal("alpha" + Path.DirectorySeparatorChar, second);

        Assert.True(completion.TryComplete(second, caret2, _root, out string third, out int caret3));
        Assert.Equal("alfa.txt", third);

        Assert.True(completion.TryComplete(third, caret3, _root, out string wrapped, out _));
        Assert.Equal(first, wrapped);
    }

    [Fact]
    public void OnlyTheTokenUnderTheCaretIsTouched()
    {
        var completion = new PathCompletion();

        Assert.True(completion.TryComplete("copy al", 7, _root, out string text, out int caret));
        Assert.Equal("copy album" + Path.DirectorySeparatorChar, text);
        Assert.Equal(text.Length, caret);
    }

    [Fact]
    public void EditingBetweenTabsStartsAFreshSearch()
    {
        var completion = new PathCompletion();

        completion.TryComplete("al", 2, _root, out string first, out _);
        Assert.Equal("album" + Path.DirectorySeparatorChar, first);

        // A different text and caret: not "the same Tab again".
        Assert.True(completion.TryComplete("be", 2, _root, out string other, out _));
        Assert.Equal("beta.txt", other);

        completion.Reset();
        Assert.Equal(-1, completion.MatchIndex);
        Assert.Equal(0, completion.MatchCount);
    }

    [Fact]
    public void ASingleMatchDoesNotCycle()
    {
        var completion = new PathCompletion();

        Assert.True(completion.TryComplete("be", 2, _root, out string text, out int caret));
        Assert.Equal("beta.txt", text);
        Assert.False(completion.TryComplete(text, caret, _root, out _, out _));
    }

    [Fact]
    public void AnAbsolutePrefixIsKeptVerbatim()
    {
        string token = Path.Combine(_root, "al");
        IReadOnlyList<string> matches = PathCompletion.Matches(token, baseDirectory: null);

        // The temp path may itself contain a space, in which case the match comes back quoted.
        Assert.Equal(
            Path.Combine(_root, "album") + Path.DirectorySeparatorChar,
            matches[0].Trim('"'));
    }

    [Theory]
    [InlineData("dir", 3, 0, 3)]
    [InlineData("copy one two", 8, 5, 3)]
    [InlineData("copy one two", 12, 9, 3)]
    [InlineData("copy ", 5, 5, 0)]
    [InlineData("", 0, 0, 0)]
    public void TokenAtFindsTheWordUnderTheCaret(string text, int caret, int start, int length)
    {
        Assert.Equal((start, length), PathCompletion.TokenAt(text, caret));
    }

    [Fact]
    public void AQuotedTokenRunsToItsClosingQuote()
    {
        const string Text = "copy \"two words\" dest";
        Assert.Equal((5, 11), PathCompletion.TokenAt(Text, 10));
        Assert.Equal("\"two words\"", Text.Substring(5, 11));
    }

    [Fact]
    public void EnvironmentReferencesAreExpandedBeforeMatching()
    {
        Environment.SetEnvironmentVariable("OC_TEST_ROOT", _root);
        try
        {
            string reference = OperatingSystem.IsWindows() ? "%OC_TEST_ROOT%" : "$OC_TEST_ROOT";
            IReadOnlyList<string> matches = PathCompletion.Matches(reference + Path.DirectorySeparatorChar + "be", null);

            Assert.Equal(Path.Combine(_root, "beta.txt"), Assert.Single(matches).Trim('"'));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OC_TEST_ROOT", null);
        }
    }

    [Fact]
    public void BracedAndUnknownNamesBehave()
    {
        Environment.SetEnvironmentVariable("OC_TEST_VALUE", "abc");
        try
        {
            Assert.Equal("abc/x", PathCompletion.ExpandEnvironment("${OC_TEST_VALUE}/x"));
            Assert.Equal("$OC_TEST_MISSING/x", PathCompletion.ExpandEnvironment("$OC_TEST_MISSING/x"));
            Assert.Equal("plain", PathCompletion.ExpandEnvironment("plain"));
            Assert.Equal(string.Empty, PathCompletion.ExpandEnvironment(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OC_TEST_VALUE", null);
        }
    }

    [Fact]
    public void ATildeBecomesTheHomeDirectory()
    {
        string home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        Assert.StartsWith(home, PathCompletion.ExpandEnvironment("~"), StringComparison.Ordinal);
        Assert.StartsWith(home, PathCompletion.ExpandEnvironment("~/sub"), StringComparison.Ordinal);
    }

    [Fact]
    public void TabCompletionOnTheCommandLineSubstitutesTheToken()
    {
        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, new CommandHistory()) { Prefix = _root };

        Keys.Type(line, ctx, "type be");
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Tab), ctx));

        Assert.Equal("type beta.txt", line.Text);
        Assert.Equal(13, line.Caret);
    }
}

public class HelpScreenTests
{
    [Fact]
    public void EveryBindingHasASectionKeysAndADescription()
    {
        Assert.NotEmpty(HelpScreen.Bindings);
        Assert.All(HelpScreen.Bindings, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Section));
            Assert.False(string.IsNullOrWhiteSpace(b.Keys));
            Assert.False(string.IsNullOrWhiteSpace(b.Description));
        });
    }

    [Fact]
    public void TheSectionsAreTheOnesTheTaskAsksFor()
    {
        Assert.Equal(
            new[] { "Panels", "Selection", "View modes", "Sorting", "Commands", "Viewer", "Editor", "Command line" },
            HelpScreen.Sections);
    }

    [Fact]
    public void BindingsAreGroupedSoASectionNeverComesBack()
    {
        var seen = new List<string>();
        string? current = null;

        foreach ((string section, _, _) in HelpScreen.Bindings)
        {
            if (!string.Equals(section, current, StringComparison.Ordinal))
            {
                Assert.DoesNotContain(section, seen);
                seen.Add(section);
                current = section;
            }
        }
    }

    [Fact]
    public void ThePageDrawsInsideItsAreaAndScrolls()
    {
        var help = new HelpScreen(Theme.Classic());
        var buf = new ScreenBuffer(80, 12);
        help.Layout(new Rect(0, 0, 80, 12));

        help.Draw(buf);
        string top = KeyBarDrawTests.Row(buf, 1);
        Assert.Contains("Panels", top, StringComparison.Ordinal);

        help.ScrollBy(5);
        Assert.Equal(5, help.Scroll);

        help.Draw(buf);
        Assert.NotEqual(top, KeyBarDrawTests.Row(buf, 1));
    }

    [Fact]
    public void ScrollingIsClampedToTheContent()
    {
        var help = new HelpScreen(Theme.Classic());
        help.Layout(new Rect(0, 0, 80, 12));

        help.ScrollBy(-100);
        Assert.Equal(0, help.Scroll);

        help.ScrollBy(10_000);
        Assert.Equal(help.LineCount - 10, help.Scroll);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.Enter)]
    [InlineData(ConsoleKey.F1)]
    [InlineData(ConsoleKey.F10)]
    public void TheClosingKeysClose(ConsoleKey key)
    {
        var help = new HelpScreen(Theme.Classic());
        help.Layout(new Rect(0, 0, 80, 24));

        Assert.False(help.HandleInput(InputEvent.FromKey(Keys.Key(key))));
        Assert.True(help.IsClosed);
    }

    [Fact]
    public void NavigationKeysScrollWithoutClosing()
    {
        var help = new HelpScreen(Theme.Classic());
        help.Layout(new Rect(0, 0, 80, 12));

        Assert.True(help.HandleInput(InputEvent.FromKey(Keys.Key(ConsoleKey.DownArrow))));
        Assert.Equal(1, help.Scroll);

        Assert.True(help.HandleInput(InputEvent.FromKey(Keys.Key(ConsoleKey.PageDown))));
        Assert.True(help.Scroll > 1);

        Assert.True(help.HandleInput(InputEvent.FromKey(Keys.Key(ConsoleKey.Home))));
        Assert.Equal(0, help.Scroll);

        Assert.True(help.HandleInput(InputEvent.FromKey(Keys.Key(ConsoleKey.End))));
        Assert.True(help.Scroll > 0);

        Assert.False(help.IsClosed);
    }

    [Fact]
    public void TheWheelScrolls()
    {
        var help = new HelpScreen(Theme.Classic());
        help.Layout(new Rect(0, 0, 80, 12));

        Assert.True(help.HandleInput(InputEvent.FromMouse(
            new MouseEvent(MouseKind.Wheel, 10, 10, MouseButton.None, -1, KeyMods.None))));

        Assert.Equal(3, help.Scroll);
    }

    [Fact]
    public void TheMarkdownIsGeneratedFromTheSameTable()
    {
        string markdown = HelpScreen.ToMarkdown();

        foreach (string section in HelpScreen.Sections)
        {
            Assert.Contains("### " + section, markdown, StringComparison.Ordinal);
        }

        Assert.Contains("| `F5` | Copy |", markdown, StringComparison.Ordinal);
        Assert.Equal(HelpScreen.Sections.Count, markdown.Split("| Key | Action |").Length - 1);
    }

    [Fact]
    public void ATinyAreaIsDeclinedRatherThanDrawnWrong()
    {
        var help = new HelpScreen(Theme.Classic());
        var buf = new ScreenBuffer(3, 2);
        help.Layout(new Rect(0, 0, 3, 2));

        help.Draw(buf);
        Assert.Equal("   ", KeyBarDrawTests.Row(buf, 0));
    }

    [Fact]
    public void TheKeyBarOffersF10()
    {
        var help = new HelpScreen(Theme.Classic());
        Assert.Equal("Quit", help.KeyBarFor(KeyMods.None)![9]);
        Assert.All(help.KeyBarFor(KeyMods.Ctrl)!.Labels, Assert.Empty);
    }
}

/// <summary>The colouring of the typed command and the ghost suggestion drawn after it.</summary>
public class CommandLineColouringTests
{
    private static List<CommandToken> Tokens(string text)
    {
        var tokens = new List<CommandToken>();
        CommandLineSyntax.Tokenize(text, tokens);
        return tokens;
    }

    private static string Slice(string text, CommandToken t) => text.Substring(t.Start, t.Length);

    [Fact]
    public void TheCommandOptionsStringsAndVariablesAreEachTheirOwnToken()
    {
        const string Line = "git commit -m \"first cut\" --amend %USERNAME% $env:HOME";
        List<CommandToken> tokens = Tokens(Line);

        Assert.Collection(
            tokens,
            t => { Assert.Equal(CommandTokenKind.Command, t.Kind); Assert.Equal("git", Slice(Line, t)); },
            t => { Assert.Equal(CommandTokenKind.Option, t.Kind); Assert.Equal("-m", Slice(Line, t)); },
            t => { Assert.Equal(CommandTokenKind.String, t.Kind); Assert.Equal("\"first cut\"", Slice(Line, t)); },
            t => { Assert.Equal(CommandTokenKind.Option, t.Kind); Assert.Equal("--amend", Slice(Line, t)); },
            t => { Assert.Equal(CommandTokenKind.Variable, t.Kind); Assert.Equal("%USERNAME%", Slice(Line, t)); },
            t => { Assert.Equal(CommandTokenKind.Variable, t.Kind); Assert.Equal("$env:HOME", Slice(Line, t)); });
    }

    [Fact]
    public void APipeOrAChainStartsANewCommand()
    {
        const string Line = "dir /s | findstr x && echo done";
        List<CommandToken> tokens = Tokens(Line);

        string[] commands = [.. tokens.Where(t => t.Kind == CommandTokenKind.Command).Select(t => Slice(Line, t))];
        Assert.Equal(["dir", "findstr", "echo"], commands);
        Assert.Contains(tokens, t => t.Kind == CommandTokenKind.Option && Slice(Line, t) == "/s");
    }

    [Fact]
    public void APathIsNotAnOptionAndAQuotedFirstWordIsStillTheCommand()
    {
        const string Line = "\"C:\\Program Files\\tool.exe\" /usr/bin/x -";
        List<CommandToken> tokens = Tokens(Line);

        Assert.Equal(CommandTokenKind.Command, tokens[0].Kind);
        Assert.Equal("\"C:\\Program Files\\tool.exe\"", Slice(Line, tokens[0]));
        Assert.DoesNotContain(tokens, t => t.Kind == CommandTokenKind.Option); // neither the path nor the lone dash
    }

    [Fact]
    public void TheLineIsDrawnColouredWithTheGhostAfterTheText()
    {
        var theme = Theme.Classic();
        var history = new CommandHistory();
        history.Add("git commit -m \"x\" --amend");

        var ctx = new RecordingContext();
        var line = new CommandLine(theme, history);
        Keys.Type(line, ctx, "git commit -m \"x\"");

        var buffer = new ScreenBuffer(80, 1);
        line.Draw(buffer, 0);

        // With no prefix the prompt is just ">" in column 0; the text starts at column 1.
        Assert.Equal(theme.CommandLinePrefix, buffer.Get(0, 0).Style);
        Assert.Equal(theme.CommandLineCommand, buffer.Get(1, 0).Style);   // g
        Assert.Equal(theme.CommandLineText, buffer.Get(5, 0).Style);      // c of commit
        Assert.Equal(theme.CommandLineOption, buffer.Get(12, 0).Style);   // -
        Assert.Equal(theme.CommandLineString, buffer.Get(15, 0).Style);   // "

        // The ghost " --amend" follows the text in the suggestion colour, glyphs included.
        int ghost = 1 + line.Text.Length;
        Assert.Equal(' ', buffer.Get(ghost, 0).Ch);
        Assert.Equal('-', buffer.Get(ghost + 1, 0).Ch);
        Assert.Equal(theme.CommandLineSuggestion, buffer.Get(ghost + 1, 0).Style);

        // The caret sits at the end of the real text, not after the ghost.
        Assert.Equal(ghost, line.CaretX);
    }
}

/// <summary>Edits that arrive from outside the keyboard end a history walk like any other edit.</summary>
public class CommandLineRecallResetTests
{
    [Fact]
    public void SettingTheTextEndsTheWalkSoDownCannotUndoIt()
    {
        var history = new CommandHistory();
        history.Add("git status");
        history.Add("git push");

        var ctx = new RecordingContext();
        var line = new CommandLine(ctx.Theme, history);
        Keys.Type(line, ctx, "git");
        line.HandleKey(Keys.Key(ConsoleKey.UpArrow), ctx);
        Assert.Equal("git push", line.Text);

        // The Alt+F8 history dialog assigns the pick straight to Text.
        line.Text = "dir";
        Assert.True(history.Cursor < 0);

        line.HandleKey(Keys.Key(ConsoleKey.DownArrow), ctx);
        Assert.Equal("dir", line.Text); // not the stale "git"
    }

    [Fact]
    public void ARedirectionAmpersandDoesNotStartANewCommand()
    {
        const string Line = "dotnet build 2>&1 | more";
        var tokens = new List<CommandToken>();
        CommandLineSyntax.Tokenize(Line, tokens);

        string[] commands = [.. tokens.Where(t => t.Kind == CommandTokenKind.Command).Select(t => Line.Substring(t.Start, t.Length))];
        Assert.Equal(["dotnet", "more"], commands);
    }
}

/// <summary>Tab completion the way a shell does it: narrow first, then offer a list.</summary>
public class CommandLineTabCompletionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oc-tab-" + Guid.NewGuid().ToString("N")[..8]);

    public CommandLineTabCompletionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "album"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(_root, "alfa.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "beta.txt"), "b");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A context whose UI answers the completion menu with a canned pick.</summary>
    private sealed class MenuContext(int pick) : IAppContext
    {
        public FakeUi Fake { get; } = new() { MenuAnswer = pick };

        public Theme Theme { get; } = Theme.Classic();

        public Settings Settings { get; } = new();

        public Terminal Terminal => throw new NotSupportedException();

        public IUiServices Ui => Fake;

        public IFilePanel ActivePanel => throw new NotSupportedException();

        public IFilePanel PassivePanel => throw new NotSupportedException();

        public IFilePanel LeftPanel => throw new NotSupportedException();

        public IFilePanel RightPanel => throw new NotSupportedException();

        public void SwapPanels() => throw new NotSupportedException();

        public void SwitchPanel() => throw new NotSupportedException();

        public void RequestQuit() => throw new NotSupportedException();

        public void Redraw() => throw new NotSupportedException();

        public void RefreshBothPanels() => throw new NotSupportedException();

        public void RunShellCommand(string command)
        {
        }

        public void InsertIntoCommandLine(string text) => throw new NotSupportedException();
    }

    private CommandLine Line(IAppContext ctx) => new(ctx.Theme, new CommandHistory()) { Prefix = _root };

    [Fact]
    public void ASingleMatchIsTakenOutright()
    {
        var ctx = new MenuContext(-1);
        CommandLine line = Line(ctx);
        Keys.Type(line, ctx, "type be");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Tab), ctx));
        Assert.Equal("type beta.txt", line.Text);
    }

    [Fact]
    public void SeveralMatchesAreFirstNarrowedToWhatTheyShare()
    {
        var ctx = new MenuContext(-1);
        CommandLine line = Line(ctx);
        Keys.Type(line, ctx, "a");

        // album, alpha and alfa.txt share "al": the token grows, and no list opens yet.
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Tab), ctx));
        Assert.Equal("al", line.Text);
        Assert.Equal(2, line.Caret);
        Assert.Empty(ctx.Fake.Shown);
    }

    [Fact]
    public void WhenNothingMoreIsSharedAListOpensAndThePickIsTaken()
    {
        var ctx = new MenuContext(2); // the third entry: directories first, then alfa.txt
        CommandLine line = Line(ctx);
        Keys.Type(line, ctx, "type al");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Tab), ctx));
        Assert.Equal("type alfa.txt", line.Text);
        Assert.Equal(line.Text.Length, line.Caret);
    }

    [Fact]
    public void CancellingTheListLeavesTheLineAlone()
    {
        var ctx = new MenuContext(-1);
        CommandLine line = Line(ctx);
        Keys.Type(line, ctx, "al");

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Tab), ctx));
        Assert.Equal("al", line.Text);
    }

    [Fact]
    public void TheCommonPrefixIgnoresCaseAndKeepsQuotesWhereNeeded()
    {
        Assert.Equal("al", PathCompletion.CommonPrefix(["album\\", "ALPHA\\", "alfa.txt"]));
        Assert.Equal("\"two w\"", PathCompletion.CommonPrefix(["\"two words\\\"", "\"two wide.txt\""]));
        Assert.Equal(string.Empty, PathCompletion.CommonPrefix(["a", "b"]));
        Assert.Equal(string.Empty, PathCompletion.CommonPrefix([]));
    }
}

/// <summary>Ctrl+R: the incremental reverse search through the history.</summary>
public class CommandLineReverseSearchTests
{
    private static (CommandLine Line, RecordingContext Ctx) New(params string[] oldestFirst)
    {
        var history = new CommandHistory();
        foreach (string entry in oldestFirst)
        {
            history.Add(entry);
        }

        var ctx = new RecordingContext();
        return (new CommandLine(ctx.Theme, history), ctx);
    }

    private static KeyEvent Ctrl(ConsoleKey key) => Keys.Key(key, KeyMods.Ctrl);

    [Fact]
    public void CtrlRWithTextSearchesForItAndStepsOlderOnRepeat()
    {
        (CommandLine line, RecordingContext ctx) = New("git status", "dir", "git push", "dotnet build");
        Keys.Type(line, ctx, "git");

        Assert.True(line.HandleKey(Ctrl(ConsoleKey.R), ctx));
        Assert.True(line.IsSearching);
        Assert.Equal("git", line.SearchQuery);
        Assert.Equal("git push", line.Text); // the newest match

        line.HandleKey(Ctrl(ConsoleKey.R), ctx);
        Assert.Equal("git status", line.Text); // the older one

        line.HandleKey(Ctrl(ConsoleKey.R), ctx);
        Assert.Equal("git status", line.Text); // nothing older: stays put

        line.HandleKey(Ctrl(ConsoleKey.S), ctx);
        Assert.Equal("git push", line.Text); // and back towards the newest
    }

    [Fact]
    public void TypingNarrowsTheQueryAndBackspaceWidensIt()
    {
        (CommandLine line, RecordingContext ctx) = New("git status", "git push", "dotnet build");
        Keys.Type(line, ctx, "g");
        line.HandleKey(Ctrl(ConsoleKey.R), ctx);
        Assert.Equal("git push", line.Text);

        line.HandleKey(Keys.Char('i'), ctx);
        line.HandleKey(Keys.Char('t'), ctx);
        line.HandleKey(Keys.Char(' '), ctx);
        line.HandleKey(Keys.Char('s'), ctx);
        Assert.Equal("git s", line.SearchQuery);
        Assert.Equal("git status", line.Text);

        line.HandleKey(Keys.Key(ConsoleKey.Backspace), ctx);
        Assert.Equal("git ", line.SearchQuery);
        Assert.Equal("git push", line.Text); // widened: the newest match again

        // A query nothing contains keeps the current match, the way bash's "failing" search does.
        line.HandleKey(Keys.Char('z'), ctx);
        Assert.Equal("git push", line.Text);
    }

    [Fact]
    public void EnterKeepsTheMatchAndEscapeRestoresTheOriginalLine()
    {
        (CommandLine line, RecordingContext ctx) = New("dotnet build", "dotnet test");
        Keys.Type(line, ctx, "net");

        line.HandleKey(Ctrl(ConsoleKey.R), ctx);
        Assert.Equal("dotnet test", line.Text);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Escape), ctx));
        Assert.False(line.IsSearching);
        Assert.Equal("net", line.Text);
        Assert.Empty(ctx.Commands);

        line.HandleKey(Ctrl(ConsoleKey.R), ctx);
        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.Enter), ctx));
        Assert.False(line.IsSearching);
        Assert.Equal("dotnet test", line.Text);
        Assert.Empty(ctx.Commands); // Enter keeps the line; the next Enter runs it
    }

    [Fact]
    public void AnArrowEndsTheSearchKeepingTheMatchAndIsThenHandledAsUsual()
    {
        (CommandLine line, RecordingContext ctx) = New("dotnet build");
        Keys.Type(line, ctx, "build");
        line.HandleKey(Ctrl(ConsoleKey.R), ctx);
        Assert.Equal("dotnet build", line.Text);

        Assert.True(line.HandleKey(Keys.Key(ConsoleKey.LeftArrow), ctx));
        Assert.False(line.IsSearching);
        Assert.Equal("dotnet build", line.Text);
        Assert.Equal("dotnet build".Length - 1, line.Caret);
    }

    [Fact]
    public void CtrlROnAnEmptyLineStillBelongsToThePanel()
    {
        (CommandLine line, RecordingContext ctx) = New("dir");

        Assert.False(line.HandleKey(Ctrl(ConsoleKey.R), ctx));
        Assert.False(line.IsSearching);
    }

    [Fact]
    public void ThePromptShowsTheQueryAndTheMatchIsHighlighted()
    {
        (CommandLine line, RecordingContext ctx) = New("dotnet build");
        Keys.Type(line, ctx, "net");
        line.HandleKey(Ctrl(ConsoleKey.R), ctx);

        var buf = new ScreenBuffer(80, 1);
        line.Draw(buf, 0, @"C:\Work");
        string row = buf.RenderPlainText().TrimEnd();

        const string Prompt = "(reverse-i-search)'net': ";
        Assert.StartsWith(Prompt + "dotnet build", row, StringComparison.Ordinal);
        Assert.Equal(ctx.Theme.CommandLinePrefix, buf.Get(0, 0).Style);
        Assert.Equal(ctx.Theme.CommandLineSelected, buf.Get(Prompt.Length + 3, 0).Style); // the 'n' of net
        Assert.Equal(ctx.Theme.CommandLineCommand, buf.Get(Prompt.Length, 0).Style);      // 'd' of dotnet
        Assert.Null(line.Suggestion);
    }
}
