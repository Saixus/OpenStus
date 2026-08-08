using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Tests;

public class ThemeDefaultsTests
{
    private static CellStyle Style(ConsoleColor fg, ConsoleColor bg) => new(fg, bg);

    [Fact]
    public void FarDefaultMatchesThePanelPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelBox);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelBoxActive);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelTitle);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.PanelTitleActive);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.PanelColumnTitle);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelText);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelDirectory);
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.DarkBlue), t.PanelHidden);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelExecutable);
        Assert.Equal(Style(ConsoleColor.Magenta, ConsoleColor.DarkBlue), t.PanelArchive);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.PanelSelectedFile);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.PanelCursor);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Cyan), t.PanelCursorSelected);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelStatus);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelStatusFile);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.PanelTotals);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelScrollBar);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.PanelEmpty);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.PanelDriveInfo);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.QuickSearch);
    }

    [Fact]
    public void FarDefaultMatchesTheScreenChromePalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.Desktop);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.Clock);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.KeyBarNum);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.KeyBarText);
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.Black), t.KeyBarBackground);
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.DarkBlue), t.CommandLinePrefix);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.CommandLineText);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.CommandLineSelected);
    }

    [Fact]
    public void FarDefaultMatchesTheMenuPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.MenuBarText);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Cyan), t.MenuBarHighlight);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.MenuBarSelected);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Black), t.MenuBarSelectedHighlight);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.MenuBox);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.MenuTitle);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.MenuText);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Cyan), t.MenuHighlight);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.Black), t.MenuSelected);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.Black), t.MenuSelectedHighlight);
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.Cyan), t.MenuDisabled);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.MenuSeparator);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.MenuScroll);
    }

    [Fact]
    public void FarDefaultMatchesTheDialogPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogBox);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogBoxTitle);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogText);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Gray), t.DialogHighlight);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.DialogEdit);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkBlue), t.DialogEditSelected);
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.Gray), t.DialogEditDisabled);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogButton);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Gray), t.DialogButtonHighlight);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.DialogButtonSelected);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Cyan), t.DialogButtonSelectedHighlight);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.DialogListText);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Gray), t.DialogListHighlight);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.DialogListSelected);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Cyan), t.DialogListSelectedHighlight);
    }

    [Fact]
    public void FarDefaultMatchesTheWarningDialogPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogBox);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogBoxTitle);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogText);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkRed), t.WarnDialogHighlight);
        Assert.Equal(Style(ConsoleColor.White, ConsoleColor.DarkRed), t.WarnDialogButton);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkRed), t.WarnDialogButtonHighlight);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Gray), t.WarnDialogButtonSelected);
        Assert.Equal(Style(ConsoleColor.DarkRed, ConsoleColor.Gray), t.WarnDialogButtonSelectedHighlight);
    }

    [Fact]
    public void FarDefaultMatchesTheViewerEditorAndProgressPalette()
    {
        var t = Theme.FarDefault();
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.DarkBlue), t.ViewerText);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.ViewerSelected);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.ViewerStatus);
        Assert.Equal(Style(ConsoleColor.Yellow, ConsoleColor.DarkBlue), t.ViewerArrows);
        Assert.Equal(Style(ConsoleColor.Gray, ConsoleColor.DarkBlue), t.EditorText);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.EditorSelected);
        Assert.Equal(Style(ConsoleColor.Black, ConsoleColor.Cyan), t.EditorStatus);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.EditorScroll);
        Assert.Equal(Style(ConsoleColor.Cyan, ConsoleColor.DarkBlue), t.ProgressBar);
        Assert.Equal(Style(ConsoleColor.DarkGray, ConsoleColor.DarkBlue), t.ProgressBarEmpty);
    }

    [Fact]
    public void EveryContractMemberIsAddressableAndTheTableIsComplete()
    {
        Assert.Equal(74, Theme.Slots.Count);
        Assert.Equal(74, Theme.Slots.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

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
        Assert.Contains("\"PanelTitleActive\": \"Black on Cyan\"", json, StringComparison.Ordinal);
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
}
