using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using OpenStus.Archives;
using OpenStus.Core;
using OpenStus.Files;
using OpenStus.Input;
using OpenStus.Operations;
using OpenStus.Panels;
using OpenStus.Rendering;
using OpenStus.Theming;

namespace OpenStus.Tests;

/// <summary>A scratch folder holding archives built for one test.</summary>
internal sealed class ArchiveTree : IDisposable
{
    public ArchiveTree(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), "oc-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Writes a zip holding the given entries; a name ending in '/' is a folder entry.</summary>
    public string Zip(string name, params (string Entry, string? Text)[] entries)
    {
        string path = Path.Combine(Root, name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entry, string? text) in entries)
        {
            ZipArchiveEntry created = zip.CreateEntry(entry);
            created.LastWriteTime = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero);
            if (text is not null)
            {
                using var writer = new StreamWriter(created.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return path;
    }

    /// <summary>Builds a folder tree and packs it as a tar, gzip'd when asked.</summary>
    public string Tar(string name, bool gzip, params (string Path, string Text)[] files)
    {
        string source = Path.Combine(Root, "src-" + Guid.NewGuid().ToString("N")[..6]);
        foreach ((string file, string text) in files)
        {
            string full = Path.Combine(source, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        string path = Path.Combine(Root, name);
        using (FileStream output = File.Create(path))
        using (Stream stream = gzip ? new GZipStream(output, CompressionLevel.Fastest) : output)
        {
            TarFile.CreateFromDirectory(source, stream, includeBaseDirectory: false);
        }

        Directory.Delete(source, recursive: true);
        return path;
    }

    public string Folder(string name)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}

/// <summary>An input backend that plays back a fixed list of events, and fails a test that waits for more.</summary>
internal sealed class ScriptedInput(params InputEvent[] events) : IInputBackend
{
    private readonly Queue<InputEvent> _events = new(events);
    private int _idle;
    private bool _yield;

    public KeyMods CurrentModifiers => KeyMods.None;

    public bool SupportsMouse => false;

    public bool SupportsModifierTracking => false;

    public int Remaining => _events.Count;

    public static InputEvent Key(ConsoleKey key, KeyMods mods = KeyMods.None) =>
        InputEvent.FromKey(new KeyEvent(key, '\0', mods));

    public bool TryRead(out InputEvent ev)
    {
        // One event per pump, the way a person types: the loop drains everything available in one
        // go, so a second key queued behind the one closing a dialog would reach that dialog again.
        if (_yield)
        {
            _yield = false;
            ev = default;
            return false;
        }

        if (_events.TryDequeue(out ev))
        {
            _idle = 0;
            _yield = true;
            return true;
        }

        // A dialog nobody scripted an answer for would otherwise spin the test forever.
        if (++_idle > 400)
        {
            throw new InvalidOperationException("The script ran out while a dialog was still waiting.");
        }

        return false;
    }

    public void Dispose()
    {
    }
}

public class ArchiveFileTests
{
    [Fact]
    public void AZipCatalogueSynthesisesTheFoldersItOnlyImplies()
    {
        using var tree = new ArchiveTree("zip-catalogue");
        string zip = tree.Zip(
            "tools.zip",
            ("readme.txt", "hello"),
            ("bin/run.cmd", "@echo off"),
            ("docs/guide/intro.md", "# intro"),
            ("empty/", null));

        ArchiveFile archive = ArchiveFile.Open(zip);

        Assert.Equal(ArchiveKind.Zip, archive.Kind);
        Assert.Equal(["bin", "docs", "empty", "readme.txt"], archive.List(string.Empty).Select(static i => i.Name).Order(StringComparer.Ordinal));
        Assert.True(archive.IsDirectory("docs/guide"));
        Assert.Equal("intro.md", Assert.Single(archive.List("docs/guide")).Name);
        Assert.Empty(archive.List("empty"));
        Assert.Equal(5, archive.Find("readme.txt")!.Size);
        Assert.False(archive.IsDirectory("readme.txt"));

        DirectorySize docs = archive.Measure("docs");
        Assert.Equal((7L, 1L, 1L), (docs.Bytes, docs.Files, docs.Directories));
    }

    [Theory]
    [InlineData("a\\b.txt", "a/b.txt")]
    [InlineData("./a//b/./c.txt", "a/b/c.txt")]
    [InlineData("dir/", "dir")]
    [InlineData("../evil.txt", null)]
    [InlineData("a/../../evil.txt", null)]
    [InlineData("/etc/passwd", null)]
    [InlineData("C:/Windows/win.ini", null)]
    [InlineData("", null)]
    public void EntryNamesAreSanitised(string name, string? expected) =>
        Assert.Equal(expected, ArchiveFile.Sanitize(name));

    [Fact]
    public void AnEntryThatClimbsOutIsNeverListedNorExtracted()
    {
        using var tree = new ArchiveTree("zip-slip");
        string zip = tree.Zip("evil.zip", ("../evil.txt", "boom"), ("safe.txt", "ok"));
        string target = tree.Folder("out");

        ArchiveFile archive = ArchiveFile.Open(zip);
        Assert.Equal("safe.txt", Assert.Single(archive.Items).Name);

        OperationsResult(archive.Extract(["safe.txt", "../evil.txt"], target));

        Assert.True(File.Exists(Path.Combine(target, "safe.txt")));
        Assert.False(File.Exists(Path.Combine(tree.Root, "evil.txt")));
    }

    [Fact]
    public void ExtractingAFolderKeepsItsShapeAndTheTimeStamps()
    {
        using var tree = new ArchiveTree("zip-extract");
        string zip = tree.Zip(
            "site.zip",
            ("site/index.html", "<p>hi</p>"),
            ("site/css/main.css", "body{}"),
            ("site/empty/", null),
            ("other.txt", "not asked for"));
        string target = tree.Folder("out");

        ArchiveFile archive = ArchiveFile.Open(zip);
        OperationsResult(archive.Extract(["site"], target));

        Assert.Equal("<p>hi</p>", File.ReadAllText(Path.Combine(target, "site", "index.html")));
        Assert.Equal("body{}", File.ReadAllText(Path.Combine(target, "site", "css", "main.css")));
        Assert.True(Directory.Exists(Path.Combine(target, "site", "empty")));
        Assert.False(File.Exists(Path.Combine(target, "other.txt")));
        // A zip records wall-clock time, which is what the extracted file carries.
        Assert.Equal(
            new DateTime(2024, 3, 15, 10, 30, 0),
            File.GetLastWriteTime(Path.Combine(target, "site", "index.html")));
    }

    [Fact]
    public void ExtractingFromASubfolderDropsThePathAboveIt()
    {
        using var tree = new ArchiveTree("zip-inner");
        string zip = tree.Zip("deep.zip", ("a/b/c.txt", "c"), ("a/b/d/e.txt", "e"));
        string target = tree.Folder("out");

        OperationsResult(ArchiveFile.Open(zip).Extract(["a/b/c.txt", "a/b/d"], target));

        Assert.Equal("c", File.ReadAllText(Path.Combine(target, "c.txt")));
        Assert.Equal("e", File.ReadAllText(Path.Combine(target, "d", "e.txt")));
    }

    [Theory]
    [InlineData("pack.tar", false)]
    [InlineData("pack.tar.gz", true)]
    [InlineData("pack.tgz", true)]
    public void TarAndGzippedTarAreReadAndExtracted(string name, bool gzip)
    {
        using var tree = new ArchiveTree("tar");
        string tar = tree.Tar(name, gzip, ("src/main.c", "int main;"), ("README", "read me"));
        string target = tree.Folder("out");

        ArchiveFile archive = ArchiveFile.Open(tar);
        Assert.Equal(gzip ? ArchiveKind.TarGzip : ArchiveKind.Tar, archive.Kind);
        Assert.True(archive.IsDirectory("src"));
        Assert.Equal(9, archive.Find("src/main.c")!.Size);

        OperationsResult(archive.Extract(["src", "README"], target));
        Assert.Equal("int main;", File.ReadAllText(Path.Combine(target, "src", "main.c")));
        Assert.Equal("read me", File.ReadAllText(Path.Combine(target, "README")));
    }

    [Fact]
    public void ALoneGzipHoldsOneFileNamedAfterIt()
    {
        using var tree = new ArchiveTree("gz");
        string gz = Path.Combine(tree.Root, "server.log.gz");
        using (var output = new GZipStream(File.Create(gz), CompressionLevel.Fastest))
        {
            output.Write(Encoding.UTF8.GetBytes("line one\nline two\n"));
        }

        ArchiveFile archive = ArchiveFile.Open(gz);
        ArchiveItem item = Assert.Single(archive.Items);
        Assert.Equal("server.log", item.Name);
        Assert.Equal(18, item.Size);

        string target = tree.Folder("out");
        OperationsResult(archive.Extract(["server.log"], target));
        Assert.Equal("line one\nline two\n", File.ReadAllText(Path.Combine(target, "server.log")));
    }

    [Theory]
    [InlineData("a.zip", true)]
    [InlineData("A.JAR", true)]
    [InlineData("pkg.nupkg", true)]
    [InlineData("x.tar", true)]
    [InlineData("x.tar.gz", true)]
    [InlineData("x.tgz", true)]
    [InlineData("x.7z", false)]
    [InlineData("x.rar", false)]
    [InlineData("x.txt", false)]
    [InlineData(null, false)]
    public void OnlyTheFormatsTheRuntimeReadsCanBeOpened(string? name, bool expected) =>
        Assert.Equal(expected, ArchiveFile.CanOpen(name));

    [Fact]
    public void ACorruptArchiveThrowsAnInvalidDataError()
    {
        using var tree = new ArchiveTree("corrupt");
        string zip = Path.Combine(tree.Root, "broken.zip");
        File.WriteAllText(zip, "this is not a zip");

        Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(zip));
    }

    private static void OperationsResult(OperationResult result) =>
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(static e => e.ToString())));
}

public class ArchivePathTests
{
    [Fact]
    public void APathRunningThroughAnArchiveSplitsAtTheArchiveFile()
    {
        using var tree = new ArchiveTree("split");
        string zip = tree.Zip("a.zip", ("x/y.txt", "y"));

        Assert.True(ArchivePath.TrySplit(Path.Combine(zip, "x", "y.txt"), out string file, out string inner));
        Assert.Equal(zip, file);
        Assert.Equal("x/y.txt", inner);

        Assert.True(ArchivePath.TrySplit(zip, out _, out string root));
        Assert.Equal(string.Empty, root);

        Assert.False(ArchivePath.TrySplit(tree.Root, out _, out _));
        Assert.False(ArchivePath.TrySplit(Path.Combine(tree.Root, "missing", "deeper"), out _, out _));
    }

    [Fact]
    public void AnOrdinaryFileIsNoArchive()
    {
        using var tree = new ArchiveTree("plain");
        string text = Path.Combine(tree.Root, "notes.txt");
        File.WriteAllText(text, "x");

        Assert.False(ArchivePath.TrySplit(Path.Combine(text, "inside"), out _, out _));
    }

    [Fact]
    public void CombineAndTryGetInnerAreInverses()
    {
        string archive = Path.Combine(Path.GetTempPath(), "pack.zip");
        string combined = ArchivePath.Combine(archive, "a/b/c.txt");

        Assert.Equal(Path.Combine(archive, "a", "b", "c.txt"), combined);
        Assert.True(ArchivePath.TryGetInner(combined, archive, out string inner));
        Assert.Equal("a/b/c.txt", inner);
        Assert.True(ArchivePath.TryGetInner(archive, archive, out string top));
        Assert.Equal(string.Empty, top);
        Assert.False(ArchivePath.TryGetInner(archive + "x", archive, out _));
    }
}

public class ArchivePanelTests
{
    private static FilePanel Panel(string path)
    {
        var panel = new FilePanel(null, Theme.Classic(), isLeft: true) { Bounds = new Rect(0, 0, 60, 20) };
        panel.Navigate(path);
        return panel;
    }

    private static void Focus(FilePanel panel, string name) =>
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == name);

    private static void Press(FilePanel panel, ConsoleKey key, KeyMods mods = KeyMods.None) =>
        panel.HandleKey(new KeyEvent(key, '\0', mods), null!);

    [Fact]
    public void EnterWalksIntoAnArchiveAndBackOutOfIt()
    {
        using var tree = new ArchiveTree("panel-enter");
        string zip = tree.Zip("tools.zip", ("bin/run.cmd", "@echo off"), ("readme.txt", "hello"));
        FilePanel panel = Panel(tree.Root);

        Focus(panel, "tools.zip");
        Press(panel, ConsoleKey.Enter);

        Assert.True(panel.InArchive);
        Assert.Equal(zip, panel.CurrentPath);
        Assert.Equal(tree.Root, panel.WorkingDirectory);
        Assert.Equal(["..", "bin", "readme.txt"], panel.Entries.Select(static e => e.Name));
        Assert.Null(panel.Error);

        Focus(panel, "bin");
        Press(panel, ConsoleKey.Enter);
        Assert.Equal(Path.Combine(zip, "bin"), panel.CurrentPath);
        Assert.Equal("bin", panel.ArchiveInnerPath);
        Assert.Equal("run.cmd", panel.Entries[1].Name);

        // Up twice: to the archive's root, then out to the folder holding it, cursor on the archive.
        Press(panel, ConsoleKey.Backspace);
        Assert.Equal(zip, panel.CurrentPath);
        Press(panel, ConsoleKey.Backspace);

        Assert.False(panel.InArchive);
        Assert.Equal(tree.Root, panel.CurrentPath);
        Assert.Equal("tools.zip", panel.Current!.Name);
    }

    [Fact]
    public void CtrlPgDnEntersAnArchiveToo()
    {
        using var tree = new ArchiveTree("panel-ctrl-pgdn");
        string zip = tree.Zip("a.zip", ("x.txt", "x"));
        FilePanel panel = Panel(tree.Root);

        Focus(panel, "a.zip");
        Press(panel, ConsoleKey.PageDown, KeyMods.Ctrl);

        Assert.True(panel.InArchive);
        Assert.Equal(zip, panel.CurrentPath);
    }

    [Fact]
    public void NavigatingStraightToAPathInsideAnArchiveOpensIt()
    {
        using var tree = new ArchiveTree("panel-direct");
        string zip = tree.Zip("a.zip", ("deep/er/file.txt", "x"));

        FilePanel panel = Panel(Path.Combine(zip, "deep", "er"));

        Assert.True(panel.InArchive);
        Assert.Equal("deep/er", panel.ArchiveInnerPath);
        Assert.Equal("file.txt", panel.Entries[1].Name);
        Assert.Equal(Path.Combine(zip, "deep"), panel.Entries[0].FullPath);
    }

    [Fact]
    public void AnUnreadableArchiveSaysSoAndOffersTheWayBack()
    {
        using var tree = new ArchiveTree("panel-corrupt");
        string zip = Path.Combine(tree.Root, "broken.zip");
        File.WriteAllText(zip, "garbage");

        FilePanel panel = Panel(zip);

        Assert.True(panel.InArchive);
        Assert.NotNull(panel.Error);
        Assert.StartsWith("Cannot read the archive", panel.Error, StringComparison.Ordinal);
        Assert.Equal(tree.Root, Assert.Single(panel.Entries).FullPath);
    }

    [Fact]
    public void ExtractToTempTakesOneFileOut()
    {
        using var tree = new ArchiveTree("panel-temp");
        string zip = tree.Zip("a.zip", ("notes/today.txt", "remember"));
        FilePanel panel = Panel(Path.Combine(zip, "notes"));

        string? extracted = panel.ExtractToTemp(panel.Entries[1], out string? error);
        try
        {
            Assert.Null(error);
            Assert.NotNull(extracted);
            Assert.Equal("today.txt", Path.GetFileName(extracted));
            Assert.Equal("remember", File.ReadAllText(extracted));
        }
        finally
        {
            TempArea.Delete(Path.GetDirectoryName(extracted));
        }
    }
}

/// <summary>The shell's side of archives: copying out, refusing to write in.</summary>
public class ArchiveShellTests
{
    private static Application Build(IInputBackend input, string left, string right)
    {
        Terminal terminal = Terminal.Create(120, 40);
        var app = new Application(terminal, new Settings { ShowClock = false, ConfirmDelete = true }, Theme.Classic(), input);
        app.Initialize(new CommandLineArgs { LeftPath = left, RightPath = right });
        return app;
    }

    private static void Press(Application app, ConsoleKey key, KeyMods mods = KeyMods.None) =>
        app.ProcessInput(ScriptedInput.Key(key, mods));

    private static void Focus(FilePanel panel, string name) =>
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == name);

    [Fact]
    public void F5CopiesAFolderOutOfAnArchive()
    {
        using var tree = new ArchiveTree("shell-copy");
        string zip = tree.Zip("site.zip", ("site/index.html", "<p>hi</p>"), ("site/css/main.css", "body{}"));
        string target = tree.Folder("target");

        // Enter accepts the destination the copy dialog offers: the other panel's folder.
        var input = new ScriptedInput(ScriptedInput.Key(ConsoleKey.Enter));
        using Application app = Build(input, zip, target);
        Focus(app.LeftFilePanel, "site");

        Press(app, ConsoleKey.F5);

        Assert.Equal(0, input.Remaining);
        Assert.Equal("<p>hi</p>", File.ReadAllText(Path.Combine(target, "site", "index.html")));
        Assert.Equal("body{}", File.ReadAllText(Path.Combine(target, "site", "css", "main.css")));
        Assert.Contains(app.RightFilePanel.Entries, static e => e.Name == "site");
    }

    [Fact]
    public void DeletingInsideAnArchiveIsRefused()
    {
        using var tree = new ArchiveTree("shell-delete");
        string zip = tree.Zip("a.zip", ("keep.txt", "keep"));
        long before = new FileInfo(zip).Length;

        // One Enter dismisses the "read-only" message.
        var input = new ScriptedInput(ScriptedInput.Key(ConsoleKey.Enter));
        using Application app = Build(input, zip, tree.Root);
        Focus(app.LeftFilePanel, "keep.txt");

        Press(app, ConsoleKey.F8);

        Assert.Equal(0, input.Remaining);
        Assert.Equal(before, new FileInfo(zip).Length);
        Assert.Contains(app.LeftFilePanel.Entries, static e => e.Name == "keep.txt");
    }

    [Fact]
    public void CopyingIntoAnArchiveIsRefused()
    {
        using var tree = new ArchiveTree("shell-into");
        string source = tree.Folder("source");
        File.WriteAllText(Path.Combine(source, "new.txt"), "new");
        string zip = tree.Zip("a.zip", ("old.txt", "old"));
        long before = new FileInfo(zip).Length;

        // Enter accepts the archive as the destination, a second Enter dismisses the refusal.
        var input = new ScriptedInput(ScriptedInput.Key(ConsoleKey.Enter), ScriptedInput.Key(ConsoleKey.Enter));
        using Application app = Build(input, source, zip);
        Focus(app.LeftFilePanel, "new.txt");

        Press(app, ConsoleKey.F5);

        Assert.Equal(0, input.Remaining);
        Assert.Equal(before, new FileInfo(zip).Length);
        Assert.True(File.Exists(zip));
    }
}

public class DriveMenuTests
{
    private static DriveList.DriveItem Drive(string root) =>
        new(root, string.Empty, "NTFS", DriveType.Fixed, 100, 50, true);

    [Fact]
    public void PickingTheOtherPanelsDriveLandsOnTheOtherPanelsFolder()
    {
        DriveList.DriveItem[] drives = [Drive(@"C:\"), Drive(@"D:\")];

        Assert.Equal(@"D:\Work\db", Application.DriveTarget(drives, 1, @"D:\Work\db"));
        Assert.Equal(@"C:\", Application.DriveTarget(drives, 0, @"D:\Work\db"));
        Assert.Equal(@"C:\", Application.DriveTarget(drives, 0, null));
    }

    [Fact]
    public void TheLongestMountPointOwnsAPath()
    {
        DriveList.DriveItem[] drives = [Drive("/"), Drive("/mnt/data")];

        Assert.Equal("/mnt/data/logs", Application.DriveTarget(drives, 1, "/mnt/data/logs"));
        Assert.Equal("/", Application.DriveTarget(drives, 0, "/mnt/data/logs"));
        Assert.Equal("/home/me", Application.DriveTarget(drives, 0, "/home/me"));
    }
}

/// <summary>Ctrl+O leaves the command line live, and with no panel to claim them Shift and the arrows select.</summary>
public class HiddenPanelsSelectionTests
{
    [Fact]
    public void ShiftArrowsSelectOnTheCommandLineOnceCtrlOHidThePanels()
    {
        using var tree = new ShellTree("ctrl-o-select");
        Terminal terminal = Terminal.Create(120, 40);
        using var app = new Application(terminal, new Settings { ShowClock = false }, Theme.Classic(), input: null);
        app.Initialize(new CommandLineArgs { LeftPath = tree.Root, RightPath = tree.Root });

        app.ProcessInput(ScriptedInput.Key(ConsoleKey.O, KeyMods.Ctrl));
        Assert.True(app.PanelsHidden);

        app.CommandLineWidget.Text = "echo hello";
        int tagged = app.LeftFilePanel.Entries.Count(static e => e.Selected);

        for (int i = 0; i < 5; i++)
        {
            app.ProcessInput(ScriptedInput.Key(ConsoleKey.LeftArrow, KeyMods.Shift));
        }

        Assert.Equal("hello", app.CommandLineWidget.SelectedText);
        Assert.Equal(tagged, app.LeftFilePanel.Entries.Count(static e => e.Selected));
        Assert.True(app.PanelsHidden);
    }
}
