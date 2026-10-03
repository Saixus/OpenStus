using System.Diagnostics;
using OpenStus.Core;
using OpenStus.Input;
using OpenStus.Panels;
using OpenStus.Rendering;
using OpenStus.Shell;
using OpenStus.Theming;

namespace OpenStus.Tests;

/// <summary>
/// A context that records the files the panel asks to run or open. Every other member throws, so
/// an accidental new dependency shows up as a failing test.
/// </summary>
internal sealed class LaunchRecorder : IAppContext
{
    public List<string> Ran { get; } = [];

    public List<string> Opened { get; } = [];

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

    public void RunShellCommand(string command) => throw new NotSupportedException();

    public void RunFile(string path) => Ran.Add(path);

    public void OpenExternally(string path) => Opened.Add(path);

    public void InsertIntoCommandLine(string text) => throw new NotSupportedException();
}

/// <summary>Enter and Shift+Enter on the panel: what gets run, and what gets handed to the system.</summary>
public class PanelLaunchTests
{
    private static (FilePanel Panel, LaunchRecorder Ctx) New(int cursor)
    {
        var ctx = new LaunchRecorder();
        FilePanel panel = PanelFixture.Panel();
        panel.CursorIndex = cursor;
        return (panel, ctx);
    }

    [Fact]
    public void EnterOnAFileRunsItThroughTheShellsChoice()
    {
        (FilePanel panel, LaunchRecorder ctx) = New(4); // notes.txt

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Enter), ctx));

        Assert.Equal(new[] { Path.Combine(PanelFixture.DemoPath, "notes.txt") }, ctx.Ran);
        Assert.Empty(ctx.Opened);
    }

    [Fact]
    public void ShiftEnterOnAFolderOpensItInTheSystemFileManager()
    {
        (FilePanel panel, LaunchRecorder ctx) = New(1); // Documents

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Enter, KeyMods.Shift), ctx));

        Assert.Equal(new[] { Path.Combine(PanelFixture.DemoPath, "Documents") }, ctx.Opened);
        Assert.Equal(PanelFixture.DemoPath, panel.CurrentPath); // the panel stays where it was
    }

    [Fact]
    public void ShiftEnterOnDotDotOpensTheFolderShown()
    {
        (FilePanel panel, LaunchRecorder ctx) = New(0);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Enter, KeyMods.Shift), ctx);

        Assert.Equal(new[] { PanelFixture.DemoPath }, ctx.Opened);
    }

    [Fact]
    public void ShiftEnterOnAFileOpensItRatherThanWalkingIntoIt()
    {
        (FilePanel panel, LaunchRecorder ctx) = New(3); // archive.zip

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Enter, KeyMods.Shift), ctx);

        Assert.Equal(new[] { Path.Combine(PanelFixture.DemoPath, "archive.zip") }, ctx.Opened);
        Assert.Empty(ctx.Ran);
        Assert.Equal(PanelFixture.DemoPath, panel.CurrentPath);
    }

    [Fact]
    public void TheSortLetterIsTheDriveButton()
    {
        FilePanel panel = PanelFixture.Panel();

        Assert.True(panel.IsDriveButtonAt(1, 1));
        Assert.False(panel.IsDriveButtonAt(2, 1));  // the rest of the column titles
        Assert.False(panel.IsDriveButtonAt(1, 0));  // the top frame
        Assert.False(panel.IsDriveButtonAt(0, 1));  // the left frame

        panel.IsVisible = false;
        Assert.False(panel.IsDriveButtonAt(1, 1));
    }
}

/// <summary>Deciding between the console and the desktop, and the hand-over to the desktop.</summary>
public class CommandExecutorLaunchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oc-tests", "launch-" + Guid.NewGuid().ToString("N")[..8]);

    public CommandExecutorLaunchTests() => Directory.CreateDirectory(_root);

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

    /// <summary>The smallest file the PE reader accepts: "MZ", the header offset, "PE\0\0" and a subsystem.</summary>
    private string Image(string name, ushort subsystem)
    {
        const int peAt = 0x80;
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(peAt).CopyTo(bytes, 0x3C);
        bytes[peAt] = (byte)'P';
        bytes[peAt + 1] = (byte)'E';
        BitConverter.GetBytes(subsystem).CopyTo(bytes, peAt + 4 + 20 + 68);

        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void TheSubsystemTellsConsoleProgramsFromWindowedOnes()
    {
        Assert.True(CommandExecutor.IsConsoleProgram(Image("tool.exe", 3)));
        Assert.False(CommandExecutor.IsConsoleProgram(Image("app.exe", 2)));
        Assert.Null(CommandExecutor.IsConsoleProgram(Image("driver.sys", 1)));
    }

    [Fact]
    public void AnythingButAProgramHasNoSubsystem()
    {
        string text = Path.Combine(_root, "notes.txt");
        File.WriteAllText(text, new string('x', 512));

        Assert.Null(CommandExecutor.IsConsoleProgram(text));
        Assert.Null(CommandExecutor.IsConsoleProgram(Path.Combine(_root, "missing.exe")));
    }

    [Fact]
    public void WindowedProgramsLeaveTheConsoleAndConsoleProgramsStay()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(CommandExecutor.RunsInConsole(Image("tool.exe", 3)));
            Assert.False(CommandExecutor.RunsInConsole(Image("app.exe", 2)));
            Assert.True(CommandExecutor.RunsInConsole(Path.Combine(_root, "build.cmd")));
            Assert.True(CommandExecutor.RunsInConsole(Path.Combine(Environment.SystemDirectory, "cmd.exe")));
        }
        else
        {
            string script = Path.Combine(_root, "build.sh");
            File.WriteAllText(script, "#!/bin/sh\n");
            Assert.False(CommandExecutor.RunsInConsole(script));

            File.SetUnixFileMode(script, File.GetUnixFileMode(script) | UnixFileMode.UserExecute);
            Assert.True(CommandExecutor.RunsInConsole(script));
        }
    }

    [Fact]
    public void AFolderGoesToTheFileManagerAndAFileToItsAssociation()
    {
        string file = Path.Combine(_root, "report.docx");
        File.WriteAllText(file, "x");

        ProcessStartInfo folder = CommandExecutor.BuildOpenStartInfo(_root, _root);
        ProcessStartInfo document = CommandExecutor.BuildOpenStartInfo(file, _root);

        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith("explorer.exe", folder.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { _root }, folder.ArgumentList);

            Assert.Equal(file, document.FileName);
            Assert.True(document.UseShellExecute);
        }
        else
        {
            string opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Assert.Equal("/bin/sh", document.FileName);
            Assert.Equal(opener, document.ArgumentList[2]);
            Assert.Equal(file, document.ArgumentList[3]);
            Assert.Equal(_root, folder.ArgumentList[3]);
        }

        Assert.Equal(_root, document.WorkingDirectory);
    }

    [Fact]
    public void AHeadlessTerminalStartsNothing()
    {
        Terminal terminal = Terminal.Create(80, 25);

        Assert.True(CommandExecutor.Open(_root, _root, terminal, out string? error));
        Assert.Null(error);
    }
}

/// <summary>The mouse moves the focus only when it clicks.</summary>
public class PanelFocusTests
{
    private static Application Build(string root, IInputBackend? input = null)
    {
        Terminal terminal = Terminal.Create(120, 40);
        var app = new Application(terminal, new Settings { ShowClock = false }, Theme.Classic(), input);
        app.Initialize(new CommandLineArgs { LeftPath = root, RightPath = root });
        app.RenderNow();
        return app;
    }

    private static InputEvent Mouse(MouseKind kind, int x, int y, int wheel = 0) =>
        InputEvent.FromMouse(new MouseEvent(
            kind,
            x,
            y,
            kind is MouseKind.Move or MouseKind.Wheel ? MouseButton.None : MouseButton.Left,
            wheel,
            KeyMods.None));

    [Fact]
    public void ThePointerPassingOverThePassivePanelLeavesTheFocusAlone()
    {
        using var tree = new ShellTree("focus-hover");
        using Application app = Build(tree.Root);
        FilePanel right = app.RightFilePanel;
        int x = right.Bounds.X + 5;
        int y = right.Bounds.Y + 3;

        app.ProcessInput(Mouse(MouseKind.Move, x, y));
        app.ProcessInput(Mouse(MouseKind.Up, x, y));

        Assert.True(app.LeftFilePanel.IsActive);
        Assert.False(right.IsActive);

        app.ProcessInput(Mouse(MouseKind.Down, x, y));

        Assert.True(right.IsActive);
        Assert.False(app.LeftFilePanel.IsActive);
    }

    [Fact]
    public void TheWheelScrollsThePassivePanelWithoutFocusingIt()
    {
        using var tree = new ShellTree("focus-wheel");
        for (int i = 0; i < 120; i++)
        {
            File.WriteAllText(Path.Combine(tree.Root, $"file{i:D3}.txt"), "x");
        }

        using Application app = Build(tree.Root);
        FilePanel right = app.RightFilePanel;
        right.Reload();

        app.ProcessInput(Mouse(MouseKind.Wheel, right.Bounds.X + 5, right.Bounds.Y + 3, wheel: -1));

        Assert.Equal(FilePanel.WheelRows, right.TopIndex);
        Assert.False(right.IsActive);
        Assert.True(app.LeftFilePanel.IsActive);
    }

    [Fact]
    public void AClickOnTheSortLetterOpensThatPanelsDriveMenu()
    {
        using var tree = new ShellTree("focus-drives");
        var input = new ScriptedInput(ScriptedInput.Key(ConsoleKey.Escape));
        using Application app = Build(tree.Root, input);
        FilePanel right = app.RightFilePanel;

        // Elsewhere on the column titles a click only moves the focus; nothing modal opens.
        app.ProcessInput(Mouse(MouseKind.Down, right.Bounds.X + 5, right.Bounds.Y + 1));
        Assert.True(right.IsActive);
        Assert.Equal(1, input.Remaining);

        // On the letter the drive menu (or, with no drives, its message) opens and takes the Escape.
        app.ProcessInput(Mouse(MouseKind.Down, right.Bounds.X + 1, right.Bounds.Y + 1));
        Assert.Equal(0, input.Remaining);
    }
}
