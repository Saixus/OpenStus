using OpenStus.Core;
using OpenStus.Panels;
using OpenStus.Rendering;
using OpenStus.Theming;

namespace OpenStus.Tests;

/// <summary>Tabs remembered between runs: the file, the restore, and what happens to folders that are gone.</summary>
public class SessionStateTests
{
    private static Application Build(string root)
    {
        Terminal terminal = Terminal.Create(120, 40);
        var app = new Application(terminal, new Settings { ShowClock = false }, Theme.Classic(), input: null);
        app.Initialize(new CommandLineArgs { LeftPath = root, RightPath = root });
        return app;
    }

    [Fact]
    public void TheSessionRoundTripsThroughItsFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "oc-session-" + Guid.NewGuid().ToString("N")[..8], "session.json");
        var state = new SessionState
        {
            Left = new PanelSession { Tabs = [@"C:\one", @"C:\two"], Active = 1 },
            Right = new PanelSession { Tabs = [@"D:\"], Active = 0 },
            LeftActive = false,
        };

        try
        {
            Assert.True(state.SaveTo(path));

            SessionState? back = SessionState.LoadFrom(path);
            Assert.NotNull(back);
            Assert.Equal(state.Left.Tabs, back.Left.Tabs);
            Assert.Equal(1, back.Left.Active);
            Assert.Equal(state.Right.Tabs, back.Right.Tabs);
            Assert.False(back.LeftActive);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void AMissingOrBrokenFileIsNoSession()
    {
        Assert.Null(SessionState.LoadFrom(Path.Combine(Path.GetTempPath(), "oc-no-such-" + Guid.NewGuid().ToString("N") + ".json")));
        Assert.Null(SessionState.LoadFrom(null));

        string broken = Path.Combine(Path.GetTempPath(), "oc-broken-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(broken, "{ not json");
        try
        {
            Assert.Null(SessionState.LoadFrom(broken));
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public void CaptureAndRestoreBringTheTabsAndTheFocusBack()
    {
        using var tree = new ShellTree("session-capture");
        using Application app = Build(tree.Root);
        string docs = Path.Combine(tree.Root, "docs");
        string src = Path.Combine(tree.Root, "src");

        app.LeftFilePanel.OpenTab();
        app.LeftFilePanel.Navigate(docs);
        app.RightFilePanel.Navigate(src);
        app.SwitchPanel();

        SessionState captured = app.CaptureSession();
        Assert.Equal([tree.Root, docs], captured.Left.Tabs);
        Assert.Equal(1, captured.Left.Active);
        Assert.Equal([src], captured.Right.Tabs);
        Assert.False(captured.LeftActive);

        // A fresh shell on the same folder, fed the captured session.
        using Application next = Build(tree.Root);
        next.RestoreSession(captured);

        Assert.Equal([tree.Root, docs], next.LeftFilePanel.TabPaths);
        Assert.Equal(1, next.LeftFilePanel.TabIndex);
        Assert.Equal(docs, next.LeftFilePanel.CurrentPath);
        Assert.Equal([src], next.RightFilePanel.TabPaths);
        Assert.Same(next.RightFilePanel, next.ActivePanel);
    }

    [Fact]
    public void AFolderThatIsGoneIsDroppedAndAnEmptySessionChangesNothing()
    {
        using var tree = new ShellTree("session-gone");
        using Application app = Build(tree.Root);
        string docs = Path.Combine(tree.Root, "docs");
        string gone = Path.Combine(tree.Root, "vanished");

        app.RestoreSession(new SessionState
        {
            Left = new PanelSession { Tabs = [gone, docs, gone], Active = 1 },
            Right = new PanelSession { Tabs = [gone], Active = 0 },
        });

        Assert.Equal([docs], app.LeftFilePanel.TabPaths);
        Assert.Equal(docs, app.LeftFilePanel.CurrentPath);
        Assert.Equal([tree.Root], app.RightFilePanel.TabPaths); // nothing survived: unchanged

        app.RestoreSession(null);
        Assert.Equal([docs], app.LeftFilePanel.TabPaths);
    }

    [Fact]
    public void TheActiveTabIndexFollowsTheSurvivors()
    {
        using var tree = new ShellTree("session-index");
        var panel = new FilePanel(null, Theme.Classic(), isLeft: true);
        string docs = Path.Combine(tree.Root, "docs");
        string src = Path.Combine(tree.Root, "src");

        int restored = panel.RestoreTabs([Path.Combine(tree.Root, "nope"), docs, src], active: 2);

        Assert.Equal(2, restored);
        Assert.Equal(1, panel.TabIndex); // src, whose index shifted down when "nope" fell out
        Assert.Equal(src, panel.CurrentPath);
    }
}
