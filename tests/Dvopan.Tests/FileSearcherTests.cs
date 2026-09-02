using System.Text;
using Dvopan.Files;
using Dvopan.Operations;

namespace Dvopan.Tests;

public class FileSearcherMaskTests
{
    [Fact]
    public void FindsEveryMatchingFileInTheWholeTree()
    {
        using var temp = new OpsTempDirectory();
        temp.File("top.txt", "x");
        temp.File(Path.Combine("sub", "middle.txt"), "x");
        temp.File(Path.Combine("sub", "deep", "bottom.txt"), "x");
        temp.File(Path.Combine("sub", "notes.md"), "x");

        var found = new List<string>();
        SearchResult result = FileSearcher.Search(
            temp.Path,
            new SearchOptions { Mask = "*.txt" },
            entry => found.Add(entry.Name));

        Assert.False(result.Cancelled);
        Assert.Equal(3, result.Matches);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(["bottom.txt", "middle.txt", "top.txt"], found.OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(result.Items, e => Assert.False(e.IsDirectory));
    }

    [Fact]
    public void AMaskListWithAnExclusionIsHonoured()
    {
        using var temp = new OpsTempDirectory();
        temp.File("keep.txt", "x");
        temp.File("skip-me.txt", "x");
        temp.File("notes.md", "x");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Mask = "*.txt,!skip*" });

        Assert.Equal(1, result.Matches);
        Assert.Equal("keep.txt", result.Items[0].Name);
    }

    [Fact]
    public void AnEmptyMaskFindsEverything()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "x");
        temp.File("b.md", "x");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Mask = string.Empty });

        Assert.Equal(2, result.Matches);
    }

    [Fact]
    public void ANonRecursiveSearchStaysInTheStartingFolder()
    {
        using var temp = new OpsTempDirectory();
        temp.File("top.txt", "x");
        temp.File(Path.Combine("sub", "inner.txt"), "x");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Recursive = false });

        Assert.Equal(1, result.Matches);
        Assert.Equal("top.txt", result.Items[0].Name);
        Assert.Equal(1, result.DirectoriesScanned);
    }

    [Fact]
    public void MaxDepthLimitsHowFarTheWalkGoes()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "x");
        temp.File(Path.Combine("one", "b.txt"), "x");
        temp.File(Path.Combine("one", "two", "c.txt"), "x");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { MaxDepth = 1 });

        Assert.Equal(2, result.Matches);
        Assert.DoesNotContain(result.Items, e => e.Name == "c.txt");
    }

    [Fact]
    public void FoldersAreOnlyReportedWhenAsked()
    {
        using var temp = new OpsTempDirectory();
        temp.Dir("target");
        temp.File("target.txt", "x");

        SearchResult without = FileSearcher.Search(temp.Path, new SearchOptions { Mask = "target*" });
        Assert.Equal(1, without.Matches);

        SearchResult with = FileSearcher.Search(temp.Path, new SearchOptions { Mask = "target*", MatchDirectories = true });
        Assert.Equal(2, with.Matches);
        Assert.Contains(with.Items, e => e.IsDirectory && e.Name == "target");
    }

    [Fact]
    public void HiddenFilesAreSkippedWhenAsked()
    {
        using var temp = new OpsTempDirectory();
        temp.File("visible.txt", "x");
        string hidden = temp.File(".hidden.txt", "x");

        try
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Unix derives "hidden" from the leading dot, so the attribute call is not needed there.
        }

        SearchResult all = FileSearcher.Search(temp.Path, new SearchOptions { IncludeHidden = true });
        Assert.Equal(2, all.Matches);

        SearchResult some = FileSearcher.Search(temp.Path, new SearchOptions { IncludeHidden = false });
        Assert.Equal(1, some.Matches);
        Assert.Equal("visible.txt", some.Items[0].Name);
    }

    [Fact]
    public void MaxResultsStopsTheSearchEarly()
    {
        using var temp = new OpsTempDirectory();
        for (int i = 0; i < 20; i++)
        {
            temp.File($"f{i}.txt", "x");
        }

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { MaxResults = 5 });

        Assert.Equal(5, result.Matches);
        Assert.True(result.Cancelled);
    }

    [Fact]
    public void EveryFolderIsAnnouncedThroughTheProgressCallback()
    {
        using var temp = new OpsTempDirectory();
        temp.Dir("one");
        temp.Dir("one", "two");

        var seen = new List<string>();
        FileSearcher.Search(temp.Path, new SearchOptions(), onDirectory: seen.Add);

        Assert.Equal(3, seen.Count);
        Assert.Contains(seen, p => p.EndsWith("two", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellationStopsTheWalk()
    {
        using var temp = new OpsTempDirectory();
        for (int i = 0; i < 10; i++)
        {
            temp.File($"f{i}.txt", "x");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions(), cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Matches);
    }

    [Fact]
    public void CollectingCanBeSwitchedOff()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "x");

        int reported = 0;
        SearchResult result = FileSearcher.Search(
            temp.Path,
            new SearchOptions { CollectMatches = false },
            _ => reported++);

        Assert.Equal(1, result.Matches);
        Assert.Equal(1, reported);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void AMissingFolderIsReportedRatherThanThrown()
    {
        SearchResult result = FileSearcher.Search(
            Path.Combine(Path.GetTempPath(), "oc-missing-" + Guid.NewGuid().ToString("N")),
            new SearchOptions());

        Assert.Equal(0, result.Matches);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void EmptyAndNullInputAreAccepted()
    {
        Assert.Equal(0, FileSearcher.Search("   ", new SearchOptions()).Matches);
        Assert.Equal(0, FileSearcher.Search([], new SearchOptions()).Matches);
    }

    [Fact]
    public void ADirectorySymlinkIsNotWalkedInto()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("real", "inside.txt"), "x");

        if (!OpsTestHelpers.TryCreateDirectoryLink(temp.Combine("link"), temp.Combine("real")))
        {
            // Neither a symlink nor a junction can be created here.
            return;
        }

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions());

        // Only the real copy: the link is not followed, so "inside.txt" is found exactly once.
        Assert.Equal(1, result.Matches);

        SearchResult followed = FileSearcher.Search(temp.Path, new SearchOptions { FollowLinks = true });
        Assert.Equal(2, followed.Matches);
    }
}

public class FileSearcherContentTests
{
    [Fact]
    public void FindsALiteralInsideAFile()
    {
        using var temp = new OpsTempDirectory();
        temp.File("hit.txt", "the needle is here");
        temp.File("miss.txt", "nothing to see");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" });

        Assert.Equal(1, result.Matches);
        Assert.Equal("hit.txt", result.Items[0].Name);
        Assert.Equal(2, result.FilesScanned);
    }

    [Fact]
    public void TheMaskStillFiltersWhenSearchingContent()
    {
        using var temp = new OpsTempDirectory();
        temp.File("hit.txt", "needle");
        temp.File("hit.md", "needle");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Mask = "*.txt", Text = "needle" });

        Assert.Equal(1, result.Matches);
        Assert.Equal("hit.txt", result.Items[0].Name);
    }

    [Fact]
    public void ContentMatchingIgnoresCaseUnlessAsked()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "The NEEDLE is here");

        Assert.Equal(1, FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" }).Matches);
        Assert.Equal(0, FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle", CaseSensitive = true }).Matches);
        Assert.Equal(1, FileSearcher.Search(temp.Path, new SearchOptions { Text = "NEEDLE", CaseSensitive = true }).Matches);
    }

    [Fact]
    public void WholeWordsRequireBoundaries()
    {
        using var temp = new OpsTempDirectory();
        temp.File("joined.txt", "needlework");
        temp.File("alone.txt", "a needle, sharp");

        SearchResult loose = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" });
        Assert.Equal(2, loose.Matches);

        SearchResult strict = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle", WholeWords = true });
        Assert.Equal(1, strict.Matches);
        Assert.Equal("alone.txt", strict.Items[0].Name);
    }

    [Fact]
    public void RegularExpressionsAreSupported()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "order 4711 shipped");
        temp.File("b.txt", "order pending");

        SearchResult result = FileSearcher.Search(
            temp.Path,
            new SearchOptions { Text = @"order\s+\d{4}", UseRegex = true });

        Assert.Equal(1, result.Matches);
        Assert.Equal("a.txt", result.Items[0].Name);
    }

    [Fact]
    public void ABrokenRegularExpressionIsReportedNotThrown()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "anything");

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Text = "[unclosed", UseRegex = true });

        Assert.Equal(0, result.Matches);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Utf16FilesAreSearchedThroughTheirByteOrderMark()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.Combine("utf16.txt");
        File.WriteAllText(path, "a needle in unicode", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" });

        Assert.Equal(1, result.Matches);
    }

    [Fact]
    public void Utf16WithoutAByteOrderMarkIsDetectedFromTheBytes()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.Combine("utf16-nobom.txt");
        File.WriteAllBytes(path, new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes(new string('x', 200) + " needle "));

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" });

        Assert.Equal(1, result.Matches);
    }

    [Fact]
    public void NonUnicodeBytesStillMatchALiteral()
    {
        using var temp = new OpsTempDirectory();

        // Latin-1 bytes that are not valid UTF-8: the detector must fall back rather than give up.
        byte[] bytes = [.. Encoding.Latin1.GetBytes("café needle ÿþý")];
        temp.Binary("latin.txt", bytes);

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" });

        Assert.Equal(1, result.Matches);
    }

    [Fact]
    public void AMatchStraddlingAChunkBoundaryIsStillFound()
    {
        using var temp = new OpsTempDirectory();
        string filler = new('a', FileSearcher.ContentChunkChars - 3);
        temp.File("big.txt", filler + "needle" + new string('b', 100));

        SearchResult result = FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" });

        Assert.Equal(1, result.Matches);
    }

    [Fact]
    public void FilesOverTheSizeLimitAreNotOpened()
    {
        using var temp = new OpsTempDirectory();
        temp.File("big.txt", new string('x', 1000) + "needle");

        Assert.Equal(0, FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle", MaxContentBytes = 100 }).Matches);
        Assert.Equal(1, FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle", MaxContentBytes = 1_000_000 }).Matches);
    }

    [Fact]
    public void AnEmptyFileNeverMatches()
    {
        using var temp = new OpsTempDirectory();
        temp.File("empty.txt", string.Empty);

        Assert.Equal(0, FileSearcher.Search(temp.Path, new SearchOptions { Text = "needle" }).Matches);
    }

    [Fact]
    public void ContainsTextAnswersDirectly()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "hello world");

        Assert.True(FileSearcher.ContainsText(path, "WORLD"));
        Assert.False(FileSearcher.ContainsText(path, "WORLD", caseSensitive: true));
        Assert.True(FileSearcher.ContainsText(path, "w.rld", useRegex: true));
        Assert.False(FileSearcher.ContainsText(Path.Combine(temp.Path, "nope.txt"), "anything"));
    }

    [Fact]
    public void EncodingDetectionRecognisesTheCommonCases()
    {
        using var temp = new OpsTempDirectory();
        const string text = "hello";

        string utf8Bom = temp.Combine("utf8bom.txt");
        File.WriteAllText(utf8Bom, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.Equal(Encoding.UTF8.CodePage, FileSearcher.DetectEncoding(utf8Bom).CodePage);

        string utf16 = temp.Combine("utf16.txt");
        File.WriteAllText(utf16, text, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        Assert.Equal(Encoding.Unicode.CodePage, FileSearcher.DetectEncoding(utf16).CodePage);

        string bigEndian = temp.Combine("utf16be.txt");
        File.WriteAllText(bigEndian, text, new UnicodeEncoding(bigEndian: true, byteOrderMark: true));
        Assert.Equal(Encoding.BigEndianUnicode.CodePage, FileSearcher.DetectEncoding(bigEndian).CodePage);

        string plain = temp.File("plain.txt", text);
        Assert.Equal(Encoding.UTF8.CodePage, FileSearcher.DetectEncoding(plain).CodePage);

        string binary = temp.Binary("binary.bin", [0xFF, 0xE0, 0x81, 0x90, 0xC0, 0xC0, 0xC0]);
        Assert.Equal(Encoding.Latin1.CodePage, FileSearcher.DetectEncoding(binary).CodePage);
    }

    [Fact]
    public void EncodingDetectionHandlesAnEmptySample()
    {
        Assert.Equal(Encoding.UTF8.CodePage, FileSearcher.DetectEncoding(ReadOnlySpan<byte>.Empty).CodePage);
        Assert.Equal(Encoding.Latin1.CodePage, FileSearcher.DetectEncoding(new byte[] { 0xC0, 0xC0, 0xC0, 0xC0 }).CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, FileSearcher.DetectEncoding("café"u8).CodePage);
    }
}

public class DirectorySizeCalculatorTests
{
    [Fact]
    public void AddsUpAWholeTree()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", new string('a', 10));
        temp.File(Path.Combine("sub", "b.txt"), new string('b', 20));
        temp.File(Path.Combine("sub", "deep", "c.txt"), new string('c', 30));
        temp.Dir("empty");

        DirectorySize size = DirectorySizeCalculator.Calculate(temp.Path);

        Assert.True(size.Complete);
        Assert.Equal(60, size.Bytes);
        Assert.Equal(3, size.Files);
        Assert.Equal(3, size.Directories);
        Assert.False(size.IsEmpty);
    }

    [Fact]
    public void HiddenEntriesCanBeLeftOut()
    {
        using var temp = new OpsTempDirectory();
        temp.File("visible.txt", new string('a', 10));
        string hidden = temp.File(".hidden.txt", new string('b', 5));

        try
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Unix uses the leading dot instead.
        }

        Assert.Equal(15, DirectorySizeCalculator.Calculate(temp.Path, includeHidden: true).Bytes);
        Assert.Equal(10, DirectorySizeCalculator.Calculate(temp.Path, includeHidden: false).Bytes);
    }

    [Fact]
    public void AFileMeasuresAsItself()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", new string('a', 42));

        DirectorySize size = DirectorySizeCalculator.Calculate(path);

        Assert.Equal(42, size.Bytes);
        Assert.Equal(1, size.Files);
        Assert.Equal(0, size.Directories);
        Assert.True(size.Complete);
    }

    [Fact]
    public void AMissingPathComesBackIncomplete()
    {
        DirectorySize size = DirectorySizeCalculator.Calculate(
            Path.Combine(Path.GetTempPath(), "oc-missing-" + Guid.NewGuid().ToString("N")));

        Assert.False(size.Complete);
        Assert.True(size.IsEmpty);
        Assert.Equal(DirectorySize.Empty, DirectorySizeCalculator.Calculate("   "));
    }

    [Fact]
    public void CancellationYieldsAnIncompleteTotal()
    {
        using var temp = new OpsTempDirectory();
        temp.File("a.txt", "aaa");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        DirectorySize size = DirectorySizeCalculator.Calculate(temp.Path, cancellationToken: cts.Token);

        Assert.False(size.Complete);
    }

    [Fact]
    public void SeveralEntriesAreMeasuredAndReportedOneByOne()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("one", "a.txt"), new string('a', 10));
        temp.File(Path.Combine("two", "b.txt"), new string('b', 20));

        FileEntry[] entries =
        [
            OpsTestHelpers.Entry(temp.Combine("one")),
            OpsTestHelpers.Entry(temp.Combine("two")),
            FileEntry.ParentOf(temp.Path),
        ];

        var reported = new List<string>();
        IReadOnlyList<KeyValuePair<FileEntry, DirectorySize>> results =
            DirectorySizeCalculator.Calculate(entries, onEach: (entry, _) => reported.Add(entry.Name));

        Assert.Equal(2, results.Count);
        Assert.Equal(["one", "two"], reported);
        Assert.Equal(30, DirectorySizeCalculator.Total(results).Bytes);
        Assert.Equal(2, DirectorySizeCalculator.Total(results).Files);
    }

    [Fact]
    public void TotalsAddUpAndPropagateIncompleteness()
    {
        var complete = new DirectorySize(10, 1, 0, true);
        var partial = new DirectorySize(5, 1, 1, false);

        DirectorySize sum = complete + partial;

        Assert.Equal(15, sum.Bytes);
        Assert.Equal(2, sum.Files);
        Assert.Equal(1, sum.Directories);
        Assert.False(sum.Complete);
        Assert.Equal(sum, complete.Add(partial));
        Assert.StartsWith(">", sum.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AJunctionIsCountedButNotWalkedInto()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("real", "inside.txt"), new string('a', 10));

        if (!OpsTestHelpers.TryCreateDirectoryLink(temp.Combine("link"), temp.Combine("real")))
        {
            return;
        }

        DirectorySize size = DirectorySizeCalculator.Calculate(temp.Path);

        Assert.Equal(10, size.Bytes);
        Assert.Equal(1, size.Files);
        Assert.Equal(2, size.Directories);
    }
}
