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
    private static Application Build(string root)
    {
        Terminal terminal = Terminal.Create(120, 40);
        var app = new Application(terminal, new Settings { ShowClock = false }, Theme.FarDefault(), input: null);

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
}
