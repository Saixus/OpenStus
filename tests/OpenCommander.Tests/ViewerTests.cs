using System.Text;
using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Text;
using OpenCommander.Theming;
using OpenCommander.Viewer;

namespace OpenCommander.Tests;

/// <summary>
/// A recording stand-in for the shell's modal services, so components can be driven headlessly.
/// </summary>
internal sealed class FakeUi : IUiServices
{
    public List<string> Shown { get; } = [];

    public Queue<string?> Answers { get; } = new();

    public DialogResult MessageAnswer { get; set; } = DialogResult.Ok;

    public bool ConfirmAnswer { get; set; } = true;

    public int MenuAnswer { get; set; } = -1;

    public DialogResult Message(string title, string[] lines, MessageButtons buttons, bool warning = false)
    {
        Shown.Add($"{title}: {string.Join(" | ", lines)}");
        return MessageAnswer;
    }

    public bool Confirm(string title, string[] lines, bool warning = false)
    {
        Shown.Add($"{title}: {string.Join(" | ", lines)}");
        return ConfirmAnswer;
    }

    public void Error(string title, string message)
    {
        Shown.Add($"{title}: {message}");
    }

    public string? Input(string title, string prompt, string initial = "", string? historyKey = null)
    {
        Shown.Add($"{title}/{prompt}");
        return Answers.Count > 0 ? Answers.Dequeue() : null;
    }

    public int Menu(string title, IReadOnlyList<MenuItem> items, int selected = 0, Rect? position = null) => MenuAnswer;

    public void RunModal(IScreenComponent component)
    {
    }

    public void Redraw()
    {
    }
}

/// <summary>Creates and cleans up a temporary file for one test.</summary>
internal sealed class TempFile : IDisposable
{
    public TempFile(byte[] content, string extension = ".txt")
    {
        // Short names on purpose: the viewer centres the file name in its title row and the render
        // tests use narrow viewports, so a full GUID would be truncated with an ellipsis.
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"oc{Guid.NewGuid().ToString("N")[..8]}{extension}");

        File.WriteAllBytes(Path, content);
    }

    public TempFile(string content, string extension = ".txt")
        : this(Encoding.UTF8.GetBytes(content), extension)
    {
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // A test asserting on a locked file must not fail during cleanup.
        }
    }
}

public class ViewerModelTests
{
    private static ViewerModel Model(string content) =>
        ViewerModel.FromBytes(Encoding.UTF8.GetBytes(content), "memory.txt");

    [Fact]
    public void LinesAreWalkedForwardsAndBackwardsToTheSameOffsets()
    {
        using var model = Model("a\r\nbb\nccc");

        Assert.Equal(0, model.FirstLineOffset);
        Assert.Equal(9, model.Length);

        Assert.Equal("a", model.ReadLine(0, out long next));
        Assert.Equal(3, next);
        Assert.Equal("bb", model.ReadLine(3, out next));
        Assert.Equal(6, next);
        Assert.Equal("ccc", model.ReadLine(6, out next));
        Assert.Equal(9, next);

        Assert.Equal(6, model.PreviousLineOffset(9));
        Assert.Equal(3, model.PreviousLineOffset(6));
        Assert.Equal(0, model.PreviousLineOffset(3));
        Assert.Equal(0, model.PreviousLineOffset(0));
    }

    [Fact]
    public void CarriageReturnOnlyFilesAreWalkedToo()
    {
        using var model = Model("one\rtwo\rthree");

        Assert.Equal("one", model.ReadLine(0, out long next));
        Assert.Equal(4, next);
        Assert.Equal("two", model.ReadLine(next, out next));
        Assert.Equal(8, next);
        Assert.Equal(4, model.PreviousLineOffset(8));
        Assert.Equal(LineEndingStyle.Cr, model.LineEnding);
    }

    [Fact]
    public void AByteOrderMarkIsSkippedAndReportedRatherThanShown()
    {
        byte[] bytes = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes("hello\nworld")];
        using var model = ViewerModel.FromBytes(bytes, "bom.txt");

        Assert.True(model.HasBom);
        Assert.Equal(3, model.FirstLineOffset);
        Assert.Equal("UTF-8 BOM", model.EncodingName);
        Assert.Equal("hello", model.ReadLine(model.FirstLineOffset, out _));
    }

    [Fact]
    public void AnEmptyFileHasNothingToRead()
    {
        using var model = Model(string.Empty);

        Assert.Equal(0, model.Length);
        Assert.Equal(string.Empty, model.ReadLine(0, out long next));
        Assert.Equal(0, next);
        Assert.Equal(0, model.LastPageOffset(20));
    }

    [Fact]
    public void AdvanceMovesWholeLinesAndClampsAtBothEnds()
    {
        using var model = Model("1\n2\n3\n4\n5\n");

        Assert.Equal(4, model.Advance(0, 2));
        Assert.Equal(0, model.Advance(4, -2));
        Assert.Equal(0, model.Advance(0, -50));
        Assert.Equal(model.Length, model.Advance(0, 50));
    }

    [Fact]
    public void LastPageOffsetLandsTheGivenNumberOfLinesBackFromTheEnd()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.Append("line").Append(i.ToString("000")).Append('\n');
        }

        using var model = Model(sb.ToString());

        long top = model.LastPageOffset(8);
        Assert.Equal("line092", model.ReadLine(top, out _));
    }

    [Fact]
    public void ALineLongerThanTheWindowIsBrokenOnAStrideThatStepsBackExactly()
    {
        using var model = Model(new string('x', ViewerModel.MaxLineBytes + 2000));

        string first = model.ReadLine(0, out long next);

        Assert.Equal(ViewerModel.MaxLineBytes, first.Length);
        Assert.Equal(ViewerModel.MaxLineBytes, next);
        Assert.Equal(2000, model.ReadLine(next, out _).Length);
        Assert.Equal(0, model.PreviousLineOffset(next));
    }

    [Fact]
    public void FindingTextForwardsReportsTheLineOffsetAndColumn()
    {
        using var model = Model("alpha\nbeta\ngamma beta\n");

        Assert.True(model.Find("beta", 0, ignoreCase: false, backwards: false, out long at, out int column));
        Assert.Equal(6, at);
        Assert.Equal(0, column);

        Assert.True(model.Find("beta", 11, ignoreCase: false, backwards: false, out at, out column));
        Assert.Equal(11, at);
        Assert.Equal(6, column);
    }

    [Fact]
    public void FindingIsCaseInsensitiveOnRequestAndFailsCleanly()
    {
        using var model = Model("Alpha\n");

        Assert.True(model.Find("alpha", 0, ignoreCase: true, backwards: false, out _, out _));
        Assert.False(model.Find("alpha", 0, ignoreCase: false, backwards: false, out _, out _));
        Assert.False(model.Find("nothing here", 0, ignoreCase: true, backwards: false, out _, out _));
        Assert.False(model.Find(string.Empty, 0, ignoreCase: true, backwards: false, out _, out _));
    }

    [Fact]
    public void FindingBackwardsReturnsTheLastMatchBeforeThePosition()
    {
        using var model = Model("hit\nmiss\nhit\nmiss\n");

        Assert.True(model.Find("hit", 13, ignoreCase: false, backwards: true, out long at, out _));
        Assert.Equal(9, at);
    }

    [Fact]
    public void TheLazyLineIndexGrowsAsThePageIsScrolledThroughAndGivesLineNumbers()
    {
        using var model = Model("a\nb\nc\nd\n");

        Assert.False(model.TryGetLineNumber(6, out _));

        model.EnsureIndexed(model.Length);

        Assert.True(model.TryGetLineNumber(0, out int first));
        Assert.Equal(1, first);
        Assert.True(model.TryGetLineNumber(4, out int third));
        Assert.Equal(3, third);
        Assert.True(model.IndexedLineCount >= 4);
    }

    [Fact]
    public void ReadBlockClampsAtTheEndOfTheFile()
    {
        using var model = Model("abcdef");

        Assert.Equal("abc"u8.ToArray(), model.ReadBlock(0, 3));
        Assert.Equal("ef"u8.ToArray(), model.ReadBlock(4, 100));
        Assert.Empty(model.ReadBlock(6, 10));
    }
}

public class FileViewerRenderTests
{
    private static readonly Theme Palette = Theme.FarDefault();

    private static InputEvent Key(ConsoleKey key, KeyMods mods = KeyMods.None) =>
        InputEvent.FromKey(new KeyEvent(key, '\0', mods));

    [Fact]
    public void ASmallTextFileRendersAsATitleTheLinesAndAStatusBar()
    {
        using var file = new TempFile("alpha\nbeta\ngamma\n");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        string[] rows = viewer.RenderToText(40, 10).Split('\n');

        Assert.Equal(10, rows.Length);
        Assert.Equal(file.Name, rows[0].Trim());
        Assert.Equal("alpha", rows[1].Trim());
        Assert.Equal("beta", rows[2].Trim());
        Assert.Equal("gamma", rows[3].Trim());
        Assert.Equal(string.Empty, rows[4].Trim());

        string status = rows[9];
        Assert.Contains("Col 0", status, StringComparison.Ordinal);
        Assert.Contains("0%", status, StringComparison.Ordinal);
        Assert.Contains("UTF-8", status, StringComparison.Ordinal);
        Assert.Contains("LF", status, StringComparison.Ordinal);
        Assert.Empty(ui.Shown);
    }

    [Fact]
    public void HexModeShowsTheOffsetSixteenBytesAndTheAsciiGutter()
    {
        using var file = new TempFile("alpha\nbeta\ngamma\n");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        Assert.True(viewer.HandleInput(Key(ConsoleKey.F4)));
        Assert.True(viewer.HexMode);

        string[] rows = viewer.RenderToText(90, 10).Split('\n');

        Assert.Equal(
            "00000000: 61 6C 70 68 61 0A 62 65  74 61 0A 67 61 6D 6D 61 │ alpha.beta.gamma",
            rows[1]);
        Assert.StartsWith("00000010: 0A ", rows[2], StringComparison.Ordinal);
        Assert.EndsWith("│ .", rows[2], StringComparison.Ordinal);
        Assert.Contains("[hex]", rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void HexModeOfAnEmptyFileDrawsOneEmptyRowRatherThanNothingOrACrash()
    {
        using var file = new TempFile(string.Empty);
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.HandleInput(Key(ConsoleKey.F4));
        string[] rows = viewer.RenderToText(90, 6).Split('\n');

        Assert.StartsWith("00000000:", rows[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TogglingHexBackToTextLandsOnALineStart()
    {
        // Sixteen byte lines, so the hex row grid and the line grid coincide and the round trip
        // is exact rather than rounded down to the row the line started in.
        using var file = new TempFile("0123456789abcde\n0123456789abcde\n0123456789abcde\n");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.RenderToText(90, 10);
        viewer.HandleInput(Key(ConsoleKey.DownArrow));
        Assert.Equal(16, viewer.TopOffset);

        viewer.HandleInput(Key(ConsoleKey.F4));
        Assert.True(viewer.HexMode);
        Assert.Equal(16, viewer.TopOffset);

        viewer.HandleInput(Key(ConsoleKey.F4));
        Assert.False(viewer.HexMode);
        Assert.Equal(16, viewer.TopOffset);
    }

    [Fact]
    public void ArrowAndPageKeysScrollTheViewport()
    {
        var content = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            content.Append("line").Append(i.ToString("000")).Append('\n');
        }

        using var file = new TempFile(content.ToString());
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.RenderToText(40, 10);
        viewer.HandleInput(Key(ConsoleKey.DownArrow));
        Assert.Equal("line001", viewer.RenderToText(40, 10).Split('\n')[1].Trim());

        viewer.HandleInput(Key(ConsoleKey.PageDown));
        Assert.Equal("line009", viewer.RenderToText(40, 10).Split('\n')[1].Trim());

        viewer.HandleInput(Key(ConsoleKey.Home, KeyMods.Ctrl));
        Assert.Equal("line000", viewer.RenderToText(40, 10).Split('\n')[1].Trim());

        viewer.HandleInput(Key(ConsoleKey.End, KeyMods.Ctrl));
        Assert.Equal("line092", viewer.RenderToText(40, 10).Split('\n')[1].Trim());
    }

    [Fact]
    public void UnwrappedModeScrollsHorizontallyAndReportsTheColumn()
    {
        using var file = new TempFile("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ\nshort\n");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.HandleInput(Key(ConsoleKey.RightArrow, KeyMods.Ctrl));

        string[] rows = viewer.RenderToText(40, 6).Split('\n');

        // Far overlays a marker on the first column once the text is scrolled off to the left.
        Assert.Equal("◄LMNOPQRSTUVWXYZ", rows[1]);
        Assert.Contains("Col 20", rows[5], StringComparison.Ordinal);
    }

    [Fact]
    public void WrapModeBreaksALongLineIntoViewportWidthRows()
    {
        using var file = new TempFile(new string('a', 30) + new string('b', 20) + "\n");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        Assert.True(viewer.HandleInput(Key(ConsoleKey.F2)));
        Assert.True(viewer.Wrap);

        string[] rows = viewer.RenderToText(20, 8).Split('\n');

        Assert.Equal(new string('a', 20), rows[1]);
        Assert.Equal(new string('a', 10) + new string('b', 10), rows[2]);
        Assert.Equal(new string('b', 10), rows[3]);
    }

    [Fact]
    public void TabsAreExpandedForDisplay()
    {
        using var file = new TempFile("a\tb\n");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path) { TabSize = 8 };

        Assert.Equal("a       b", viewer.RenderToText(40, 6).Split('\n')[1]);
    }

    [Fact]
    public void SearchJumpsToTheMatchingLine()
    {
        using var file = new TempFile("alpha\nbeta\ngamma\n");
        var ui = new FakeUi();
        ui.Answers.Enqueue("gamma");
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.HandleInput(Key(ConsoleKey.F7));

        Assert.Equal("gamma", viewer.RenderToText(40, 10).Split('\n')[1].Trim());
    }

    [Fact]
    public void SearchIsCaseInsensitiveByDefaultAndReportsAMiss()
    {
        using var file = new TempFile("alpha\nBETA\n");
        var ui = new FakeUi();
        ui.Answers.Enqueue("beta");
        ui.Answers.Enqueue("nowhere");
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.HandleInput(Key(ConsoleKey.F7));
        Assert.Equal(6, viewer.TopOffset);

        viewer.HandleInput(Key(ConsoleKey.F7));
        Assert.Contains(ui.Shown, s => s.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void GoToAcceptsDecimalHexAndPercentages()
    {
        var content = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            content.Append("line").Append(i.ToString("000")).Append('\n');
        }

        using var file = new TempFile(content.ToString());
        var ui = new FakeUi();
        ui.Answers.Enqueue("16");
        ui.Answers.Enqueue("0x20");
        ui.Answers.Enqueue("0%");
        using var viewer = new FileViewer(Palette, ui, file.Path);

        viewer.HandleInput(Key(ConsoleKey.F5));
        Assert.Equal(16, viewer.TopOffset);

        viewer.HandleInput(Key(ConsoleKey.F5));
        Assert.Equal(32, viewer.TopOffset);

        viewer.HandleInput(Key(ConsoleKey.F5));
        Assert.Equal(0, viewer.TopOffset);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F3)]
    [InlineData(ConsoleKey.F10)]
    public void EscapeF3AndF10AllClose(ConsoleKey key)
    {
        using var file = new TempFile("x\n");
        using var viewer = new FileViewer(Palette, new FakeUi(), file.Path);

        Assert.False(viewer.HandleInput(Key(key)));
        Assert.True(viewer.IsClosed);
    }

    [Fact]
    public void TheViewerKeyBarIsFarsAndTracksTheToggles()
    {
        using var file = new TempFile("x\n");
        using var viewer = new FileViewer(Palette, new FakeUi(), file.Path);

        Assert.Equal(
            new[] { "Help", "Wrap", "Quit", "Hex", "Goto", "", "Search", "", "", "Quit", "", "Screen" },
            viewer.KeyBarFor(KeyMods.None)!.Labels);

        viewer.HandleInput(Key(ConsoleKey.F2));
        viewer.HandleInput(Key(ConsoleKey.F4));

        var bar = viewer.KeyBarFor(KeyMods.None)!;
        Assert.Equal("Unwrap", bar[1]);
        Assert.Equal("Text", bar[3]);
        Assert.Equal("Next", viewer.KeyBarFor(KeyMods.Shift)![6]);
    }

    [Fact]
    public void AMissingFileIsReportedAndTheViewerStartsClosed()
    {
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, Path.Combine(Path.GetTempPath(), "oc-does-not-exist-2f8a.txt"));

        Assert.True(viewer.IsClosed);
        Assert.Single(ui.Shown);
        Assert.Null(FileViewer.TryOpen(Palette, new FakeUi(), Path.Combine(Path.GetTempPath(), "oc-nope-2f8a.txt")));
    }

    [Fact]
    public void ABinaryFileStillRendersInBothModes()
    {
        byte[] bytes = [0x00, 0x01, 0x02, 0xFF, 0xFE, 0x41, 0x42, 0x0A];
        using var file = new TempFile(bytes, ".bin");
        var ui = new FakeUi();
        using var viewer = new FileViewer(Palette, ui, file.Path);

        Assert.True(viewer.Model!.IsBinary);
        Assert.Equal(
            EncodingDetector.DisplayName(EncodingDetector.AnsiFallback, false),
            viewer.Model.EncodingName);

        viewer.RenderToText(90, 8);
        viewer.HandleInput(Key(ConsoleKey.F4));

        string[] rows = viewer.RenderToText(90, 8).Split('\n');
        Assert.StartsWith("00000000: 00 01 02 FF FE 41 42 0A", rows[1], StringComparison.Ordinal);
        Assert.EndsWith("│ .....AB.", rows[1], StringComparison.Ordinal);
    }
}
