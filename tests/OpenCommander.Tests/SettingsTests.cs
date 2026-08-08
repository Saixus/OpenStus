using System.Text.Json;
using OpenCommander.Core;

namespace OpenCommander.Tests;

/// <summary>
/// A scratch directory that removes itself, so settings tests never touch the real
/// <see cref="Settings.SettingsFilePath"/>.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "oc-settings-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp folder is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public class SettingsDefaultsTests
{
    [Fact]
    public void ANewInstanceCarriesTheShippingDefaults()
    {
        var s = new Settings();

        Assert.True(s.ShowHiddenFiles);
        Assert.True(s.DirectoriesFirst);
        Assert.False(s.NumericSort);
        Assert.False(s.CaseSensitiveSort);
        Assert.True(s.ShowStatusBar);
        Assert.True(s.ShowKeyBar);
        Assert.True(s.ShowClock);
        Assert.True(s.ShowCommandLine);
        Assert.True(s.ConfirmDelete);
        Assert.True(s.ConfirmOverwrite);
        Assert.True(s.UseRecycleBin);
        Assert.True(s.ClearSelectionAfterOperation);
        Assert.False(s.AutoChangeDirectory);
        Assert.Null(s.ThemePath);
    }

    [Fact]
    public void SettingsFilePathSitsUnderTheApplicationConfigFolder()
    {
        string path = Settings.SettingsFilePath;

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Path.IsPathRooted(path));
        Assert.Equal(Settings.FileName, Path.GetFileName(path));
        Assert.Equal(Settings.AppFolderName, Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(Settings.ConfigDirectory, Path.GetDirectoryName(path));
    }
}

public class SettingsJsonRoundTripTests
{
    private static Settings NonDefault() => new()
    {
        ShowHiddenFiles = false,
        DirectoriesFirst = false,
        NumericSort = true,
        CaseSensitiveSort = true,
        ShowStatusBar = false,
        ShowKeyBar = false,
        ShowClock = false,
        ShowCommandLine = false,
        ConfirmDelete = false,
        ConfirmOverwrite = false,
        UseRecycleBin = false,
        ClearSelectionAfterOperation = false,
        AutoChangeDirectory = true,
        ThemePath = "/themes/midnight.json",
    };

    private static void AssertSame(Settings expected, Settings actual)
    {
        Assert.Equal(expected.ShowHiddenFiles, actual.ShowHiddenFiles);
        Assert.Equal(expected.DirectoriesFirst, actual.DirectoriesFirst);
        Assert.Equal(expected.NumericSort, actual.NumericSort);
        Assert.Equal(expected.CaseSensitiveSort, actual.CaseSensitiveSort);
        Assert.Equal(expected.ShowStatusBar, actual.ShowStatusBar);
        Assert.Equal(expected.ShowKeyBar, actual.ShowKeyBar);
        Assert.Equal(expected.ShowClock, actual.ShowClock);
        Assert.Equal(expected.ShowCommandLine, actual.ShowCommandLine);
        Assert.Equal(expected.ConfirmDelete, actual.ConfirmDelete);
        Assert.Equal(expected.ConfirmOverwrite, actual.ConfirmOverwrite);
        Assert.Equal(expected.UseRecycleBin, actual.UseRecycleBin);
        Assert.Equal(expected.ClearSelectionAfterOperation, actual.ClearSelectionAfterOperation);
        Assert.Equal(expected.AutoChangeDirectory, actual.AutoChangeDirectory);
        Assert.Equal(expected.ThemePath, actual.ThemePath);
    }

    [Fact]
    public void EveryPropertySurvivesAFileRoundTrip()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        var original = NonDefault();

        Assert.True(original.SaveTo(path));
        Assert.True(File.Exists(path));

        AssertSame(original, Settings.LoadFrom(path));
    }

    [Fact]
    public void DefaultsSurviveAFileRoundTrip()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        var original = new Settings();

        Assert.True(original.SaveTo(path));
        AssertSame(original, Settings.LoadFrom(path));
    }

    [Fact]
    public void EveryPropertySurvivesAStringRoundTrip()
    {
        var original = NonDefault();
        AssertSame(original, Settings.FromJson(original.ToJson()));
    }

    [Fact]
    public void SaveCreatesTheContainingDirectory()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "nested", "deeper", "settings.json");

        Assert.True(new Settings().SaveTo(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TheWrittenFileIsIndentedCamelCaseJson()
    {
        string json = new Settings { NumericSort = true }.ToJson();

        Assert.Contains("\"showHiddenFiles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"numericSort\": true", json, StringComparison.Ordinal);
        Assert.Contains("\n", json, StringComparison.Ordinal);

        // Round-trippable through the plain reader as well, i.e. it really is valid JSON.
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void EveryPropertyIsWrittenEvenWhenItHoldsItsDefault()
    {
        string json = new Settings().ToJson();

        foreach (string name in new[]
                 {
                     "showHiddenFiles", "directoriesFirst", "numericSort", "caseSensitiveSort",
                     "showStatusBar", "showKeyBar", "showClock", "showCommandLine",
                     "confirmDelete", "confirmOverwrite", "useRecycleBin",
                     "clearSelectionAfterOperation", "autoChangeDirectory", "themePath",
                 })
        {
            Assert.Contains($"\"{name}\"", json, StringComparison.Ordinal);
        }
    }
}

public class SettingsToleranceTests
{
    [Fact]
    public void AMissingFileYieldsTheDefaults()
    {
        using var dir = new TempDir();
        var s = Settings.LoadFrom(dir.File("does-not-exist.json"));

        Assert.True(s.ShowHiddenFiles);
        Assert.True(s.DirectoriesFirst);
        Assert.Null(s.ThemePath);
    }

    [Fact]
    public void AMalformedFileYieldsTheDefaultsInsteadOfThrowing()
    {
        using var dir = new TempDir();
        string path = dir.File("broken.json");
        File.WriteAllText(path, "{ this is not json at all ");

        var s = Settings.LoadFrom(path);
        Assert.True(s.ShowKeyBar);
        Assert.True(s.ConfirmDelete);
    }

    [Fact]
    public void AnEmptyFileYieldsTheDefaults()
    {
        using var dir = new TempDir();
        string path = dir.File("empty.json");
        File.WriteAllText(path, "   \n");

        Assert.True(Settings.LoadFrom(path).ShowClock);
    }

    [Fact]
    public void AJsonNullFileYieldsTheDefaults()
    {
        using var dir = new TempDir();
        string path = dir.File("null.json");
        File.WriteAllText(path, "null");

        Assert.True(Settings.LoadFrom(path).ShowCommandLine);
    }

    [Fact]
    public void APartialFileKeepsTheDefaultsForEverythingElse()
    {
        using var dir = new TempDir();
        string path = dir.File("partial.json");
        File.WriteAllText(path, """{ "numericSort": true, "themePath": "x.json" }""");

        var s = Settings.LoadFrom(path);
        Assert.True(s.NumericSort);
        Assert.Equal("x.json", s.ThemePath);
        Assert.True(s.ShowHiddenFiles);      // untouched default
        Assert.True(s.UseRecycleBin);        // untouched default
        Assert.False(s.AutoChangeDirectory); // untouched default
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        using var dir = new TempDir();
        string path = dir.File("future.json");
        File.WriteAllText(path, """{ "showClock": false, "somethingFromTheFuture": 42 }""");

        Assert.False(Settings.LoadFrom(path).ShowClock);
    }

    [Fact]
    public void PropertyNamesAreCaseInsensitive()
    {
        using var dir = new TempDir();
        string path = dir.File("pascal.json");
        File.WriteAllText(path, """{ "ShowHiddenFiles": false, "DIRECTORIESFIRST": false }""");

        var s = Settings.LoadFrom(path);
        Assert.False(s.ShowHiddenFiles);
        Assert.False(s.DirectoriesFirst);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        using var dir = new TempDir();
        string path = dir.File("commented.json");
        File.WriteAllText(
            path,
            """
            {
              // hand edited
              "showKeyBar": false,
              "useRecycleBin": false,
            }
            """);

        var s = Settings.LoadFrom(path);
        Assert.False(s.ShowKeyBar);
        Assert.False(s.UseRecycleBin);
    }

    [Fact]
    public void CloneIsIndependentOfTheOriginal()
    {
        var original = new Settings { NumericSort = true, ThemePath = "a.json" };
        var copy = original.Clone();

        copy.NumericSort = false;
        copy.ThemePath = "b.json";

        Assert.True(original.NumericSort);
        Assert.Equal("a.json", original.ThemePath);
        Assert.False(copy.NumericSort);
        Assert.Equal("b.json", copy.ThemePath);
    }
}
