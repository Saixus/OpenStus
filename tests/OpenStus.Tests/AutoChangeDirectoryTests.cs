using OpenStus.Core;
using OpenStus.Files;
using OpenStus.Input;
using OpenStus.Panels;
using OpenStus.Rendering;
using OpenStus.Theming;

namespace OpenStus.Tests;

/// <summary>
/// Checks the <see cref="Settings.AutoChangeDirectory"/> option end to end: the shell follows the
/// active panel with the process working directory only while the option is set, it survives a
/// folder that cannot become the working directory, and the Options menu can turn it on and off.
/// </summary>
/// <remarks>
/// The working directory is process-wide state, so the entry value is captured in the constructor
/// and put back in <see cref="Dispose"/>. Nothing here leaves the process anywhere but where it
/// started, which is what keeps the rest of the suite independent of the order these tests run in.
/// </remarks>
public sealed class AutoChangeDirectoryTests : IDisposable
{
    private readonly string _entryDirectory;
    private readonly string _root;
    private readonly string _sub;

    public AutoChangeDirectoryTests()
    {
        _entryDirectory = Directory.GetCurrentDirectory();

        _root = Path.Combine(Path.GetTempPath(), "oc-autocd", Guid.NewGuid().ToString("N")[..8]);
        _sub = Path.Combine(_root, "sub");

        Directory.CreateDirectory(_sub);
        File.WriteAllText(Path.Combine(_sub, "marker.txt"), "here\n");
    }

    public void Dispose()
    {
        // Order matters: the scratch tree cannot be removed while it is the working directory.
        Directory.SetCurrentDirectory(_entryDirectory);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp folder is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void TheWorkingDirectoryIsLeftAloneWhileTheOptionIsOff()
    {
        Directory.SetCurrentDirectory(_entryDirectory);
        Application app = Build(autoChangeDirectory: false);

        app.ActiveFilePanel.Navigate(_sub);
        app.SyncWorkingDirectory();

        AssertWorkingDirectoryIs(_entryDirectory);
    }

    [Fact]
    public void NavigatingWithTheOptionOffChangesNothingEitherThroughTheEventLoop()
    {
        Directory.SetCurrentDirectory(_entryDirectory);
        Application app = Build(autoChangeDirectory: false);

        EnterSubFolder(app);

        Assert.Equal(Normalize(_sub), Normalize(app.ActiveFilePanel.CurrentPath));
        AssertWorkingDirectoryIs(_entryDirectory);
    }

    [Fact]
    public void TheWorkingDirectoryFollowsTheActivePanel()
    {
        Directory.SetCurrentDirectory(_entryDirectory);
        Application app = Build(autoChangeDirectory: true);

        // The start folders already count as a navigation.
        AssertWorkingDirectoryIs(_root);

        app.ActiveFilePanel.Navigate(_sub);
        app.SyncWorkingDirectory();

        AssertWorkingDirectoryIs(_sub);

        // Proof that it is genuinely the process working directory and not just a string we agree
        // on: a relative path now resolves inside the folder the panel is showing.
        Assert.True(File.Exists("marker.txt"));
    }

    [Fact]
    public void EnteringAFolderFromTheEventLoopMovesTheWorkingDirectory()
    {
        Application app = Build(autoChangeDirectory: true);

        EnterSubFolder(app);

        Assert.Equal(Normalize(_sub), Normalize(app.ActiveFilePanel.CurrentPath));
        AssertWorkingDirectoryIs(_sub);
    }

    [Fact]
    public void SwitchingPanelsMovesTheWorkingDirectory()
    {
        Application app = Build(autoChangeDirectory: true);
        app.RightFilePanel.Navigate(_sub);

        AssertWorkingDirectoryIs(_root); // the left panel still has the focus

        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.Tab, '\t', KeyMods.None)));

        Assert.Same(app.RightFilePanel, app.ActiveFilePanel);
        AssertWorkingDirectoryIs(_sub);
    }

    [Fact]
    public void SwappingPanelsMovesTheWorkingDirectory()
    {
        Application app = Build(autoChangeDirectory: true);
        app.RightFilePanel.Navigate(_sub);

        app.SwapPanels();

        AssertWorkingDirectoryIs(_sub);
    }

    [Fact]
    public void AFolderThatCannotBecomeTheWorkingDirectoryIsIgnored()
    {
        Application app = Build(autoChangeDirectory: true);
        AssertWorkingDirectoryIs(_root);

        app.ActiveFilePanel.Navigate(Path.Combine(_root, "does-not-exist"));

        // The panel is free to show a folder the process cannot enter; this must not throw and must
        // not disturb the working directory.
        app.SyncWorkingDirectory();
        app.SyncWorkingDirectory();

        AssertWorkingDirectoryIs(_root);
    }

    [Fact]
    public void AFolderThatDisappearsUnderTheCursorIsIgnored()
    {
        Application app = Build(autoChangeDirectory: true);

        string doomed = Path.Combine(_root, "doomed");
        Directory.CreateDirectory(doomed);

        app.ActiveFilePanel.Navigate(doomed);
        app.SyncWorkingDirectory();
        AssertWorkingDirectoryIs(doomed);

        Directory.SetCurrentDirectory(_root);
        Directory.Delete(doomed);

        app.ActiveFilePanel.Navigate(_sub);
        app.SyncWorkingDirectory();
        AssertWorkingDirectoryIs(_sub);

        app.ActiveFilePanel.Navigate(doomed);
        app.SyncWorkingDirectory();

        AssertWorkingDirectoryIs(_sub); // the last folder that could be entered
    }

    [Fact]
    public void TheOptionsMenuCarriesACheckableToggle()
    {
        Application app = Build(autoChangeDirectory: false);

        MenuItem item = Toggle(app);
        Assert.False(item.Checked);
        Assert.NotNull(item.Action);

        item.Action!.Invoke();
        Assert.True(app.Settings.AutoChangeDirectory);
        Assert.True(Toggle(app).Checked); // a rebuilt menu shows the tick

        Toggle(app).Action!.Invoke();
        Assert.False(app.Settings.AutoChangeDirectory);
        Assert.False(Toggle(app).Checked);
    }

    [Fact]
    public void TheToggleTakesEffectAtOnceAndStopsWhenSwitchedOff()
    {
        Directory.SetCurrentDirectory(_entryDirectory);
        Application app = Build(autoChangeDirectory: false);

        app.ActiveFilePanel.Navigate(_sub);
        AssertWorkingDirectoryIs(_entryDirectory);

        Toggle(app).Action!.Invoke();
        AssertWorkingDirectoryIs(_sub);

        Toggle(app).Action!.Invoke();
        app.ActiveFilePanel.Navigate(_root);
        app.SyncWorkingDirectory();

        AssertWorkingDirectoryIs(_sub); // switched off again: the shell stopped following
    }

    [Fact]
    public void TheToggledOptionIsPersisted()
    {
        Application app = Build(autoChangeDirectory: false);
        Toggle(app).Action!.Invoke();

        string file = Path.Combine(_root, "settings.json");
        Assert.True(app.Settings.SaveTo(file));
        Assert.True(Settings.LoadFrom(file).AutoChangeDirectory);
    }

    // ------------------------------------------------------------------ helpers

    private Application Build(bool autoChangeDirectory)
    {
        var settings = new Settings
        {
            ShowClock = false,
            AutoChangeDirectory = autoChangeDirectory,
        };

        var app = new Application(Terminal.Create(100, 30), settings, Theme.Classic(), input: null);
        app.Initialize(new CommandLineArgs { LeftPath = _root, RightPath = _root });
        return app;
    }

    /// <summary>Puts the cursor on <c>sub</c> and presses Enter, exactly as a user would.</summary>
    private static void EnterSubFolder(Application app)
    {
        FilePanel panel = app.ActiveFilePanel;

        for (int i = 0; i < panel.Entries.Count; i++)
        {
            FileEntry entry = panel.Entries[i];
            if (!entry.IsParent && entry.IsDirectory && entry.Name == "sub")
            {
                panel.CursorIndex = i;
                break;
            }
        }

        Assert.Equal("sub", panel.Current?.Name);
        app.ProcessInput(InputEvent.FromKey(new KeyEvent(ConsoleKey.Enter, '\r', KeyMods.None)));
    }

    private static MenuItem Toggle(Application app) =>
        Assert.Single(
            MainMenu.OptionsMenu(app),
            static i => i.Text.Contains("Auto change", StringComparison.OrdinalIgnoreCase));

    private static void AssertWorkingDirectoryIs(string expected) =>
        Assert.Equal(Normalize(expected), Normalize(Directory.GetCurrentDirectory()));

    private static string Normalize(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }
}
