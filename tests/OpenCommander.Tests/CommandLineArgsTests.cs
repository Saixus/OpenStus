using System.Runtime.CompilerServices;
using OpenCommander.Core;
using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Tests;

/// <summary>
/// Checks of the option combinations <c>oc</c> accepts and refuses. The general parsing contract
/// lives in <c>CommandLineArgsTests</c>; this file is about <c>--size</c>, which only means anything
/// alongside <c>--screenshot</c>, and about the two colour options and their documentation.
/// </summary>
public class CommandLineArgsOptionTests
{
    [Theory]
    [InlineData("--size", "80x25")]
    [InlineData("--size=80x25", null)]
    public void SizeWithoutScreenshotIsRejected(string first, string? second)
    {
        string[] argv = second is null ? [first] : [first, second];

        CommandLineArgs args = CommandLineArgs.Parse(argv);

        // Terminal.Create treats a forced size as headless, so an interactive run would render one
        // frame into a screen nobody can see and exit 0 having printed nothing.
        Assert.True(args.HasError);
        Assert.Equal("--size requires --screenshot", args.Error);
    }

    [Fact]
    public void SizeWithAPositionalPathButNoScreenshotIsStillRejected()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["C:/Temp", "--size", "80x25"]);

        Assert.True(args.HasError);
        Assert.Contains("--screenshot", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--screenshot", "--size", "160x50")]
    [InlineData("--size", "160x50", "--screenshot")] // order must not matter
    public void SizeWithScreenshotIsAccepted(string a, string b, string c)
    {
        CommandLineArgs args = CommandLineArgs.Parse([a, b, c]);

        Assert.False(args.HasError);
        Assert.True(args.Screenshot);
        Assert.Equal(160, args.Width);
        Assert.Equal(50, args.Height);
        Assert.Equal(160, args.EffectiveWidth);
        Assert.Equal(50, args.EffectiveHeight);
    }

    [Fact]
    public void ScreenshotWithoutSizeKeepsTheDocumentedDefault()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot"]);

        Assert.False(args.HasError);
        Assert.Null(args.Width);
        Assert.Equal(CommandLineArgs.DefaultScreenshotWidth, args.EffectiveWidth);
        Assert.Equal(CommandLineArgs.DefaultScreenshotHeight, args.EffectiveHeight);
    }

    [Fact]
    public void AMalformedSizeIsReportedAsMalformedNotAsMissingScreenshot()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot", "--size", "wide"]);

        Assert.True(args.HasError);
        Assert.Contains("WxH", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public void HelpAndVersionStillWinOverTheSizeRule(string option)
    {
        // "oc --size 80x25 --help" should print the usage text, not a parse error.
        CommandLineArgs args = CommandLineArgs.Parse(["--size", "80x25", option]);

        Assert.False(args.HasError);
    }

    [Fact]
    public void TheUsageTextTiesSizeToScreenshotTheWayAnsiIsTied()
    {
        string usage = CommandLineArgs.UsageText;

        Assert.Contains("--ansi                 with --screenshot", usage, StringComparison.Ordinal);
        Assert.Contains("--size <WxH>           with --screenshot", usage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeDocumentsSizeTheSameWayTheUsageTextDoes()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return; // the sources are not next to the test assembly; nothing to check
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        string usage = CommandLineArgs.UsageText.Replace("\r\n", "\n", StringComparison.Ordinal);
        int optionsAt = usage.IndexOf("  --left", StringComparison.Ordinal);
        Assert.True(optionsAt > 0);

        // The README's option block is the usage text verbatim, so the two cannot drift apart.
        Assert.Contains(usage[optionsAt..].Trim(), readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUsageTextDocumentsBothColourOptions()
    {
        string usage = CommandLineArgs.UsageText;

        Assert.Contains(
            "--colors <mode>        auto (the default), truecolor or indexed;",
            usage,
            StringComparison.Ordinal);

        Assert.Contains(
            "--palette <file.json>  RGB values for the 16 colour slots, used by",
            usage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeExplainsWhyTheColoursAreWhatTheyAre()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return; // the sources are not next to the test assembly; nothing to check
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        // The section exists, names the culprit, backs the claim with the measured numbers, and
        // documents both escape hatches.
        Assert.Contains("## Colours", readme, StringComparison.Ordinal);
        Assert.Contains("Campbell", readme, StringComparison.Ordinal);
        Assert.Contains("10.84:1", readme, StringComparison.Ordinal);
        Assert.Contains("4.73:1", readme, StringComparison.Ordinal);
        Assert.Contains("--colors indexed", readme, StringComparison.Ordinal);
        Assert.Contains("NO_COLOR", readme, StringComparison.Ordinal);
        Assert.Contains("--palette", readme, StringComparison.Ordinal);
    }

    private static string? FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        // The compiled test assembly lives under an artifacts folder that may sit anywhere, so the
        // source path is the only reliable way back to the repository.
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenCommander.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "README.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>
/// Checks of <c>--colors</c> and <c>--palette</c>: the spellings they accept, what they refuse, and
/// that a refusal says which words are allowed rather than just "unknown option".
/// </summary>
public class ColorOptionParsingTests
{
    [Theory]
    [InlineData("auto", ColorMode.Auto)]
    [InlineData("truecolor", ColorMode.TrueColor)]
    [InlineData("indexed", ColorMode.Indexed)]
    public void TheThreeDocumentedModesParse(string value, ColorMode expected)
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--colors", value]);

        Assert.False(args.HasError);
        Assert.Equal(expected, args.Colors);
    }

    [Theory]
    [InlineData("--colors=truecolor")]
    [InlineData("--colours=truecolor")] // the British spelling of the option name
    [InlineData("--colors=TrueColor")]  // values are case insensitive
    [InlineData("--colors=TRUECOLOUR")]
    [InlineData("--colors=24bit")]
    [InlineData("--colors= truecolor ")]
    public void TheEqualsSpellingTheOptionAliasAndTheValueAliasesAllWork(string argument)
    {
        CommandLineArgs args = CommandLineArgs.Parse([argument]);

        Assert.False(args.HasError);
        Assert.Equal(ColorMode.TrueColor, args.Colors);
    }

    [Fact]
    public void AnAbsentOptionIsNullRatherThanAuto()
    {
        // The difference is load bearing: null defers to the saved setting, Auto overrides it.
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot"]);

        Assert.Null(args.Colors);
        Assert.Null(args.PalettePath);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("none")]      // there is no "no colour" mode; NO_COLOR means indexed
    [InlineData("256")]       // 256-colour is deliberately not a mode
    [InlineData("1")]         // the enum's numeric values must not be a back door
    [InlineData("Indexed16")] // the ColorDepth spelling, not the option's
    public void AnUnknownModeIsRejectedWithTheAllowedWords(string value)
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--colors", value]);

        Assert.True(args.HasError);
        Assert.Equal("--colors expects auto, truecolor or indexed", args.Error);
    }

    [Theory]
    [InlineData("--colors")]
    [InlineData("--palette")]
    public void AColourOptionMissingItsValueIsAnError(string option)
    {
        CommandLineArgs args = CommandLineArgs.Parse([option]);

        Assert.True(args.HasError);
        Assert.Contains("needs a value", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--palette", "vga.json")]
    [InlineData("--palette=vga.json", null)]
    public void ThePaletteFileIsTakenVerbatim(string first, string? second)
    {
        string[] argv = second is null ? [first] : [first, second];

        CommandLineArgs args = CommandLineArgs.Parse(argv);

        Assert.False(args.HasError);
        Assert.Equal("vga.json", args.PalettePath);
    }

    [Fact]
    public void ThePaletteFileIsNotValidatedAtParseTime()
    {
        // Loading is where a missing file is dealt with, and it falls back rather than failing.
        CommandLineArgs args = CommandLineArgs.Parse(["--palette", "C:/no/such/palette.json"]);

        Assert.False(args.HasError);
        Assert.Equal("C:/no/such/palette.json", args.PalettePath);
    }

    [Fact]
    public void TheColourOptionsSitAlongsideTheRestOfTheLine()
    {
        CommandLineArgs args = CommandLineArgs.Parse(
        [
            "--left", "L", "--theme", "t.json", "--colors", "indexed",
            "--palette", "p.json", "--screenshot", "--ansi",
        ]);

        Assert.False(args.HasError);
        Assert.Equal("L", args.LeftPath);
        Assert.Equal("t.json", args.ThemePath);
        Assert.Equal(ColorMode.Indexed, args.Colors);
        Assert.Equal("p.json", args.PalettePath);
        Assert.True(args.Screenshot);
        Assert.True(args.Ansi);
    }

    [Theory]
    [InlineData("auto", ColorMode.Auto)]
    [InlineData("truecolor", ColorMode.TrueColor)]
    [InlineData("indexed", ColorMode.Indexed)]
    public void TheSettingsFileAndTheCommandLineAgreeOnTheSpelling(string text, ColorMode expected)
    {
        // The settings converter reads through the same parser, so the two cannot drift apart.
        Assert.True(CommandLineArgs.TryParseColorMode(text, out ColorMode mode));
        Assert.Equal(expected, mode);
        Assert.Equal(text, ColorModeJsonConverter.ToText(expected));
    }
}

/// <summary>
/// Checks of the precedence <see cref="Application.ResolveColorDepth(CommandLineArgs, Settings)"/>
/// applies: an explicit <c>--colors</c>, then <c>NO_COLOR</c>, then the saved setting, then the
/// terminal probes.
/// </summary>
public class ColorDepthResolutionTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    private static Func<string, string?> Env(params (string Name, string Value)[] entries) =>
        name => entries.FirstOrDefault(e => e.Name == name).Value;

    private static ColorDepth Resolve(
        CommandLineArgs args,
        Settings settings,
        Func<string, string?>? environment = null,
        bool outputRedirected = false,
        ColorDepth platformDefault = ColorDepth.Indexed16) =>
        Application.ResolveColorDepth(
            args,
            settings,
            environment ?? NoEnvironment,
            outputRedirected,
            platformDefault);

    [Theory]
    [InlineData(ColorMode.TrueColor, ColorDepth.TrueColor)]
    [InlineData(ColorMode.Indexed, ColorDepth.Indexed16)]
    public void AnExplicitOptionWinsOverTheSavedSetting(ColorMode option, ColorDepth expected)
    {
        var settings = new Settings
        {
            Colors = option == ColorMode.TrueColor ? ColorMode.Indexed : ColorMode.TrueColor,
        };

        Assert.Equal(expected, Resolve(new CommandLineArgs { Colors = option }, settings));
    }

    [Theory]
    [InlineData(ColorMode.TrueColor, ColorDepth.TrueColor)]
    [InlineData(ColorMode.Indexed, ColorDepth.Indexed16)]
    public void AnExplicitOptionWinsOverNoColorInBothDirections(ColorMode option, ColorDepth expected)
    {
        // --colors is the escape hatch in both directions: someone who types it has decided, and
        // NO_COLOR is an ambient declaration rather than an instruction about this run.
        ColorDepth depth = Resolve(
            new CommandLineArgs { Colors = option },
            new Settings(),
            Env(("NO_COLOR", "1"), ("COLORTERM", "truecolor")));

        Assert.Equal(expected, depth);
    }

    [Fact]
    public void NoColorBeatsTheSavedSettingAndTheDetection()
    {
        var settings = new Settings { Colors = ColorMode.TrueColor };

        Assert.Equal(
            ColorDepth.Indexed16,
            Resolve(new CommandLineArgs(), settings, Env(("NO_COLOR", "1"), ("WT_SESSION", "abc"))));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]     // present and non-empty is the whole test, whatever the value
    [InlineData("false")]
    [InlineData(" ")]
    public void AnyNonEmptyNoColorCounts(string value) =>
        Assert.Equal(
            ColorDepth.Indexed16,
            Resolve(
                new CommandLineArgs(),
                new Settings(),
                Env(("NO_COLOR", value), ("COLORTERM", "truecolor")),
                platformDefault: ColorDepth.TrueColor));

    [Fact]
    public void AnEmptyNoColorIsNotSet() =>
        Assert.Equal(
            ColorDepth.TrueColor,
            Resolve(new CommandLineArgs(), new Settings(), Env(("NO_COLOR", string.Empty), ("COLORTERM", "truecolor"))));

    [Theory]
    [InlineData(ColorMode.TrueColor, ColorDepth.TrueColor)]
    [InlineData(ColorMode.Indexed, ColorDepth.Indexed16)]
    public void TheSavedSettingIsUsedWhenTheOptionIsAbsent(ColorMode saved, ColorDepth expected)
    {
        ColorDepth depth = Resolve(
            new CommandLineArgs(),
            new Settings { Colors = saved },
            platformDefault: saved == ColorMode.TrueColor ? ColorDepth.Indexed16 : ColorDepth.TrueColor);

        Assert.Equal(expected, depth);
    }

    [Fact]
    public void AnExplicitAutoOverridesTheSavedSettingAndDetectsInstead()
    {
        var settings = new Settings { Colors = ColorMode.Indexed };

        // "--colors auto" is how one run asks for detection despite a saved preference.
        Assert.Equal(
            ColorDepth.TrueColor,
            Resolve(new CommandLineArgs { Colors = ColorMode.Auto }, settings, Env(("COLORTERM", "truecolor"))));

        Assert.Equal(ColorDepth.Indexed16, Resolve(new CommandLineArgs(), settings));
    }

    [Fact]
    public void WithNothingSaidAtAllTheDetectorDecides()
    {
        Assert.Equal(
            ColorDepth.TrueColor,
            Resolve(new CommandLineArgs(), new Settings(), Env(("WT_SESSION", "1"))));

        Assert.Equal(
            ColorDepth.Indexed16,
            Resolve(new CommandLineArgs(), new Settings(), Env(("TERM", "xterm-256color"))));

        Assert.Equal(
            ColorDepth.TrueColor,
            Resolve(new CommandLineArgs(), new Settings(), platformDefault: ColorDepth.TrueColor));
    }

    [Fact]
    public void RedirectedOutputStaysIndexedUnlessAskedOtherwise()
    {
        Assert.Equal(
            ColorDepth.Indexed16,
            Resolve(
                new CommandLineArgs(),
                new Settings(),
                Env(("WT_SESSION", "1")),
                outputRedirected: true,
                platformDefault: ColorDepth.TrueColor));

        // ...but "--screenshot --ansi > frame.txt" is an explicit request, so --colors still wins.
        Assert.Equal(
            ColorDepth.TrueColor,
            Resolve(
                new CommandLineArgs { Screenshot = true, Ansi = true, Colors = ColorMode.TrueColor },
                new Settings(),
                NoEnvironment,
                outputRedirected: true));
    }

    [Fact]
    public void ResolveRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Application.ResolveColorDepth(null!, new Settings()));
        Assert.Throws<ArgumentNullException>(() => Application.ResolveColorDepth(new CommandLineArgs(), null!));
        Assert.Throws<ArgumentNullException>(
            () => Application.ResolveColorDepth(new CommandLineArgs(), new Settings(), null!, false, ColorDepth.Indexed16));
    }
}

/// <summary>
/// Checks of the palette override: the option beats the setting, and an unusable file falls back to
/// the built-in table rather than failing the start-up.
/// </summary>
public class PaletteOptionTests
{
    [Fact]
    public void TheOptionBeatsTheSetting()
    {
        using var dir = new TempDir();
        string fromOption = dir.File("option.json");
        string fromSetting = dir.File("setting.json");

        File.WriteAllText(fromOption, """{ "name": "option", "colors": { "DarkBlue": "#123456" } }""");
        File.WriteAllText(fromSetting, """{ "name": "setting", "colors": { "DarkBlue": "#654321" } }""");

        Palette palette = Application.ResolvePalette(
            new CommandLineArgs { PalettePath = fromOption },
            new Settings { PalettePath = fromSetting });

        Assert.Equal("option", palette.Name);
        Assert.Equal("#123456", palette[ConsoleColor.DarkBlue].ToHex());
    }

    [Fact]
    public void TheSettingIsUsedWhenTheOptionIsAbsent()
    {
        using var dir = new TempDir();
        string path = dir.File("setting.json");
        File.WriteAllText(path, """{ "name": "setting", "colors": { "DarkBlue": "#654321" } }""");

        Palette palette = Application.ResolvePalette(new CommandLineArgs(), new Settings { PalettePath = path });

        Assert.Equal("#654321", palette[ConsoleColor.DarkBlue].ToHex());
    }

    [Fact]
    public void NothingConfiguredMeansTheTableFarInstalls() =>
        Assert.Same(Palette.WindowsNt, Application.ResolvePalette(new CommandLineArgs(), new Settings()));

    [Theory]
    [InlineData("C:/no/such/palette.json")]
    [InlineData("")]
    public void AnUnusableFileFallsBackRatherThanThrowing(string path) =>
        Assert.Same(
            Palette.Default,
            Application.ResolvePalette(new CommandLineArgs { PalettePath = path }, new Settings()));

    [Fact]
    public void AMalformedFileFallsBackToo()
    {
        using var dir = new TempDir();
        string path = dir.File("broken.json");
        File.WriteAllText(path, "{ not json at all");

        Assert.Same(
            Palette.Default,
            Application.ResolvePalette(new CommandLineArgs { PalettePath = path }, new Settings()));
    }

    [Fact]
    public void ResolvePaletteRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Application.ResolvePalette(null!, new Settings()));
        Assert.Throws<ArgumentNullException>(() => Application.ResolvePalette(new CommandLineArgs(), null!));
    }
}

/// <summary>
/// Checks that the two colour preferences survive the settings file, and that a mistyped colour word
/// cannot take the rest of the file down with it.
/// </summary>
public class ColorSettingsPersistenceTests
{
    [Fact]
    public void TheDefaultsAreAutoAndTheBuiltInPalette()
    {
        var settings = new Settings();

        Assert.Equal(ColorMode.Auto, settings.Colors);
        Assert.Null(settings.PalettePath);
    }

    [Theory]
    [InlineData(ColorMode.Auto, "auto")]
    [InlineData(ColorMode.TrueColor, "truecolor")]
    [InlineData(ColorMode.Indexed, "indexed")]
    public void TheModeIsWrittenAsTheWordTheOptionAccepts(ColorMode mode, string word)
    {
        string json = new Settings { Colors = mode }.ToJson();

        Assert.Contains($"\"colors\": \"{word}\"", json, StringComparison.Ordinal);
        Assert.Equal(mode, Settings.FromJson(json).Colors);
    }

    [Fact]
    public void BothEntriesSurviveAFileRoundTrip()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");

        var original = new Settings { Colors = ColorMode.TrueColor, PalettePath = "/palettes/vga.json" };
        Assert.True(original.SaveTo(path));

        var loaded = Settings.LoadFrom(path);
        Assert.Equal(ColorMode.TrueColor, loaded.Colors);
        Assert.Equal("/palettes/vga.json", loaded.PalettePath);
    }

    [Theory]
    [InlineData("\"nonsense\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("null")]
    public void AnUnreadableModeFallsBackToAutoWithoutLosingTheRestOfTheFile(string value)
    {
        var settings = Settings.FromJson($$"""{ "colors": {{value}}, "numericSort": true }""");

        Assert.Equal(ColorMode.Auto, settings.Colors);
        Assert.True(settings.NumericSort);
    }

    [Fact]
    public void TheModeIsReadCaseInsensitivelyAndTolerantly()
    {
        Assert.Equal(ColorMode.TrueColor, Settings.FromJson("""{ "colors": "TrueColor" }""").Colors);
        Assert.Equal(ColorMode.TrueColor, Settings.FromJson("""{ "COLORS": "24bit" }""").Colors);
        Assert.Equal(ColorMode.Indexed, Settings.FromJson("""{ "colors": " INDEXED " }""").Colors);
    }

    [Fact]
    public void APartialFileLeavesTheColourEntriesAtTheirDefaults()
    {
        var settings = Settings.FromJson("""{ "numericSort": true }""");

        Assert.Equal(ColorMode.Auto, settings.Colors);
        Assert.Null(settings.PalettePath);
    }
}

/// <summary>
/// Checks that <c>--screenshot --ansi</c> prints what the live terminal is sent, rather than always
/// falling back to the indexed slots - which is what makes the two colour options inspectable
/// without starting the interactive shell.
/// </summary>
public class ScreenshotColorTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void AnsiFollowsTheTerminalsDepth()
    {
        using var tree = new ShellTree("screenshot-depth");

        using Application trueColor = Build(tree.Root, ColorDepth.TrueColor, Palette.ClassicVga);
        string rgb = Program.RenderFrame(trueColor, new CommandLineArgs { Ansi = true });

        Assert.Contains(Esc + "[38;2;", rgb, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc + "[96;44m", rgb, StringComparison.Ordinal);

        using Application indexed = Build(tree.Root, ColorDepth.Indexed16, Palette.ClassicVga);
        string slots = Program.RenderFrame(indexed, new CommandLineArgs { Ansi = true });

        Assert.Contains(Esc + "[96;44m", slots, StringComparison.Ordinal);
        Assert.DoesNotContain("38;2;", slots, StringComparison.Ordinal);
    }

    [Fact]
    public void AnsiFollowsThePaletteTheOptionAskedFor()
    {
        using var dir = new TempDir();
        string path = dir.File("odd.json");
        File.WriteAllText(path, """{ "name": "odd", "colors": { "DarkBlue": "#123456" } }""");

        var args = new CommandLineArgs
        {
            Screenshot = true,
            Ansi = true,
            Colors = ColorMode.TrueColor,
            PalettePath = path,
        };

        var settings = new Settings();
        Assert.Equal(ColorDepth.TrueColor, Application.ResolveColorDepth(args, settings, _ => null, true, ColorDepth.Indexed16));

        using var tree = new ShellTree("screenshot-palette");
        using Application app = Build(tree.Root, ColorDepth.TrueColor, Application.ResolvePalette(args, settings));

        // The panels are drawn on DarkBlue, so the file's value has to appear as a background.
        Assert.Contains("48;2;18;52;86", Program.RenderFrame(app, args), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnsiTheFrameIsStillPlainTextWhateverTheDepth()
    {
        using var tree = new ShellTree("screenshot-plain");
        using Application app = Build(tree.Root, ColorDepth.TrueColor, Palette.ClassicVga);

        string frame = Program.RenderFrame(app, new CommandLineArgs { Screenshot = true });

        Assert.DoesNotContain(Esc, frame, StringComparison.Ordinal);
        Assert.Contains("Bytes:", frame, StringComparison.Ordinal);
    }

    private static Application Build(string root, ColorDepth depth, Palette palette)
    {
        Terminal terminal = Terminal.Create(100, 30, depth, palette);
        var app = new Application(terminal, new Settings { ShowClock = false }, Theme.FarDefault(), input: null);

        app.Initialize(new CommandLineArgs { LeftPath = root, RightPath = root });
        app.Layout();
        app.DrawFrame();
        return app;
    }
}
