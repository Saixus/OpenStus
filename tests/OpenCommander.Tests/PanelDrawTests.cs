using OpenCommander.Core;
using OpenCommander.Files;
using OpenCommander.Input;
using OpenCommander.Panels;
using OpenCommander.Rendering;
using OpenCommander.Theming;
using OpenCommander.Ui;

namespace OpenCommander.Tests;

/// <summary>Shared fixtures for the panel tests: an in-memory listing and a 40x20 panel over it.</summary>
internal static class PanelFixture
{
    public const int Width = 40;
    public const int Height = 20;
    public const string DemoPath = @"C:\Work\Demo";

    public static readonly DateTime Stamp = new(2026, 8, 8, 14, 30, 0);

    public static string DisplayPath => FileSystemProvider.NormalizeDisplayPath(DemoPath);

    public static FileEntry Parent() => new()
    {
        Name = "..",
        FullPath = @"C:\Work",
        IsDirectory = true,
        IsParent = true,
        Attributes = FileAttributes.Directory,
        Modified = Stamp,
        Created = Stamp,
        Accessed = Stamp,
    };

    public static FileEntry Dir(string name) => new()
    {
        Name = name,
        FullPath = Path.Combine(DemoPath, name),
        IsDirectory = true,
        Attributes = FileAttributes.Directory,
        Modified = Stamp,
        Created = Stamp,
        Accessed = Stamp,
    };

    public static FileEntry File(
        string name,
        long size,
        FileAttributes attributes = FileAttributes.Archive,
        bool executable = false) => new()
        {
            Name = name,
            FullPath = Path.Combine(DemoPath, name),
            Size = size,
            Attributes = attributes,
            Modified = Stamp,
            Created = Stamp,
            Accessed = Stamp,

            // Set explicitly so the executable colour test behaves the same on Unix, where the
            // extension means nothing and the panel would otherwise stat a file that is not there.
            UnixExecutable = executable,
        };

    /// <summary>Six entries: two folders, four files, 7378 bytes in total.</summary>
    public static List<FileEntry> Listing() =>
    [
        Parent(),
        Dir("Documents"),
        Dir("Projects"),
        File("archive.zip", 2048),
        File("notes.txt", 1234),
        File("setup.exe", 4096, executable: true),
        File("hidden.dat", 0, FileAttributes.Archive | FileAttributes.Hidden),
    ];

    public static List<FileEntry> ManyFiles(int count)
    {
        var list = new List<FileEntry>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(File($"file{i:D3}.txt", 100));
        }

        return list;
    }

    public static FilePanel Panel(IReadOnlyList<FileEntry>? entries = null, string? error = null)
    {
        var panel = new FilePanel(null, Theme.FarDefault(), isLeft: true)
        {
            Bounds = new Rect(0, 0, Width, Height),
            IsActive = true,
        };

        panel.SetEntriesForTest(DemoPath, entries ?? Listing(), error);
        return panel;
    }

    public static string[] Render(FilePanel panel, out ScreenBuffer buffer)
    {
        buffer = new ScreenBuffer(Width, Height);
        panel.Draw(buffer);
        return buffer.RenderPlainText().Split('\n');
    }

    public static string[] Render(FilePanel panel) => Render(panel, out _);

    public static KeyEvent Key(ConsoleKey key, KeyMods mods = KeyMods.None, char ch = '\0') =>
        new(key, ch, mods);

    /// <summary>A directory that is a symlink, junction or mount point.</summary>
    public static FileEntry Link(string name) => new()
    {
        Name = name,
        FullPath = Path.Combine(DemoPath, name),
        IsDirectory = true,
        Attributes = FileAttributes.Directory | FileAttributes.ReparsePoint,
        Modified = Stamp,
        Created = Stamp,
        Accessed = Stamp,
    };

    /// <summary>
    /// The last column of <paramref name="row"/> that holds anything other than a space, searching
    /// only inside <c>[x, x + width)</c>, or <c>-1</c> when the whole span is blank.
    /// </summary>
    public static int LastNonSpace(ScreenBuffer buffer, int row, int x, int width)
    {
        for (int i = x + width - 1; i >= x; i--)
        {
            if (buffer.Get(i, row).Ch != ' ')
            {
                return i;
            }
        }

        return -1;
    }
}

public class PanelDrawTests
{
    [Fact]
    public void FrameIsASingleLineBoxWithATeeSeparatorAboveTheStatusLine()
    {
        string[] lines = PanelFixture.Render(PanelFixture.Panel());

        Assert.Equal(PanelFixture.Height, lines.Length);

        // Top frame.
        Assert.Equal('┌', lines[0][0]);
        Assert.Equal('┐', lines[0][39]);

        // Column titles and file rows carry the vertical frame.
        for (int y = 1; y <= 16; y++)
        {
            Assert.Equal('│', lines[y][0]);
            Assert.Equal('│', lines[y][39]);
        }

        // The separator above the status line, then the bottom frame.
        Assert.Equal('├', lines[17][0]);
        Assert.Equal('┤', lines[17][39]);
        Assert.Equal(new string('─', 38), lines[17][1..39]);
        Assert.Equal('└', lines[19][0]);
        Assert.Equal('┘', lines[19][39]);
    }

    [Fact]
    public void TheStatusLineHasNoSideFrameCharacters()
    {
        string[] lines = PanelFixture.Render(PanelFixture.Panel());

        Assert.DoesNotContain("│", lines[18], StringComparison.Ordinal);
        Assert.NotEqual('│', lines[18][0]);
    }

    [Fact]
    public void TheTitleIsThePathCentredOnTheTopFrame()
    {
        string[] lines = PanelFixture.Render(PanelFixture.Panel());
        string title = " " + PanelFixture.DisplayPath + " ";

        Assert.Contains(title, lines[0]);

        int expectedStart = (PanelFixture.Width - title.Length) / 2;
        Assert.Equal(expectedStart, lines[0].IndexOf(title, StringComparison.Ordinal));
        Assert.Equal('─', lines[0][expectedStart - 1]);
    }

    [Fact]
    public void ALongPathLosesItsHeadNotItsTail()
    {
        var panel = PanelFixture.Panel();
        panel.SetEntriesForTest(
            @"C:\a-very-long-directory-name\that-will-never-fit\inside-a-forty-column-panel",
            PanelFixture.Listing());

        string[] lines = PanelFixture.Render(panel);

        Assert.True(lines[0].Contains(ScreenBuffer.Ellipsis));
        Assert.Contains("panel ", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\a-very", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void MediumModeDrawsTwoNameColumns()
    {
        string[] lines = PanelFixture.Render(PanelFixture.Panel());

        // "Name" centred in a 19 cell stripe, then in an 18 cell one.
        Assert.Equal("│       Name        │       Name       │", lines[1]);
    }

    [Fact]
    public void TheParentEntryIsTheFirstFileRowAndIsNotBracketed()
    {
        string[] lines = PanelFixture.Render(PanelFixture.Panel());

        Assert.Equal("│ ..                │                  │", lines[2]);
        Assert.Equal("│ Documents         │                  │", lines[3]);
        Assert.Equal("│ Projects          │                  │", lines[4]);
    }

    [Fact]
    public void TheStatusLineShowsTheEntryUnderTheCursor()
    {
        var panel = PanelFixture.Panel();
        panel.CursorIndex = 4; // notes.txt, 1234 bytes

        string[] lines = PanelFixture.Render(panel);

        Assert.StartsWith(" notes.txt", lines[18], StringComparison.Ordinal);
        Assert.EndsWith("1 234  08/08/26  14:30", lines[18], StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusLineSaysFolderAndUpInsteadOfAByteCount()
    {
        var panel = PanelFixture.Panel();

        panel.CursorIndex = 0;
        Assert.EndsWith("Up  08/08/26  14:30", PanelFixture.Render(panel)[18], StringComparison.Ordinal);

        panel.CursorIndex = 1;
        Assert.EndsWith("Folder  08/08/26  14:30", PanelFixture.Render(panel)[18], StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusLineRightBlockEndsOnThePanelsLastColumn()
    {
        var panel = PanelFixture.Panel();
        panel.CursorIndex = 4; // notes.txt

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        const int Row = 18;
        Assert.Equal(
            PanelFixture.Width - 1,
            PanelFixture.LastNonSpace(buffer, Row, 0, PanelFixture.Width));

        // The very last cell is the final digit of "14:30", not the blank the inset used to leave.
        Assert.Equal('0', buffer.Get(PanelFixture.Width - 1, Row).Ch);
    }

    [Fact]
    public void TheStatusLineIsRightAlignedForAPanelThatDoesNotStartAtColumnZero()
    {
        // The right panel of a split screen: the block has to hug column 32, not the screen edge.
        var panel = PanelFixture.Panel();
        panel.Bounds = new Rect(3, 0, 30, PanelFixture.Height);
        panel.CursorIndex = 4;

        var buffer = new ScreenBuffer(PanelFixture.Width, PanelFixture.Height);
        panel.Draw(buffer);

        Assert.Equal(32, PanelFixture.LastNonSpace(buffer, 18, 3, 30));
        Assert.Equal('0', buffer.Get(32, 18).Ch);
    }

    [Fact]
    public void TheStatusLineKeepsItsSingleLeadingSpaceBeforeTheName()
    {
        var panel = PanelFixture.Panel();
        panel.CursorIndex = 4;

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        Assert.Equal(' ', buffer.Get(0, 18).Ch);
        Assert.Equal('n', buffer.Get(1, 18).Ch);
    }

    [Fact]
    public void TheSizeColumnAndTheStatusLineAgreeOnEveryDirectoryMarker()
    {
        FileEntry[] entries =
        [
            PanelFixture.Parent(),
            PanelFixture.Dir("Documents"),
            PanelFixture.Link("junction"),
        ];

        string[] expected = ["Up", "Folder", "Symlink"];

        for (int i = 0; i < entries.Length; i++)
        {
            // The status line shows the bare token, the Size column the same token in brackets and
            // with no padding inside them, so the two readings can never drift apart.
            string status = FilePanel.StatusRightText(entries[i]);
            Assert.StartsWith(expected[i] + "  ", status, StringComparison.Ordinal);
            Assert.Equal(
                "<" + expected[i] + ">",
                FilePanel.CellText(entries[i], PanelColumnKind.Size));
        }
    }

    [Fact]
    public void TheParentMarkerIsFlushRightInTheSizeColumnLikeEveryOtherRow()
    {
        var panel = PanelFixture.Panel();
        panel.ViewMode = PanelViewMode.Full;

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        // The Size column is cells 15..23 of a 40 wide panel; every row's marker or count ends on 23.
        Assert.Equal(23, PanelFixture.LastNonSpace(buffer, 2, 15, PanelColumn.SizeWidth)); // "<Up>"
        Assert.Equal(23, PanelFixture.LastNonSpace(buffer, 3, 15, PanelColumn.SizeWidth)); // "<Folder>"
        Assert.Equal(23, PanelFixture.LastNonSpace(buffer, 6, 15, PanelColumn.SizeWidth)); // "1 234"
    }

    [Fact]
    public void TheTotalsLineIsCentredOnTheBottomFrame()
    {
        string[] lines = PanelFixture.Render(PanelFixture.Panel());

        // 2048 + 1234 + 4096 + 0 = 7378 bytes across four files, in two folders.
        const string Totals = " Bytes: 7.2 K, files: 4, folders: 2 ";
        Assert.Contains(Totals, lines[19]);
        Assert.Equal((PanelFixture.Width - Totals.Length) / 2, lines[19].IndexOf(Totals, StringComparison.Ordinal));
    }

    [Fact]
    public void TaggingEntriesSwitchesTheTotalsLineToTheSelection()
    {
        var panel = PanelFixture.Panel();
        panel.Bounds = new Rect(0, 0, 60, PanelFixture.Height);
        panel.Entries[3].Selected = true; // archive.zip, 2048
        panel.Entries[1].Selected = true; // Documents

        var buffer = new ScreenBuffer(60, PanelFixture.Height);
        panel.Draw(buffer);
        string[] lines = buffer.RenderPlainText().Split('\n');

        Assert.Contains(" Selected: 2.0 K, files: 1, folders: 1 ", lines[19], StringComparison.Ordinal);
        Assert.DoesNotContain("Bytes:", lines[19], StringComparison.Ordinal);
    }

    [Fact]
    public void ATotalsLineTooLongForThePanelIsTruncatedInsideTheFrame()
    {
        var panel = PanelFixture.Panel();
        panel.Entries[3].Selected = true;
        panel.Entries[1].Selected = true;

        // "Selected: 2.0 K, files: 1, folders: 1" plus its padding needs 39 of the 38 cells
        // between the corners, so it loses its tail rather than overwriting the frame.
        string[] lines = PanelFixture.Render(panel);

        Assert.Equal('└', lines[19][0]);
        Assert.Equal('┘', lines[19][39]);
        Assert.Equal(ScreenBuffer.Ellipsis, lines[19][38]);
        Assert.StartsWith("└ Selected: 2.0 K, files: 1, folders: ", lines[19], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyListingStillDrawsTheFrameAndZeroTotals()
    {
        var panel = PanelFixture.Panel([]);

        string[] lines = PanelFixture.Render(panel);

        Assert.Contains(" Bytes: 0 B, files: 0, folders: 0 ", lines[19]);
        Assert.Equal("│                   │                  │", lines[2]);
    }

    [Fact]
    public void AReadErrorIsCentredInTheFileArea()
    {
        var panel = PanelFixture.Panel([PanelFixture.Parent()], "Access denied");

        string[] lines = PanelFixture.Render(panel);

        // Fifteen file rows, rows 2..16, so the middle one is row 9.
        Assert.Contains("Access denied", lines[9]);
        Assert.DoesNotContain("..", lines[2]);
    }

    [Fact]
    public void FullModeDrawsNameSizeDateAndTime()
    {
        var panel = PanelFixture.Panel();
        panel.ViewMode = PanelViewMode.Full;

        string[] lines = PanelFixture.Render(panel);

        Assert.Equal("│    Name     │  Size   │  Date  │Time │", lines[1]);
        Assert.Equal("│ ..          │     <Up>│08/08/26│14:30│", lines[2]);
        Assert.Equal("│ Documents   │ <Folder>│08/08/26│14:30│", lines[3]);
        Assert.Equal("│ notes.txt   │    1 234│08/08/26│14:30│", lines[6]);
    }

    [Fact]
    public void DetailedModeAddsTheAttributeColumn()
    {
        var panel = PanelFixture.Panel();
        panel.ViewMode = PanelViewMode.Detailed;

        string[] lines = PanelFixture.Render(panel);

        Assert.EndsWith("Attr │", lines[1]);
        Assert.EndsWith("----D│", lines[3]);   // Documents
        Assert.EndsWith("-A---│", lines[6]);   // notes.txt
        Assert.EndsWith("-A-H-│", lines[8]);   // hidden.dat
    }

    [Fact]
    public void BriefModeDrawsThreeNameColumns()
    {
        var panel = PanelFixture.Panel();
        panel.ViewMode = PanelViewMode.Brief;

        string[] lines = PanelFixture.Render(panel);

        Assert.Equal(3, lines[1].Split("Name").Length - 1);
        Assert.Equal(4, lines[1].Count(c => c == '│'));
    }

    [Fact]
    public void EntriesFillColumnMajor()
    {
        // Fifteen file rows and two stripes: entry 15 is the top of the second stripe.
        var panel = PanelFixture.Panel(PanelFixture.ManyFiles(20));

        string[] lines = PanelFixture.Render(panel);

        Assert.StartsWith("│ file000.txt", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("│ file014.txt", lines[16], StringComparison.Ordinal);
        Assert.Contains("file015.txt", lines[2]);
        Assert.Contains("file019.txt", lines[6]);
    }

    [Fact]
    public void TheScrollBarOnlyAppearsWhenTheListingOverflows()
    {
        var small = PanelFixture.Panel();
        PanelFixture.Render(small, out ScreenBuffer buffer);
        Assert.Equal('│', buffer.Get(39, 2).Ch);

        var big = PanelFixture.Panel(PanelFixture.ManyFiles(100));
        PanelFixture.Render(big, out buffer);

        Assert.Equal(BoxChars.ScrollBarThumb, buffer.Get(39, 2).Ch);
        Assert.Equal(BoxChars.ScrollBarTrack, buffer.Get(39, 16).Ch);
        Assert.Equal(Theme.FarDefault().PanelScrollBar, buffer.Get(39, 2).Style);
    }

    [Fact]
    public void TheScrollBarThumbFollowsTheWindow()
    {
        var panel = PanelFixture.Panel(PanelFixture.ManyFiles(100));
        panel.CursorIndex = 99;

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        Assert.Equal(BoxChars.ScrollBarTrack, buffer.Get(39, 2).Ch);
        Assert.Equal(BoxChars.ScrollBarThumb, buffer.Get(39, 16).Ch);
    }

    [Fact]
    public void APanelTooSmallToDrawIsPaintedBlankInsteadOfThrowing()
    {
        var panel = PanelFixture.Panel();
        panel.Bounds = new Rect(0, 0, 3, 4);

        var buffer = new ScreenBuffer(PanelFixture.Width, PanelFixture.Height);
        panel.Draw(buffer);

        Assert.Equal(' ', buffer.Get(0, 0).Ch);
        Assert.Equal(Theme.FarDefault().PanelText, buffer.Get(0, 0).Style);
    }

    [Fact]
    public void AHiddenPanelDrawsNothing()
    {
        var panel = PanelFixture.Panel();
        panel.IsVisible = false;

        var buffer = new ScreenBuffer(PanelFixture.Width, PanelFixture.Height);
        buffer.Clear(CellStyle.Default);
        panel.Draw(buffer);

        Assert.Equal(string.Empty, buffer.RenderPlainText().Replace("\n", string.Empty, StringComparison.Ordinal).Trim());
    }
}

public class PanelColourTests
{
    private static readonly Theme Palette = Theme.FarDefault();

    [Fact]
    public void TheCursorRowUsesThePanelCursorColours()
    {
        PanelFixture.Render(PanelFixture.Panel(), out ScreenBuffer buffer);

        // The cursor starts on "..", which is the first file row.
        for (int x = 1; x <= 19; x++)
        {
            Assert.Equal(Palette.PanelCursor, buffer.Get(x, 2).Style);
        }

        // The bar stops at the stripe separator, which keeps the frame colour.
        Assert.Equal(Palette.PanelBoxActive, buffer.Get(20, 2).Style);
        Assert.NotEqual(Palette.PanelCursor, buffer.Get(21, 2).Style);
    }

    [Fact]
    public void ATaggedEntryUnderTheCursorUsesTheSelectedCursorColours()
    {
        var panel = PanelFixture.Panel();
        panel.CursorIndex = 4;
        panel.Entries[4].Selected = true;

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        Assert.Equal(Palette.PanelCursorSelected, buffer.Get(1, 6).Style);
    }

    [Fact]
    public void EntryColoursFollowTheContractedPrecedence()
    {
        var panel = PanelFixture.Panel();
        panel.Entries[2].Selected = true; // Projects: tagged beats directory

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        Assert.Equal(Palette.PanelDirectory, buffer.Get(1, 3).Style);      // Documents
        Assert.Equal(Palette.PanelSelectedFile, buffer.Get(1, 4).Style);   // Projects, tagged
        Assert.Equal(Palette.PanelArchive, buffer.Get(1, 5).Style);        // archive.zip
        Assert.Equal(Palette.PanelText, buffer.Get(1, 6).Style);           // notes.txt
        Assert.Equal(Palette.PanelExecutable, buffer.Get(1, 7).Style);     // setup.exe
        Assert.Equal(Palette.PanelHidden, buffer.Get(1, 8).Style);         // hidden.dat
    }

    [Fact]
    public void AnInactivePanelDrawsNoCursorBarAndAPlainTitle()
    {
        var panel = PanelFixture.Panel();
        panel.IsActive = false;

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        Assert.Equal(Palette.PanelDirectory, buffer.Get(1, 2).Style);
        Assert.NotEqual(Palette.PanelCursor, buffer.Get(1, 2).Style);
        Assert.Equal(Palette.PanelTitle, buffer.Get((PanelFixture.Width / 2) - 1, 0).Style);
    }

    [Fact]
    public void TheActivePanelTitleIsInverted()
    {
        PanelFixture.Render(PanelFixture.Panel(), out ScreenBuffer buffer);

        Assert.Equal(Palette.PanelTitleActive, buffer.Get((PanelFixture.Width / 2) - 1, 0).Style);
    }

    [Fact]
    public void ColumnTitlesAndTotalsUseTheirOwnColours()
    {
        PanelFixture.Render(PanelFixture.Panel(), out ScreenBuffer buffer);

        Assert.Equal(Palette.PanelColumnTitle, buffer.Get(8, 1).Style);
        Assert.Equal(Palette.PanelTotals, buffer.Get(PanelFixture.Width / 2, 19).Style);
        Assert.Equal(Palette.PanelStatus, buffer.Get(0, 18).Style);
    }

    [Fact]
    public void TheDividersInsideTheCursorStripeTakeTheCursorBackground()
    {
        var panel = PanelFixture.Panel();
        panel.ViewMode = PanelViewMode.Full;

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        // Full mode puts a divider at column 14, inside the single stripe.
        Assert.Equal('│', buffer.Get(14, 2).Ch);
        Assert.Equal(Palette.PanelCursor, buffer.Get(14, 2).Style);

        // A row without the cursor keeps the plain frame colour.
        Assert.Equal(Palette.PanelBoxActive, buffer.Get(14, 3).Style);
    }
}

public class PanelNavigationTests
{
    private static FilePanel Panel(int count) => PanelFixture.Panel(PanelFixture.ManyFiles(count));

    [Fact]
    public void TheDefaultsAreMediumNameAndDirectoriesFirst()
    {
        var panel = new FilePanel(null, Theme.FarDefault(), isLeft: true);

        Assert.Equal(PanelViewMode.Medium, panel.ViewMode);
        Assert.Equal(SortMode.Name, panel.SortMode);
        Assert.False(panel.ReverseSort);
        Assert.True(panel.IsVisible);
    }

    [Fact]
    public void ArrowsMoveOneEntryAndClampAtTheEnds()
    {
        var panel = Panel(40);

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow), null!));
        Assert.Equal(1, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.UpArrow), null!);
        panel.HandleKey(PanelFixture.Key(ConsoleKey.UpArrow), null!);
        Assert.Equal(0, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.End), null!);
        Assert.Equal(39, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow), null!);
        Assert.Equal(39, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Home), null!);
        Assert.Equal(0, panel.CursorIndex);
    }

    [Fact]
    public void LeftAndRightMoveByOneWholeStripe()
    {
        var panel = Panel(40); // 15 rows, two stripes

        panel.HandleKey(PanelFixture.Key(ConsoleKey.RightArrow), null!);
        Assert.Equal(15, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.RightArrow), null!);
        Assert.Equal(30, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.LeftArrow), null!);
        Assert.Equal(15, panel.CursorIndex);
    }

    [Fact]
    public void InASingleStripeModeLeftAndRightMoveOneScreen()
    {
        var panel = Panel(40);
        panel.ViewMode = PanelViewMode.Full;

        Assert.Equal(1, panel.VisibleStripes);
        Assert.Equal(15, panel.VisibleRows);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.RightArrow), null!);
        Assert.Equal(15, panel.CursorIndex);
        Assert.Equal(panel.PageSize, panel.VisibleRows);
    }

    [Fact]
    public void PageKeysMoveOneWholeScreenful()
    {
        var panel = Panel(100); // page = 15 * 2 = 30

        panel.HandleKey(PanelFixture.Key(ConsoleKey.PageDown), null!);
        Assert.Equal(30, panel.CursorIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.PageUp), null!);
        Assert.Equal(0, panel.CursorIndex);
    }

    [Fact]
    public void WalkingOffTheBottomScrollsByRowsNotPages()
    {
        var panel = Panel(100);

        for (int i = 0; i < 30; i++)
        {
            panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow), null!);
        }

        Assert.Equal(30, panel.CursorIndex);
        Assert.Equal(1, panel.TopIndex);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow), null!);
        Assert.Equal(2, panel.TopIndex);
    }

    [Fact]
    public void TheWheelScrollsTheWindowWithoutMovingTheCursor()
    {
        var panel = Panel(100);

        Assert.True(panel.HandleMouse(new MouseEvent(MouseKind.Wheel, 5, 5, MouseButton.None, -1, KeyMods.None), null!));
        Assert.Equal(FilePanel.WheelRows, panel.TopIndex);
        Assert.Equal(0, panel.CursorIndex);

        panel.HandleMouse(new MouseEvent(MouseKind.Wheel, 5, 5, MouseButton.None, 1, KeyMods.None), null!);
        Assert.Equal(0, panel.TopIndex);
    }

    [Fact]
    public void ClickingMovesTheCursorToTheEntryUnderThePointer()
    {
        var panel = Panel(40);

        // Row 4 of the screen is the third file row, so entry 2 of the first stripe.
        Assert.True(panel.HandleMouse(new MouseEvent(MouseKind.Down, 5, 4, MouseButton.Left, 0, KeyMods.None), null!));
        Assert.Equal(2, panel.CursorIndex);

        // The second stripe starts at screen column 21.
        panel.HandleMouse(new MouseEvent(MouseKind.Down, 25, 4, MouseButton.Left, 0, KeyMods.None), null!);
        Assert.Equal(17, panel.CursorIndex);
    }

    [Fact]
    public void RightClickingTogglesTheTagAndMovesTheCursor()
    {
        var panel = Panel(40);

        panel.HandleMouse(new MouseEvent(MouseKind.Down, 5, 5, MouseButton.Right, 0, KeyMods.None), null!);

        Assert.Equal(3, panel.CursorIndex);
        Assert.True(panel.Entries[3].Selected);

        panel.HandleMouse(new MouseEvent(MouseKind.Down, 5, 5, MouseButton.Right, 0, KeyMods.None), null!);
        Assert.False(panel.Entries[3].Selected);
    }

    [Fact]
    public void AnInactivePanelLetsTheShellHandleTheClick()
    {
        var panel = Panel(40);
        panel.IsActive = false;

        Assert.False(panel.HandleMouse(new MouseEvent(MouseKind.Down, 5, 4, MouseButton.Left, 0, KeyMods.None), null!));
        Assert.Equal(0, panel.CursorIndex);
    }

    [Fact]
    public void ClicksOutsideThePanelAreNotOurs()
    {
        var panel = Panel(40);

        Assert.False(panel.HandleMouse(new MouseEvent(MouseKind.Down, 90, 4, MouseButton.Left, 0, KeyMods.None), null!));
        Assert.Equal(-1, panel.IndexAt(5, 0));   // the top frame
        Assert.Equal(-1, panel.IndexAt(20, 4));  // the stripe separator
    }

    [Fact]
    public void ViewModeAcceleratorsSwitchTheLayout()
    {
        var panel = Panel(10);

        for (int n = 1; n <= 9; n++)
        {
            var key = PanelFixture.Key((ConsoleKey)((int)ConsoleKey.D0 + n), KeyMods.Ctrl);
            Assert.True(panel.HandleKey(key, null!));
            Assert.Equal(PanelViewModes.FromNumber(n), panel.ViewMode);
        }
    }

    [Fact]
    public void SortAcceleratorsSetTheKeyAndToggleTheDirection()
    {
        var panel = Panel(10);

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.F4, KeyMods.Ctrl), null!));
        Assert.Equal(SortMode.Extension, panel.SortMode);
        Assert.False(panel.ReverseSort);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.F4, KeyMods.Ctrl), null!);
        Assert.True(panel.ReverseSort);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.F6, KeyMods.Ctrl), null!);
        Assert.Equal(SortMode.Size, panel.SortMode);
        Assert.False(panel.ReverseSort);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.F7, KeyMods.Ctrl), null!);
        Assert.Equal(SortMode.Unsorted, panel.SortMode);
    }

    [Fact]
    public void SortingReordersTheListingInPlace()
    {
        var panel = PanelFixture.Panel();
        panel.SetSort(SortMode.Size, reverse: true);

        Assert.Equal("..", panel.Entries[0].Name);
        Assert.Equal("Documents", panel.Entries[1].Name);
        Assert.Equal("setup.exe", panel.Entries[3].Name);
    }

    [Fact]
    public void UnhandledKeysAreLeftForTheShell()
    {
        var panel = Panel(10);

        Assert.False(panel.HandleKey(PanelFixture.Key(ConsoleKey.Tab), null!));
        Assert.False(panel.HandleKey(PanelFixture.Key(ConsoleKey.F5), null!));
        Assert.False(panel.HandleKey(PanelFixture.Key(ConsoleKey.Escape), null!));
        Assert.False(panel.HandleKey(PanelFixture.Key(ConsoleKey.A, KeyMods.None, 'a'), null!));
    }

    [Fact]
    public void TheUnmodifiedKeyBarIsTheFarOne()
    {
        var panel = Panel(1);

        Assert.Equal(
            new[]
            {
                "Help", "UserMn", "View", "Edit", "Copy", "RenMov",
                "MkFold", "Delete", "ConfMn", "Quit", "Plugin", "Screen",
            },
            panel.KeyBarFor(KeyMods.None)!.Labels);

        Assert.Equal("Name", panel.KeyBarFor(KeyMods.Ctrl)![2]);   // Ctrl+F3 sorts by name
        Assert.Equal("Find", panel.KeyBarFor(KeyMods.Alt)![6]);    // Alt+F7 finds files
        Assert.Equal("View", panel.KeyBarFor(KeyMods.Ctrl | KeyMods.Shift)![2]);
        Assert.Equal(string.Empty, panel.KeyBarFor(KeyMods.Ctrl | KeyMods.Alt)![0]);
    }

    [Fact]
    public void ThePanelServesTheSharedKeyBarTableRatherThanACopyOfIt()
    {
        var panel = Panel(1);

        // Reference equality, not string equality: a second transcription of the captions would
        // pass an Assert.Equal and then quietly drift away from KeyBarSets.
        foreach ((KeyMods mods, KeyBarLabels labels) in KeyBarSets.All)
        {
            Assert.Same(labels, panel.KeyBarFor(mods));
        }
    }
}

public class PanelSelectionTests
{
    [Fact]
    public void InsertTogglesTheTagAndMovesDown()
    {
        var panel = PanelFixture.Panel();
        panel.CursorIndex = 3;

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Insert), null!));

        Assert.True(panel.Entries[3].Selected);
        Assert.Equal(4, panel.CursorIndex);
        Assert.True(panel.HasSelection);
    }

    [Fact]
    public void TheParentEntryCanNeverBeTagged()
    {
        var panel = PanelFixture.Panel();

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Insert), null!);

        Assert.False(panel.Entries[0].Selected);
        Assert.Equal(1, panel.CursorIndex);
        Assert.False(panel.HasSelection);
    }

    [Fact]
    public void ShiftDownAppliesTheAnchorStateToEveryEntryItPassesOver()
    {
        var panel = PanelFixture.Panel(PanelFixture.ManyFiles(10));

        for (int i = 0; i < 3; i++)
        {
            Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow, KeyMods.Shift), null!));
        }

        Assert.True(panel.Entries[0].Selected);
        Assert.True(panel.Entries[1].Selected);
        Assert.True(panel.Entries[2].Selected);
        Assert.False(panel.Entries[3].Selected);
        Assert.Equal(3, panel.CursorIndex);
    }

    [Fact]
    public void TheAnchorDecidesSelectVersusDeselectOncePerGesture()
    {
        var panel = PanelFixture.Panel(PanelFixture.ManyFiles(10));
        panel.Entries[0].Selected = true;
        panel.Entries[1].Selected = true;

        // Starting on a tagged entry means the whole gesture untags.
        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow, KeyMods.Shift), null!);
        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow, KeyMods.Shift), null!);

        Assert.False(panel.Entries[0].Selected);
        Assert.False(panel.Entries[1].Selected);
    }

    [Fact]
    public void APlainMoveEndsTheShiftGesture()
    {
        var panel = PanelFixture.Panel(PanelFixture.ManyFiles(10));

        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow, KeyMods.Shift), null!);  // tags 0
        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow), null!);                 // ends it
        panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow, KeyMods.Shift), null!);  // new anchor on 2

        Assert.True(panel.Entries[0].Selected);
        Assert.False(panel.Entries[1].Selected);
        Assert.True(panel.Entries[2].Selected);
    }

    [Fact]
    public void ShiftEndAndShiftHomeIncludeTheEntryTheyLandOn()
    {
        var panel = PanelFixture.Panel(PanelFixture.ManyFiles(10));

        panel.HandleKey(PanelFixture.Key(ConsoleKey.End, KeyMods.Shift), null!);

        Assert.Equal(9, panel.CursorIndex);
        Assert.All(panel.Entries, e => Assert.True(e.Selected));

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Home), null!);
        panel.HandleKey(PanelFixture.Key(ConsoleKey.End), null!);
        panel.HandleKey(PanelFixture.Key(ConsoleKey.Home, KeyMods.Shift), null!);

        Assert.Equal(0, panel.CursorIndex);
        Assert.All(panel.Entries, e => Assert.False(e.Selected));
    }

    [Fact]
    public void GrayStarInvertsFilesOnlyAndCtrlGrayStarIncludesFolders()
    {
        var panel = PanelFixture.Panel();

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Multiply), null!);

        Assert.False(panel.Entries[0].Selected); // ".."
        Assert.False(panel.Entries[1].Selected); // Documents
        Assert.True(panel.Entries[3].Selected);  // archive.zip

        panel.ClearSelection();
        panel.HandleKey(PanelFixture.Key(ConsoleKey.Multiply, KeyMods.Ctrl), null!);

        Assert.False(panel.Entries[0].Selected);
        Assert.True(panel.Entries[1].Selected);
    }

    [Fact]
    public void CtrlAndTheLetterATagsEveryFile()
    {
        var panel = PanelFixture.Panel();

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.A, KeyMods.Ctrl), null!));

        Assert.False(panel.Entries[0].Selected);
        Assert.False(panel.Entries[1].Selected);
        Assert.True(panel.Entries[3].Selected);
        Assert.True(panel.Entries[4].Selected);
    }

    [Fact]
    public void MaskSelectionOnlyTouchesFiles()
    {
        var panel = PanelFixture.Panel();

        Assert.Equal(1, panel.SelectByMask("*.zip", selected: true));
        Assert.True(panel.Entries[3].Selected);
        Assert.False(panel.Entries[1].Selected);

        Assert.Equal(1, panel.SelectByMask("*.zip", selected: false));
        Assert.False(panel.HasSelection);
    }

    [Fact]
    public void SelectingBySameExtensionUsesTheEntryUnderTheCursor()
    {
        var panel = PanelFixture.Panel([
            PanelFixture.File("a.txt", 1),
            PanelFixture.File("b.txt", 2),
            PanelFixture.File("c.log", 3),
        ]);

        Assert.Equal(2, panel.SelectSameExtension(true));
        Assert.True(panel.Entries[1].Selected);
        Assert.False(panel.Entries[2].Selected);
    }

    [Fact]
    public void SelectedOrCurrentFallsBackToTheCursorButNeverToTheParentEntry()
    {
        var panel = PanelFixture.Panel();

        Assert.Empty(panel.SelectedOrCurrent);

        panel.CursorIndex = 4;
        Assert.Equal("notes.txt", Assert.Single(panel.SelectedOrCurrent).Name);

        panel.Entries[3].Selected = true;
        panel.Entries[5].Selected = true;
        Assert.Equal(2, panel.SelectedOrCurrent.Count);
    }

    [Fact]
    public void ClearSelectionDropsEveryTag()
    {
        var panel = PanelFixture.Panel();
        panel.SelectAllFiles();
        Assert.True(panel.HasSelection);

        panel.ClearSelection();
        Assert.False(panel.HasSelection);
    }
}

public class PanelQuickSearchTests
{
    [Fact]
    public void AltAndALetterJumpsToTheFirstNameThatStartsWithIt()
    {
        var panel = PanelFixture.Panel();

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.N, KeyMods.Alt, 'n'), null!));

        Assert.True(panel.Search.IsActive);
        Assert.Equal("n", panel.Search.Text);
        Assert.Equal("notes.txt", panel.Current!.Name);
    }

    [Fact]
    public void FurtherCharactersNarrowTheSearch()
    {
        var panel = PanelFixture.Panel();

        panel.HandleKey(PanelFixture.Key(ConsoleKey.S, KeyMods.Alt, 's'), null!);
        Assert.Equal("setup.exe", panel.Current!.Name);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.E, KeyMods.None, 'e'), null!);
        Assert.Equal("se", panel.Search.Text);
        Assert.Equal("setup.exe", panel.Current.Name);
    }

    [Fact]
    public void BackspaceShortensTheTextAndEmptyingItClosesTheBox()
    {
        var panel = PanelFixture.Panel();

        panel.HandleKey(PanelFixture.Key(ConsoleKey.N, KeyMods.Alt, 'n'), null!);
        panel.HandleKey(PanelFixture.Key(ConsoleKey.O, KeyMods.None, 'o'), null!);
        Assert.Equal("no", panel.Search.Text);

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Backspace), null!));
        Assert.Equal("n", panel.Search.Text);

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Backspace), null!));
        Assert.False(panel.Search.IsActive);
    }

    [Fact]
    public void EscapeCancelsTheSearchAndIsConsumed()
    {
        var panel = PanelFixture.Panel();
        panel.HandleKey(PanelFixture.Key(ConsoleKey.N, KeyMods.Alt, 'n'), null!);

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Escape), null!));
        Assert.False(panel.Search.IsActive);

        // With no search running Escape belongs to the shell.
        Assert.False(panel.HandleKey(PanelFixture.Key(ConsoleKey.Escape), null!));
    }

    [Fact]
    public void AnArrowKeyEndsTheSearchAndThenMovesTheCursor()
    {
        var panel = PanelFixture.Panel();
        panel.HandleKey(PanelFixture.Key(ConsoleKey.N, KeyMods.Alt, 'n'), null!);
        int before = panel.CursorIndex;

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.DownArrow), null!));

        Assert.False(panel.Search.IsActive);
        Assert.Equal(before + 1, panel.CursorIndex);
    }

    [Fact]
    public void TheSearchBoxIsDrawnOnTheBottomFrame()
    {
        var panel = PanelFixture.Panel();
        panel.HandleKey(PanelFixture.Key(ConsoleKey.N, KeyMods.Alt, 'n'), null!);

        PanelFixture.Render(panel, out ScreenBuffer buffer);

        Assert.Equal('[', buffer.Get(1, 19).Ch);
        Assert.Equal('n', buffer.Get(2, 19).Ch);
        Assert.Equal(']', buffer.Get(3, 19).Ch);
        Assert.Equal(Theme.FarDefault().QuickSearch, buffer.Get(2, 19).Style);
    }

    [Fact]
    public void MatchingPrefersPrefixesAndFallsBackToContains()
    {
        var entries = new List<FileEntry>
        {
            PanelFixture.File("a-restore.log", 1),
            PanelFixture.File("results.txt", 2),
        };

        Assert.Equal(1, QuickSearch.Find(entries, "res", 0));
        Assert.Equal(0, QuickSearch.Find(entries, "store", 0));
        Assert.Equal(-1, QuickSearch.Find(entries, "zzz", 0));
    }

    [Fact]
    public void MatchingHonoursWildcardsAndIgnoresCase()
    {
        var entries = new List<FileEntry>
        {
            PanelFixture.File("Report.md", 1),
            PanelFixture.File("readme.txt", 2),
        };

        Assert.Equal(0, QuickSearch.Find(entries, "rep", 0));
        Assert.Equal(0, QuickSearch.Find(entries, "r?p", 0));
        Assert.Equal(1, QuickSearch.Find(entries, "*.txt", 0));
    }

    [Fact]
    public void TheSearchExpiresAfterThreeIdleSeconds()
    {
        var search = new QuickSearch();
        var start = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

        search.Append('a', start);
        Assert.False(search.ExpireIfIdle(start.AddSeconds(2)));
        Assert.True(search.IsActive);

        Assert.True(search.ExpireIfIdle(start + QuickSearch.Timeout));
        Assert.False(search.IsActive);
        Assert.Equal(string.Empty, search.Text);
    }
}

public class PanelHistoryTests
{
    [Fact]
    public void PushingKeepsTheMostRecentlyVisitedFirst()
    {
        var history = new PanelHistory();

        history.Push("a");
        history.Push("b");
        history.Push("c");

        Assert.Equal(new[] { "c", "b", "a" }, history.Items);
        Assert.Equal("c", history.Latest);
    }

    [Fact]
    public void RevisitingMovesToTheFrontInsteadOfDuplicating()
    {
        var history = new PanelHistory();

        history.Push("a");
        history.Push("b");
        history.Push("a");

        Assert.Equal(new[] { "a", "b" }, history.Items);
        Assert.Equal(2, history.Count);
        Assert.Equal(0, history.IndexOf("a"));
    }

    [Fact]
    public void TheListIsBoundedAtSixtyFourEntries()
    {
        var history = new PanelHistory();

        for (int i = 0; i < 100; i++)
        {
            history.Push("dir" + i);
        }

        Assert.Equal(PanelHistory.Capacity, history.Count);
        Assert.Equal("dir99", history.Items[0]);
        Assert.Equal("dir36", history.Items[^1]);
        Assert.False(history.Contains("dir0"));
    }

    [Fact]
    public void EmptyPathsAreIgnored()
    {
        var history = new PanelHistory();

        history.Push(null);
        history.Push(string.Empty);
        history.Push("   ");

        Assert.Empty(history.Items);
        Assert.Null(history.Latest);
    }

    [Fact]
    public void ThePanelExposesItsOwnHistory()
    {
        var panel = PanelFixture.Panel();

        panel.PushHistory(@"C:\one");
        panel.PushHistory(@"C:\two");

        Assert.Equal(new[] { @"C:\two", @"C:\one" }, panel.History);
    }
}

public class PanelDirectoryTests : IDisposable
{
    private readonly string _root;

    public PanelDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oc-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        System.IO.File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file manager test that cannot tidy up is still a passing test.
        }

        GC.SuppressFinalize(this);
    }

    private FilePanel Panel()
    {
        var panel = new FilePanel(null, Theme.FarDefault(), isLeft: true)
        {
            Bounds = new Rect(0, 0, PanelFixture.Width, PanelFixture.Height),
            IsActive = true,
        };

        panel.Navigate(_root);
        return panel;
    }

    [Fact]
    public void NavigatingReadsTheDirectoryAndRecordsIt()
    {
        var panel = Panel();

        Assert.Equal(FileSystemProvider.NormalizeDisplayPath(_root), panel.CurrentPath);
        Assert.Null(panel.Error);
        Assert.Equal("..", panel.Entries[0].Name);
        Assert.Contains(panel.Entries, e => e.Name == "sub" && e.IsDirectory);
        Assert.Contains(panel.Entries, e => e.Name == "a.txt");
        Assert.Equal(panel.CurrentPath, panel.History[0]);
    }

    [Fact]
    public void EnterOnADirectoryDescendsIntoIt()
    {
        var panel = Panel();
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == "sub");

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Enter), null!));

        Assert.EndsWith("sub", panel.CurrentPath, StringComparison.Ordinal);
        Assert.Equal(2, panel.History.Count);
    }

    [Fact]
    public void BackspaceGoesUpAndPutsTheCursorOnTheFolderWeCameFrom()
    {
        var panel = Panel();
        panel.Navigate(Path.Combine(_root, "sub"));

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.Backspace), null!));

        Assert.Equal(FileSystemProvider.NormalizeDisplayPath(_root), panel.CurrentPath);
        Assert.Equal("sub", panel.Current!.Name);
    }

    [Fact]
    public void CtrlPageDownEntersAndCtrlPageUpLeaves()
    {
        var panel = Panel();
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == "sub");

        panel.HandleKey(PanelFixture.Key(ConsoleKey.PageDown, KeyMods.Ctrl), null!);
        Assert.EndsWith("sub", panel.CurrentPath, StringComparison.Ordinal);

        panel.HandleKey(PanelFixture.Key(ConsoleKey.PageUp, KeyMods.Ctrl), null!);
        Assert.Equal(FileSystemProvider.NormalizeDisplayPath(_root), panel.CurrentPath);
    }

    [Fact]
    public void EnterOnTheParentEntryLeavesTheFolder()
    {
        var panel = Panel();
        panel.Navigate(Path.Combine(_root, "sub"));
        panel.CursorIndex = 0;

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Enter), null!);

        Assert.Equal(FileSystemProvider.NormalizeDisplayPath(_root), panel.CurrentPath);
    }

    [Fact]
    public void CtrlBackslashGoesToTheRootOfTheDrive()
    {
        var panel = Panel();

        panel.HandleKey(PanelFixture.Key(ConsoleKey.Oem5, KeyMods.Ctrl, '\\'), null!);

        Assert.Equal(
            FileSystemProvider.NormalizeDisplayPath(FileSystemProvider.GetRoot(_root)),
            panel.CurrentPath);
    }

    [Fact]
    public void ReloadKeepsTheCursorOnTheSameName()
    {
        var panel = Panel();
        panel.CursorIndex = panel.Entries.ToList().FindIndex(e => e.Name == "a.txt");

        Assert.True(panel.HandleKey(PanelFixture.Key(ConsoleKey.R, KeyMods.Ctrl), null!));

        Assert.Equal("a.txt", panel.Current!.Name);
    }

    [Fact]
    public void AnUnreadableFolderShowsTheErrorAndKeepsTheWayOut()
    {
        var panel = Panel();
        panel.Navigate(Path.Combine(_root, "does-not-exist"));

        Assert.NotNull(panel.Error);
        Assert.Single(panel.Entries);
        Assert.True(panel.Entries[0].IsParent);

        // The message is centred in the file area and truncated to the panel width.
        Assert.Contains("Cannot find", PanelFixture.Render(panel)[9], StringComparison.Ordinal);
    }
}
