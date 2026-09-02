using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Tests;

public class ThemeDefaultsTests
{
    private static CellStyle Style(ConsoleColor fg, ConsoleColor bg) => new(fg, bg);

    // Every expectation below is transcribed from far/palette.cpp; the line number is quoted so a
    // failure points straight at the authority rather than at somebody's screenshot.
    [Fact]
    public void FarDefaultMatchesThePanelPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelBox);           // :132
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelBoxActive);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelTitle);         // :82
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.PanelTitleActive);  // :83
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.PanelColumnTitle); // :84
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelText);          // :76
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelDirectory);
        Assert.Equal(Style(ConsoleColor.DarkCyan, ConsoleColor.DarkBlue), t.PanelHidden);   // Highlight: hidden/system
        Assert.Equal(Style(ConsoleColor.Green, ConsoleColor.DarkBlue), t.PanelExecutable);  // Highlight: *.exe group
        Assert.Equal(Style(ConsoleColor.Magenta, ConsoleColor.DarkBlue), t.PanelArchive);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.PanelSelectedFile);   // :77
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.PanelCursor);          // :80
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkCyan), t.PanelCursorSelected); // :81
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelStatus);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelStatusFile);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelTotals);        // :85
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.PanelSelectedTotals); // :86
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelScrollBar);     // :130
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelEmpty);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelDriveInfo);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.QuickSearch);
    }

    [Fact]
    public void FarDefaultMatchesTheScreenChromePalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.Desktop);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.Clock);          // :115
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.KeyBarNum);          // :111
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.KeyBarText);     // :112
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.KeyBarBackground);   // :113
        // :140 / :114 are Far's inherit-the-console sentinel: the command line is a strip of the
        // bare terminal, light grey on black - which is what the user's Far screenshots show.
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.CommandLinePrefix);       // :140
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.CommandLineText);         // :114
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.CommandLineSelected); // :135
    }

    [Fact]
    public void FarDefaultMatchesTheMenuPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.MenuBarText);            // :72
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkCyan), t.MenuBarHighlight);      // :74
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.MenuBarSelected);           // :73
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Black), t.MenuBarSelectedHighlight); // :75
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkCyan), t.MenuBox);                // :70
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkCyan), t.MenuTitle);              // :71
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkCyan), t.MenuText);               // :66
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkCyan), t.MenuHighlight);         // :68
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.MenuSelected);              // :67
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Black), t.MenuSelectedHighlight);    // :69
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.DarkCyan), t.MenuDisabled);        // :147
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkCyan), t.MenuSeparator);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkCyan), t.MenuScroll);             // :138
    }

    [Fact]
    public void FarDefaultMatchesTheDialogPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogBox);                    // :89
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogBoxTitle);               // :90
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogText);                   // :87
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Gray), t.DialogHighlight);             // :88
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.DialogEdit);               // :92
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.DialogEditSelected);          // :134
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.DarkCyan), t.DialogEditDisabled);    // :142
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogButton);                 // :93
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Gray), t.DialogButtonHighlight);       // :95
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.DialogButtonSelected);     // :94
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkCyan), t.DialogButtonSelectedHighlight); // :96
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogListText);               // :97
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Gray), t.DialogListHighlight);         // :99
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.DialogListSelected);          // :98
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Black), t.DialogListSelectedHighlight); // :100
    }

    [Fact]
    public void FarDefaultMatchesTheWarningDialogPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogBox);            // :103
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogBoxTitle);       // :104
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogText);           // :101
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkRed), t.WarnDialogHighlight);     // :102
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogButton);         // :107
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkRed), t.WarnDialogButtonHighlight); // :109
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.WarnDialogButtonSelected);    // :108
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Gray), t.WarnDialogButtonSelectedHighlight); // :110
    }

    [Fact]
    public void FarDefaultMatchesTheViewerEditorAndProgressPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.ViewerText);         // :116
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.ViewerSelected);    // :117
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.ViewerStatus);      // :118
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.ViewerArrows);     // :136
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.EditorText);         // :119
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.EditorSelected);    // :120
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.DarkCyan), t.EditorStatus);      // :121
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.EditorScroll);       // :193
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.ProgressBar);
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.DarkBlue), t.ProgressBarEmpty);
    }

    /// <summary>
    /// The root defect this table was corrected for. Far's background macro B_CYAN expands to
    /// C_CYAN = index 3 = DarkCyan; only the foreground F_LIGHTCYAN is index 11 = Cyan. Emitting
    /// bright cyan as a background is SGR 106 where Far writes SGR 46, and no palette can undo it -
    /// so no entry may ever use the bright slot as a background again.
    /// </summary>
    [Fact]
    public void NoEntryUsesTheBrightCyanSlotAsABackground()
    {
        var t = Theme.FarDefault();
        foreach (var slot in Theme.Slots)
        {
            Assert.NotEqual(ConsoleColor.Cyan, slot.Get(t).Bg);
        }

        // The bright slot is still very much in use - as a foreground, which is where Far puts it.
        Assert.Contains(Theme.Slots, s => s.Get(t).Fg == ConsoleColor.Cyan);
    }

    /// <summary>
    /// COL_PANELBOX and COL_PANELTEXT are different enum constants carrying the same default
    /// attribute (palette.cpp :132 and :76), as are the title, the totals line and the scroll bar.
    /// Far's panel really is near monochrome; inventing a dimmer frame to "fix" the look would be
    /// a departure from Far, not a correction.
    /// </summary>
    [Fact]
    public void ThePanelFrameAndTheFileTextShareOneAttributeOnPurpose()
    {
        var t = Theme.FarDefault();
        var one = new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue);

        Assert.Equal(one, t.PanelBox);
        Assert.Equal(one, t.PanelBoxActive);
        Assert.Equal(one, t.PanelText);
        Assert.Equal(one, t.PanelTitle);
        Assert.Equal(one, t.PanelTotals);
        Assert.Equal(one, t.PanelScrollBar);
        Assert.Equal(one, t.PanelEmpty);
    }

    /// <summary>
    /// The command line is a strip of terminal and sits on the console's black, exactly as Far
    /// draws it - light grey on black, never on the panel blue - and every colour the typed
    /// command can take shares that black, so a coloured word never shows up as a coloured box.
    /// </summary>
    [Fact]
    public void TheCommandLineSitsOnTheBlackOfTheTerminal()
    {
        var t = Theme.FarDefault();
        Assert.Equal(ConsoleColor.Black, t.CommandLineText.Bg);
        Assert.Equal(ConsoleColor.Black, t.CommandLinePrefix.Bg);
        Assert.NotEqual(t.PanelText.Bg, t.CommandLineText.Bg);

        foreach (CellStyle style in new[] { t.CommandLineCommand, t.CommandLineOption, t.CommandLineString, t.CommandLineVariable, t.CommandLineSuggestion })
        {
            Assert.Equal(ConsoleColor.Black, style.Bg);
            Assert.NotEqual(ConsoleColor.Black, style.Fg);
        }
    }

    /// <summary>Far gives Keybar.Num and Keybar.Background the same attribute (:111 and :113).</summary>
    [Fact]
    public void TheKeyBarNumberAndBackgroundShareOneAttribute()
    {
        var t = Theme.FarDefault();
        Assert.Equal(t.KeyBarBackground, t.KeyBarNum);
        Assert.NotEqual(t.KeyBarText, t.KeyBarNum);
    }

    [Fact]
    public void EveryContractMemberIsAddressableAndTheTableIsComplete()
    {
        Assert.Equal(85, Theme.Slots.Count);
        Assert.Equal(85, Theme.Slots.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var t = Theme.FarDefault();
        foreach (var slot in Theme.Slots)
        {
            Assert.True(t.TryGetByName(slot.Name, out var style));
            Assert.Equal(slot.Get(t), style);
        }
    }

    [Fact]
    public void ANewThemeAlreadyCarriesTheFarPalette()
    {
        var t = new Theme();
        Assert.Equal("Far Default", t.Name);
        Assert.Equal(Theme.FarDefault().PanelCursor, t.PanelCursor);
    }

    [Fact]
    public void NameLookupIgnoresCaseAndSeparators()
    {
        var t = Theme.FarDefault();
        var yellowOnBlack = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black);

        Assert.True(t.TrySetByName("panel.title.active", yellowOnBlack));
        Assert.Equal(yellowOnBlack, t.PanelTitleActive);

        Assert.True(t.TrySetByName("PANEL_CURSOR", yellowOnBlack));
        Assert.Equal(yellowOnBlack, t.PanelCursor);

        Assert.False(t.TrySetByName("NoSuchEntry", yellowOnBlack));
        Assert.False(t.TryGetByName("NoSuchEntry", out _));
    }
}

/// <summary>
/// The theme carries an RGB palette, and that - not a second colour table - is what differs
/// between the built-in themes and between the two colour depths.
/// </summary>
public class ThemePaletteTests
{
    internal static void AssertSamePalette(Palette expected, Palette actual)
    {
        for (int i = 0; i < Palette.Size; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    [Fact]
    public void FarDefaultCarriesTheClassicVgaPalette()
    {
        var t = Theme.FarDefault();
        AssertSamePalette(Palette.ClassicVga, t.Palette);
        Assert.Equal("Classic VGA", t.Palette.Name);
    }

    /// <summary>
    /// The second built-in theme exists to make the point that the palette is the variable: its
    /// 74 entries are identical to Far Default's, and only the RGB behind them differs.
    /// </summary>
    [Fact]
    public void FarNtIsTheSameTableOverADifferentPalette()
    {
        var far = Theme.FarDefault();
        var nt = Theme.FarNt();

        Assert.Equal("Far NT", nt.Name);
        foreach (var slot in Theme.Slots)
        {
            Assert.Equal(slot.Get(far), slot.Get(nt));
        }

        AssertSamePalette(Palette.WindowsNt, nt.Palette);
        Assert.Equal("#000080", nt.Palette[ConsoleColor.DarkBlue].ToHex());
        Assert.Equal("#0000AA", far.Palette[ConsoleColor.DarkBlue].ToHex());
    }

    /// <summary>
    /// The dominant panel pair is 78% of the screen, so it is the one that decides whether the UI
    /// reads as crisp or blended. Both built-ins have to clear the strictest WCAG level on it,
    /// where Windows Terminal's own scheme manages only 4.73:1.
    /// </summary>
    [Fact]
    public void TheDominantPanelPairIsHighContrastInBothBuiltIns()
    {
        Assert.True(Theme.FarDefault().Palette.ContrastOf(Theme.FarDefault().PanelText) > 10.0);
        Assert.True(Theme.FarNt().Palette.ContrastOf(Theme.FarNt().PanelText) > 12.0);

        // The reference case: the same style under the terminal's default scheme.
        Assert.True(Palette.Campbell.ContrastOf(Theme.FarDefault().PanelText) < 5.0);
    }

    /// <summary>
    /// The cursor bar was over-bright, not washed out - the opposite sign to the panel's problem.
    /// Black on the bright slot is a harsher bar than Far ever draws; B_CYAN is what tames it.
    /// </summary>
    [Fact]
    public void TheCursorBarIsNoLongerOverBright()
    {
        var t = Theme.FarDefault();
        double corrected = t.Palette.ContrastOf(t.PanelCursor);
        double bright = Palette.ContrastRatio(t.Palette[ConsoleColor.Black], t.Palette[ConsoleColor.Cyan]);

        Assert.True(
            corrected < bright,
            $"expected the corrected cursor ({corrected:F2}) to be softer than the bright one ({bright:F2})");
    }

    [Fact]
    public void AssigningANullPaletteThrows()
    {
        var t = Theme.FarDefault();
        Assert.Throws<ArgumentNullException>(() => t.Palette = null!);
    }

    [Theory]
    [InlineData("Far Default")]
    [InlineData("far default")]
    [InlineData("far_default")]
    [InlineData("Far")]
    [InlineData("default")]
    public void BuiltInLookupFindsFarDefault(string name)
    {
        Assert.True(Theme.TryGetBuiltIn(name, out var t));
        Assert.Equal("Far Default", t.Name);
    }

    [Theory]
    [InlineData("Far NT")]
    [InlineData("farnt")]
    [InlineData("NT")]
    [InlineData("windows-nt")]
    public void BuiltInLookupFindsFarNt(string name)
    {
        Assert.True(Theme.TryGetBuiltIn(name, out var t));
        Assert.Equal("Far NT", t.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Solarized")]
    public void BuiltInLookupRejectsAnythingElse(string? name) => Assert.False(Theme.TryGetBuiltIn(name, out _));

    [Fact]
    public void TheBuiltInNamesAreOffered()
    {
        Assert.Equal(2, Theme.BuiltInNames.Count);
        Assert.Equal("Far Default", Theme.BuiltInNames[0]);
        Assert.Equal("Far NT", Theme.BuiltInNames[1]);

        foreach (string name in Theme.BuiltInNames)
        {
            Assert.True(Theme.TryGetBuiltIn(name, out var t));
            Assert.Equal(name, t.Name);
        }
    }

    [Theory]
    [InlineData("WindowsNt")]
    [InlineData("windows nt")]
    [InlineData("NT")]
    [InlineData("far")]
    public void NamedPaletteLookupFindsWindowsNt(string name)
    {
        Assert.True(ThemePalette.TryGetBuiltIn(name, out var p));
        Assert.Equal("#00FFFF", p[ConsoleColor.Cyan].ToHex());
    }

    [Theory]
    [InlineData("ClassicVga", "#55FFFF")]
    [InlineData("vga", "#55FFFF")]
    [InlineData("dos", "#55FFFF")]
    [InlineData("Campbell", "#61D6D6")]
    public void NamedPaletteLookupFindsTheOthers(string name, string cyan)
    {
        Assert.True(ThemePalette.TryGetBuiltIn(name, out var p));
        Assert.Equal(cyan, p[ConsoleColor.Cyan].ToHex());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Nord")]
    public void NamedPaletteLookupRejectsAnythingElse(string? name) =>
        Assert.False(ThemePalette.TryGetBuiltIn(name, out _));
}

public class ThemeColorParsingTests
{
    [Theory]
    [InlineData("Cyan", ConsoleColor.Cyan)]
    [InlineData("cyan", ConsoleColor.Cyan)]
    [InlineData("  DarkBlue  ", ConsoleColor.DarkBlue)]
    [InlineData("dark blue", ConsoleColor.DarkBlue)]
    [InlineData("dark_blue", ConsoleColor.DarkBlue)]
    [InlineData("dark-blue", ConsoleColor.DarkBlue)]
    [InlineData("Brown", ConsoleColor.DarkYellow)]
    [InlineData("LightGray", ConsoleColor.Gray)]
    [InlineData("Grey", ConsoleColor.Gray)]
    [InlineData("LightCyan", ConsoleColor.Cyan)]
    [InlineData("Purple", ConsoleColor.DarkMagenta)]
    [InlineData("C_LIGHTCYAN", ConsoleColor.Cyan)]
    [InlineData("0", ConsoleColor.Black)]
    [InlineData("11", ConsoleColor.Cyan)]
    [InlineData("15", ConsoleColor.White)]
    public void ColourNamesAndAliasesParse(string text, ConsoleColor expected)
    {
        Assert.True(ThemeColor.TryParseColor(text, out var color));
        Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Chartreuse")]
    [InlineData("16")]
    [InlineData("99")]
    public void UnknownColoursAreRejected(string? text) => Assert.False(ThemeColor.TryParseColor(text, out _));

    [Theory]
    [InlineData("Cyan on DarkBlue")]
    [InlineData("cyan ON darkblue")]
    [InlineData("Cyan/DarkBlue")]
    [InlineData("Cyan,DarkBlue")]
    [InlineData("Cyan|DarkBlue")]
    [InlineData("Cyan:DarkBlue")]
    [InlineData("Cyan DarkBlue")]
    public void StyleStringsParse(string text)
    {
        Assert.True(ThemeColor.TryParseStyle(text, out var style));
        Assert.Equal(new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue), style);
    }

    [Fact]
    public void ASingleColourSetsOnlyTheForeground()
    {
        Assert.True(ThemeColor.TryParseStyle("Yellow", out var style));
        Assert.Equal(ConsoleColor.Yellow, style.Fg);
        Assert.Equal(CellStyle.Default.Bg, style.Bg);
    }

    [Theory]
    [InlineData("Cyan on Chartreuse")]
    [InlineData("")]
    [InlineData(null)]
    public void BadStyleStringsAreRejected(string? text) => Assert.False(ThemeColor.TryParseStyle(text, out _));

    [Fact]
    public void FormatRoundTripsThroughTryParse()
    {
        foreach (var slot in Theme.Slots)
        {
            var original = slot.Get(Theme.FarDefault());
            Assert.True(ThemeColor.TryParseStyle(ThemeColor.Format(original), out var parsed));
            Assert.Equal(original, parsed);
        }
    }
}

public class ThemeJsonTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "oc-theme-tests-" + Guid.NewGuid().ToString("N"));

    public ThemeJsonTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best effort.
        }

        GC.SuppressFinalize(this);
    }

    private string TempFile(string name) => Path.Combine(_dir, name);

    [Fact]
    public void SaveThenLoadRoundTripsEveryEntry()
    {
        var original = Theme.FarDefault();
        original.Name = "Round Trip";
        original.PanelText = new CellStyle(ConsoleColor.Green, ConsoleColor.Black);
        original.WarnDialogBox = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkMagenta);
        original.ProgressBarEmpty = new CellStyle(ConsoleColor.White, ConsoleColor.DarkGray);

        string file = TempFile("round-trip.json");
        original.SaveToJson(file);

        var loaded = Theme.LoadFromJson(file);
        Assert.Equal("Round Trip", loaded.Name);
        foreach (var slot in Theme.Slots)
        {
            Assert.Equal(slot.Get(original), slot.Get(loaded));
        }
    }

    [Fact]
    public void SaveCreatesMissingDirectories()
    {
        string file = TempFile(Path.Combine("nested", "deeper", "theme.json"));
        Theme.FarDefault().SaveToJson(file);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void SavedFileContainsEveryPaletteEntryAsAReadableString()
    {
        string file = TempFile("full.json");
        Theme.FarDefault().SaveToJson(file);
        string json = File.ReadAllText(file);

        Assert.Contains("\"PanelBox\": \"Cyan on DarkBlue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"PanelTitleActive\": \"Black on DarkCyan\"", json, StringComparison.Ordinal);
        foreach (var slot in Theme.Slots)
        {
            Assert.Contains("\"" + slot.Name + "\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MissingEntriesKeepTheirFarDefault()
    {
        string file = TempFile("partial.json");
        File.WriteAllText(file, """
            {
              "name": "Partial",
              "colors": {
                "PanelText": "Green on Black"
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("Partial", t.Name);
        Assert.Equal(new CellStyle(ConsoleColor.Green, ConsoleColor.Black), t.PanelText);
        Assert.Equal(Theme.FarDefault().PanelCursor, t.PanelCursor);
        Assert.Equal(Theme.FarDefault().KeyBarText, t.KeyBarText);
    }

    [Fact]
    public void UnknownEntriesAreIgnored()
    {
        string file = TempFile("unknown.json");
        File.WriteAllText(file, """
            {
              "name": "Has Junk",
              "version": 7,
              "author": "somebody",
              "colors": {
                "PanelText": "Green on Black",
                "ThisDoesNotExist": "Red on Black",
                "AlsoBogus": { "fg": "Red" }
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("Has Junk", t.Name);
        Assert.Equal(new CellStyle(ConsoleColor.Green, ConsoleColor.Black), t.PanelText);
    }

    [Fact]
    public void ObjectFormIsAccepted()
    {
        string file = TempFile("objects.json");
        File.WriteAllText(file, """
            {
              "colors": {
                "PanelText":   { "fg": "Yellow", "bg": "DarkGreen" },
                "PanelCursor": { "foreground": "White", "background": 4 },
                "PanelTotals": [ "Magenta", "Black" ],
                "MenuText":    18
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal(new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkGreen), t.PanelText);
        Assert.Equal(new CellStyle(ConsoleColor.White, ConsoleColor.DarkRed), t.PanelCursor);
        Assert.Equal(new CellStyle(ConsoleColor.Magenta, ConsoleColor.Black), t.PanelTotals);
        Assert.Equal(new CellStyle(ConsoleColor.DarkGreen, ConsoleColor.DarkBlue), t.MenuText); // 0x12 -> fg 2, bg 1
    }

    [Fact]
    public void FlatFormWithoutAColorsBlockIsAccepted()
    {
        string file = TempFile("flat.json");
        File.WriteAllText(file, """
            {
              "Name": "Flat",
              "PanelText": "Green on Black",
              "PanelCursor": "Black on Gray"
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("Flat", t.Name);
        Assert.Equal(new CellStyle(ConsoleColor.Green, ConsoleColor.Black), t.PanelText);
        Assert.Equal(new CellStyle(ConsoleColor.Black, ConsoleColor.Gray), t.PanelCursor);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        string file = TempFile("loose.json");
        File.WriteAllText(file, """
            {
              // a hand written theme
              "colors": {
                "PanelText": "Green on Black",
              },
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal(new CellStyle(ConsoleColor.Green, ConsoleColor.Black), t.PanelText);
    }

    [Fact]
    public void AFileWithoutANameIsNamedAfterItself()
    {
        string file = TempFile("midnight.json");
        File.WriteAllText(file, """{ "colors": { "PanelText": "Green on Black" } }""");
        Assert.Equal("midnight", Theme.LoadFromJson(file).Name);
    }

    [Fact]
    public void LoadOrDefaultNeverThrows()
    {
        Assert.Equal("Far Default", Theme.LoadOrDefault(null).Name);
        Assert.Equal("Far Default", Theme.LoadOrDefault("   ").Name);
        Assert.Equal("Far Default", Theme.LoadOrDefault(TempFile("does-not-exist.json")).Name);

        string broken = TempFile("broken.json");
        File.WriteAllText(broken, "{ this is not json at all ");
        var t = Theme.LoadOrDefault(broken);
        Assert.Equal("Far Default", t.Name);
        Assert.Equal(Theme.FarDefault().PanelText, t.PanelText);
    }

    [Fact]
    public void LoadOrDefaultReadsAValidFile()
    {
        string file = TempFile("good.json");
        var saved = Theme.FarDefault();
        saved.Name = "Good";
        saved.Desktop = new CellStyle(ConsoleColor.White, ConsoleColor.DarkMagenta);
        saved.SaveToJson(file);

        var t = Theme.LoadOrDefault(file);
        Assert.Equal("Good", t.Name);
        Assert.Equal(new CellStyle(ConsoleColor.White, ConsoleColor.DarkMagenta), t.Desktop);
    }

    [Fact]
    public void LoadOrDefaultAcceptsABuiltInNameWhereAPathWouldGo()
    {
        Assert.Equal("Far NT", Theme.LoadOrDefault("Far NT").Name);
        Assert.Equal("Far Default", Theme.LoadOrDefault("far").Name);

        // A path that simply is not there still falls back rather than guessing.
        Assert.Equal("Far Default", Theme.LoadOrDefault(TempFile("nope.json")).Name);
    }

    // ---- the optional "palette" block ---------------------------------------------------------

    [Fact]
    public void SaveThenLoadRoundTripsThePaletteToo()
    {
        var original = Theme.FarNt();
        original.Name = "Round Trip";
        original.Palette = Palette.WindowsNt.With(ConsoleColor.DarkBlue, new Rgb(0x01, 0x02, 0x03));

        string file = TempFile("palette-round-trip.json");
        original.SaveToJson(file);

        var loaded = Theme.LoadFromJson(file);
        Assert.Equal("Round Trip", loaded.Name);
        Assert.Equal("#010203", loaded.Palette[ConsoleColor.DarkBlue].ToHex());
        ThemePaletteTests.AssertSamePalette(original.Palette, loaded.Palette);
        Assert.Equal(original.Palette.Name, loaded.Palette.Name);

        foreach (var slot in Theme.Slots)
        {
            Assert.Equal(slot.Get(original), slot.Get(loaded));
        }
    }

    [Fact]
    public void TheSavedPaletteBlockIsReadableHex()
    {
        string file = TempFile("palette-shape.json");
        Theme.FarDefault().SaveToJson(file);
        string json = File.ReadAllText(file);

        Assert.Contains("\"palette\"", json, StringComparison.Ordinal);
        Assert.Contains("\"DarkBlue\": \"#0000AA\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Cyan\": \"#55FFFF\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Classic VGA\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole point of the block being optional: every theme file written before it existed has
    /// no "palette" key at all, and must keep loading exactly as it did.
    /// </summary>
    [Fact]
    public void AThemeFileWithoutAPaletteBlockStillLoads()
    {
        string file = TempFile("legacy.json");
        File.WriteAllText(file, """
            {
              "name": "Legacy",
              "colors": {
                "PanelText": "Green on Black",
                "PanelCursor": "Black on Gray"
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("Legacy", t.Name);
        Assert.Equal(new CellStyle(ConsoleColor.Green, ConsoleColor.Black), t.PanelText);
        Assert.Equal(new CellStyle(ConsoleColor.Black, ConsoleColor.Gray), t.PanelCursor);
        ThemePaletteTests.AssertSamePalette(Palette.ClassicVga, t.Palette);
    }

    [Fact]
    public void APaletteBlockCanNameABuiltIn()
    {
        string file = TempFile("named-palette.json");
        File.WriteAllText(file, """{ "name": "Authentic", "palette": "WindowsNt" }""");

        var t = Theme.LoadFromJson(file);
        Assert.Equal("Authentic", t.Name);
        ThemePaletteTests.AssertSamePalette(Palette.WindowsNt, t.Palette);

        // The colour table is untouched by the palette block.
        Assert.Equal(Theme.FarDefault().PanelText, t.PanelText);
    }

    [Fact]
    public void APaletteBlockCanOverrideIndividualSlots()
    {
        string file = TempFile("slots.json");
        File.WriteAllText(file, """
            {
              "palette": {
                "name": "Deep",
                "DarkBlue": "#000033",
                "LightCyan": "#66FFFF",
                "11": "#66FFFF"
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("Deep", t.Palette.Name);
        Assert.Equal("#000033", t.Palette[ConsoleColor.DarkBlue].ToHex());
        Assert.Equal("#66FFFF", t.Palette[ConsoleColor.Cyan].ToHex());

        // Everything not mentioned keeps the ClassicVga value.
        Assert.Equal("#AA0000", t.Palette[ConsoleColor.DarkRed].ToHex());
    }

    [Fact]
    public void APaletteBlockCanLayerOnANamedBase()
    {
        string file = TempFile("based.json");
        File.WriteAllText(file, """
            {
              "palette": {
                "base": "WindowsNt",
                "colors": { "DarkBlue": "#101010" }
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("#101010", t.Palette[ConsoleColor.DarkBlue].ToHex());
        Assert.Equal("#00FFFF", t.Palette[ConsoleColor.Cyan].ToHex());     // from the NT base
        Assert.Equal("#C0C0C0", t.Palette[ConsoleColor.Gray].ToHex());     // from the NT base
    }

    [Fact]
    public void APaletteBlockAcceptsAnArrayOfSixteen()
    {
        string file = TempFile("array.json");
        File.WriteAllText(file, """
            {
              "palette": [
                "#000000", "#000080", "#008000", "#008080", "#800000", "#800080", "#808000", "#C0C0C0",
                "#808080", "#0000FF", "#00FF00", "#00FFFF", "#FF0000", "#FF00FF", "#FFFF00", "#FFFFFF"
              ]
            }
            """);

        ThemePaletteTests.AssertSamePalette(Palette.WindowsNt, Theme.LoadFromJson(file).Palette);
    }

    [Fact]
    public void AMalformedPaletteBlockIsIgnoredEntryByEntry()
    {
        string file = TempFile("junk-palette.json");
        File.WriteAllText(file, """
            {
              "palette": {
                "DarkBlue": "#000033",
                "Chartreuse": "#123456",
                "Cyan": "not a colour",
                "Gray": 7
              }
            }
            """);

        var t = Theme.LoadFromJson(file);
        Assert.Equal("#000033", t.Palette[ConsoleColor.DarkBlue].ToHex());
        Assert.Equal("#55FFFF", t.Palette[ConsoleColor.Cyan].ToHex());   // bad value, kept
        Assert.Equal("#AAAAAA", t.Palette[ConsoleColor.Gray].ToHex());   // wrong type, kept
    }

    [Theory]
    [InlineData("""{ "palette": "NoSuchPalette" }""")]
    [InlineData("""{ "palette": {} }""")]
    [InlineData("""{ "palette": null }""")]
    [InlineData("""{ "palette": 42 }""")]
    [InlineData("""{ "palette": [] }""")]
    public void AnUnusablePaletteBlockLeavesTheDefaultAlone(string json)
    {
        string file = TempFile("unusable.json");
        File.WriteAllText(file, json);
        ThemePaletteTests.AssertSamePalette(Palette.ClassicVga, Theme.LoadFromJson(file).Palette);
    }

    [Fact]
    public void APaletteBlockSurvivesLoadOrDefault()
    {
        string file = TempFile("or-default.json");
        File.WriteAllText(file, """{ "palette": "campbell" }""");
        ThemePaletteTests.AssertSamePalette(Palette.Campbell, Theme.LoadOrDefault(file).Palette);
    }
}
