using System.Text;
using System.Text.RegularExpressions;
using Dvopan.Core;
using Dvopan.Rendering;
using Dvopan.Theming;

namespace Dvopan.Tests;

/// <summary>Parsing, formatting and round-tripping of a 24-bit colour.</summary>
public class RgbTests
{
    [Theory]
    [InlineData("#0000AA", 0x00, 0x00, 0xAA)]
    [InlineData("#55FFFF", 0x55, 0xFF, 0xFF)]
    [InlineData("#000000", 0x00, 0x00, 0x00)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    [InlineData("0037da", 0x00, 0x37, 0xDA)]      // no hash, lower case
    [InlineData("  #aa5500  ", 0xAA, 0x55, 0x00)] // surrounding whitespace
    public void TryParseAcceptsHexColours(string text, int r, int g, int b)
    {
        Assert.True(Rgb.TryParse(text, out Rgb? rgb));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), rgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]    // too short
    [InlineData("#1234567")]  // too long
    [InlineData("#GGGGGG")]   // not hex
    [InlineData("#00 00AA")]  // embedded space
    [InlineData("rgb(0,0,170)")]
    public void TryParseRejectsAnythingElse(string? text)
    {
        Assert.False(Rgb.TryParse(text, out Rgb? rgb));
        Assert.Null(rgb);
    }

    [Fact]
    public void ToHexRoundTripsThroughTryParse()
    {
        for (int i = 0; i < Palette.Size; i++)
        {
            Rgb original = Palette.ClassicVga[i];
            Assert.True(Rgb.TryParse(original.ToHex(), out Rgb? parsed));
            Assert.Equal(original, parsed);
        }
    }

    [Fact]
    public void ToHexIsUpperCaseWithAHash()
    {
        Assert.Equal("#0000AA", new Rgb(0x00, 0x00, 0xAA).ToHex());
        Assert.Equal("#55FFFF", new Rgb(0x55, 0xFF, 0xFF).ToString());
    }

    [Fact]
    public void ParseThrowsOnGarbage() => Assert.Throws<FormatException>(() => Rgb.Parse("not a colour"));
}

/// <summary>The palette tables, their contrast maths, and palette files.</summary>
public class PaletteTests
{
    // The authentic VGA/EGA text palette: 0x00/0xAA per channel with a +0x55 intensity offset,
    // plus the brown fixup at slot 6. These are pinned because the whole point of the true colour
    // path is that the look no longer depends on anyone's terminal scheme.
    [Theory]
    [InlineData(ConsoleColor.Black, "#000000")]
    [InlineData(ConsoleColor.DarkBlue, "#0000AA")]
    [InlineData(ConsoleColor.DarkGreen, "#00AA00")]
    [InlineData(ConsoleColor.DarkCyan, "#00AAAA")]
    [InlineData(ConsoleColor.DarkRed, "#AA0000")]
    [InlineData(ConsoleColor.DarkMagenta, "#AA00AA")]
    [InlineData(ConsoleColor.DarkYellow, "#AA5500")]
    [InlineData(ConsoleColor.Gray, "#AAAAAA")]
    [InlineData(ConsoleColor.DarkGray, "#555555")]
    [InlineData(ConsoleColor.Blue, "#5555FF")]
    [InlineData(ConsoleColor.Green, "#55FF55")]
    [InlineData(ConsoleColor.Cyan, "#55FFFF")]
    [InlineData(ConsoleColor.Red, "#FF5555")]
    [InlineData(ConsoleColor.Magenta, "#FF55FF")]
    [InlineData(ConsoleColor.Yellow, "#FFFF55")]
    [InlineData(ConsoleColor.White, "#FFFFFF")]
    public void ClassicVgaIsPinnedExactly(ConsoleColor slot, string hex) =>
        Assert.Equal(hex, Palette.ClassicVga[slot].ToHex());

    [Fact]
    public void TheFarPanelPairIsDeepNavyAndBrightCyan()
    {
        // The two colours that cover most of the screen; this is the crispness the fix is about.
        Assert.Equal("#0000AA", Palette.ClassicVga[ConsoleColor.DarkBlue].ToHex());
        Assert.Equal("#55FFFF", Palette.ClassicVga[ConsoleColor.Cyan].ToHex());
    }

    [Theory]
    [InlineData(ConsoleColor.DarkBlue, "#000080")]
    [InlineData(ConsoleColor.DarkCyan, "#008080")]
    [InlineData(ConsoleColor.Gray, "#C0C0C0")]
    [InlineData(ConsoleColor.Cyan, "#00FFFF")]
    public void WindowsNtIsTheLegacyConsoleTable(ConsoleColor slot, string hex) =>
        Assert.Equal(hex, Palette.WindowsNt[slot].ToHex());

    [Theory]
    [InlineData(ConsoleColor.DarkBlue, "#0037DA")]
    [InlineData(ConsoleColor.Cyan, "#61D6D6")]
    [InlineData(ConsoleColor.White, "#F2F2F2")]
    public void CampbellIsWindowsTerminalsDefaultScheme(ConsoleColor slot, string hex) =>
        Assert.Equal(hex, Palette.Campbell[slot].ToHex());

    [Fact]
    public void EverySlotOfEveryShippedPaletteIsPopulated()
    {
        foreach (Palette p in new[] { Palette.ClassicVga, Palette.WindowsNt, Palette.Campbell })
        {
            foreach (ConsoleColor c in Enum.GetValues<ConsoleColor>())
            {
                Assert.NotNull(p[c]);
            }
        }
    }

    [Fact]
    public void TheIndexerMasksToSixteenSlots()
    {
        Assert.Equal(Palette.ClassicVga[0], Palette.ClassicVga[16]);
        Assert.Equal(Palette.ClassicVga[ConsoleColor.Cyan], Palette.ClassicVga[11]);
    }

    [Fact]
    public void APaletteNeedsExactlySixteenEntries()
    {
        Assert.Throws<ArgumentException>(() => new Palette([new Rgb(0, 0, 0)]));
        Assert.Throws<ArgumentNullException>(() => new Palette(null!));
    }

    [Fact]
    public void WithReturnsAModifiedCopyAndLeavesTheOriginalAlone()
    {
        Palette custom = Palette.ClassicVga.With(ConsoleColor.DarkBlue, new Rgb(1, 2, 3));
        Assert.Equal("#010203", custom[ConsoleColor.DarkBlue].ToHex());
        Assert.Equal("#0000AA", Palette.ClassicVga[ConsoleColor.DarkBlue].ToHex());
    }

    // ---- contrast --------------------------------------------------------------------------

    [Fact]
    public void RelativeLuminanceMatchesTheWcagFormula()
    {
        Assert.Equal(0.0, Palette.RelativeLuminance(new Rgb(0, 0, 0)), 6);
        Assert.Equal(1.0, Palette.RelativeLuminance(new Rgb(255, 255, 255)), 6);

        // The two blues that decide whether the panel reads as crisp or as blended.
        Assert.Equal(0.02902, Palette.RelativeLuminance(Palette.ClassicVga[ConsoleColor.DarkBlue]), 5);
        Assert.Equal(0.07794, Palette.RelativeLuminance(Palette.Campbell[ConsoleColor.DarkBlue]), 5);
    }

    [Fact]
    public void ContrastRatioIsBoundedAndSymmetric()
    {
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);

        Assert.Equal(21.0, Palette.ContrastRatio(black, white), 6);
        Assert.Equal(21.0, Palette.ContrastRatio(white, black), 6);
        Assert.Equal(1.0, Palette.ContrastRatio(black, black), 6);
    }

    [Fact]
    public void TheDominantPanelPairIsFarCrisperUnderVgaThanUnderCampbell()
    {
        // Cyan on DarkBlue - Panel.Text and Panel.Box, i.e. roughly 78% of the screen.
        double vga = Palette.ContrastRatio(
            Palette.ClassicVga[ConsoleColor.Cyan],
            Palette.ClassicVga[ConsoleColor.DarkBlue]);
        double campbell = Palette.ContrastRatio(
            Palette.Campbell[ConsoleColor.Cyan],
            Palette.Campbell[ConsoleColor.DarkBlue]);

        Assert.Equal(10.841, vga, 3);
        Assert.Equal(4.728, campbell, 3);

        // The measured washout: 2.29x, and Campbell barely clears the 4.5:1 body text floor.
        Assert.Equal(2.293, vga / campbell, 3);
        Assert.True(vga > 7.0, "the VGA pair clears the strictest WCAG level");
        Assert.True(campbell < 5.0, "the Campbell pair is only just readable");
    }

    [Fact]
    public void TheDirectoryPairIsAlsoCrisperUnderVga()
    {
        // White on DarkBlue - directory entries.
        double vga = Palette.ContrastRatio(
            Palette.ClassicVga[ConsoleColor.White],
            Palette.ClassicVga[ConsoleColor.DarkBlue]);
        double campbell = Palette.ContrastRatio(
            Palette.Campbell[ConsoleColor.White],
            Palette.Campbell[ConsoleColor.DarkBlue]);

        Assert.Equal(13.287, vga, 3);
        Assert.Equal(7.331, campbell, 3);
        Assert.Equal(1.813, vga / campbell, 3);
    }

    [Fact]
    public void TheClassicNtPaletteIsHarderStillOnTheDominantPair()
    {
        double nt = Palette.ContrastRatio(
            Palette.WindowsNt[ConsoleColor.Cyan],
            Palette.WindowsNt[ConsoleColor.DarkBlue]);

        Assert.Equal(12.768, nt, 3);
    }

    [Fact]
    public void ContrastOfResolvesAStyleThroughThePalette()
    {
        var panelText = new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
        Assert.Equal(10.841, Palette.ClassicVga.ContrastOf(panelText), 3);
        Assert.Equal(4.728, Palette.Campbell.ContrastOf(panelText), 3);
    }

    // ---- files -----------------------------------------------------------------------------

    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), "oc-tests", "palette-" + Guid.NewGuid().ToString("N")[..8], name);

    [Fact]
    public void SaveAndLoadRoundTripEverySlot()
    {
        string path = TempFile("palette.json");
        try
        {
            Palette.WindowsNt.SaveToJson(path);
            Palette loaded = Palette.Load(path);

            Assert.Equal("Windows NT", loaded.Name);
            for (int i = 0; i < Palette.Size; i++)
            {
                Assert.Equal(Palette.WindowsNt[i], loaded[i]);
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void MissingUnknownAndMalformedEntriesFallBackToTheDefaultTable()
    {
        string path = TempFile("partial.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {
                  "name": "Half",
                  "colors": {
                    "DarkBlue": "#010203",
                    "LightCyan": "#040506",
                    "NotAColour": "#070809",
                    "Yellow": "wrong"
                  }
                }
                """);

            Palette p = Palette.Load(path);

            Assert.Equal("Half", p.Name);
            Assert.Equal("#010203", p[ConsoleColor.DarkBlue].ToHex());  // set
            Assert.Equal("#040506", p[ConsoleColor.Cyan].ToHex());      // set through the LightCyan alias
            Assert.Equal("#FFFF00", p[ConsoleColor.Yellow].ToHex());    // malformed value ignored
            Assert.Equal("#800000", p[ConsoleColor.DarkRed].ToHex());   // absent entirely
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void ColourNamesAreAlsoAcceptedStraightAtTheRoot()
    {
        string path = TempFile("flat.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "DarkBlue": "#000080", "11": "#00FFFF" }""");

            Palette p = Palette.Load(path);
            Assert.Equal("#000080", p[ConsoleColor.DarkBlue].ToHex());
            Assert.Equal("#00FFFF", p[ConsoleColor.Cyan].ToHex());
            Assert.Equal("flat", p.Name); // no "name" key: the file name is used
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void LoadOrDefaultNeverThrows()
    {
        Assert.Same(Palette.Default, Palette.LoadOrDefault(null));
        Assert.Same(Palette.Default, Palette.LoadOrDefault("   "));
        Assert.Same(Palette.Default, Palette.LoadOrDefault(TempFile("does-not-exist.json")));

        string path = TempFile("broken.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");
            Assert.Same(Palette.Default, Palette.LoadOrDefault(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static void Cleanup(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// The environment sniffing behind
/// <see cref="ColorDepthDetector.Detect(Func{string, string?}, bool, ColorDepth)"/>, driven
/// entirely through the injected lookup so no real environment variable is touched.
/// </summary>
public class ColorDepthDetectorTests
{
    private static ColorDepth Detect(
        Dictionary<string, string?> env,
        bool redirected = false,
        ColorDepth platformDefault = ColorDepth.Indexed16) =>
        ColorDepthDetector.Detect(name => env.GetValueOrDefault(name), redirected, platformDefault);

    private static Dictionary<string, string?> Env(params (string Name, string? Value)[] entries)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string name, string? value) in entries)
        {
            map[name] = value;
        }

        return map;
    }

    [Fact]
    public void NoColorBeatsEverySignalBelowIt()
    {
        Assert.Equal(
            ColorDepth.Indexed16,
            Detect(
                Env(("NO_COLOR", "1"), ("COLORTERM", "truecolor"), ("WT_SESSION", "abc")),
                platformDefault: ColorDepth.TrueColor));
    }

    [Fact]
    public void AnEmptyNoColorIsNotSet()
    {
        // no-color.org: the variable counts only when present AND non-empty.
        Assert.Equal(ColorDepth.TrueColor, Detect(Env(("NO_COLOR", ""), ("WT_SESSION", "abc"))));
    }

    [Fact]
    public void RedirectedOutputStaysIndexed()
    {
        Assert.Equal(
            ColorDepth.Indexed16,
            Detect(Env(("COLORTERM", "truecolor")), redirected: true, platformDefault: ColorDepth.TrueColor));
    }

    [Theory]
    [InlineData("truecolor", ColorDepth.TrueColor)]
    [InlineData("24bit", ColorDepth.TrueColor)]
    [InlineData("TrueColor", ColorDepth.TrueColor)]
    [InlineData("256", ColorDepth.Indexed16)] // says nothing about 24-bit
    [InlineData("", ColorDepth.Indexed16)]
    public void ColorTermIsTheFirstCapabilitySignal(string value, ColorDepth expected) =>
        Assert.Equal(expected, Detect(Env(("COLORTERM", value))));

    [Fact]
    public void WindowsTerminalIsRecognisedWithoutColorTerm()
    {
        // WT renders 24-bit but still sets no COLORTERM, which is exactly the reported bug.
        Assert.Equal(ColorDepth.TrueColor, Detect(Env(("WT_SESSION", "0dcb1f2a-9a1f"))));
    }

    [Theory]
    [InlineData("ON", ColorDepth.TrueColor)]
    [InlineData("on", ColorDepth.TrueColor)]
    [InlineData("OFF", ColorDepth.Indexed16)]
    public void ConEmuIsRecognisedThroughConEmuAnsi(string value, ColorDepth expected) =>
        Assert.Equal(expected, Detect(Env(("ConEmuANSI", value))));

    [Theory]
    [InlineData("vscode")]
    [InlineData("WezTerm")]
    [InlineData("iTerm.app")]
    [InlineData("ghostty")]
    [InlineData("Hyper")]
    [InlineData("rio")]
    public void SelfIdentifyingTerminalsGetTrueColor(string program) =>
        Assert.Equal(ColorDepth.TrueColor, Detect(Env(("TERM_PROGRAM", program))));

    [Fact]
    public void AppleTerminalIsAnExplicitNegative()
    {
        // 256 colours only; it must not fall through to the optimistic platform default.
        Assert.Equal(
            ColorDepth.Indexed16,
            Detect(
                Env(("TERM_PROGRAM", "Apple_Terminal"), ("TERM", "xterm-256color")),
                platformDefault: ColorDepth.TrueColor));
    }

    [Theory]
    [InlineData("xterm-truecolor", ColorDepth.TrueColor)]
    [InlineData("xterm-direct", ColorDepth.TrueColor)]
    [InlineData("xterm-direct256", ColorDepth.TrueColor)]
    [InlineData("xterm-256color", ColorDepth.Indexed16)] // 256 indexed colours, not 24-bit
    [InlineData("dumb", ColorDepth.Indexed16)]
    [InlineData("linux", ColorDepth.Indexed16)]
    public void TermIsAWeakHintAnd256ColorIsNotOneOfThem(string term, ColorDepth expected) =>
        Assert.Equal(expected, Detect(Env(("TERM", term))));

    [Fact]
    public void DumbTerminalsStayIndexedEvenWhenThePlatformIsOptimistic() =>
        Assert.Equal(ColorDepth.Indexed16, Detect(Env(("TERM", "dumb")), platformDefault: ColorDepth.TrueColor));

    [Theory]
    [InlineData(ColorDepth.Indexed16)]
    [InlineData(ColorDepth.TrueColor)]
    public void ATerminalThatAdvertisesNothingGetsThePlatformDefault(ColorDepth platformDefault) =>
        Assert.Equal(platformDefault, Detect(Env(), platformDefault: platformDefault));

    [Fact]
    public void TheEnvironmentLookupIsRequired() =>
        Assert.Throws<ArgumentNullException>(() => ColorDepthDetector.Detect(null!, false, ColorDepth.Indexed16));

    [Fact]
    public void DetectingTheRealEnvironmentDoesNotThrow()
    {
        ColorDepth depth = ColorDepthDetector.Detect();
        Assert.True(depth is ColorDepth.Indexed16 or ColorDepth.TrueColor);
    }

    [Fact]
    public void ThePlatformDefaultIsOptimisticOnlyOnAModernWindows()
    {
        ColorDepth expected = OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063)
            ? ColorDepth.TrueColor
            : ColorDepth.Indexed16;

        Assert.Equal(expected, ColorDepthDetector.PlatformDefault());
    }
}

/// <summary>
/// The rendering half: the escape sequences produced in each mode, and the byte cost of a full
/// repaint of a real frame.
/// </summary>
public class TrueColorRenderingTests
{
    private const int Width = 130;
    private const int Height = 30;

    /// <summary>ASCII ESC, built from its code point so no control character sits in this file.</summary>
    private static readonly string E = ((char)27).ToString();

    private static readonly Regex SgrPattern = new(Regex.Escape(((char)27).ToString()) + @"\[[0-9;]*m");

    [Fact]
    public void RenderAnsiEmitsTwentyFourBitSgrInTrueColorMode()
    {
        var b = new ScreenBuffer(4, 1);
        b.Set(0, 0, 'A', new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));

        // #55FFFF on #0000AA: the crisp classic panel pair, pinned regardless of the terminal scheme.
        Assert.Equal(
            E + "[38;2;85;255;255;48;2;0;0;170mA" + E + "[0m",
            b.RenderAnsi(ColorDepth.TrueColor));
    }

    [Fact]
    public void RenderAnsiStillEmitsIndexedSgrInIndexedMode()
    {
        var b = new ScreenBuffer(4, 1);
        b.Set(0, 0, 'A', new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));

        string expected = E + "[96;44mA" + E + "[0m";
        Assert.Equal(expected, b.RenderAnsi());
        Assert.Equal(expected, b.RenderAnsi(ColorDepth.Indexed16));
    }

    [Fact]
    public void RenderAnsiHonoursTheSuppliedPalette()
    {
        var b = new ScreenBuffer(2, 1);
        b.Set(0, 0, 'A', new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));

        Assert.Equal(
            E + "[38;2;0;255;255;48;2;0;0;128mA" + E + "[0m",
            b.RenderAnsi(ColorDepth.TrueColor, Palette.WindowsNt));
    }

    [Fact]
    public void OneSgrCarriesBothChannelsAndOnlyStyleChangesEmitOne()
    {
        var b = new ScreenBuffer(4, 1);
        var a = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        var c = new CellStyle(ConsoleColor.Black, ConsoleColor.DarkCyan);
        b.Set(0, 0, 'x', a);
        b.Set(1, 0, 'y', a);
        b.Set(2, 0, 'z', c);

        Assert.Equal(
            E + "[38;2;255;255;85;48;2;0;0;170mxy" + E + "[38;2;0;0;0;48;2;0;170;170mz" + E + "[0m",
            b.RenderAnsi(ColorDepth.TrueColor));
    }

    [Fact]
    public void EverySlotSurvivesTheRoundTripToRgb()
    {
        foreach (ConsoleColor color in Enum.GetValues<ConsoleColor>())
        {
            var b = new ScreenBuffer(1, 1);
            b.Set(0, 0, 'q', new CellStyle(color, ConsoleColor.Black));

            Rgb fg = Palette.ClassicVga[color];
            Assert.Equal(
                E + $"[38;2;{fg.R};{fg.G};{fg.B};48;2;0;0;0mq" + E + "[0m",
                b.RenderAnsi(ColorDepth.TrueColor));
        }
    }

    // ---- the live diff renderer -------------------------------------------------------------

    [Fact]
    public void AHeadlessTerminalStaysIndexedSoScreenshotsAreReproducible()
    {
        using var t = Terminal.Create(20, 5);
        Assert.Equal(ColorDepth.Indexed16, t.ColorDepth);
        Assert.Same(Palette.ClassicVga, t.Palette);
    }

    [Fact]
    public void TheDiffRendererUsesTheTerminalsDepthAndPalette()
    {
        using var t = Terminal.Create(2, 1, ColorDepth.TrueColor);
        t.Buffer.Clear(new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));

        Assert.Contains(E + "[38;2;85;255;255;48;2;0;0;170m", t.BuildFrameText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SwappingThePaletteRepaintsTheWholeScreen()
    {
        using var t = Terminal.Create(2, 1, ColorDepth.TrueColor);
        t.Buffer.Clear(new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue));
        _ = t.BuildFrameText();
        Assert.Equal(string.Empty, t.BuildFrameText()); // nothing changed

        t.Palette = Palette.WindowsNt;

        Assert.Contains(E + "[38;2;0;255;255;48;2;0;0;128m", t.BuildFrameText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePaletteCannotBeSetToNull()
    {
        using var t = Terminal.Create(2, 1);
        Assert.Throws<ArgumentNullException>(() => t.Palette = null!);
    }

    // ---- byte budget --------------------------------------------------------------------------

    [Fact]
    public void AFullTrueColorRepaintStaysWithinASaneByteBudget()
    {
        using var tree = new PaletteShellTree();

        string indexed = FullRepaint(tree.Root, ColorDepth.Indexed16);
        string trueColor = FullRepaint(tree.Root, ColorDepth.TrueColor);

        int indexedBytes = Encoding.UTF8.GetByteCount(indexed);
        int trueColorBytes = Encoding.UTF8.GetByteCount(trueColor);

        // A full 130x30 repaint measures ~7 KB indexed and ~13 KB in true colour. The budget has
        // headroom for layout changes but would catch a per-cell SGR regression, which at 3900
        // cells times 31 bytes would be well over 100 KB.
        Assert.True(
            trueColorBytes < 24_000,
            $"a full true colour repaint was {trueColorBytes} bytes, budget is 24000");

        Assert.True(
            trueColorBytes < indexedBytes * 5 / 2,
            $"true colour grew the frame from {indexedBytes} to {trueColorBytes} bytes, more than 2.5x");

        // The "emit an SGR only when the style changes" optimisation is what keeps that true: the
        // two modes must differ in the size of each sequence, never in how many are written.
        Assert.Equal(SgrPattern.Matches(indexed).Count, SgrPattern.Matches(trueColor).Count);
    }

    [Fact]
    public void TheSecondFrameOfAnUnchangedScreenIsFreeInTrueColorToo()
    {
        using var tree = new PaletteShellTree();
        using Application app = BuildApp(tree.Root, ColorDepth.TrueColor);

        app.Layout();
        app.DrawFrame();
        Assert.True(SgrPattern.Matches(app.Terminal.BuildFrameText()).Count > 1);

        // Nothing on screen changed, so the second frame repaints no cell and therefore carries no
        // SGR at all - only the synchronized-update wrapper and the command line cursor move.
        app.DrawFrame();
        string second = app.Terminal.BuildFrameText();
        Assert.Empty(SgrPattern.Matches(second));
        Assert.True(Encoding.UTF8.GetByteCount(second) < 64, $"an idle frame was {second.Length} chars");
    }

    private static string FullRepaint(string root, ColorDepth depth)
    {
        using Application app = BuildApp(root, depth);
        app.Layout();
        app.DrawFrame();
        return app.Terminal.BuildFrameText();
    }

    private static Application BuildApp(string root, ColorDepth depth)
    {
        var settings = new Settings { ShowClock = false };
        Terminal terminal = Terminal.Create(Width, Height, depth);
        var app = new Application(terminal, settings, Theme.Classic(), input: null);
        app.Initialize(new CommandLineArgs { LeftPath = root, RightPath = root });
        return app;
    }

    /// <summary>A throwaway folder with enough entries to fill both panels.</summary>
    private sealed class PaletteShellTree : IDisposable
    {
        public PaletteShellTree()
        {
            Root = Path.Combine(Path.GetTempPath(), "oc-tests", "palette-frame-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(Root, "docs"));
            Directory.CreateDirectory(Path.Combine(Root, "src"));

            for (int i = 0; i < 12; i++)
            {
                File.WriteAllText(Path.Combine(Root, $"file{i:D2}.txt"), new string('x', 64 * (i + 1)));
            }
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
            }
        }
    }
}
