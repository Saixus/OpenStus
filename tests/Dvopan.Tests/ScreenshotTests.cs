using System.Runtime.CompilerServices;
using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Panels;
using Dvopan.Rendering;
using Dvopan.Theming;
using Dvopan.Ui;

namespace Dvopan.Tests;

/// <summary>
/// A throwaway folder tree that the shell can be pointed at, cleaned up when the test finishes.
/// </summary>
internal sealed class ShellTree : IDisposable
{
    public ShellTree(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), "oc-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);

        Directory.CreateDirectory(Path.Combine(Root, "docs"));
        Directory.CreateDirectory(Path.Combine(Root, "src"));

        File.WriteAllText(Path.Combine(Root, "readme.md"), "# hello\n");
        File.WriteAllText(Path.Combine(Root, "notes.txt"), new string('x', 4096));
        File.WriteAllText(Path.Combine(Root, "docs", "guide.md"), "guide\n");
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// End to end checks of the one-frame <c>--screenshot</c> path: the shell is built on a headless
/// terminal with no input backend, laid out, drawn once, and the resulting frame is inspected.
/// </summary>
public class ScreenshotTests
{
    private const int Width = 120;
    private const int Height = 40;

    private static Application Build(
        string leftPath,
        string rightPath,
        int width = Width,
        int height = Height,
        Action<Settings>? configure = null)
    {
        var settings = new Settings { ShowClock = false };
        configure?.Invoke(settings);

        Terminal terminal = Terminal.Create(width, height);
        var app = new Application(terminal, settings, Theme.Classic(), input: null);

        app.Initialize(new CommandLineArgs { LeftPath = leftPath, RightPath = rightPath });
        return app;
    }

    private static string Frame(Application app)
    {
        app.Layout();
        app.DrawFrame();
        return app.Terminal.Buffer.RenderPlainText();
    }

    private static string Row(Application app, int y)
    {
        ScreenBuffer buffer = app.Terminal.Buffer;
        var chars = new char[buffer.Width];

        for (int x = 0; x < buffer.Width; x++)
        {
            chars[x] = buffer.Get(x, y).Glyph;
        }

        return new string(chars);
    }

    [Fact]
    public void TheFrameHasTwoPanelsSplitDownTheMiddle()
    {
        using var tree = new ShellTree("split");
        using Application app = Build(tree.Root, tree.Root);

        string frame = Frame(app);
        string[] rows = frame.Split('\n');

        Assert.Equal(2, rows[0].Count(c => c == '\u2554')); // two double top-left corners
        Assert.Equal(2, rows[0].Count(c => c == '\u2557')); // two double top-right corners

        // The right panel starts exactly at the half-way column.
        Assert.Equal('\u2554', app.Terminal.Buffer.Get(Width / 2, 0).Glyph);
        Assert.Equal(new Rect(0, 0, Width / 2, Height - 2), app.LeftFilePanel.Bounds);
        Assert.Equal(new Rect(Width / 2, 0, Width / 2, Height - 2), app.RightFilePanel.Bounds);
    }

    [Fact]
    public void TheFrameShowsThePathsTheColumnHeadersAndTheListing()
    {
        using var tree = new ShellTree("content");
        using Application app = Build(tree.Root, tree.Root);

        string frame = Frame(app);

        Assert.Contains(Path.GetFileName(tree.Root), frame, StringComparison.Ordinal);
        Assert.Contains("Name", frame, StringComparison.Ordinal);
        Assert.Contains("readme.md", frame, StringComparison.Ordinal);
        Assert.Contains("docs", frame, StringComparison.Ordinal);
        Assert.Contains("Bytes:", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void TheColumnHeaderRowIsDrawnInTheColumnTitleColour()
    {
        using var tree = new ShellTree("colours");
        using Application app = Build(tree.Root, tree.Root);

        Frame(app);

        string header = Row(app, 1);
        int nameAt = header.IndexOf("Name", StringComparison.Ordinal);
        Assert.True(nameAt > 0);

        Cell cell = app.Terminal.Buffer.Get(nameAt, 1);
        Assert.Equal(Theme.Classic().PanelColumnTitle, cell.Style);
    }

    [Fact]
    public void TheKeyBarIsTheBottomRow()
    {
        using var tree = new ShellTree("keybar");
        using Application app = Build(tree.Root, tree.Root);

        Frame(app);

        string bar = Row(app, Height - 1);
        foreach (string caption in KeyBarSets.None.Labels)
        {
            Assert.Contains(caption, bar, StringComparison.Ordinal);
        }

        Assert.Equal(Height - 1, app.KeyBarRow);
    }

    [Fact]
    public void TheCommandLineShowsTheActivePanelsFolder()
    {
        using var tree = new ShellTree("cmdline");
        using Application app = Build(tree.Root, tree.Root);

        Frame(app);

        string row = Row(app, Height - 2);
        Assert.Equal(Height - 2, app.CommandLineRow);
        Assert.Contains(">", row, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(tree.Root), row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusLineDescribesTheItemUnderTheCursor()
    {
        using var tree = new ShellTree("status");
        using Application app = Build(tree.Root, tree.Root);

        Frame(app);

        // Panel height is Height - 2; the status row is the second from its bottom.
        string status = Row(app, Height - 2 - 2);
        Assert.Contains("..", status, StringComparison.Ordinal);
    }

    [Fact]
    public void HidingTheLeftPanelGivesTheRightOneTheWholeWidth()
    {
        using var tree = new ShellTree("hide");
        using Application app = Build(tree.Root, tree.Root);

        app.TogglePanel(left: true);
        Frame(app);

        Assert.False(app.LeftFilePanel.IsVisible);
        Assert.Equal(new Rect(0, 0, Width, Height - 2), app.RightFilePanel.Bounds);
        Assert.True(app.RightFilePanel.IsActive);
    }

    [Fact]
    public void HidingTheKeyBarGivesTheRowBackToThePanels()
    {
        using var tree = new ShellTree("nokeybar");
        using Application app = Build(tree.Root, tree.Root);

        app.ToggleKeyBar();
        Frame(app);

        Assert.Equal(-1, app.KeyBarRow);
        Assert.Equal(Height - 1, app.CommandLineRow);
        Assert.Equal(Height - 1, app.LeftFilePanel.Bounds.Height);
    }

    [Fact]
    public void TabSwitchesTheActivePanel()
    {
        using var tree = new ShellTree("tab");
        using Application app = Build(tree.Root, tree.Root);

        Assert.True(app.LeftFilePanel.IsActive);

        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.Tab, '\t', KeyMods.None)));

        Assert.True(app.RightFilePanel.IsActive);
        Assert.False(app.LeftFilePanel.IsActive);
        Assert.Same(app.RightFilePanel, app.ActivePanel);
    }

    [Fact]
    public void HoldingAModifierSwapsTheKeyBarCaptionsLive()
    {
        using var tree = new ShellTree("mods");
        using Application app = Build(tree.Root, tree.Root);

        Frame(app);
        Assert.Contains("UserMn", Row(app, Height - 1), StringComparison.Ordinal);

        app.ProcessInput(InputEvent.ModifiersChangedTo(KeyMods.Shift));
        Frame(app);

        string shifted = Row(app, Height - 1);
        Assert.Contains("Extrct", shifted, StringComparison.Ordinal);
        Assert.DoesNotContain("UserMn", shifted, StringComparison.Ordinal);

        app.ProcessInput(InputEvent.ModifiersChangedTo(KeyMods.Ctrl));
        Frame(app);
        Assert.Contains("Unsort", Row(app, Height - 1), StringComparison.Ordinal);

        app.ProcessInput(InputEvent.ModifiersChangedTo(KeyMods.None));
        Frame(app);
        Assert.Contains("UserMn", Row(app, Height - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlOTogglesThePanelsAndTypingStaysOnTheCommandLine()
    {
        using var tree = new ShellTree("ctrlo");
        using Application app = Build(tree.Root, tree.Root);

        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.O, 'o', KeyMods.Ctrl)));
        Assert.True(app.PanelsHidden);

        string frame = Frame(app);
        Assert.DoesNotContain("readme.md", frame, StringComparison.Ordinal);

        // The command line stays live while the panels are off: typing lands there and the
        // panels stay hidden.
        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.D, 'd', KeyMods.None)));
        Assert.True(app.PanelsHidden);
        Assert.Equal("d", app.CommandLineWidget.Text);
        Assert.DoesNotContain("readme.md", Frame(app), StringComparison.Ordinal);

        // Only Ctrl+O brings them back.
        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.O, 'o', KeyMods.Ctrl)));
        Assert.False(app.PanelsHidden);
        Assert.Contains("readme.md", Frame(app), StringComparison.Ordinal);
        Assert.Equal("d", app.CommandLineWidget.Text);
    }

    /// <summary>
    /// While the panels are hidden the key bar is not on screen, so a click on the bottom row must
    /// restore the panels - not fire the function key whose invisible cell it landed in.
    /// </summary>
    [Fact]
    public void AClickWhilePanelsAreHiddenRestoresThemInsteadOfPressingTheInvisibleKeyBar()
    {
        using var tree = new ShellTree("ctrlo-mouse");
        using Application app = Build(tree.Root, tree.Root);

        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.O, 'o', KeyMods.Ctrl)));
        Assert.True(app.PanelsHidden);
        Assert.True(app.KeyBarRow >= 0);

        app.ProcessInput(InputEvent.FromMouse(
            new MouseEvent(MouseKind.Down, 5, app.KeyBarRow, MouseButton.Left, 0, KeyMods.None)));

        Assert.False(app.QuitRequested);
        Assert.False(app.PanelsHidden);
    }

    [Fact]
    public void CtrlEnterInsertsTheNameQuotedWhenItNeedsToBe()
    {
        using var tree = new ShellTree("ctrlenter");
        File.WriteAllText(Path.Combine(tree.Root, "my file.txt"), "x");
        using Application app = Build(tree.Root, tree.Root);

        // A name with a space arrives quoted - the same rule as Ctrl+J, whose synonym the help
        // screen documents Ctrl+Enter to be - and the trailing space comes with it.
        FilePanel panel = app.LeftFilePanel;
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == "my file.txt");
        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.Enter, '\r', KeyMods.Ctrl)));

        Assert.Equal("\"my file.txt\" ", app.CommandLineWidget.Text);

        // A plain name goes in bare, so repeated presses build up an argument list.
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == "readme.md");
        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.Enter, '\r', KeyMods.Ctrl)));

        Assert.Equal("\"my file.txt\" readme.md ", app.CommandLineWidget.Text);
    }

    [Fact]
    public void SwappingThePanelsExchangesTheirSides()
    {
        using var tree = new ShellTree("swap");
        using var other = new ShellTree("swap-other");
        using Application app = Build(tree.Root, other.Root);

        FilePanel wasLeft = app.LeftFilePanel;
        app.SwapPanels();

        Assert.Same(wasLeft, app.RightFilePanel);
        Assert.Equal(0, app.LeftFilePanel.Bounds.X);
        Assert.Equal(Width / 2, app.RightFilePanel.Bounds.X);

        // The focus stays on the left-hand side, and exactly one panel claims it.
        Assert.Same(app.LeftFilePanel, app.ActivePanel);
        Assert.True(app.LeftFilePanel.IsActive);
        Assert.False(app.RightFilePanel.IsActive);
        Assert.Equal(other.Root, app.LeftFilePanel.CurrentPath);
    }

    [Fact]
    public void TheAnsiRenderCarriesColourEscapes()
    {
        using var tree = new ShellTree("ansi");
        using Application app = Build(tree.Root, tree.Root);

        app.Layout();
        app.DrawFrame();

        string ansi = app.Terminal.Buffer.RenderAnsi();
        Assert.Contains('\u001b', ansi);
        Assert.Contains("readme.md", ansi, StringComparison.Ordinal);
    }

    [Fact]
    public void AViewModeFromTheCommandLineIsApplied()
    {
        using var tree = new ShellTree("viewmode");

        var settings = new Settings { ShowClock = false };
        Terminal terminal = Terminal.Create(Width, Height);
        using var app = new Application(terminal, settings, Theme.Classic(), input: null);

        app.Initialize(new CommandLineArgs { LeftPath = tree.Root, RightPath = tree.Root, ViewMode = 3 });

        Assert.Equal(PanelViewMode.Full, app.LeftFilePanel.ViewMode);
        Assert.Contains("Size", Frame(app), StringComparison.Ordinal);
    }

    [Fact]
    public void AVeryNarrowScreenStillRendersWithoutThrowing()
    {
        using var tree = new ShellTree("narrow");
        using Application app = Build(tree.Root, tree.Root, width: 20, height: 10);

        string frame = Frame(app);
        Assert.False(string.IsNullOrEmpty(frame));
    }

    [Fact]
    public void RunWithoutAnInputBackendRendersOneFrameAndReturns()
    {
        using var tree = new ShellTree("run");
        using Application app = Build(tree.Root, tree.Root);

        Assert.Equal(0, app.Run());
    }
}

/// <summary>Checks of the <c>oc</c> command line contract.</summary>
public class CommandLineArgsTests
{
    [Fact]
    public void AnEmptyCommandLineIsValidAndAsksForNothing()
    {
        CommandLineArgs args = CommandLineArgs.Parse([]);

        Assert.False(args.HasError);
        Assert.False(args.Screenshot);
        Assert.Null(args.StartPath);
        Assert.Null(args.EffectiveWidth);
    }

    [Fact]
    public void ThePositionalArgumentIsTheStartFolder()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["C:/Temp"]);

        Assert.Equal("C:/Temp", args.StartPath);
        Assert.False(args.HasError);
    }

    [Fact]
    public void TheDocumentedOptionsAllParse()
    {
        CommandLineArgs args = CommandLineArgs.Parse(
        [
            "--left", "L", "--right", "R", "--theme", "t.json",
            "--screenshot", "--ansi", "--size", "160x50", "--view", "4",
        ]);

        Assert.False(args.HasError);
        Assert.Equal("L", args.LeftPath);
        Assert.Equal("R", args.RightPath);
        Assert.Equal("t.json", args.ThemePath);
        Assert.True(args.Screenshot);
        Assert.True(args.Ansi);
        Assert.Equal(160, args.Width);
        Assert.Equal(50, args.Height);
        Assert.Equal(4, args.ViewMode);
    }

    [Fact]
    public void OptionsAlsoAcceptTheEqualsSpelling()
    {
        // --size requires --screenshot; this test is about the "--opt=value" spelling, not that rule.
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot", "--size=80x25", "--view=1"]);

        Assert.False(args.HasError);
        Assert.Equal(80, args.Width);
        Assert.Equal(25, args.Height);
        Assert.Equal(1, args.ViewMode);
    }

    [Fact]
    public void ScreenshotWithoutASizeUsesTheDocumentedDefault()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot"]);

        Assert.Equal(CommandLineArgs.DefaultScreenshotWidth, args.EffectiveWidth);
        Assert.Equal(CommandLineArgs.DefaultScreenshotHeight, args.EffectiveHeight);
    }

    [Theory]
    [InlineData("--size")]
    [InlineData("--left")]
    [InlineData("--theme")]
    [InlineData("--view")]
    public void AnOptionMissingItsValueIsAnError(string option)
    {
        CommandLineArgs args = CommandLineArgs.Parse([option]);

        Assert.True(args.HasError);
        Assert.Contains("needs a value", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("160")]
    [InlineData("160x")]
    [InlineData("1x1")]
    [InlineData("5000x5000")]
    public void AMalformedSizeIsRejected(string size)
    {
        Assert.False(CommandLineArgs.TryParseSize(size, out _, out _));
    }

    [Fact]
    public void AnUnknownOptionIsAnError()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--nope"]);

        Assert.True(args.HasError);
        Assert.Contains("Unknown option", args.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutOfRangeViewModeIsAnError()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--view", "42"]);

        Assert.True(args.HasError);
    }

    [Fact]
    public void HelpAndVersionAreRecognised()
    {
        Assert.True(CommandLineArgs.Parse(["--help"]).ShowHelp);
        Assert.True(CommandLineArgs.Parse(["--version"]).ShowVersion);
        Assert.Contains("Dvopan", CommandLineArgs.VersionText, StringComparison.Ordinal);
        Assert.Contains("--screenshot", CommandLineArgs.UsageText, StringComparison.Ordinal);
    }
}

/// <summary>Checks of the global key dispatch table.</summary>
public class KeyBindingsTests
{
    [Theory]
    [InlineData(KeyMods.None, ConsoleKey.F1)]
    [InlineData(KeyMods.None, ConsoleKey.F5)]
    [InlineData(KeyMods.None, ConsoleKey.F10)]
    [InlineData(KeyMods.None, ConsoleKey.Tab)]
    [InlineData(KeyMods.Shift, ConsoleKey.F5)]
    [InlineData(KeyMods.Alt, ConsoleKey.F1)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.F1)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.U)]
    public void TheDocumentedChordsAreBound(KeyMods mods, ConsoleKey key) =>
        Assert.NotNull(KeyBindings.Default.Find(mods, key));

    [Theory]
    // These belong to FilePanel and the global table must not steal them.
    [InlineData(KeyMods.Ctrl, ConsoleKey.R)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.H)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.A)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.D1)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.F3)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.F12)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.PageUp)]
    [InlineData(KeyMods.Ctrl, ConsoleKey.PageDown)]
    [InlineData(KeyMods.None, ConsoleKey.Insert)]
    [InlineData(KeyMods.None, ConsoleKey.Enter)]
    public void ThePanelsOwnKeysAreLeftAlone(KeyMods mods, ConsoleKey key) =>
        Assert.Null(KeyBindings.Default.Find(mods, key));

    [Fact]
    public void EveryBindingDescribesItself() =>
        Assert.All(KeyBindings.Default.All, b => Assert.False(string.IsNullOrWhiteSpace(b.Description)));

    [Fact]
    public void ChordsAreUnique()
    {
        var seen = new HashSet<(KeyMods, ConsoleKey)>();
        Assert.All(KeyBindings.Default.All, b => Assert.True(seen.Add((b.Mods, b.Key))));
    }

    [Fact]
    public void DeleteOnlyDeletesWhileTheCommandLineIsEmpty()
    {
        using var tree = new ShellTree("delete-guard");

        Terminal terminal = Terminal.Create(80, 25);
        using var app = new Application(terminal, new Settings { ShowClock = false }, Theme.Classic(), input: null);
        app.Initialize(new CommandLineArgs { LeftPath = tree.Root, RightPath = tree.Root });

        KeyBindings.Binding binding = Assert.IsType<KeyBindings.Binding>(
            KeyBindings.Default.Find(KeyMods.None, ConsoleKey.Delete));

        Assert.NotNull(binding.CanRun);
        Assert.True(binding.CanRun!(app));

        app.CommandLineWidget.Text = "dir";
        Assert.False(binding.CanRun(app));
    }
}

/// <summary>Checks of the F9 menu definition and of the F2 user menu file.</summary>
public class MainMenuTests
{
    private static Application NewApp(string path)
    {
        Terminal terminal = Terminal.Create(100, 30);
        var app = new Application(terminal, new Settings { ShowClock = false }, Theme.Classic(), input: null);
        app.Initialize(new CommandLineArgs { LeftPath = path, RightPath = path });
        return app;
    }

    [Fact]
    public void TheBarHasTheFiveClassicTitles()
    {
        using var tree = new ShellTree("menu");
        using Application app = NewApp(tree.Root);

        IReadOnlyList<MenuItem> bar = MainMenu.Build(app);

        Assert.Equal(
            new[] { "&Left", "&Files", "&Commands", "&Options", "&Right" },
            bar.Select(i => i.Text));

        Assert.All(bar, i => Assert.NotNull(i.SubItems));
        Assert.All(bar, i => Assert.NotEmpty(i.SubItems!));
    }

    [Fact]
    public void EveryLeafCarriesAnActionAndEverySeparatorDoesNot()
    {
        using var tree = new ShellTree("menu-actions");
        using Application app = NewApp(tree.Root);

        foreach (MenuItem top in MainMenu.Build(app))
        {
            foreach (MenuItem item in top.SubItems!)
            {
                if (item.IsSeparator)
                {
                    Assert.Null(item.Action);
                }
                else
                {
                    Assert.NotNull(item.Action);
                    Assert.False(string.IsNullOrWhiteSpace(item.Text));
                }
            }
        }
    }

    [Fact]
    public void ThePanelMenuTicksTheCurrentViewMode()
    {
        using var tree = new ShellTree("menu-view");
        using Application app = NewApp(tree.Root);

        app.LeftFilePanel.ViewMode = PanelViewMode.Wide;
        IReadOnlyList<MenuItem> left = MainMenu.PanelMenu(app, left: true);

        MenuItem wide = left.First(i => i.Text == "&Wide");
        Assert.True(wide.Checked);
        Assert.All(left.Where(i => i.Text == "&Brief"), i => Assert.False(i.Checked));
    }

    [Fact]
    public void AMissingUserMenuFileYieldsNull() =>
        Assert.Null(MainMenu.LoadUserMenu(Path.Combine(Path.GetTempPath(), "oc-no-such-usermenu.json")));

    [Fact]
    public void AUserMenuFileIsRead()
    {
        string path = Path.Combine(Path.GetTempPath(), "oc-usermenu-" + Guid.NewGuid().ToString("N")[..8] + ".json");

        try
        {
            File.WriteAllText(
                path,
                """
                { "items": [ { "title": "&Build", "command": "dotnet build" },
                             { "title": "&Test",  "command": "dotnet test"  } ] }
                """);

            IReadOnlyList<UserMenuEntry>? entries = MainMenu.LoadUserMenu(path);

            Assert.NotNull(entries);
            Assert.Equal(2, entries!.Count);
            Assert.Equal("&Build", entries[0].Title);
            Assert.Equal("dotnet test", entries[1].Command);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMalformedUserMenuFileYieldsNullRatherThanThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), "oc-usermenu-bad-" + Guid.NewGuid().ToString("N")[..8] + ".json");

        try
        {
            File.WriteAllText(path, "{ not json at all");
            Assert.Null(MainMenu.LoadUserMenu(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Keeps the README's key binding table in step with <see cref="HelpScreen.Bindings"/>, which is the
/// single source of truth for both the help screen and the documentation.
/// </summary>
public class ReadmeTests
{
    [Fact]
    public void TheReadmeCarriesTheGeneratedKeyBindingTable()
    {
        string? root = RepositoryRoot();
        if (root is null)
        {
            return; // the sources are not next to the test assembly; nothing to check
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        string expected = HelpScreen.ToMarkdown().Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(expected.Trim(), readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeShowsARenderedFrame()
    {
        string? root = RepositoryRoot();
        if (root is null)
        {
            return;
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("Bytes:", readme, StringComparison.Ordinal);
        Assert.Contains("1Help", readme, StringComparison.Ordinal);
        Assert.Contains("MIT", readme, StringComparison.Ordinal);
    }

    private static string? RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dvopan.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "README.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
