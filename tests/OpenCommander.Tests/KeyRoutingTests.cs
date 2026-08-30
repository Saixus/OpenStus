using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Panels;
using OpenCommander.Rendering;
using OpenCommander.Theming;
using OpenCommander.Ui;

namespace OpenCommander.Tests;

/// <summary>
/// End to end checks of the three-way key routing - the binding table, then the command line, then
/// the active panel - for the chords where the order of those three is the whole point: the grey
/// keypad selection keys, Tab, and the function keys the key bar advertises.
/// </summary>
public class KeyRoutingTests
{
    private static Application Build(string root, Action<Settings>? configure = null)
    {
        var settings = new Settings { ShowClock = false };
        configure?.Invoke(settings);

        Terminal terminal = Terminal.Create(120, 40);
        var app = new Application(terminal, settings, Theme.FarDefault(), input: null);

        app.Initialize(new CommandLineArgs { LeftPath = root, RightPath = root });
        return app;
    }

    private static void Press(Application app, ConsoleKey key, KeyMods mods = KeyMods.None, char ch = '\0') =>
        app.ProcessInput(InputEvent.FromKey(new KeyEvent(key, ch, mods)));

    private static int TaggedCount(FilePanel panel) => panel.Entries.Count(static e => e.Selected);

    // ------------------------------------------------------------- Gray+ / Gray- / Gray*

    /// <summary>
    /// The regression guard for the dead grey keypad keys: the Windows backend reports Gray+ as
    /// <c>KeyEvent(ConsoleKey.Add, '+', None)</c>, so a command line that treats it as a printable
    /// character eats it before the panel - which owns selection - ever sees it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("copy ")]
    public void ShiftGrayPlusTagsEveryFileWhateverIsOnTheCommandLine(string typed)
    {
        using var tree = new ShellTree("gray-plus");
        using Application app = Build(tree.Root);

        app.CommandLineWidget.Text = typed;
        Press(app, ConsoleKey.Add, KeyMods.Shift, '+');

        // readme.md and notes.txt; ".." and the two folders are left alone.
        Assert.Equal(2, TaggedCount(app.LeftFilePanel));
        Assert.Equal(typed, app.CommandLineWidget.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("copy ")]
    public void ShiftGrayMinusUntagsEveryFileWhateverIsOnTheCommandLine(string typed)
    {
        using var tree = new ShellTree("gray-minus");
        using Application app = Build(tree.Root);

        app.LeftFilePanel.SelectAllFiles();
        Assert.Equal(2, TaggedCount(app.LeftFilePanel));

        app.CommandLineWidget.Text = typed;
        Press(app, ConsoleKey.Subtract, KeyMods.Shift, '-');

        Assert.Equal(0, TaggedCount(app.LeftFilePanel));
        Assert.Equal(typed, app.CommandLineWidget.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("copy ")]
    public void GrayStarInvertsTheSelectionWhateverIsOnTheCommandLine(string typed)
    {
        using var tree = new ShellTree("gray-star");
        using Application app = Build(tree.Root);

        app.CommandLineWidget.Text = typed;

        Press(app, ConsoleKey.Multiply, KeyMods.None, '*');
        Assert.Equal(2, TaggedCount(app.LeftFilePanel));

        Press(app, ConsoleKey.Multiply, KeyMods.None, '*');
        Assert.Equal(0, TaggedCount(app.LeftFilePanel));

        Assert.Equal(typed, app.CommandLineWidget.Text);
    }

    /// <summary>
    /// Plain Gray+ and Gray- open the select-by-mask dialog, which a headless shell closes at once,
    /// so the observable part here is the routing: the key must not be typed into the command line.
    /// </summary>
    [Theory]
    [InlineData(ConsoleKey.Add, '+')]
    [InlineData(ConsoleKey.Subtract, '-')]
    public void ThePlainGreyKeypadKeysAreNeverTypedIntoTheCommandLine(ConsoleKey key, char ch)
    {
        using var tree = new ShellTree("gray-mask");
        using Application app = Build(tree.Root);

        app.CommandLineWidget.Text = "copy ";
        Press(app, key, KeyMods.None, ch);

        Assert.Equal("copy ", app.CommandLineWidget.Text);
    }

    [Theory]
    [InlineData(KeyMods.None, ConsoleKey.Add)]
    [InlineData(KeyMods.Shift, ConsoleKey.Add)]
    [InlineData(KeyMods.None, ConsoleKey.Subtract)]
    [InlineData(KeyMods.Shift, ConsoleKey.Subtract)]
    [InlineData(KeyMods.None, ConsoleKey.Multiply)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.Multiply)]
    [InlineData(KeyMods.None, ConsoleKey.Divide)]
    public void TheGlobalTableDoesNotStealTheGreyKeypadKeys(KeyMods mods, ConsoleKey key) =>
        Assert.Null(KeyBindings.Default.Find(mods, key));

    /// <summary>
    /// Gray/ is deliberately not in the group Gray+, Gray- and Gray* form: the panel binds nothing to
    /// it, so handing it over would only mean a slash the user cannot type into a path.
    /// </summary>
    [Fact]
    public void GraySlashStillTypesASlashBecauseThePanelWantsNothingWithIt()
    {
        using var tree = new ShellTree("gray-slash");
        using Application app = Build(tree.Root);

        var panel = app.LeftFilePanel;
        Assert.False(panel.HandleKey(new KeyEvent(ConsoleKey.Divide, '/', KeyMods.None), app));

        app.CommandLineWidget.Text = "cd sub";
        Press(app, ConsoleKey.Divide, KeyMods.None, '/');

        Assert.Equal("cd sub/", app.CommandLineWidget.Text);
    }

    // ------------------------------------------------------------- Tab

    [Fact]
    public void TabSwitchesPanelsWhileTheCommandLineIsEmpty()
    {
        using var tree = new ShellTree("tab-empty");
        using Application app = Build(tree.Root);

        Assert.True(app.LeftFilePanel.IsActive);

        Press(app, ConsoleKey.Tab, KeyMods.None, '\t');

        Assert.True(app.RightFilePanel.IsActive);
    }

    /// <summary>
    /// With something typed, Tab has to reach <see cref="CommandLine"/> and complete the path under
    /// the caret. An unguarded Tab binding would answer first and leave completion unreachable.
    /// </summary>
    [Fact]
    public void TabCompletesThePathWhenTheCommandLineHasText()
    {
        using var tree = new ShellTree("tab-complete");
        using Application app = Build(tree.Root);

        app.CommandLineWidget.Prefix = tree.Root;
        app.CommandLineWidget.Text = "type read";

        Press(app, ConsoleKey.Tab, KeyMods.None, '\t');

        Assert.Equal("type readme.md", app.CommandLineWidget.Text);
        Assert.True(app.LeftFilePanel.IsActive); // and the panels did not swap under the user
    }

    [Fact]
    public void ShiftTabSwitchesPanelsEvenWithACommandHalfTyped()
    {
        using var tree = new ShellTree("tab-shift");
        using Application app = Build(tree.Root);

        app.CommandLineWidget.Text = "type read";
        Press(app, ConsoleKey.Tab, KeyMods.Shift, '\t');

        Assert.True(app.RightFilePanel.IsActive);
        Assert.Equal("type read", app.CommandLineWidget.Text);
    }

    [Fact]
    public void TheTabBindingIsGuardedAndShiftTabIsNot()
    {
        KeyBindings.Binding tab = Assert.IsType<KeyBindings.Binding>(
            KeyBindings.Default.Find(KeyMods.None, ConsoleKey.Tab));
        KeyBindings.Binding shiftTab = Assert.IsType<KeyBindings.Binding>(
            KeyBindings.Default.Find(KeyMods.Shift, ConsoleKey.Tab));

        Assert.NotNull(tab.CanRun);
        Assert.Null(shiftTab.CanRun);
    }

    // ------------------------------------------------------------- the key bar and the table

    /// <summary>
    /// Chords the panel answers itself, which is why the global table must not contain them: the Ctrl
    /// row's F3..F11 pick a sort order and Ctrl+F12 opens the sort menu. Every other caption on the
    /// bar has to be answered by <see cref="KeyBindings.Default"/>.
    /// </summary>
    private static bool IsPanelOwned(KeyMods mods, ConsoleKey key) =>
        mods == KeyMods.Ctrl && key is >= ConsoleKey.F3 and <= ConsoleKey.F12;

    /// <summary>
    /// The guard that keeps the two tables in step: a caption the user can read off the key bar must
    /// always answer, even if the answer is only "not implemented in this version".
    /// </summary>
    [Fact]
    public void EveryCaptionOnTheKeyBarHasABinding()
    {
        var missing = new List<string>();

        foreach ((KeyMods mods, KeyBarLabels labels) in KeyBarSets.All)
        {
            for (int i = 0; i < KeyBarLabels.KeyCount; i++)
            {
                if (string.IsNullOrEmpty(labels[i]))
                {
                    continue;
                }

                var key = (ConsoleKey)((int)ConsoleKey.F1 + i);
                if (IsPanelOwned(mods, key) || KeyBindings.Default.Find(mods, key) is not null)
                {
                    continue;
                }

                missing.Add($"{new KeyEvent(key, '\0', mods).ToDisplayString()} \"{labels[i]}\"");
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>The mirror of the check above: nothing claims a key the bar draws blank.</summary>
    [Fact]
    public void NoFunctionKeyIsBoundBehindABlankCaption()
    {
        var unadvertised = new List<string>();

        foreach ((KeyMods mods, KeyBarLabels labels) in KeyBarSets.All)
        {
            for (int i = 0; i < KeyBarLabels.KeyCount; i++)
            {
                var key = (ConsoleKey)((int)ConsoleKey.F1 + i);
                if (string.IsNullOrEmpty(labels[i]) && KeyBindings.Default.Find(mods, key) is not null)
                {
                    unadvertised.Add(new KeyEvent(key, '\0', mods).ToDisplayString());
                }
            }
        }

        Assert.Empty(unadvertised);
    }

    /// <summary>Each chord Finding 3 listed, with the caption it is advertised under.</summary>
    [Theory]
    [InlineData(KeyMods.Shift, ConsoleKey.F1, "Add to archive")]
    [InlineData(KeyMods.Shift, ConsoleKey.F2, "Extract from archive")]
    [InlineData(KeyMods.Shift, ConsoleKey.F3, "Archive commands")]
    [InlineData(KeyMods.Shift, ConsoleKey.F10, "Last menu item")]
    [InlineData(KeyMods.Shift, ConsoleKey.F11, "Sort groups")]
    [InlineData(KeyMods.Shift, ConsoleKey.F12, "Show selected first")]
    [InlineData(KeyMods.Alt, ConsoleKey.F3, "View with...")]
    [InlineData(KeyMods.Alt, ConsoleKey.F4, "Edit with...")]
    [InlineData(KeyMods.Alt, ConsoleKey.F5, "Print")]
    [InlineData(KeyMods.Alt, ConsoleKey.F6, "Create link")]
    [InlineData(KeyMods.Alt, ConsoleKey.F9, "Video mode")]
    [InlineData(KeyMods.Alt, ConsoleKey.F10, "Folder tree")]
    [InlineData(KeyMods.Alt, ConsoleKey.F11, "Viewer history")]
    [InlineData(KeyMods.Ctrl | KeyMods.Shift, ConsoleKey.F3, "Quick view")]
    [InlineData(KeyMods.Ctrl | KeyMods.Shift, ConsoleKey.F4, "Quick edit")]
    [InlineData(KeyMods.Alt | KeyMods.Shift, ConsoleKey.F9, "Plugin configuration")]
    public void TheAdvertisedChordsAnswerWithTheirOwnName(KeyMods mods, ConsoleKey key, string description)
    {
        KeyBindings.Binding binding = Assert.IsType<KeyBindings.Binding>(KeyBindings.Default.Find(mods, key));
        Assert.Equal(description, binding.Description);

        using var tree = new ShellTree("chord");
        using Application app = Build(tree.Root);

        // A headless shell closes the message box at once; what matters is that the chord is claimed
        // and runs to completion instead of falling through to nothing.
        Assert.True(KeyBindings.Default.TryHandle(new KeyEvent(key, '\0', mods), app));
    }

    /// <summary>Alt+F10 used to say "Find folder" while the bar said "Tree".</summary>
    [Fact]
    public void AltF10AgreesWithItsCaption()
    {
        KeyBindings.Binding binding = Assert.IsType<KeyBindings.Binding>(
            KeyBindings.Default.Find(KeyMods.Alt, ConsoleKey.F10));

        Assert.Equal("Folder tree", binding.Description);
        Assert.Equal("Tree", KeyBarSets.Alt[9]);
    }

    // ------------------------------------------------------------- quick search routing

    /// <summary>
    /// The regression guard for the search that could only be typed with Alt held: once Alt+letter
    /// has opened the box, plain letters must keep feeding it - the panel gets first pick - instead
    /// of landing on the command line.
    /// </summary>
    [Fact]
    public void QuickSearchContinuesWithoutHoldingAlt()
    {
        using var tree = new ShellTree("search-plain");
        using Application app = Build(tree.Root);

        // Listing order: "..", docs, src, readme.md, notes.txt.
        Press(app, ConsoleKey.R, KeyMods.Alt, 'r');
        Assert.True(app.LeftFilePanel.Search.IsActive);
        Assert.Equal("readme.md", app.LeftFilePanel.Current?.Name);

        Press(app, ConsoleKey.E, KeyMods.None, 'e');
        Assert.True(app.LeftFilePanel.Search.IsActive);
        Assert.Equal("re", app.LeftFilePanel.Search.Text);
        Assert.Equal("readme.md", app.LeftFilePanel.Current?.Name);

        // Not a single character may have leaked onto the command line.
        Assert.Equal(string.Empty, app.CommandLineWidget.Text);

        Press(app, ConsoleKey.Escape);
        Assert.False(app.LeftFilePanel.Search.IsActive);
        Assert.Equal(string.Empty, app.CommandLineWidget.Text);
    }

    /// <summary>Ctrl+Enter walks to the next match while the box is open, as in Far.</summary>
    [Fact]
    public void QuickSearchCtrlEnterWalksToTheNextMatch()
    {
        using var tree = new ShellTree("search-next");
        using Application app = Build(tree.Root);

        // "o" prefixes nothing, so the first containing name wins: docs.
        Press(app, ConsoleKey.O, KeyMods.Alt, 'o');
        Assert.Equal("docs", app.LeftFilePanel.Current?.Name);

        // The next name containing an "o" is notes.txt.
        Press(app, ConsoleKey.Enter, KeyMods.Ctrl, '\r');
        Assert.True(app.LeftFilePanel.Search.IsActive);
        Assert.Equal("notes.txt", app.LeftFilePanel.Current?.Name);
    }

    /// <summary>Enter closes the box without activating the entry under the cursor.</summary>
    [Fact]
    public void EnterOnlyClosesTheQuickSearchBox()
    {
        using var tree = new ShellTree("search-enter");
        using Application app = Build(tree.Root);

        Press(app, ConsoleKey.D, KeyMods.Alt, 'd');
        Assert.Equal("docs", app.LeftFilePanel.Current?.Name);

        Press(app, ConsoleKey.Enter, KeyMods.None, '\r');
        Assert.False(app.LeftFilePanel.Search.IsActive);

        // The panel is still in the root: the folder was not entered.
        Assert.Equal(tree.Root, app.LeftFilePanel.CurrentPath);
    }

    /// <summary>Switching panels closes a half-typed search rather than leaving the box behind.</summary>
    [Fact]
    public void SwitchingPanelsCancelsTheQuickSearch()
    {
        using var tree = new ShellTree("search-tab");
        using Application app = Build(tree.Root);

        Press(app, ConsoleKey.D, KeyMods.Alt, 'd');
        Assert.True(app.LeftFilePanel.Search.IsActive);

        Press(app, ConsoleKey.Tab);
        Assert.Same(app.RightFilePanel, app.ActivePanel);
        Assert.False(app.LeftFilePanel.Search.IsActive);
    }

    // ------------------------------------------------------------- Ctrl+[ / Ctrl+]

    /// <summary>What the console cooks Ctrl+[ into: the ESC control character.</summary>
    private const char CtrlLeftBracketChar = (char)27;

    /// <summary>What the console cooks Ctrl+] into: the GS control character.</summary>
    private const char CtrlRightBracketChar = (char)29;

    private static string QuotedPath(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? "\"" + path + "\"" : path;

    /// <summary>
    /// Far's Ctrl+[ and Ctrl+] put the left and the right panel's folder on the command line, each
    /// with a trailing space so the next argument can follow at once.
    /// </summary>
    [Fact]
    public void CtrlBracketsInsertThePanelPaths()
    {
        using var tree = new ShellTree("bracket");
        using Application app = Build(tree.Root);
        app.RightFilePanel.Navigate(Path.Combine(tree.Root, "docs"));

        Press(app, ConsoleKey.Oem4, KeyMods.Ctrl, CtrlLeftBracketChar);
        Press(app, ConsoleKey.Oem6, KeyMods.Ctrl, CtrlRightBracketChar);

        Assert.Equal(
            QuotedPath(app.LeftFilePanel.CurrentPath) + " " + QuotedPath(app.RightFilePanel.CurrentPath) + " ",
            app.CommandLineWidget.Text);
    }

    /// <summary>A folder with a space in its path is quoted, or the shell would split it.</summary>
    [Fact]
    public void CtrlBracketQuotesAPathContainingASpace()
    {
        using var tree = new ShellTree("bracket-quote");
        Directory.CreateDirectory(Path.Combine(tree.Root, "my docs"));

        using Application app = Build(tree.Root);
        app.LeftFilePanel.Navigate(Path.Combine(tree.Root, "my docs"));

        Press(app, ConsoleKey.Oem4, KeyMods.Ctrl, CtrlLeftBracketChar);

        Assert.Equal("\"" + app.LeftFilePanel.CurrentPath + "\" ", app.CommandLineWidget.Text);
    }

    /// <summary>
    /// A layout without the brackets on Oem4/Oem6 delivers a different virtual key, so the chord
    /// must also answer by its character - the literal, or the control character the console cooks
    /// it into - exactly like the panel's Ctrl+\.
    /// </summary>
    [Theory]
    [InlineData('[', true)]
    [InlineData((char)27, true)]
    [InlineData(']', false)]
    [InlineData((char)29, false)]
    public void CtrlBracketsWorkByCharacterOnNonUsLayouts(char ch, bool left)
    {
        using var tree = new ShellTree("bracket-char");
        using Application app = Build(tree.Root);
        app.RightFilePanel.Navigate(Path.Combine(tree.Root, "docs"));

        Press(app, (ConsoleKey)0, KeyMods.Ctrl, ch);

        FilePanel panel = left ? app.LeftFilePanel : app.RightFilePanel;
        Assert.Equal(QuotedPath(panel.CurrentPath) + " ", app.CommandLineWidget.Text);
    }

    // ------------------------------------------------------------- F8 / Shift+F8

    /// <summary>
    /// Far's Shift+F8 ignores the selection and deletes only the item under the cursor; the
    /// permanent, bypass-the-recycle-bin variant lives on Shift+Del.
    /// </summary>
    [Fact]
    public void ShiftF8DeletesOnlyTheItemUnderTheCursor()
    {
        using var tree = new ShellTree("del-cursor");
        using Application app = Build(tree.Root, static s =>
        {
            s.ConfirmDelete = false;
            s.UseRecycleBin = false; // keep the test out of the real recycle bin
        });

        app.LeftFilePanel.SelectAllFiles();
        Assert.Equal(2, TaggedCount(app.LeftFilePanel));

        Press(app, ConsoleKey.End); // the cursor lands on the last file of the listing
        string victim = app.LeftFilePanel.Current!.Name;
        Press(app, ConsoleKey.F8, KeyMods.Shift);

        string other = victim == "notes.txt" ? "readme.md" : "notes.txt";
        Assert.False(File.Exists(Path.Combine(tree.Root, victim)));
        Assert.True(File.Exists(Path.Combine(tree.Root, other)));
    }

    /// <summary>Plain F8 still takes the whole selection.</summary>
    [Fact]
    public void F8DeletesTheWholeSelection()
    {
        using var tree = new ShellTree("del-all");
        using Application app = Build(tree.Root, static s =>
        {
            s.ConfirmDelete = false;
            s.UseRecycleBin = false;
        });

        app.LeftFilePanel.SelectAllFiles();
        Press(app, ConsoleKey.F8);

        Assert.False(File.Exists(Path.Combine(tree.Root, "readme.md")));
        Assert.False(File.Exists(Path.Combine(tree.Root, "notes.txt")));
    }

    /// <summary>
    /// The descriptions must not lie: Shift+F8 is the cursor-only delete that still honours the
    /// recycle bin setting, and only Shift+Del bypasses the bin.
    /// </summary>
    [Fact]
    public void TheDeleteChordsSayWhatTheyDo()
    {
        KeyBindings.Binding shiftF8 = Assert.IsType<KeyBindings.Binding>(
            KeyBindings.Default.Find(KeyMods.Shift, ConsoleKey.F8));
        KeyBindings.Binding shiftDel = Assert.IsType<KeyBindings.Binding>(
            KeyBindings.Default.Find(KeyMods.Shift, ConsoleKey.Delete));

        Assert.Equal("Delete the item under the cursor", shiftF8.Description);
        Assert.Equal("Delete permanently, bypassing the recycle bin", shiftDel.Description);
    }

    // ------------------------------------------------------------- non-empty folder confirmation

    /// <summary>
    /// Deleting a folder with content asks a second time, as Far does. Headless, the question
    /// cannot be answered, so the observable behaviour is that the folder survives even with the
    /// general delete confirmation switched off.
    /// </summary>
    [Fact]
    public void ANonEmptyFolderIsNotDeletedWithoutAnAnswer()
    {
        using var tree = new ShellTree("del-nonempty");
        using Application app = Build(tree.Root, static s =>
        {
            s.ConfirmDelete = false;
            s.UseRecycleBin = false;
        });

        Press(app, ConsoleKey.D, KeyMods.Alt, 'd'); // quick search puts the cursor on docs
        Press(app, ConsoleKey.Escape);
        Assert.Equal("docs", app.LeftFilePanel.Current?.Name);

        Press(app, ConsoleKey.F8);

        Assert.True(Directory.Exists(Path.Combine(tree.Root, "docs")));
    }

    /// <summary>An empty folder raises no such question and goes at once.</summary>
    [Fact]
    public void AnEmptyFolderIsDeletedWithoutTheExtraQuestion()
    {
        using var tree = new ShellTree("del-empty");
        using Application app = Build(tree.Root, static s =>
        {
            s.ConfirmDelete = false;
            s.UseRecycleBin = false;
        });

        Press(app, ConsoleKey.S, KeyMods.Alt, 's'); // quick search puts the cursor on src
        Press(app, ConsoleKey.Escape);
        Assert.Equal("src", app.LeftFilePanel.Current?.Name);

        Press(app, ConsoleKey.F8);

        Assert.False(Directory.Exists(Path.Combine(tree.Root, "src")));
    }
}
