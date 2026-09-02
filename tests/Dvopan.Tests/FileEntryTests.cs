using Dvopan.Files;

namespace Dvopan.Tests;

/// <summary>
/// A scratch directory tree that removes itself, so the provider tests can read a directory whose
/// exact contents they control.
/// </summary>
internal sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "oc-files-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddDirectory(string name)
    {
        string path = System.IO.Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string AddFile(string name, int bytes = 0)
    {
        string path = System.IO.Path.Combine(Root, name);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    public string AddHiddenFile(string name, int bytes = 0)
    {
        // A leading dot is what makes a file hidden on Unix; the attribute is what does it on
        // Windows. Doing both keeps the test meaningful on either host.
        string path = AddFile("." + name, bytes);
        if (OperatingSystem.IsWindows())
        {
            System.IO.File.SetAttributes(path, System.IO.File.GetAttributes(path) | FileAttributes.Hidden);
        }

        return path;
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    System.IO.File.SetAttributes(file, FileAttributes.Normal);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leaked temp folder is not worth failing a test over.
        }
    }
}

public class FileEntryPropertyTests
{
    private static FileEntry File(string name, FileAttributes attributes = FileAttributes.Normal) =>
        new() { Name = name, FullPath = "/tmp/" + name, Attributes = attributes };

    [Theory]
    [InlineData("readme.txt", "txt")]
    [InlineData("README.TXT", "txt")]
    [InlineData("archive.TAR.GZ", "gz")]
    [InlineData("noextension", "")]
    [InlineData(".gitignore", "")]
    [InlineData("trailingdot.", "")]
    [InlineData("", "")]
    public void ExtensionIsLowerCaseAndDotless(string name, string expected) =>
        Assert.Equal(expected, File(name).Extension);

    [Theory]
    [InlineData("readme.txt", "readme")]
    [InlineData("archive.TAR.GZ", "archive.TAR")]
    [InlineData("noextension", "noextension")]
    [InlineData(".gitignore", ".gitignore")]
    [InlineData("trailingdot.", "trailingdot.")]
    public void BaseNameDropsTheExtension(string name, string expected) =>
        Assert.Equal(expected, File(name).BaseName);

    [Fact]
    public void AttributeStringUsesFarsOrderAndIsAlwaysFiveCharacters()
    {
        Assert.Equal("-----", File("a").AttributeString);
        Assert.Equal("R----", File("a", FileAttributes.ReadOnly).AttributeString);
        Assert.Equal("-A---", File("a", FileAttributes.Archive).AttributeString);
        Assert.Equal("--S--", File("a", FileAttributes.System).AttributeString);
        Assert.Equal("---H-", File("a", FileAttributes.Hidden).AttributeString);

        var all = new FileEntry
        {
            Name = "a",
            IsDirectory = true,
            Attributes = FileAttributes.ReadOnly | FileAttributes.Archive | FileAttributes.System |
                         FileAttributes.Hidden | FileAttributes.Directory,
        };

        Assert.Equal("RASHD", all.AttributeString);
        Assert.Equal(5, all.AttributeString.Length);
    }

    [Fact]
    public void HiddenCoversTheSystemAttributeToo()
    {
        Assert.True(File("a", FileAttributes.Hidden).IsHidden);
        Assert.True(File("a", FileAttributes.System).IsHidden);
        Assert.False(File("a").IsHidden);
        Assert.True(File("a", FileAttributes.System).IsSystem);
        Assert.False(File("a", FileAttributes.Hidden).IsSystem);
        Assert.True(File("a", FileAttributes.ReadOnly).IsReadOnly);
        Assert.True(File("a", FileAttributes.ReparsePoint).IsReparsePoint);
    }

    [Theory]
    [InlineData("backup.zip")]
    [InlineData("backup.ZIP")]
    [InlineData("backup.7z")]
    [InlineData("backup.tar")]
    [InlineData("backup.gz")]
    [InlineData("image.iso")]
    public void ArchiveExtensionsAreRecognised(string name) => Assert.True(File(name).IsArchive);

    [Fact]
    public void ADirectoryIsNeverAnArchiveOrExecutable()
    {
        var dir = new FileEntry { Name = "backup.zip", IsDirectory = true, UnixExecutable = true };

        Assert.False(dir.IsArchive);
        Assert.False(dir.IsExecutable);
    }

    [Fact]
    public void ExecutablesFollowThePlatformRule()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(File("setup.exe").IsExecutable);
            Assert.True(File("SETUP.EXE").IsExecutable);
            Assert.True(File("run.cmd").IsExecutable);
            Assert.True(File("run.ps1").IsExecutable);
            Assert.False(File("run.sh").IsExecutable);
            Assert.False(File("readme.txt").IsExecutable);
        }
        else
        {
            Assert.True(new FileEntry { Name = "run.sh", UnixExecutable = true }.IsExecutable);
            Assert.False(new FileEntry { Name = "setup.exe", UnixExecutable = false }.IsExecutable);
        }
    }

    [Fact]
    public void TheParentEntryCanNeverBeTagged()
    {
        FileEntry parent = FileEntry.ParentOf(System.IO.Path.GetTempPath());

        parent.Selected = true;

        Assert.False(parent.Selected);
        Assert.True(parent.IsParent);
        Assert.True(parent.IsDirectory);
        Assert.Equal("..", parent.Name);
    }

    [Fact]
    public void AnOrdinaryEntryCanBeTagged()
    {
        FileEntry entry = File("a.txt");

        entry.Selected = true;
        Assert.True(entry.Selected);

        entry.Selected = false;
        Assert.False(entry.Selected);
    }

    [Fact]
    public void ParentOfPointsAtTheContainingDirectory()
    {
        string root = System.IO.Path.GetTempPath();
        string child = System.IO.Path.Combine(root, "oc-parent-of-probe");

        FileEntry parent = FileEntry.ParentOf(child);

        Assert.Equal(
            System.IO.Path.TrimEndingDirectorySeparator(root),
            System.IO.Path.TrimEndingDirectorySeparator(parent.FullPath));
    }

    [Fact]
    public void ParentOfARootPointsBackAtTheRoot()
    {
        string root = FileSystemProvider.GetRoot(System.IO.Path.GetTempPath());

        Assert.Equal(root, FileEntry.ParentOf(root).FullPath);
    }
}

public class FileTypeClassifierTests
{
    [Fact]
    public void ExtensionSetsAreCaseInsensitive()
    {
        Assert.True(FileTypeClassifier.IsExecutableExtension("EXE"));
        Assert.True(FileTypeClassifier.IsExecutableExtension("ps1"));
        Assert.False(FileTypeClassifier.IsExecutableExtension("txt"));
        Assert.False(FileTypeClassifier.IsExecutableExtension(""));
        Assert.False(FileTypeClassifier.IsExecutableExtension(null));

        Assert.True(FileTypeClassifier.IsArchiveExtension("ZIP"));
        Assert.True(FileTypeClassifier.IsArchiveExtension("bz2"));
        Assert.False(FileTypeClassifier.IsArchiveExtension("zipper"));
        Assert.False(FileTypeClassifier.IsArchiveExtension(null));
    }

    [Fact]
    public void TheAdvertisedExtensionListsMatchTheContract()
    {
        Assert.Equal(
            ["bat", "cmd", "com", "exe", "msi", "ps1"],
            FileTypeClassifier.ExecutableExtensions.Order(StringComparer.Ordinal));

        Assert.Equal(
            ["7z", "bz2", "cab", "gz", "iso", "rar", "tar", "xz", "zip"],
            FileTypeClassifier.ArchiveExtensions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ClassifyFollowsThePanelsColourOrder()
    {
        var directory = new FileEntry { Name = "src", IsDirectory = true, Attributes = FileAttributes.Directory | FileAttributes.Hidden };
        var hidden = new FileEntry { Name = "secret.zip", Attributes = FileAttributes.Hidden };
        var archive = new FileEntry { Name = "backup.zip" };
        var normal = new FileEntry { Name = "readme.txt" };

        // A hidden directory is still a directory, and a hidden archive is still hidden.
        Assert.Equal(FileCategory.Directory, FileTypeClassifier.Classify(directory));
        Assert.Equal(FileCategory.Hidden, FileTypeClassifier.Classify(hidden));
        Assert.Equal(FileCategory.Archive, FileTypeClassifier.Classify(archive));
        Assert.Equal(FileCategory.Normal, FileTypeClassifier.Classify(normal));

        var executable = OperatingSystem.IsWindows()
            ? new FileEntry { Name = "setup.exe" }
            : new FileEntry { Name = "setup", UnixExecutable = true };

        Assert.Equal(FileCategory.Executable, FileTypeClassifier.Classify(executable));
    }

    [Fact]
    public void ProbingAMissingFileIsFalseRatherThanAThrow() =>
        Assert.False(FileTypeClassifier.ProbeUnixExecutable(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-no-such-file-" + Guid.NewGuid().ToString("N"))));
}

public class FileSystemProviderReadTests
{
    [Fact]
    public void ReadListsFilesAndDirectoriesWithTheParentFirst()
    {
        using var tree = new TempTree();
        tree.AddDirectory("sub");
        tree.AddFile("b.txt", 10);
        tree.AddFile("a.txt", 3);

        DirectoryListing listing = FileSystemProvider.Read(tree.Root, includeHidden: true);

        Assert.Null(listing.Error);
        Assert.False(listing.HasError);
        Assert.True(listing.HasParent);
        Assert.Same(listing.Entries[0], listing.Parent);
        Assert.Equal("..", listing.Entries[0].Name);
        Assert.Equal(4, listing.Count);

        string[] names = [.. listing.Entries.Select(e => e.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["..", "a.txt", "b.txt", "sub"], names);
    }

    [Fact]
    public void ReadReportsSizesTimesAndDirectoryFlags()
    {
        using var tree = new TempTree();
        tree.AddDirectory("sub");
        tree.AddFile("data.bin", 1234);

        DirectoryListing listing = FileSystemProvider.Read(tree.Root, includeHidden: true);

        FileEntry file = listing.Entries.Single(e => e.Name == "data.bin");
        FileEntry dir = listing.Entries.Single(e => e.Name == "sub");

        Assert.Equal(1234, file.Size);
        Assert.False(file.IsDirectory);
        Assert.Equal(System.IO.Path.Combine(tree.Root, "data.bin"), file.FullPath);
        Assert.True(file.Modified > DateTime.Now.AddMinutes(-5));

        Assert.True(dir.IsDirectory);
        Assert.Equal(0, dir.Size);
    }

    [Fact]
    public void ReadCountsFilesFoldersAndBytesForTheTotalsLine()
    {
        using var tree = new TempTree();
        tree.AddDirectory("one");
        tree.AddDirectory("two");
        tree.AddFile("a.bin", 100);
        tree.AddFile("b.bin", 200);
        tree.AddFile("c.bin", 300);

        DirectoryListing listing = FileSystemProvider.Read(tree.Root, includeHidden: true);

        Assert.Equal(3, listing.FileCount);
        Assert.Equal(2, listing.DirectoryCount);
        Assert.Equal(600, listing.TotalBytes);
    }

    [Fact]
    public void HiddenEntriesAreVisibleOnlyWhenAskedFor()
    {
        using var tree = new TempTree();
        tree.AddFile("visible.txt");
        tree.AddHiddenFile("secret.txt");

        DirectoryListing shown = FileSystemProvider.Read(tree.Root, includeHidden: true);
        DirectoryListing filtered = FileSystemProvider.Read(tree.Root, includeHidden: false);

        Assert.Contains(shown.Entries, e => e.Name == ".secret.txt");
        Assert.True(shown.Entries.Single(e => e.Name == ".secret.txt").IsHidden);

        Assert.DoesNotContain(filtered.Entries, e => e.Name == ".secret.txt");
        Assert.Contains(filtered.Entries, e => e.Name == "visible.txt");
        Assert.True(filtered.HasParent);
    }

    [Fact]
    public void AMissingDirectoryYieldsAnErrorAndJustTheParentEntry()
    {
        using var tree = new TempTree();
        string missing = System.IO.Path.Combine(tree.Root, "not-here");

        DirectoryListing listing = FileSystemProvider.Read(missing, includeHidden: true);

        Assert.True(listing.HasError);
        Assert.False(string.IsNullOrWhiteSpace(listing.Error));
        Assert.Single(listing.Entries);
        Assert.Equal("..", listing.Entries[0].Name);
        Assert.Equal(0, listing.FileCount);
        Assert.Equal(0, listing.DirectoryCount);
    }

    [Fact]
    public void AGarbagePathIsAnErrorRatherThanAThrow()
    {
        DirectoryListing listing = FileSystemProvider.Read("C:\\bad\0path", includeHidden: true);

        Assert.True(listing.HasError);
        Assert.DoesNotContain(listing.Entries, e => !e.IsParent);
    }

    [Fact]
    public void AnEmptyPathIsAnErrorRatherThanAThrow()
    {
        Assert.True(FileSystemProvider.Read("", includeHidden: true).HasError);
        Assert.True(FileSystemProvider.Read("   ", includeHidden: true).HasError);
        Assert.Empty(FileSystemProvider.Read("", includeHidden: true).Entries);
    }

    [Fact]
    public void AFileIsNotADirectoryAndReadsAsAnError()
    {
        using var tree = new TempTree();
        string file = tree.AddFile("a.txt", 1);

        DirectoryListing listing = FileSystemProvider.Read(file, includeHidden: true);

        Assert.True(listing.HasError);
    }

    [Fact]
    public void ARootHasNoParentEntry()
    {
        string root = FileSystemProvider.GetRoot(System.IO.Path.GetTempPath());

        DirectoryListing listing = FileSystemProvider.Read(root, includeHidden: false);

        Assert.False(listing.HasParent);
        Assert.Null(listing.Parent);
        Assert.DoesNotContain(listing.Entries, e => e.Name == "..");
    }

    [Fact]
    public void TheSortIsAppliedAndTheParentStaysFirstEvenReversed()
    {
        using var tree = new TempTree();
        tree.AddFile("b.txt");
        tree.AddFile("a.txt");
        tree.AddDirectory("zsub");

        var reversed = new FileEntryComparer(SortMode.Name, reverse: true, directoriesFirst: true, numeric: false, caseSensitive: false);
        DirectoryListing listing = FileSystemProvider.Read(tree.Root, includeHidden: true, reversed);

        Assert.Equal(["..", "zsub", "b.txt", "a.txt"], listing.Entries.Select(e => e.Name));
    }

    [Fact]
    public void ADirectorySymlinkIsListedAsADirectoryWithoutBeingFollowed()
    {
        using var tree = new TempTree();
        string target = tree.AddDirectory("target");
        System.IO.File.WriteAllText(System.IO.Path.Combine(target, "inside.txt"), "x");
        string link = System.IO.Path.Combine(tree.Root, "link");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Creating a symlink needs a privilege this machine has not granted; nothing to test.
            return;
        }

        DirectoryListing listing = FileSystemProvider.Read(tree.Root, includeHidden: true);

        FileEntry entry = listing.Entries.Single(e => e.Name == "link");
        Assert.True(entry.IsDirectory);
        Assert.True(entry.IsReparsePoint);

        // Not followed: the link's own contents never appear in this listing.
        Assert.DoesNotContain(listing.Entries, e => e.Name == "inside.txt");
    }
}

public class FileSystemProviderPathTests
{
    [Fact]
    public void IsRootRecognisesTheRootOfTheTempPath()
    {
        string root = FileSystemProvider.GetRoot(System.IO.Path.GetTempPath());

        Assert.True(FileSystemProvider.IsRoot(root));
        Assert.False(FileSystemProvider.IsRoot(System.IO.Path.Combine(root, "somewhere")));
        Assert.False(FileSystemProvider.IsRoot(""));
        Assert.False(FileSystemProvider.IsRoot(null));
    }

    [Fact]
    public void ARootKeepsItsTrailingSeparatorAndNothingElseDoes()
    {
        string root = FileSystemProvider.GetRoot(System.IO.Path.GetTempPath());
        string nested = System.IO.Path.Combine(root, "a", "b");

        string normalisedRoot = FileSystemProvider.NormalizeDisplayPath(root);
        string normalisedNested = FileSystemProvider.NormalizeDisplayPath(nested + System.IO.Path.DirectorySeparatorChar);

        Assert.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), normalisedRoot, StringComparison.Ordinal);
        Assert.True(FileSystemProvider.IsRoot(normalisedRoot));
        Assert.False(normalisedNested.EndsWith(System.IO.Path.DirectorySeparatorChar));
        Assert.Equal(nested, normalisedNested);
    }

    [Fact]
    public void NormalizeIsForgivingOfNonsense()
    {
        Assert.Equal(string.Empty, FileSystemProvider.NormalizeDisplayPath(null));
        Assert.Equal(string.Empty, FileSystemProvider.NormalizeDisplayPath("   "));
        Assert.Equal("C:\\bad\0path", FileSystemProvider.NormalizeDisplayPath("C:\\bad\0path"));
    }

    [Fact]
    public void GetParentWalksUpAndStopsAtTheRoot()
    {
        using var tree = new TempTree();
        string sub = tree.AddDirectory("sub");

        Assert.Equal(tree.Root, FileSystemProvider.GetParent(sub));

        string root = FileSystemProvider.GetRoot(tree.Root);
        Assert.Null(FileSystemProvider.GetParent(root));
        Assert.Null(FileSystemProvider.GetParent(null));
        Assert.Null(FileSystemProvider.GetParent(""));
    }

    [Fact]
    public void DirectoryExistsAnswersWithoutThrowing()
    {
        using var tree = new TempTree();
        string file = tree.AddFile("a.txt");

        Assert.True(FileSystemProvider.DirectoryExists(tree.Root));
        Assert.False(FileSystemProvider.DirectoryExists(file));
        Assert.False(FileSystemProvider.DirectoryExists(System.IO.Path.Combine(tree.Root, "nope")));
        Assert.False(FileSystemProvider.DirectoryExists(null));
        Assert.False(FileSystemProvider.DirectoryExists("C:\\bad\0path"));
    }

    [Fact]
    public void TryResolveHandlesRelativeAbsoluteAndParentInput()
    {
        using var tree = new TempTree();
        string sub = tree.AddDirectory("sub");

        Assert.True(FileSystemProvider.TryResolve(tree.Root, "sub", out string relative));
        Assert.Equal(sub, relative);

        Assert.True(FileSystemProvider.TryResolve(sub, "..", out string up));
        Assert.Equal(tree.Root, up);

        Assert.True(FileSystemProvider.TryResolve(tree.Root, sub, out string absolute));
        Assert.Equal(sub, absolute);

        Assert.True(FileSystemProvider.TryResolve(tree.Root, "\"sub\"", out string quoted));
        Assert.Equal(sub, quoted);

        Assert.True(FileSystemProvider.TryResolve(tree.Root, "does-not-exist", out string missing));
        Assert.Equal(System.IO.Path.Combine(tree.Root, "does-not-exist"), missing);
    }

    [Fact]
    public void TryResolveRefusesEmptyInput()
    {
        Assert.False(FileSystemProvider.TryResolve("C:\\", "", out string a));
        Assert.Equal(string.Empty, a);

        Assert.False(FileSystemProvider.TryResolve("C:\\", "   ", out string b));
        Assert.Equal(string.Empty, b);

        Assert.False(FileSystemProvider.TryResolve("C:\\", "\"\"", out string c));
        Assert.Equal(string.Empty, c);
    }

    [Fact]
    public void TryResolveExpandsTheHomeShorthand()
    {
        string home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrEmpty(home))
        {
            return;
        }

        Assert.True(FileSystemProvider.TryResolve(System.IO.Path.GetTempPath(), "~", out string resolved));
        Assert.Equal(FileSystemProvider.NormalizeDisplayPath(home), resolved);
    }
}

public class DriveListTests
{
    [Fact]
    public void GetReturnsUsableItemsAndNeverThrows()
    {
        IReadOnlyList<DriveList.DriveItem> drives = DriveList.Get();

        Assert.NotNull(drives);
        Assert.All(drives, d =>
        {
            Assert.False(string.IsNullOrEmpty(d.Root));
            Assert.NotNull(d.Label);
            Assert.NotNull(d.FileSystem);
            Assert.True(d.TotalBytes >= 0);
            Assert.True(d.FreeBytes >= 0);
            Assert.True(d.UsedBytes >= 0);
            Assert.NotEqual('\0', d.Letter);
        });
    }

    [Fact]
    public void ANotReadyDriveStillAppearsButCarriesNoDetails()
    {
        foreach (DriveList.DriveItem drive in DriveList.Get().Where(d => !d.IsReady))
        {
            Assert.Equal(string.Empty, drive.Label);
            Assert.Equal(string.Empty, drive.FileSystem);
            Assert.Equal(0, drive.TotalBytes);
            Assert.Equal(0, drive.FreeBytes);
        }
    }

    [Fact]
    public void ForPathFindsTheDriveHoldingAPath()
    {
        if (DriveList.Get().Count == 0)
        {
            return;
        }

        DriveList.DriveItem? drive = DriveList.ForPath(System.IO.Path.GetTempPath());

        Assert.NotNull(drive);
        Assert.StartsWith(
            drive.Root,
            System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [Fact]
    public void ForPathIsNullForNonsense()
    {
        Assert.Null(DriveList.ForPath(null));
        Assert.Null(DriveList.ForPath("   "));
        Assert.Null(DriveList.ForPath("C:\\bad\0path"));
    }

    [Fact]
    public void DisplayNameFallsBackToTheRootWithoutALabel()
    {
        var unlabelled = new DriveList.DriveItem("D:\\", "", "NTFS", DriveType.Fixed, 100, 40, true);
        var labelled = unlabelled with { Label = "Data" };

        Assert.Equal("D:\\", unlabelled.DisplayName);
        Assert.Equal("D:\\  Data", labelled.DisplayName);
        Assert.Equal('D', labelled.Letter);
        Assert.Equal(60, labelled.UsedBytes);
    }
}

public class DirectoryListingTests
{
    [Fact]
    public void AFailedListingCarriesTheErrorAndNoEntries()
    {
        DirectoryListing listing = DirectoryListing.Failed("C:\\nope", "Access denied");

        Assert.True(listing.HasError);
        Assert.Equal("Access denied", listing.Error);
        Assert.Empty(listing.Entries);
        Assert.False(listing.HasParent);
        Assert.Null(listing.Parent);
        Assert.Equal(0, listing.Count);
    }

    [Fact]
    public void AnEmptyErrorStringCountsAsSuccess() =>
        Assert.False(new DirectoryListing("C:\\", [], "").HasError);

    [Fact]
    public void TotalsIgnoreTheParentEntryAndDirectorySizes()
    {
        FileEntry[] entries =
        [
            FileEntry.ParentOf("C:\\work\\sub"),
            new FileEntry { Name = "sub", IsDirectory = true, Size = 4096 },
            new FileEntry { Name = "a.txt", Size = 10 },
            new FileEntry { Name = "b.txt", Size = 32 },
        ];

        var listing = new DirectoryListing("C:\\work", entries);

        Assert.Equal(4, listing.Count);
        Assert.Equal(2, listing.FileCount);
        Assert.Equal(1, listing.DirectoryCount);
        Assert.Equal(42, listing.TotalBytes);
    }
}
