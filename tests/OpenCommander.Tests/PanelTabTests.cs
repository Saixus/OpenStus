using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Panels;
using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Tests;

/// <summary>Panel tabs: the strip, the keys, the mouse, and what a tab remembers.</summary>
public class PanelTabTests
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

    private static string Row(Application app, int y)
    {
        app.RenderNow();
        return app.Terminal.Buffer.RenderPlainText().Split('\n')[y];
    }

    [Fact]
    public void ASinglePanelHasOneTabAndNoStrip()
    {
        var panel = PanelFixture.Panel();

        Assert.Equal(1, panel.TabCount);
        Assert.Equal(0, panel.TabIndex);

        string[] lines = PanelFixture.Render(panel);
        Assert.Contains("Name", lines[1]); // the column titles sit right under the frame
    }

    [Fact]
    public void CtrlTOpensATabOnTheCurrentFolderAndTheStripAppears()
    {
        using var tree = new ShellTree("tabs-open");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;

        Press(app, ConsoleKey.T, KeyMods.Ctrl);

        Assert.Equal(2, panel.TabCount);
        Assert.Equal(1, panel.TabIndex);
        Assert.Equal(tree.Root, panel.CurrentPath);
        Assert.Equal([tree.Root, tree.Root], panel.TabPaths);

        // Row 1 carries the strip: both captions are the folder's name, the shown one highlighted.
        string caption = FilePanel.TabCaption(tree.Root);
        string strip = Row(app, 1);
        Assert.Contains(" " + caption + " │ " + caption + " ", strip, StringComparison.Ordinal);
        Assert.Contains("Name", Row(app, 2)); // the column titles moved down one row
        Assert.Equal(Theme.FarDefault().PanelTitle, app.Terminal.Buffer.Get(1, 1).Style);
        Assert.Equal(Theme.FarDefault().PanelTitleActive, app.Terminal.Buffer.Get(caption.Length + 5, 1).Style);
    }

    [Fact]
    public void EachTabKeepsItsOwnFolderAndSwitchingRestoresIt()
    {
        using var tree = new ShellTree("tabs-switch");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;
        string docs = Path.Combine(tree.Root, "docs");

        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        panel.Navigate(docs);
        Assert.Equal([tree.Root, docs], panel.TabPaths);

        Press(app, ConsoleKey.Tab, KeyMods.Ctrl | KeyMods.Shift, '\t');
        Assert.Equal(0, panel.TabIndex);
        Assert.Equal(tree.Root, panel.CurrentPath);

        Press(app, ConsoleKey.RightArrow, KeyMods.Ctrl | KeyMods.Alt);
        Assert.Equal(1, panel.TabIndex);
        Assert.Equal(docs, panel.CurrentPath);

        Press(app, ConsoleKey.Tab, KeyMods.Ctrl, '\t'); // wraps round
        Assert.Equal(0, panel.TabIndex);
    }

    [Fact]
    public void ATabRemembersItsCursorSortAndView()
    {
        using var tree = new ShellTree("tabs-state");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;

        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        panel.SetSort(OpenCommander.Files.SortMode.Size);
        panel.ViewMode = PanelViewMode.Full;
        panel.CursorIndex = panel.Entries.Count - 1;
        string last = panel.Current!.Name;

        Press(app, ConsoleKey.Tab, KeyMods.Ctrl, '\t');
        Assert.Equal(OpenCommander.Files.SortMode.Name, panel.SortMode);
        Assert.Equal(PanelViewMode.Medium, panel.ViewMode);
        Assert.Equal(0, panel.CursorIndex);

        Press(app, ConsoleKey.Tab, KeyMods.Ctrl, '\t');
        Assert.Equal(OpenCommander.Files.SortMode.Size, panel.SortMode);
        Assert.Equal(PanelViewMode.Full, panel.ViewMode);
        Assert.Equal(last, panel.Current!.Name);
    }

    [Fact]
    public void CtrlWClosesTheTabAndTheLastOneStays()
    {
        using var tree = new ShellTree("tabs-close");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;
        string docs = Path.Combine(tree.Root, "docs");

        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        panel.Navigate(docs);

        Press(app, ConsoleKey.W, KeyMods.Ctrl);
        Assert.Equal(1, panel.TabCount);
        Assert.Equal(tree.Root, panel.CurrentPath);
        Assert.Contains("Name", Row(app, 1)); // the strip is gone: row 1 is the column titles again

        Press(app, ConsoleKey.W, KeyMods.Ctrl);
        Assert.Equal(1, panel.TabCount);
    }

    [Fact]
    public void AClickOnACaptionSwitchesToThatTab()
    {
        using var tree = new ShellTree("tabs-click");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;

        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        Assert.Equal(1, panel.TabIndex);
        app.RenderNow();

        // The first caption starts at column 1 of the left panel.
        app.ProcessInput(InputEvent.FromMouse(new MouseEvent(MouseKind.Down, 2, 1, MouseButton.Left, 0, KeyMods.None)));
        Assert.Equal(0, panel.TabIndex);
    }

    [Fact]
    public void FileRowsAndMouseHitsMoveDownWithTheStrip()
    {
        using var tree = new ShellTree("tabs-rows");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;

        int rowsBefore = panel.VisibleRows;
        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        Assert.Equal(rowsBefore - 1, panel.VisibleRows);

        app.RenderNow();
        Assert.Equal(0, panel.IndexAt(2, 3)); // ".." is now on row 3
        Assert.Equal(-1, panel.IndexAt(2, 2)); // the column titles are not an entry
    }

    [Fact]
    public void TaggedEntriesSurviveATabSwitchAndAreCopiedIntoANewTab()
    {
        using var tree = new ShellTree("tabs-tags");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;

        panel.SelectAllFiles(); // readme.md and notes.txt
        Assert.Equal(2, panel.Entries.Count(e => e.Selected));

        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        Assert.Equal(2, panel.Entries.Count(e => e.Selected)); // the copy inherits the tags

        panel.ClearSelection();
        Press(app, ConsoleKey.Tab, KeyMods.Ctrl, '\t'); // back to the original
        Assert.Equal(2, panel.Entries.Count(e => e.Selected));

        Press(app, ConsoleKey.Tab, KeyMods.Ctrl, '\t'); // and the copy kept its cleared state
        Assert.Equal(0, panel.Entries.Count(e => e.Selected));
    }

    [Fact]
    public void ClosingAMiddleTabShowsItsLeftNeighbour()
    {
        using var tree = new ShellTree("tabs-left");
        using Application app = Build(tree.Root);
        FilePanel panel = app.LeftFilePanel;
        string docs = Path.Combine(tree.Root, "docs");
        string src = Path.Combine(tree.Root, "src");

        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        panel.Navigate(docs);
        Press(app, ConsoleKey.T, KeyMods.Ctrl);
        panel.Navigate(src);
        Press(app, ConsoleKey.Tab, KeyMods.Ctrl | KeyMods.Shift, '\t'); // on docs, the middle one

        Press(app, ConsoleKey.W, KeyMods.Ctrl);
        Assert.Equal([tree.Root, src], panel.TabPaths);
        Assert.Equal(tree.Root, panel.CurrentPath);
    }

    [Fact]
    public void TheCaptionIsTheFolderNameOrTheRootItself()
    {
        Assert.Equal("Demo", FilePanel.TabCaption(@"C:\Work\Demo"));
        Assert.Equal(@"C:\", FilePanel.TabCaption(@"C:\"));
        Assert.Equal(string.Empty, FilePanel.TabCaption(null));
    }
}
