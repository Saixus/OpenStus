using Dvopan.Core;
using Dvopan.Files;

namespace Dvopan.Tests;

public class NaturalComparerTests
{
    [Theory]
    [InlineData("file2", "file10")]
    [InlineData("file9", "file10")]
    [InlineData("2", "10")]
    [InlineData("a", "b")]
    [InlineData("img", "img1")]
    [InlineData("v1.9.0", "v1.10.0")]
    [InlineData("chapter 2 - end", "chapter 10 - end")]
    [InlineData("a1b2", "a1b10")]
    public void TheLeftNameSortsFirst(string first, string second)
    {
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare(first, second) < 0);
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare(second, first) > 0);
    }

    [Fact]
    public void IdenticalNamesCompareEqual()
    {
        Assert.Equal(0, NaturalComparer.OrdinalIgnoreCase.Compare("file10", "file10"));
        Assert.Equal(0, NaturalComparer.OrdinalIgnoreCase.Compare("", ""));
        Assert.Equal(0, NaturalComparer.OrdinalIgnoreCase.Compare("File10", "file10"));
    }

    [Fact]
    public void CaseSensitivityIsSelectable()
    {
        Assert.Equal(0, NaturalComparer.Compare("ABC", "abc", caseSensitive: false));
        Assert.NotEqual(0, NaturalComparer.Compare("ABC", "abc", caseSensitive: true));
        Assert.False(NaturalComparer.Ordinal.CaseSensitive == NaturalComparer.OrdinalIgnoreCase.CaseSensitive);
        Assert.Same(NaturalComparer.Ordinal, NaturalComparer.For(caseSensitive: true));
        Assert.Same(NaturalComparer.OrdinalIgnoreCase, NaturalComparer.For(caseSensitive: false));
    }

    [Fact]
    public void NullsSortFirst()
    {
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare(null, "a") < 0);
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare("a", null) > 0);
        Assert.Equal(0, NaturalComparer.OrdinalIgnoreCase.Compare(null, null));
    }

    [Fact]
    public void LeadingZerosOnlyBreakTiesLast()
    {
        // The numbers are equal, so the padding decides - and only then.
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare("7", "007") < 0);
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare("file007", "file8") < 0);
    }

    [Fact]
    public void ALongDigitRunIsComparedByValueNotByCharacter()
    {
        // Plain string comparison would put "9999999999999999999999" first.
        Assert.True(NaturalComparer.OrdinalIgnoreCase.Compare("a9999999999999999999999", "a10000000000000000000000") < 0);
    }

    [Fact]
    public void SortingAListGivesTheHumanOrder()
    {
        string[] names = ["file10", "file2", "file1", "File3"];
        Array.Sort(names, NaturalComparer.OrdinalIgnoreCase);

        Assert.Equal(["file1", "file2", "File3", "file10"], names);
    }
}

public class FileEntryComparerTests
{
    private static FileEntry Dir(string name, long size = 0, int day = 1) => new()
    {
        Name = name,
        FullPath = "/x/" + name,
        IsDirectory = true,
        Size = size,
        Modified = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Local),
        Created = new DateTime(2026, 2, day, 0, 0, 0, DateTimeKind.Local),
        Accessed = new DateTime(2026, 3, day, 0, 0, 0, DateTimeKind.Local),
        Attributes = FileAttributes.Directory,
    };

    private static FileEntry File(string name, long size = 0, int day = 1) => new()
    {
        Name = name,
        FullPath = "/x/" + name,
        Size = size,
        Modified = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Local),
        Created = new DateTime(2026, 2, day, 0, 0, 0, DateTimeKind.Local),
        Accessed = new DateTime(2026, 3, day, 0, 0, 0, DateTimeKind.Local),
    };

    private static FileEntry Parent() => FileEntry.ParentOf(Path.GetTempPath());

    private static FileEntryComparer Comparer(
        SortMode mode = SortMode.Name,
        bool reverse = false,
        bool directoriesFirst = true,
        bool numeric = false,
        bool caseSensitive = false) =>
        new(mode, reverse, directoriesFirst, numeric, caseSensitive);

    private static string[] Order(FileEntryComparer comparer, params FileEntry[] entries) =>
        [.. comparer.Sort(entries).Select(e => e.Name)];

    [Fact]
    public void TheParentEntryComesFirstWhateverElseIsAsked()
    {
        FileEntry[] entries = [File("a.txt"), Dir("zzz"), Parent(), File("b.txt")];

        Assert.Equal("..", Order(Comparer(), entries)[0]);
        Assert.Equal("..", Order(Comparer(reverse: true), entries)[0]);
        Assert.Equal("..", Order(Comparer(SortMode.Size, reverse: true), entries)[0]);
        Assert.Equal("..", Order(Comparer(directoriesFirst: false), entries)[0]);
        Assert.Equal("..", Order(Comparer(SortMode.Unsorted), entries)[0]);
    }

    [Fact]
    public void TwoParentEntriesCompareEqual() =>
        Assert.Equal(0, Comparer().Compare(Parent(), Parent()));

    [Fact]
    public void NullsSortToTheEnd()
    {
        FileEntryComparer c = Comparer();

        Assert.True(c.Compare(null, File("a")) > 0);
        Assert.True(c.Compare(File("a"), null) < 0);
        Assert.Equal(0, c.Compare(null, null));
    }

    [Fact]
    public void DirectoriesLeadFilesAndReverseDoesNotChangeThat()
    {
        FileEntry[] entries = [File("a.txt"), Dir("zzz"), File("b.txt"), Dir("aaa")];

        Assert.Equal(["aaa", "zzz", "a.txt", "b.txt"], Order(Comparer(), entries));
        Assert.Equal(["zzz", "aaa", "b.txt", "a.txt"], Order(Comparer(reverse: true), entries));
    }

    [Fact]
    public void GroupingCanBeTurnedOff()
    {
        FileEntry[] entries = [File("a.txt"), Dir("zzz"), File("b.txt"), Dir("aaa")];

        Assert.Equal(["a.txt", "aaa", "b.txt", "zzz"], Order(Comparer(directoriesFirst: false), entries));
    }

    [Fact]
    public void NamesCompareCaseInsensitivelyByDefault()
    {
        FileEntry[] entries = [File("beta.txt"), File("Alpha.txt"), File("gamma.txt")];

        Assert.Equal(["Alpha.txt", "beta.txt", "gamma.txt"], Order(Comparer(), entries));
    }

    [Fact]
    public void CaseSensitiveSortingPutsCapitalsFirst()
    {
        FileEntry[] entries = [File("beta.txt"), File("Beta.txt"), File("alpha.txt")];

        Assert.Equal(["Beta.txt", "alpha.txt", "beta.txt"], Order(Comparer(caseSensitive: true), entries));
    }

    [Fact]
    public void CaseVariantsStillGetADeterministicOrder()
    {
        FileEntryComparer c = Comparer();

        Assert.True(c.Compare(File("README"), File("readme")) < 0);
        Assert.True(c.Compare(File("readme"), File("README")) > 0);
    }

    [Fact]
    public void NumericSortingReadsDigitRunsAsNumbers()
    {
        FileEntry[] entries = [File("file10.txt"), File("file2.txt"), File("file1.txt")];

        Assert.Equal(["file1.txt", "file2.txt", "file10.txt"], Order(Comparer(numeric: true), entries));
        Assert.Equal(["file1.txt", "file10.txt", "file2.txt"], Order(Comparer(numeric: false), entries));
    }

    [Fact]
    public void ExtensionSortingGroupsByExtensionThenName()
    {
        FileEntry[] entries = [File("b.cs"), File("a.zip"), File("a.cs"), File("readme")];

        // The extensionless name sorts first: an empty extension is the smallest one.
        Assert.Equal(["readme", "a.cs", "b.cs", "a.zip"], Order(Comparer(SortMode.Extension), entries));
    }

    [Fact]
    public void ExtensionSortingIgnoresDirectoryExtensions()
    {
        FileEntry[] entries = [Dir("bin"), Dir("archive.old"), File("z.aaa"), File("a.zip")];

        // The classic default: a folder named "archive.old" is not an "old" file, so directories
        // order purely by name among themselves.
        Assert.Equal(["archive.old", "bin", "z.aaa", "a.zip"], Order(Comparer(SortMode.Extension), entries));

        // Reverse flips the key alone; the directories' empty keys stay equal, so the name
        // fallback keeps them ascending.
        Assert.Equal(["archive.old", "bin", "a.zip", "z.aaa"], Order(Comparer(SortMode.Extension, reverse: true), entries));
    }

    [Fact]
    public void UngroupedExtensionSortingTreatsAFolderExtensionAsEmpty()
    {
        FileEntry[] entries = [File("a.cs"), Dir("readme.txt"), File("plain")];

        // The directory keeps its empty key even mixed in with the files.
        Assert.Equal(["plain", "readme.txt", "a.cs"], Order(Comparer(SortMode.Extension, directoriesFirst: false), entries));
    }

    [Fact]
    public void SizeSortingOrdersByBytes()
    {
        FileEntry[] entries = [File("big", 5000), File("small", 10), File("medium", 900)];

        Assert.Equal(["small", "medium", "big"], Order(Comparer(SortMode.Size), entries));
        Assert.Equal(["big", "medium", "small"], Order(Comparer(SortMode.Size, reverse: true), entries));
    }

    [Fact]
    public void EqualKeysFallBackToTheNameAscendingEvenWhenReversed()
    {
        FileEntry[] entries = [File("c.txt", 100), File("a.txt", 100), File("b.txt", 100)];

        Assert.Equal(["a.txt", "b.txt", "c.txt"], Order(Comparer(SortMode.Size), entries));
        Assert.Equal(["a.txt", "b.txt", "c.txt"], Order(Comparer(SortMode.Size, reverse: true), entries));
    }

    [Fact]
    public void TimeSortingUsesTheRequestedStamp()
    {
        FileEntry[] entries = [File("c", day: 3), File("a", day: 1), File("b", day: 2)];

        Assert.Equal(["a", "b", "c"], Order(Comparer(SortMode.Modified), entries));
        Assert.Equal(["c", "b", "a"], Order(Comparer(SortMode.Modified, reverse: true), entries));
        Assert.Equal(["a", "b", "c"], Order(Comparer(SortMode.Created), entries));
        Assert.Equal(["a", "b", "c"], Order(Comparer(SortMode.Accessed), entries));
    }

    [Fact]
    public void DirectoriesSortAmongThemselvesByTheSameKey()
    {
        FileEntry[] entries = [Dir("new", day: 5), Dir("old", day: 1), File("z", day: 9)];

        Assert.Equal(["old", "new", "z"], Order(Comparer(SortMode.Modified), entries));
    }

    [Fact]
    public void UnsortedKeepsTheFileSystemOrderApartFromTheGrouping()
    {
        FileEntry[] entries = [File("zebra"), File("apple"), Dir("later"), Dir("earlier")];

        Assert.Equal(["later", "earlier", "zebra", "apple"], Order(Comparer(SortMode.Unsorted), entries));
        Assert.Equal(["zebra", "apple", "later", "earlier"], Order(Comparer(SortMode.Unsorted, directoriesFirst: false), entries));
    }

    [Fact]
    public void DescriptionAndOwnerFallBackToTheNameForNow()
    {
        FileEntry[] entries = [File("b"), File("a")];

        Assert.Equal(["a", "b"], Order(Comparer(SortMode.Description), entries));
        Assert.Equal(["a", "b"], Order(Comparer(SortMode.Owner), entries));
    }

    [Fact]
    public void ForTakesTheGroupingAndNameFlagsFromTheSettings()
    {
        var settings = new Settings
        {
            DirectoriesFirst = false,
            NumericSort = true,
            CaseSensitiveSort = true,
        };

        FileEntryComparer c = FileEntryComparer.For(SortMode.Size, reverse: true, settings);

        Assert.Equal(SortMode.Size, c.Mode);
        Assert.True(c.Reverse);
        Assert.False(c.DirectoriesFirst);
        Assert.True(c.Numeric);
        Assert.True(c.CaseSensitive);
    }

    [Fact]
    public void SortReturnsANewListAndLeavesTheInputAlone()
    {
        var entries = new List<FileEntry> { File("b"), File("a") };

        List<FileEntry> sorted = Comparer().Sort(entries);

        Assert.Equal(["a", "b"], sorted.Select(e => e.Name));
        Assert.Equal(["b", "a"], entries.Select(e => e.Name));
        Assert.NotSame(entries, sorted);
    }
}
