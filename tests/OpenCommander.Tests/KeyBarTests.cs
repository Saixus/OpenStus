using System.Text;
using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Theming;
using OpenCommander.Ui;

namespace OpenCommander.Tests;

public class KeyBarSetsTests
{
    [Fact]
    public void TheUnmodifiedRowIsTheOneTheReferenceScreenshotShows()
    {
        Assert.Equal(
            new[]
            {
                "Help", "UserMn", "View", "Edit", "Copy", "RenMov",
                "MkFold", "Delete", "ConfMn", "Quit", "Plugin", "Screen",
            },
            KeyBarSets.ForPanels(KeyMods.None).Labels);
    }

    [Fact]
    public void EveryModifierCombinationHasARow()
    {
        foreach (KeyMods mods in AllModifierCombinations())
        {
            KeyBarLabels labels = KeyBarSets.ForPanels(mods);
            Assert.Equal(KeyBarLabels.KeyCount, labels.Labels.Length);
            Assert.All(labels.Labels, Assert.NotNull);
        }
    }

    [Fact]
    public void NoCaptionIsWiderThanACellCanShow()
    {
        foreach (KeyMods mods in AllModifierCombinations())
        {
            foreach (string caption in KeyBarSets.ForPanels(mods).Labels)
            {
                Assert.True(
                    caption.Length <= KeyBarSets.MaxCaptionLength,
                    $"\"{caption}\" ({mods}) is longer than {KeyBarSets.MaxCaptionLength} characters");
            }
        }
    }

    [Fact]
    public void CtrlAltRowsAreEntirelyBlank()
    {
        Assert.All(KeyBarSets.ForPanels(KeyMods.Ctrl | KeyMods.Alt).Labels, Assert.Empty);
        Assert.All(KeyBarSets.ForPanels(KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift).Labels, Assert.Empty);
    }

    [Fact]
    public void SparseRowsOnlyBindTheKeysFarBinds()
    {
        KeyBarLabels ctrlShift = KeyBarSets.ForPanels(KeyMods.Ctrl | KeyMods.Shift);
        Assert.Equal("View", ctrlShift[2]);
        Assert.Equal("Edit", ctrlShift[3]);
        Assert.Empty(ctrlShift[0]);
        Assert.Empty(ctrlShift[11]);

        Assert.Equal("ConfPl", KeyBarSets.ForPanels(KeyMods.Alt | KeyMods.Shift)[8]);
        Assert.Empty(KeyBarSets.ForPanels(KeyMods.Shift)[6]);
    }

    [Fact]
    public void TheTableEnumeratesEveryRowExactlyOnce()
    {
        Assert.Equal(8, KeyBarSets.All.Count);
        Assert.Equal(8, KeyBarSets.All.Select(static r => r.Mods).Distinct().Count());

        foreach ((KeyMods mods, KeyBarLabels labels) in KeyBarSets.All)
        {
            Assert.Same(labels, KeyBarSets.ForPanels(mods));
        }
    }

    /// <summary>
    /// <c>All</c> documents itself as listing the rows in the order <c>ForPanels</c> selects them,
    /// which means Alt+Shift comes before Ctrl+Alt, exactly as the switch arms do.
    /// </summary>
    [Fact]
    public void TheTableIsListedInTheOrderForPanelsSelectsTheRows()
    {
        Assert.Equal(
            new[]
            {
                KeyMods.None,
                KeyMods.Shift,
                KeyMods.Ctrl,
                KeyMods.Alt,
                KeyMods.Ctrl | KeyMods.Shift,
                KeyMods.Alt | KeyMods.Shift,
                KeyMods.Ctrl | KeyMods.Alt,
                KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift,
            },
            KeyBarSets.All.Select(static r => r.Mods));
    }

    /// <summary>
    /// Every modifier combination has to land on its own row - a misplaced switch arm would show the
    /// Ctrl+Alt blanks while Alt+Shift is held, and the bar would lie about what the keyboard does.
    /// </summary>
    [Fact]
    public void ForPanelsPicksTheRowThatBelongsToEachCombination()
    {
        Assert.Same(KeyBarSets.None, KeyBarSets.ForPanels(KeyMods.None));
        Assert.Same(KeyBarSets.Shift, KeyBarSets.ForPanels(KeyMods.Shift));
        Assert.Same(KeyBarSets.Ctrl, KeyBarSets.ForPanels(KeyMods.Ctrl));
        Assert.Same(KeyBarSets.Alt, KeyBarSets.ForPanels(KeyMods.Alt));
        Assert.Same(KeyBarSets.CtrlShift, KeyBarSets.ForPanels(KeyMods.Ctrl | KeyMods.Shift));
        Assert.Same(KeyBarSets.AltShift, KeyBarSets.ForPanels(KeyMods.Alt | KeyMods.Shift));
        Assert.Same(KeyBarSets.CtrlAlt, KeyBarSets.ForPanels(KeyMods.Ctrl | KeyMods.Alt));
        Assert.Same(
            KeyBarSets.CtrlAltShift,
            KeyBarSets.ForPanels(KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift));
    }

    /// <summary>
    /// The caption has six columns to say what <c>Core/KeyBindings.cs</c> binds under a longer name;
    /// if one of the two is renamed the other has to follow.
    /// </summary>
    [Fact]
    public void AltF10IsTheFolderTreeKey() => Assert.Equal("Tree", KeyBarSets.Alt[9]);

    private static IEnumerable<KeyMods> AllModifierCombinations()
    {
        for (int i = 0; i <= 7; i++)
        {
            yield return (KeyMods)i;
        }
    }
}

public class KeyBarLayoutTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(203)]
    public void TheTwelveCellsTileTheRowExactly(int width)
    {
        int covered = 0;
        int expectedStart = 0;

        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            (int start, int cellWidth) = KeyBar.CellBounds(i, width);
            Assert.Equal(expectedStart, start);
            covered += cellWidth;
            expectedStart = start + cellWidth;
        }

        Assert.Equal(width, covered);
    }

    /// <summary>
    /// The tiling invariant, for every caption row and every width a console can plausibly have: no
    /// column may be covered twice and none may be left uncovered.
    /// </summary>
    [Fact]
    public void EveryRowTilesEveryWidthFromOneToFourHundred()
    {
        foreach ((KeyMods mods, KeyBarLabels labels) in KeyBarSets.All)
        {
            for (int width = 1; width <= 400; width++)
            {
                int expectedStart = 0;

                for (int i = 0; i < KeyBarLabels.KeyCount; i++)
                {
                    (int start, int cellWidth) = KeyBar.CellBounds(i, width, labels);
                    Assert.True(cellWidth >= 0, $"{mods} at {width}: cell {i} is {cellWidth} wide");
                    Assert.Equal(expectedStart, start);
                    expectedStart = start + cellWidth;
                }

                Assert.True(
                    expectedStart == width,
                    $"{mods} at {width}: the twelve cells covered {expectedStart} columns");
            }
        }
    }

    /// <summary>
    /// Eighty columns is the canonical Far width. The bar needs fifteen columns of key numbers and
    /// sixty two of captions, so every cell must get what it asks for with three columns to spare.
    /// </summary>
    [Fact]
    public void EveryCellGetsTheColumnsItsNumberAndCaptionNeed()
    {
        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            int demand = KeyBar.NumberOf(i).Length + KeyBarSets.None[i].Length;
            (_, int cellWidth) = KeyBar.CellBounds(i, 80);

            Assert.True(
                cellWidth >= demand,
                $"cell {i} needs {demand} columns for \"{KeyBar.NumberOf(i)}{KeyBarSets.None[i]}\" " +
                $"but got {cellWidth}");
        }
    }

    [Fact]
    public void TheSpareColumnsGoToTheLeftmostCells()
    {
        // 80 - 77 == 3 spare columns, so the three leftmost cells get one padding column each and
        // the rest are exactly as wide as their content.
        Assert.Equal((0, 6), KeyBar.CellBounds(0, 80));   // "1Help" + gap
        Assert.Equal((6, 8), KeyBar.CellBounds(1, 80));   // "2UserMn" + gap
        Assert.Equal((14, 6), KeyBar.CellBounds(2, 80));  // "3View" + gap
        Assert.Equal((20, 5), KeyBar.CellBounds(3, 80));  // "4Edit", no column left over
        Assert.Equal((64, 8), KeyBar.CellBounds(10, 80)); // "11Plugin" whole
        Assert.Equal((72, 8), KeyBar.CellBounds(11, 80)); // "12Screen" whole
    }

    [Fact]
    public void ACellIsNeverNarrowerThanItsContentWhileThereIsRoom()
    {
        foreach (int width in new[] { 77, 78, 80, 81, 95, 96, 120, 200, 400 })
        {
            for (int i = 0; i < KeyBarLabels.KeyCount; i++)
            {
                int demand = KeyBar.NumberOf(i).Length + KeyBarSets.None[i].Length;
                (_, int cellWidth) = KeyBar.CellBounds(i, width);
                Assert.True(cellWidth >= demand, $"cell {i} at {width} columns got {cellWidth}");
            }
        }
    }

    /// <summary>
    /// Below the total demand the widest cells give columns up first, so the short captions survive
    /// long after a plain <c>width / 12</c> split would have started cutting the last three.
    /// </summary>
    [Fact]
    public void ARowTooNarrowForEverythingTrimsTheWidestCellsFirst()
    {
        // Sixty columns caps every cell at five: "1Help" needs exactly five and stays whole, while
        // the eight column "11Plugin" is one of the cells that has to give something up.
        Assert.Equal((0, 5), KeyBar.CellBounds(0, 60));
        Assert.Equal((50, 5), KeyBar.CellBounds(10, 60));

        // Forty columns caps them at three, the four spare columns going to the leftmost cells.
        int[] widths = new int[KeyBarLabels.KeyCount];
        for (int i = 0; i < widths.Length; i++)
        {
            widths[i] = KeyBar.CellBounds(i, 40).Width;
        }

        Assert.Equal(new[] { 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 3 }, widths);
    }

    [Fact]
    public void AnUnboundKeyAsksForNoColumnsOfItsOwn()
    {
        Assert.Equal(0, KeyBar.DemandOf(0, string.Empty));
        Assert.Equal(0, KeyBar.DemandOf(0, null));
        Assert.Equal(5, KeyBar.DemandOf(0, "Help"));
        Assert.Equal(8, KeyBar.DemandOf(11, "Screen"));
    }

    [Fact]
    public void CellBoundsRejectsNonsense()
    {
        Assert.Equal((0, 0), KeyBar.CellBounds(-1, 120));
        Assert.Equal((0, 0), KeyBar.CellBounds(12, 120));
        Assert.Equal((0, 0), KeyBar.CellBounds(0, 0));
        Assert.Equal((0, 0), KeyBar.CellBounds(-1, 120, KeyBarSets.Ctrl));
    }

    [Fact]
    public void CellBoundsWithoutARowUsesThePanelCaptions()
    {
        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            Assert.Equal(KeyBar.CellBounds(i, 111, KeyBarSets.None), KeyBar.CellBounds(i, 111));
            Assert.Equal(KeyBar.CellBounds(i, 111), KeyBar.CellBounds(i, 111, labels: null));
        }
    }

    [Fact]
    public void HitTestMapsColumnsBackToKeys()
    {
        var bar = new KeyBar(Theme.FarDefault());

        Assert.Equal(0, bar.HitTest(0, 120));
        Assert.Equal(0, bar.HitTest(8, 120));
        Assert.Equal(1, bar.HitTest(9, 120));
        Assert.Equal(9, bar.HitTest(95, 120));
        Assert.Equal(11, bar.HitTest(119, 120));
    }

    [Fact]
    public void HitTestFollowsTheCellsAtEightyColumns()
    {
        var bar = new KeyBar(Theme.FarDefault());

        Assert.Equal(0, bar.HitTest(0, 80));
        Assert.Equal(0, bar.HitTest(5, 80));
        Assert.Equal(1, bar.HitTest(6, 80));
        Assert.Equal(10, bar.HitTest(71, 80));
        Assert.Equal(11, bar.HitTest(72, 80));
        Assert.Equal(11, bar.HitTest(79, 80));
    }

    [Fact]
    public void HitTestAgreesWithTheCellBoundsAtEveryColumn()
    {
        var bar = new KeyBar(Theme.FarDefault());

        foreach (int width in new[] { 1, 5, 12, 24, 40, 60, 80, 81, 95, 96, 100, 120, 200, 400 })
        {
            for (int x = 0; x < width; x++)
            {
                int expected = -1;
                for (int i = 0; i < KeyBarLabels.KeyCount; i++)
                {
                    (int start, int cellWidth) = KeyBar.CellBounds(i, width);
                    if (cellWidth > 0 && x >= start && x < start + cellWidth)
                    {
                        expected = i;
                        break;
                    }
                }

                Assert.Equal(expected, bar.HitTest(x, width));
            }
        }
    }

    [Fact]
    public void HitTestUsesTheCaptionsCurrentlyOnTheBar()
    {
        var bar = new KeyBar(Theme.FarDefault()) { Override = KeyBarLabels.Of("Aaa", "Bbb") };

        // Two captions and ten blanks: the two cells that have something to say are the wide ones.
        Assert.Equal(0, bar.HitTest(13, 120));
        Assert.Equal(1, bar.HitTest(14, 120));
    }

    [Fact]
    public void HitTestReturnsMinusOneOffTheBar()
    {
        var bar = new KeyBar(Theme.FarDefault());

        Assert.Equal(-1, bar.HitTest(-1, 120));
        Assert.Equal(-1, bar.HitTest(120, 120));
        Assert.Equal(-1, bar.HitTest(0, 0));
    }
}

public class KeyBarDrawTests
{
    private static readonly Theme T = Theme.FarDefault();

    /// <summary>
    /// The regression this bar was rebuilt for: at eighty columns Far shows all twelve captions, and
    /// so must we - no "9Conf...", no "11Plu...", no "12Scr...".
    /// </summary>
    [Fact]
    public void TheUnmodifiedRowAtEightyColumnsShowsAllTwelveCaptions()
    {
        var buf = new ScreenBuffer(80, 1);
        new KeyBar(T).Draw(buf, 0);

        Assert.Equal(
            "1Help " + "2UserMn " + "3View " + "4Edit" + "5Copy" + "6RenMov" +
            "7MkFold" + "8Delete" + "9ConfMn" + "10Quit" + "11Plugin" + "12Screen",
            Row(buf, 0));
    }

    /// <summary>
    /// Every caption of every row, drawn whole, at the widths where the row can hold them: the
    /// number and its caption must appear as one unbroken run starting at the cell.
    /// </summary>
    [Theory]
    [InlineData(80)]
    [InlineData(81)]
    [InlineData(95)]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(200)]
    public void NoCaptionIsEverTruncatedAtTheseWidths(int width)
    {
        foreach ((KeyMods mods, KeyBarLabels labels) in KeyBarSets.All)
        {
            var buf = new ScreenBuffer(width, 1);
            new KeyBar(T) { Modifiers = mods }.Draw(buf, 0);

            string row = Row(buf, 0);
            int expectedStart = 0;

            for (int i = 0; i < KeyBarLabels.KeyCount; i++)
            {
                (int start, int cellWidth) = KeyBar.CellBounds(i, width, labels);
                Assert.Equal(expectedStart, start);
                expectedStart = start + cellWidth;

                string caption = labels[i];
                if (caption.Length == 0)
                {
                    Assert.Equal(new string(' ', cellWidth), row.Substring(start, cellWidth));
                    continue;
                }

                string block = KeyBar.NumberOf(i) + caption;
                Assert.True(
                    cellWidth >= block.Length,
                    $"{mods} at {width}: cell {i} is {cellWidth} columns, \"{block}\" needs {block.Length}");
                Assert.Equal(block, row.Substring(start, block.Length));
                Assert.Equal(
                    new string(' ', cellWidth - block.Length),
                    row.Substring(start + block.Length, cellWidth - block.Length));
            }

            Assert.Equal(width, expectedStart);
        }
    }

    [Fact]
    public void TheUnmodifiedRowNeverReachesForTheEllipsisOnARealConsole()
    {
        foreach (int width in new[] { 77, 78, 80, 81, 95, 96, 100, 120, 132, 200, 400 })
        {
            var buf = new ScreenBuffer(width, 1);
            new KeyBar(T).Draw(buf, 0);

            Assert.DoesNotContain(ScreenBuffer.Ellipsis, Row(buf, 0));
        }
    }

    [Fact]
    public void TheUnmodifiedRowAtOneHundredAndTwentyColumns()
    {
        var buf = new ScreenBuffer(120, 1);
        new KeyBar(T).Draw(buf, 0);

        Assert.Equal(
            "1Help    2UserMn    3View    4Edit    5Copy    6RenMov    " +
            "7MkFold    8Delete   9ConfMn   10Quit   11Plugin   12Screen   ",
            Row(buf, 0));
    }

    [Fact]
    public void TheCtrlRowFollowsTheHeldModifiers()
    {
        var buf = new ScreenBuffer(120, 1);
        var bar = new KeyBar(T) { Modifiers = KeyMods.Ctrl };
        bar.Draw(buf, 0);

        Assert.StartsWith("1Left    2Right    3Name    4Extens", Row(buf, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverrideWinsOverTheModifiers()
    {
        var buf = new ScreenBuffer(120, 1);
        var bar = new KeyBar(T)
        {
            Modifiers = KeyMods.Ctrl,
            Override = KeyBarLabels.Of("Aaa", "Bbb"),
        };

        bar.Draw(buf, 0);

        string row = Row(buf, 0);
        Assert.StartsWith("1Aaa          2Bbb          ", row, StringComparison.Ordinal);
        Assert.Equal(new string(' ', 92), row[28..]);
    }

    [Fact]
    public void NumberCaptionAndGapUseTheThreeKeyBarStyles()
    {
        var buf = new ScreenBuffer(120, 1);
        new KeyBar(T).Draw(buf, 0);

        // Cell 0 is nine columns: one number column, the four caption columns, then four padding
        // columns that are the gap to the next caption block.
        Assert.Equal(T.KeyBarNum, buf.Get(0, 0).Style);
        Assert.Equal('1', buf.Get(0, 0).Glyph);

        for (int x = 1; x <= 4; x++)
        {
            Assert.Equal(T.KeyBarText, buf.Get(x, 0).Style);
        }

        for (int x = 5; x <= 8; x++)
        {
            Assert.Equal(T.KeyBarBackground, buf.Get(x, 0).Style);
            Assert.Equal(' ', buf.Get(x, 0).Glyph);
        }

        // F10 has a two character number, so its caption starts one column further into the cell.
        Assert.Equal(T.KeyBarNum, buf.Get(89, 0).Style);
        Assert.Equal(T.KeyBarNum, buf.Get(90, 0).Style);
        Assert.Equal(T.KeyBarText, buf.Get(91, 0).Style);
        Assert.Equal(T.KeyBarText, buf.Get(94, 0).Style);
        Assert.Equal(T.KeyBarBackground, buf.Get(95, 0).Style);
    }

    [Fact]
    public void AnUnboundKeyDrawsNoNumberAtAll()
    {
        var buf = new ScreenBuffer(120, 1);
        var bar = new KeyBar(T) { Modifiers = KeyMods.Ctrl | KeyMods.Shift };
        bar.Draw(buf, 0);

        string row = Row(buf, 0);
        Assert.Equal(
            new string(' ', 20) + "3View" + new string(' ', 9) + "4Edit" + new string(' ', 9 + 72),
            row);

        // The blank cells are background, not caption colour.
        Assert.Equal(T.KeyBarBackground, buf.Get(0, 0).Style);
        Assert.Equal(T.KeyBarBackground, buf.Get(119, 0).Style);
    }

    [Fact]
    public void ARowWithNothingBoundIsEntirelyBackground()
    {
        var buf = new ScreenBuffer(60, 1);
        var bar = new KeyBar(T) { Modifiers = KeyMods.Ctrl | KeyMods.Alt };
        bar.Draw(buf, 0);

        Assert.Equal(new string(' ', 60), Row(buf, 0));
        for (int x = 0; x < 60; x++)
        {
            Assert.Equal(T.KeyBarBackground, buf.Get(x, 0).Style);
        }
    }

    [Fact]
    public void EveryColumnIsPaintedAtEveryWidth()
    {
        foreach (int width in new[] { 12, 24, 40, 80, 100, 120, 203 })
        {
            var buf = new ScreenBuffer(width, 1);
            buf.Clear(new CellStyle(ConsoleColor.Red, ConsoleColor.DarkMagenta));
            new KeyBar(T).Draw(buf, 0);

            for (int x = 0; x < width; x++)
            {
                CellStyle style = buf.Get(x, 0).Style;
                Assert.True(
                    style == T.KeyBarNum || style == T.KeyBarText || style == T.KeyBarBackground,
                    $"column {x} of a {width} column bar was left unpainted");
            }
        }
    }

    [Fact]
    public void ACaptionTooWideForItsCellIsTruncatedWithAnEllipsis()
    {
        // 48 columns is four per cell: the number plus three columns of caption.
        var buf = new ScreenBuffer(48, 1);
        new KeyBar(T).Draw(buf, 0);

        string row = Row(buf, 0);
        Assert.Equal(48, row.Length);
        Assert.Contains(ScreenBuffer.Ellipsis, row);
        Assert.StartsWith("1He" + ScreenBuffer.Ellipsis, row, StringComparison.Ordinal);
    }

    [Fact]
    public void DrawingOutsideTheBufferIsIgnored()
    {
        var buf = new ScreenBuffer(40, 2);
        var bar = new KeyBar(T);

        bar.Draw(buf, 5);
        bar.Draw(buf, -1);

        Assert.Equal(new string(' ', 40), Row(buf, 0));
        Assert.Equal(new string(' ', 40), Row(buf, 1));
    }

    [Fact]
    public void CurrentReportsWhatWouldBeDrawn()
    {
        var bar = new KeyBar(T) { Modifiers = KeyMods.Alt };
        Assert.Same(KeyBarSets.Alt, bar.Current);

        KeyBarLabels empty = KeyBarLabels.Empty;
        bar.Override = empty;
        Assert.Same(empty, bar.Current);
        Assert.All(bar.Current.Labels, Assert.Empty);
    }

    [Fact]
    public void ANullThemeIsRejectedUpFront()
    {
        Assert.Throws<ArgumentNullException>(static () => new KeyBar(null!));
        Assert.Throws<ArgumentNullException>(static () => new KeyBar(Theme.FarDefault()).Draw(null!, 0));
    }

    internal static string Row(ScreenBuffer buf, int y)
    {
        var sb = new StringBuilder(buf.Width);
        for (int x = 0; x < buf.Width; x++)
        {
            sb.Append(buf.Get(x, y).Glyph);
        }

        return sb.ToString();
    }
}

public class ClockWidgetTests
{
    [Fact]
    public void TheTimeIsRightAlignedOnTheTopRow()
    {
        var buf = new ScreenBuffer(30, 3);
        var clock = new ClockWidget { TimeSource = () => new DateTime(2026, 8, 8, 10, 6, 0) };

        clock.Draw(buf, Theme.FarDefault());

        Assert.Equal(new string(' ', 22) + "10:06 AM", KeyBarDrawTests.Row(buf, 0));
        Assert.Equal(new string(' ', 30), KeyBarDrawTests.Row(buf, 1));
    }

    [Theory]
    [InlineData(0, 0, "12:00 AM")]
    [InlineData(9, 59, "9:59 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(23, 5, "11:05 PM")]
    public void TheFormatIsATwelveHourInvariantClock(int hour, int minute, string expected)
    {
        Assert.Equal(expected, ClockWidget.Format(new DateTime(2026, 1, 1, hour, minute, 0)));
    }

    [Fact]
    public void TheClockStyleIsUsed()
    {
        var theme = Theme.FarDefault();
        var buf = new ScreenBuffer(20, 1);
        new ClockWidget { TimeSource = () => new DateTime(2026, 8, 8, 1, 2, 0) }.Draw(buf, theme);

        Assert.Equal(theme.Clock, buf.Get(19, 0).Style);
        Assert.Equal(theme.Clock, buf.Get(13, 0).Style);
    }

    [Fact]
    public void AConsoleTooNarrowForTheTimeShowsNothingRatherThanARuinedRow()
    {
        var buf = new ScreenBuffer(5, 1);
        new ClockWidget { TimeSource = () => new DateTime(2026, 8, 8, 10, 6, 0) }.Draw(buf, Theme.FarDefault());

        Assert.Equal("     ", KeyBarDrawTests.Row(buf, 0));
    }
}
