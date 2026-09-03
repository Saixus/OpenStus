using System;
using System.Linq;
using OpenStus.Rendering;
using OpenStus.Ui;
using Xunit;

namespace OpenStus.Tests;

/// <summary>
/// Checks of the notice printed on the user screen at startup - the one Ctrl+O reveals.
/// </summary>
public class StartupBannerTests
{
    private static string[] Lines(string banner) =>
        banner.Split(Environment.NewLine, StringSplitOptions.None);

    [Fact]
    public void TheNoticeIsPrintableAsciiAndThePortraitIsOnlyBlockShades()
    {
        // The portrait is allowed the block shades and nothing else: they are what survive whatever
        // font the terminal uses. The notice has no reason to leave ASCII at all.
        Assert.All(
            StartupBanner.Notice.SelectMany(static line => line),
            c => Assert.InRange(c, ' ', '~'));

        Assert.All(
            StartupBanner.Portrait.SelectMany(static line => line),
            c => Assert.Contains(c, " ░▒▓█"));
    }

    [Fact]
    public void AWideTerminalGetsThePortraitAndTheNoticeSideBySide()
    {
        string banner = StartupBanner.Render(120)!;

        Assert.Contains(StartupBanner.Portrait[0].TrimEnd(), banner, StringComparison.Ordinal);
        Assert.Contains("Vasyl Stus", banner, StringComparison.Ordinal);
        Assert.Contains("(1938-1985)", banner, StringComparison.Ordinal);
        Assert.Contains("Open Stus", banner, StringComparison.Ordinal);

        // The first portrait row and the first notice line share one line of output.
        string first = Lines(banner).First(l => l.Contains("Open Stus", StringComparison.Ordinal));
        Assert.StartsWith(StartupBanner.Portrait[0], first, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEightyColumnTerminalItIsSizedForStillFitsSideBySide()
    {
        // 80 columns is the floor every terminal offers; the layout is chosen to sit inside it.
        Assert.True(StartupBanner.PortraitWidth + StartupBanner.Gutter + StartupBanner.NoticeWidth <= 80);

        string banner = StartupBanner.Render(80)!;

        Assert.All(Lines(banner), line => Assert.True(line.Length <= 80, $"'{line}' is {line.Length} columns"));
    }

    [Fact]
    public void ANarrowTerminalGetsTheNoticeWithoutThePortrait()
    {
        string banner = StartupBanner.Render(StartupBanner.NoticeWidth + 1)!;

        Assert.Contains("Vasyl Stus", banner, StringComparison.Ordinal);
        Assert.Contains("(1938-1985)", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("▓", banner, StringComparison.Ordinal);
        Assert.All(
            Lines(banner),
            line => Assert.True(line.Length <= StartupBanner.NoticeWidth, $"'{line}' is too wide"));
    }

    [Fact]
    public void ATerminalTooNarrowForEitherGetsNothing()
    {
        Assert.Null(StartupBanner.Render(StartupBanner.NoticeWidth));
        Assert.Null(StartupBanner.Render(10));
        Assert.Null(StartupBanner.Render(0));
        Assert.Null(StartupBanner.Render(-1));
    }

    [Fact]
    public void NoLineEverFillsTheLastColumn()
    {
        // A line flush against the right edge wraps eagerly on the pre-VT Windows console, and the
        // newline behind it then eats a second row. Whatever width the banner accepts, it has to
        // leave that last column alone - including at the two widths where a layout starts to fit.
        for (int width = 1; width <= 140; width++)
        {
            if (StartupBanner.Render(width) is not string banner)
            {
                continue;
            }

            Assert.All(
                Lines(banner),
                line => Assert.True(line.Length < width, $"at width {width}, '{line}' fills the line"));
        }
    }

    [Fact]
    public void NoLineCarriesTrailingBlanks()
    {
        // Trailing blanks would repaint cells the terminal already owns, smearing the background
        // colour across the width of the banner.
        Assert.All(Lines(StartupBanner.Render(120)!), line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void TheNoticeCreditsThePhotographAndTheLicence()
    {
        string banner = StartupBanner.Render(120)!;

        Assert.Contains("1980 arrest", banner, StringComparison.Ordinal);
        Assert.Contains("public domain", banner, StringComparison.Ordinal);
        Assert.Contains("Wikimedia Commons", banner, StringComparison.Ordinal);
        Assert.Contains("Dmytro Soyenko", banner, StringComparison.Ordinal);
        Assert.Contains("MIT", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadlessTerminalIsNeverGreeted()
    {
        // --screenshot runs headless, and a banner on its stdout would corrupt the one frame it
        // exists to emit. Terminal.Create must not even ask for the text.
        bool asked = false;

        using var terminal = Terminal.Create(80, 25, banner: _ =>
        {
            asked = true;
            return "should never be printed";
        });

        Assert.True(terminal.IsHeadless);
        Assert.False(asked);
    }

    [Fact]
    public void TheBannerIsShortEnoughForTheSmallestUsualTerminal()
    {
        // A 25 row terminal has to keep the whole notice and still show the prompt Ctrl+O draws
        // on its bottom row. The banner ends with a line break, so the split leaves one empty
        // trailing element that costs no row.
        string banner = StartupBanner.Render(120)!;

        Assert.EndsWith(Environment.NewLine, banner, StringComparison.Ordinal);
        Assert.True(Lines(banner).Length - 1 <= 24, $"the banner is {Lines(banner).Length - 1} rows tall");
    }
}
