using System.Text;
using Dvopan.Core;
using Dvopan.Files;
using Dvopan.Operations;

namespace Dvopan.Tests;

/// <summary>
/// A throwaway directory under the system temp folder, removed on dispose even when the test left
/// read-only files behind.
/// </summary>
internal sealed class OpsTempDirectory : IDisposable
{
    public OpsTempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public string Dir(params string[] parts)
    {
        string full = Combine(parts);
        Directory.CreateDirectory(full);
        return full;
    }

    public string File(string relative, string content = "")
    {
        string full = Combine(relative);
        string? parent = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        System.IO.File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    public string Binary(string relative, byte[] content)
    {
        string full = Combine(relative);
        string? parent = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Clear(Path);
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leaked handle on a CI box must not fail an otherwise green test.
        }
    }

    private static void Clear(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }
}

/// <summary>Helpers shared by the operation tests.</summary>
internal static class OpsTestHelpers
{
    /// <summary>Builds the panel entry the operations take, straight from the file system.</summary>
    public static FileEntry Entry(string path)
    {
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return new FileEntry
            {
                Name = info.Name,
                FullPath = info.FullName,
                IsDirectory = true,
                Modified = info.LastWriteTime,
                Created = info.CreationTime,
                Accessed = info.LastAccessTime,
                Attributes = info.Attributes,
            };
        }

        var file = new FileInfo(path);
        return new FileEntry
        {
            Name = file.Name,
            FullPath = file.FullName,
            IsDirectory = false,
            Size = file.Exists ? file.Length : 0,
            Modified = file.LastWriteTime,
            Created = file.CreationTime,
            Accessed = file.LastAccessTime,
            Attributes = file.Exists ? file.Attributes : 0,
        };
    }

    public static FileEntry[] Entries(params string[] paths) => [.. paths.Select(Entry)];

    /// <summary>Options with the throttle switched off so tests see every progress callback.</summary>
    public static OperationOptions Options(Action<OperationOptions>? tweak = null)
    {
        var options = new OperationOptions
        {
            UseRecycleBin = false,
            ProgressIntervalMs = 0,
        };

        tweak?.Invoke(options);
        return options;
    }

    /// <summary>
    /// Creates a directory link, falling back to a junction on Windows because
    /// <see cref="Directory.CreateSymbolicLink"/> needs a privilege a developer box often withholds.
    /// </summary>
    /// <returns><see langword="false"/> when this machine allows neither, so the test can bow out.</returns>
    public static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Fall through to the junction attempt below.
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(15_000);

            return Directory.Exists(link) &&
                   (System.IO.File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>An overwrite prompt that always answers the same way and counts its calls.</summary>
    public static OverwritePrompt Answer(DialogResult answer, Action? onCall = null) =>
        (FileEntry source, FileInfo target, ref string newName) =>
        {
            onCall?.Invoke();
            return answer;
        };
}

public class FileOperationsCopyTests
{
    [Fact]
    public void CopyingANestedTreeReproducesEveryFileAndFolder()
    {
        using var temp = new OpsTempDirectory();

        temp.File(Path.Combine("src", "top.txt"), "top");
        temp.File(Path.Combine("src", "sub", "middle.txt"), "middle");
        temp.File(Path.Combine("src", "sub", "deep", "bottom.txt"), "bottom");
        temp.Dir("src", "sub", "empty");
        string destination = temp.Dir("dest");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("src")),
            destination,
            OpsTestHelpers.Options());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(3, result.FilesProcessed);
        Assert.Equal("top", File.ReadAllText(temp.Combine("dest", "src", "top.txt")));
        Assert.Equal("middle", File.ReadAllText(temp.Combine("dest", "src", "sub", "middle.txt")));
        Assert.Equal("bottom", File.ReadAllText(temp.Combine("dest", "src", "sub", "deep", "bottom.txt")));
        Assert.True(Directory.Exists(temp.Combine("dest", "src", "sub", "empty")));

        // The source is untouched by a copy.
        Assert.True(File.Exists(temp.Combine("src", "top.txt")));
    }

    [Fact]
    public void CopyingASingleFileToAPathThatDoesNotExistUsesItAsTheNewName()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("renamed.txt"),
            OpsTestHelpers.Options());

        Assert.True(result.Success);
        Assert.Equal("hello", File.ReadAllText(temp.Combine("renamed.txt")));
        Assert.False(Directory.Exists(temp.Combine("renamed.txt")));
    }

    [Fact]
    public void CopyingAFolderToAPathThatDoesNotExistRenamesItOnTheWay()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "a.txt"), "a");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("src")),
            temp.Combine("renamed"),
            OpsTestHelpers.Options());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("a", File.ReadAllText(temp.Combine("renamed", "a.txt")));
        Assert.False(Directory.Exists(temp.Combine("renamed", "src")));
    }

    [Fact]
    public void ATrailingSeparatorForcesTheDestinationToBeAFolder()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("box") + Path.DirectorySeparatorChar,
            OpsTestHelpers.Options());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("hello", File.ReadAllText(temp.Combine("box", "a.txt")));
    }

    [Fact]
    public void SeveralSourcesAlwaysLandInsideTheDestinationFolder()
    {
        using var temp = new OpsTempDirectory();
        string one = temp.File("one.txt", "1");
        string two = temp.File("two.txt", "2");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(one, two),
            temp.Combine("dest"),
            OpsTestHelpers.Options());

        Assert.True(result.Success);
        Assert.Equal("1", File.ReadAllText(temp.Combine("dest", "one.txt")));
        Assert.Equal("2", File.ReadAllText(temp.Combine("dest", "two.txt")));
    }

    [Fact]
    public void CopyingAFolderIntoItsOwnSubfolderIsRefused()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("root", "a.txt"), "a");
        string inside = temp.Dir("root", "sub");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("root")),
            inside,
            OpsTestHelpers.Options());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("into itself", StringComparison.Ordinal));
        Assert.Equal(0, result.FilesProcessed);
        Assert.False(Directory.Exists(temp.Combine("root", "sub", "root")));
    }

    [Fact]
    public void ASiblingWithASharedNamePrefixIsNotMistakenForASubfolder()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("data", "a.txt"), "a");
        string sibling = temp.Dir("data2");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("data")),
            sibling,
            OpsTestHelpers.Options());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("a", File.ReadAllText(temp.Combine("data2", "data", "a.txt")));
    }

    [Fact]
    public void CopyingAFolderOntoItselfIsRefused()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("root", "a.txt"), "a");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("root")),
            temp.Combine("root"),
            OpsTestHelpers.Options());

        Assert.False(result.Success);
        Assert.Equal(0, result.FilesProcessed);
    }

    [Fact]
    public void CopyingAFileOntoItselfIsRefused()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "a");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            source,
            OpsTestHelpers.Options());

        Assert.False(result.Success);
        Assert.Equal("a", File.ReadAllText(source));
    }

    [Fact]
    public void TimestampsAndAttributesAreCarriedOver()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        var when = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
        File.SetLastWriteTime(source, when);

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("copy.txt"),
            OpsTestHelpers.Options());

        Assert.True(result.Success);
        Assert.Equal(when, File.GetLastWriteTime(temp.Combine("copy.txt")));
    }

    [Fact]
    public void ProgressIsCountedUpFrontAndEndsFull()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "a.txt"), new string('a', 100));
        temp.File(Path.Combine("src", "b", "b.txt"), new string('b', 50));

        var progress = new OperationProgress();
        int callbacks = 0;

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("src")),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            progress,
            () => callbacks++);

        Assert.True(result.Success);
        Assert.True(progress.TotalsKnown);
        Assert.Equal(2, progress.TotalFiles);
        Assert.Equal(150, progress.TotalBytes);
        Assert.Equal(2, progress.DoneFiles);
        Assert.Equal(150, progress.DoneBytes);
        Assert.Equal(1d, progress.TotalFraction, 6);
        Assert.True(callbacks > 0);
        Assert.Equal("Copy", progress.Title);
    }

    [Fact]
    public void CancellingMidwayStopsAndRemovesThePartialFile()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.Binary("big.bin", new byte[512 * 1024]);

        var progress = new OperationProgress();
        int callbacks = 0;

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("copy.bin"),
            OpsTestHelpers.Options(o => o.BufferSize = 4096),
            progress,
            () =>
            {
                if (++callbacks >= 3)
                {
                    progress.Cancel();
                }
            });

        Assert.True(result.Cancelled);
        Assert.False(result.Success);
        Assert.False(File.Exists(temp.Combine("copy.bin")));
    }

    [Fact]
    public void NothingThrowsForEmptyOrNonsenseInput()
    {
        using var temp = new OpsTempDirectory();

        Assert.True(FileOperations.Copy([], temp.Path, OpsTestHelpers.Options()).Success);
        Assert.False(FileOperations.Copy(OpsTestHelpers.Entries(temp.File("a.txt")), "  ", OpsTestHelpers.Options()).Success);

        var missing = new FileEntry { Name = "gone.txt", FullPath = temp.Combine("gone.txt") };
        OperationResult result = FileOperations.Copy([missing], temp.Combine("dest"), OpsTestHelpers.Options());
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("no longer exists", StringComparison.Ordinal));
    }

    [Fact]
    public void TheParentEntryIsIgnored()
    {
        using var temp = new OpsTempDirectory();
        FileEntry parent = FileEntry.ParentOf(temp.Path);

        OperationResult result = FileOperations.Copy([parent], temp.Combine("dest"), OpsTestHelpers.Options());

        Assert.True(result.Success);
        Assert.Equal(0, result.FilesProcessed);
        Assert.False(Directory.Exists(temp.Combine("dest")));
    }

    [Fact]
    public void CopyingADirectoryLinkRecreatesTheLinkOrReportsAnError()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("real", "keep.txt"), "keep");
        temp.Dir("box");

        if (!OpsTestHelpers.TryCreateDirectoryLink(temp.Combine("box", "link"), temp.Combine("real")))
        {
            return;
        }

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("box", "link")),
            temp.Dir("dest"),
            OpsTestHelpers.Options());

        string copied = temp.Combine("dest", "link");
        if (result.Success)
        {
            // This machine grants the symbolic-link privilege: the copy is a link to the same
            // place, not a plain folder pretending to be one.
            Assert.True((File.GetAttributes(copied) & FileAttributes.ReparsePoint) != 0);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(copied, "keep.txt")));
        }
        else
        {
            // The privilege is withheld here (a junction cannot be recreated without it): the
            // failure is reported, and no empty plain folder stands in for the link.
            Assert.NotEmpty(result.Errors);
            Assert.False(Directory.Exists(copied));
        }

        // Either way the source link is untouched by a copy.
        Assert.True((File.GetAttributes(temp.Combine("box", "link")) & FileAttributes.ReparsePoint) != 0);
    }
}

public class FileOperationsOverwriteTests
{
    [Fact]
    public void SkipLeavesTheExistingFileAlone()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "new");
        string existing = temp.File(Path.Combine("dest", "a.txt"), "old");

        int asked = 0;
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Skip, () => asked++));

        Assert.Equal(1, asked);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FilesProcessed);
        Assert.Equal("old", File.ReadAllText(existing));
    }

    [Fact]
    public void SkipAllIsOnlyAskedOnce()
    {
        using var temp = new OpsTempDirectory();
        string one = temp.File("a.txt", "new-a");
        string two = temp.File("b.txt", "new-b");
        temp.File(Path.Combine("dest", "a.txt"), "old-a");
        temp.File(Path.Combine("dest", "b.txt"), "old-b");

        int asked = 0;
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(one, two),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.SkipAll, () => asked++));

        Assert.Equal(1, asked);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal("old-a", File.ReadAllText(temp.Combine("dest", "a.txt")));
        Assert.Equal("old-b", File.ReadAllText(temp.Combine("dest", "b.txt")));
    }

    [Fact]
    public void AllIsOnlyAskedOnceAndOverwritesEverything()
    {
        using var temp = new OpsTempDirectory();
        string one = temp.File("a.txt", "new-a");
        string two = temp.File("b.txt", "new-b");
        temp.File(Path.Combine("dest", "a.txt"), "old-a");
        temp.File(Path.Combine("dest", "b.txt"), "old-b");

        int asked = 0;
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(one, two),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.All, () => asked++));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(1, asked);
        Assert.Equal(2, result.FilesProcessed);
        Assert.Equal("new-a", File.ReadAllText(temp.Combine("dest", "a.txt")));
        Assert.Equal("new-b", File.ReadAllText(temp.Combine("dest", "b.txt")));
    }

    [Fact]
    public void OverwriteAnswersForOneFileOnly()
    {
        using var temp = new OpsTempDirectory();
        string one = temp.File("a.txt", "new-a");
        string two = temp.File("b.txt", "new-b");
        temp.File(Path.Combine("dest", "a.txt"), "old-a");
        temp.File(Path.Combine("dest", "b.txt"), "old-b");

        int asked = 0;
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(one, two),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Overwrite, () => asked++));

        Assert.True(result.Success);
        Assert.Equal(2, asked);
        Assert.Equal("new-a", File.ReadAllText(temp.Combine("dest", "a.txt")));
    }

    [Fact]
    public void RenameWritesUnderTheNameThePromptSupplied()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "new");
        temp.File(Path.Combine("dest", "a.txt"), "old");

        OverwritePrompt prompt = (FileEntry _, FileInfo _, ref string newName) =>
        {
            newName = "a-copy.txt";
            return DialogResult.Rename;
        };

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: prompt);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("old", File.ReadAllText(temp.Combine("dest", "a.txt")));
        Assert.Equal("new", File.ReadAllText(temp.Combine("dest", "a-copy.txt")));
    }

    [Fact]
    public void RenameWithNoNewNameFallsBackToAUniqueName()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "new");
        temp.File(Path.Combine("dest", "a.txt"), "old");

        OverwritePrompt prompt = (FileEntry _, FileInfo _, ref string newName) =>
        {
            newName = string.Empty;
            return DialogResult.Rename;
        };

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: prompt);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("old", File.ReadAllText(temp.Combine("dest", "a.txt")));
        Assert.Equal("new", File.ReadAllText(temp.Combine("dest", "a (2).txt")));
    }

    [Fact]
    public void AppendAddsToTheExistingFile()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "-tail");
        temp.File(Path.Combine("dest", "a.txt"), "head");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Append));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("head-tail", File.ReadAllText(temp.Combine("dest", "a.txt")));
    }

    [Fact]
    public void AFailedAppendCutsTheTargetBackToItsOriginalContent()
    {
        using var temp = new OpsTempDirectory();
        temp.Binary("big.bin", new byte[512 * 1024]);
        string target = temp.File(Path.Combine("dest", "big.bin"), "head");

        var progress = new OperationProgress();
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(temp.Combine("big.bin")),
            temp.Combine("dest"),
            OpsTestHelpers.Options(o => o.BufferSize = 4096),
            progress,
            () =>
            {
                // Only pull the plug once a few blocks have really landed, so the cut has
                // something to cut.
                if (progress.CurrentFileDone >= 4 * 4096)
                {
                    progress.Cancel();
                }
            },
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Append));

        // The partial tail is gone: a retry would append after the real content, not after the
        // leftovers of the failed attempt.
        Assert.True(result.Cancelled);
        Assert.Equal("head", File.ReadAllText(target));
    }

    [Fact]
    public void CancelFromTheOverwritePromptStopsTheWholeOperation()
    {
        using var temp = new OpsTempDirectory();
        string one = temp.File("a.txt", "new-a");
        string two = temp.File("b.txt", "new-b");
        temp.File(Path.Combine("dest", "a.txt"), "old-a");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(one, two),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Cancel));

        Assert.True(result.Cancelled);
        Assert.Equal("old-a", File.ReadAllText(temp.Combine("dest", "a.txt")));
        Assert.False(File.Exists(temp.Combine("dest", "b.txt")));
    }

    [Fact]
    public void WithoutAPromptAnExistingFileIsLeftAlone()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "new");
        temp.File(Path.Combine("dest", "a.txt"), "old");

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options());

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("old", File.ReadAllText(temp.Combine("dest", "a.txt")));
    }

    [Fact]
    public void ConfirmOverwriteOffOverwritesWithoutAsking()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "new");
        temp.File(Path.Combine("dest", "a.txt"), "old");

        int asked = 0;
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options(o => o.ConfirmOverwrite = false),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Skip, () => asked++));

        Assert.True(result.Success);
        Assert.Equal(0, asked);
        Assert.Equal("new", File.ReadAllText(temp.Combine("dest", "a.txt")));
    }

    [Fact]
    public void AReadOnlyTargetIsOverwrittenOnceTheUserAgrees()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "new");
        string target = temp.File(Path.Combine("dest", "a.txt"), "old");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            temp.Combine("dest"),
            OpsTestHelpers.Options(),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Overwrite));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("new", File.ReadAllText(target));
    }
}

public class FileOperationsErrorPromptTests
{
    /// <summary>
    /// A destination whose parent is an existing <em>file</em> fails to be created on every
    /// platform, which makes it the portable way to drive the error prompt.
    /// </summary>
    private static string BlockedTarget(OpsTempDirectory temp)
    {
        temp.File("blocker", "in the way");
        return temp.Combine("blocker", "child.txt");
    }

    [Fact]
    public void RetryRunsTheStepAgain()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        string target = BlockedTarget(temp);

        int asked = 0;
        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            target,
            OpsTestHelpers.Options(),
            onError: (op, path, error) =>
            {
                asked++;

                // Clear the obstruction and let the operation try again.
                File.Delete(temp.Combine("blocker"));
                return DialogResult.Retry;
            });

        Assert.Equal(1, asked);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("hello", File.ReadAllText(target));
    }

    [Fact]
    public void SkipRecordsTheErrorAndCarriesOn()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        string target = BlockedTarget(temp);

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            target,
            OpsTestHelpers.Options(),
            onError: (op, path, error) => DialogResult.Skip);

        Assert.False(result.Success);
        Assert.False(result.Cancelled);
        Assert.Single(result.Errors);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void CancelStopsTheOperation()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        string target = BlockedTarget(temp);

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            target,
            OpsTestHelpers.Options(),
            onError: (op, path, error) => DialogResult.Cancel);

        Assert.True(result.Cancelled);
    }

    [Fact]
    public void WithoutAnErrorPromptTheFailureIsRecordedAndSkipped()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        string target = BlockedTarget(temp);

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            target,
            OpsTestHelpers.Options());

        Assert.False(result.Success);
        Assert.False(result.Cancelled);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void APromptThatThrowsCancelsInsteadOfEscaping()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        string target = BlockedTarget(temp);

        OperationResult result = FileOperations.Copy(
            OpsTestHelpers.Entries(source),
            target,
            OpsTestHelpers.Options(),
            onError: (op, path, error) => throw new InvalidOperationException("boom"));

        Assert.True(result.Cancelled);
        Assert.Contains(result.Errors, e => e.Message.Contains("boom", StringComparison.Ordinal));
    }
}

public class FileOperationsMoveTests
{
    [Fact]
    public void MovingWithinAVolumeUsesTheRenameFastPath()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        var when = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
        File.SetLastWriteTime(source, when);

        // Timestamp preservation is switched off, so a surviving old timestamp can only mean the
        // file was renamed rather than re-written.
        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(source),
            temp.Combine("moved.txt"),
            OpsTestHelpers.Options(o => o.PreserveTimestamps = false));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(temp.Combine("moved.txt")));
        Assert.Equal(when, File.GetLastWriteTime(temp.Combine("moved.txt")));
    }

    [Fact]
    public void ForcingTheCopyThenDeletePathRewritesTheFile()
    {
        using var temp = new OpsTempDirectory();
        string source = temp.File("a.txt", "hello");
        var when = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
        File.SetLastWriteTime(source, when);

        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(source),
            temp.Combine("moved.txt"),
            OpsTestHelpers.Options(o =>
            {
                o.PreserveTimestamps = false;
                o.ForceCopyThenDelete = true;
            }));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(temp.Combine("moved.txt")));
        Assert.NotEqual(when, File.GetLastWriteTime(temp.Combine("moved.txt")));
    }

    [Fact]
    public void MovingATreeAcrossASimulatedVolumeCopiesThenDeletes()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "top.txt"), "top");
        temp.File(Path.Combine("src", "sub", "middle.txt"), "middle");
        temp.Dir("src", "sub", "empty");
        string destination = temp.Dir("dest");

        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(temp.Combine("src")),
            destination,
            OpsTestHelpers.Options(o => o.ForceCopyThenDelete = true));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(Directory.Exists(temp.Combine("src")));
        Assert.Equal("top", File.ReadAllText(temp.Combine("dest", "src", "top.txt")));
        Assert.Equal("middle", File.ReadAllText(temp.Combine("dest", "src", "sub", "middle.txt")));
        Assert.True(Directory.Exists(temp.Combine("dest", "src", "sub", "empty")));
        Assert.Equal(2, result.FilesProcessed);
    }

    [Fact]
    public void MovingATreeWithinAVolumeRelocatesItWhole()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "top.txt"), "top");
        temp.File(Path.Combine("src", "sub", "middle.txt"), "middle");
        string destination = temp.Dir("dest");

        var progress = new OperationProgress();
        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(temp.Combine("src")),
            destination,
            OpsTestHelpers.Options(),
            progress);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(Directory.Exists(temp.Combine("src")));
        Assert.Equal("middle", File.ReadAllText(temp.Combine("dest", "src", "sub", "middle.txt")));

        // The fast path still has to account for the files it never read.
        Assert.Equal(2, result.FilesProcessed);
        Assert.Equal(2, progress.DoneFiles);
        Assert.Equal(progress.TotalBytes, progress.DoneBytes);
    }

    [Fact]
    public void MergingIntoAnExistingFolderKeepsWhatIsAlreadyThere()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "new.txt"), "new");
        temp.File(Path.Combine("dest", "src", "old.txt"), "old");

        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(temp.Combine("src")),
            temp.Combine("dest"),
            OpsTestHelpers.Options());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("old", File.ReadAllText(temp.Combine("dest", "src", "old.txt")));
        Assert.Equal("new", File.ReadAllText(temp.Combine("dest", "src", "new.txt")));
        Assert.False(Directory.Exists(temp.Combine("src")));
    }

    [Fact]
    public void ASkippedFileLeavesTheSourceFolderBehind()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "a.txt"), "new");
        temp.File(Path.Combine("dest", "src", "a.txt"), "old");

        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(temp.Combine("src")),
            temp.Combine("dest"),
            OpsTestHelpers.Options(o => o.ForceCopyThenDelete = true),
            onOverwrite: OpsTestHelpers.Answer(DialogResult.Skip));

        // One skip for the file, one for the folder that could not be emptied.
        Assert.Equal(2, result.SkippedCount);
        Assert.True(File.Exists(temp.Combine("src", "a.txt")));
        Assert.Equal("old", File.ReadAllText(temp.Combine("dest", "src", "a.txt")));
    }

    [Fact]
    public void MovingAFolderIntoItsOwnSubfolderIsRefused()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("root", "a.txt"), "a");

        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(temp.Combine("root")),
            temp.Dir("root", "sub"),
            OpsTestHelpers.Options());

        Assert.False(result.Success);
        Assert.True(File.Exists(temp.Combine("root", "a.txt")));
    }

    [Fact]
    public void MovingADirectoryLinkAcrossASimulatedVolumeNeverDestroysTheSourceLink()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("real", "keep.txt"), "keep");
        temp.Dir("box");

        if (!OpsTestHelpers.TryCreateDirectoryLink(temp.Combine("box", "link"), temp.Combine("real")))
        {
            return;
        }

        OperationResult result = FileOperations.Move(
            OpsTestHelpers.Entries(temp.Combine("box", "link")),
            temp.Dir("dest"),
            OpsTestHelpers.Options(o => o.ForceCopyThenDelete = true));

        string moved = temp.Combine("dest", "link");
        if (result.Success)
        {
            // The link travelled whole: recreated at the destination, removed from the source.
            Assert.True((File.GetAttributes(moved) & FileAttributes.ReparsePoint) != 0);
            Assert.False(Directory.Exists(temp.Combine("box", "link")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(moved, "keep.txt")));
        }
        else
        {
            // Recreation failed, so nothing was transferred - the source link must survive.
            Assert.True((File.GetAttributes(temp.Combine("box", "link")) & FileAttributes.ReparsePoint) != 0);
            Assert.False(Directory.Exists(moved));
        }

        // Whatever happened to the link, the folder it points at is never harmed.
        Assert.Equal("keep", File.ReadAllText(temp.Combine("real", "keep.txt")));
    }
}

public class FileOperationsDeleteTests
{
    [Fact]
    public void DeletingATreePermanentlyRemovesEverything()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("src", "a.txt"), "a");
        temp.File(Path.Combine("src", "sub", "b.txt"), "b");
        temp.Dir("src", "empty");

        var progress = new OperationProgress();
        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(temp.Combine("src")),
            OpsTestHelpers.Options(),
            progress);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(Directory.Exists(temp.Combine("src")));
        Assert.Equal(2, result.FilesProcessed);
        Assert.Equal(3, result.DirectoriesProcessed);
        Assert.Equal(2, progress.TotalFiles);
        Assert.Equal(2, progress.DoneFiles);
    }

    [Fact]
    public void DeletingSeveralFilesRemovesThemAll()
    {
        using var temp = new OpsTempDirectory();
        string one = temp.File("a.txt", "a");
        string two = temp.File("b.txt", "b");

        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(one, two),
            OpsTestHelpers.Options());

        Assert.True(result.Success);
        Assert.False(File.Exists(one));
        Assert.False(File.Exists(two));
        Assert.Equal(2, result.FilesProcessed);
    }

    [Fact]
    public void DeletingAFolderHoldingALinkRemovesTheLinkAndNotItsTarget()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("outside", "keep.txt"), "keep");
        temp.Dir("box");

        if (!OpsTestHelpers.TryCreateDirectoryLink(temp.Combine("box", "link"), temp.Combine("outside")))
        {
            return;
        }

        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(temp.Combine("box")),
            OpsTestHelpers.Options());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(Directory.Exists(temp.Combine("box")));
        Assert.Equal("keep", File.ReadAllText(temp.Combine("outside", "keep.txt")));
    }

    [Fact]
    public void AReadOnlyFileIsConfirmedThroughTheErrorPrompt()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "a");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        int asked = 0;
        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(path),
            OpsTestHelpers.Options(),
            onError: (op, p, error) =>
            {
                asked++;
                return DialogResult.Retry;
            });

        Assert.Equal(1, asked);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AReadOnlyFileSurvivesWhenTheAnswerIsSkip()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "a");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(path),
            OpsTestHelpers.Options(),
            onError: (op, p, error) => DialogResult.Skip);

        Assert.True(File.Exists(path));
        Assert.Equal(1, result.SkippedCount);

        File.SetAttributes(path, FileAttributes.Normal);
    }

    [Fact]
    public void ConfirmationCanBeSwitchedOffForReadOnlyFiles()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "a");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        int asked = 0;
        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(path),
            OpsTestHelpers.Options(o => o.ConfirmReadOnlyDelete = false),
            onError: (op, p, error) =>
            {
                asked++;
                return DialogResult.Skip;
            });

        Assert.Equal(0, asked);
        Assert.True(result.Success);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeletingThroughTheRecycleBinRemovesTheFileOnWindows()
    {
        if (!RecycleBin.IsAvailable)
        {
            // No recycle bin on this platform; the permanent path is covered by the tests above.
            return;
        }

        using var temp = new OpsTempDirectory();
        string path = temp.File("oc-recycle-test.txt", "recycle me");

        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(path),
            OpsTestHelpers.Options(o => o.UseRecycleBin = true));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(File.Exists(path));
        Assert.Equal(1, result.FilesProcessed);
    }

    [Fact]
    public void ARecycledReadOnlyFileIsConfirmedThroughTheErrorPrompt()
    {
        if (!RecycleBin.IsAvailable)
        {
            // No recycle bin, no recycle path; the permanent path's confirmation is covered above.
            return;
        }

        using var temp = new OpsTempDirectory();
        string path = temp.File("oc-recycle-readonly.txt", "a");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        int asked = 0;
        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries(path),
            OpsTestHelpers.Options(o => o.UseRecycleBin = true),
            onError: (op, p, error) =>
            {
                asked++;
                return DialogResult.Skip;
            });

        // The recycle path asks the same read-only question the permanent path does; answering
        // Skip keeps the file out of the bin.
        Assert.Equal(1, asked);
        Assert.Equal(1, result.SkippedCount);
        Assert.True(File.Exists(path));

        File.SetAttributes(path, FileAttributes.Normal);
    }

    [Fact]
    public void RecycleBinReportsFailureRatherThanThrowing()
    {
        bool ok = RecycleBin.TryDelete(
            Path.Combine(Path.GetTempPath(), "oc-does-not-exist-" + Guid.NewGuid().ToString("N")),
            out string error);

        if (!RecycleBin.IsAvailable)
        {
            Assert.False(ok);
            Assert.Contains("recycle bin", error, StringComparison.OrdinalIgnoreCase);
            return;
        }

        // The shell has been inconsistent about missing files across Windows versions; all this
        // test guarantees is that it reports rather than throws.
        Assert.True(ok || error.Length > 0);
    }

    [Fact]
    public void AnEmptyListIsAccepted()
    {
        Assert.True(RecycleBin.TryDelete([], out string error));
        Assert.Equal(string.Empty, error);
        Assert.True(FileOperations.Delete([], OpsTestHelpers.Options()).Success);
    }

    [Fact]
    public void CancellingADeleteStopsIt()
    {
        using var temp = new OpsTempDirectory();
        for (int i = 0; i < 20; i++)
        {
            temp.File($"f{i}.txt", "x");
        }

        var progress = new OperationProgress();
        progress.Cancel();

        OperationResult result = FileOperations.Delete(
            OpsTestHelpers.Entries([.. Directory.GetFiles(temp.Path)]),
            OpsTestHelpers.Options(),
            progress);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.FilesProcessed);
        Assert.Equal(20, Directory.GetFiles(temp.Path).Length);
    }
}

public class FileOperationsCreateAndRenameTests
{
    [Fact]
    public void CreateDirectoryMakesTheWholeChain()
    {
        using var temp = new OpsTempDirectory();

        OperationResult result = FileOperations.CreateDirectory(temp.Combine("a", "b", "c"));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(Directory.Exists(temp.Combine("a", "b", "c")));
    }

    [Fact]
    public void CreateDirectoryRefusesAnExistingName()
    {
        using var temp = new OpsTempDirectory();
        temp.Dir("a");
        temp.File("b.txt", "b");

        Assert.False(FileOperations.CreateDirectory(temp.Combine("a")).Success);
        Assert.False(FileOperations.CreateDirectory(temp.Combine("b.txt")).Success);
        Assert.False(FileOperations.CreateDirectory("   ").Success);
        Assert.False(FileOperations.CreateDirectory(string.Empty).Success);
    }

    [Fact]
    public void RenamingAFileChangesItsName()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("old.txt", "content");

        OperationResult result = FileOperations.Rename(OpsTestHelpers.Entry(path), "new.txt");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(File.Exists(path));
        Assert.Equal("content", File.ReadAllText(temp.Combine("new.txt")));
    }

    [Fact]
    public void RenamingAFolderChangesItsName()
    {
        using var temp = new OpsTempDirectory();
        temp.File(Path.Combine("old", "a.txt"), "a");

        OperationResult result = FileOperations.Rename(OpsTestHelpers.Entry(temp.Combine("old")), "new");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(Directory.Exists(temp.Combine("old")));
        Assert.Equal("a", File.ReadAllText(temp.Combine("new", "a.txt")));
    }

    [Fact]
    public void RenamingWithARelativePathMovesTheFile()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "content");
        temp.Dir("box");

        OperationResult result = FileOperations.Rename(
            OpsTestHelpers.Entry(path),
            Path.Combine("box", "a.txt"));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("content", File.ReadAllText(temp.Combine("box", "a.txt")));
    }

    [Fact]
    public void RenamingOntoAnExistingNameIsRefused()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "a");
        temp.File("b.txt", "b");

        OperationResult result = FileOperations.Rename(OpsTestHelpers.Entry(path), "b.txt");

        Assert.False(result.Success);
        Assert.Equal("b", File.ReadAllText(temp.Combine("b.txt")));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RenamingRejectsTheParentEntryAndEmptyNames()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "a");

        Assert.False(FileOperations.Rename(FileEntry.ParentOf(temp.Path), "x").Success);
        Assert.False(FileOperations.Rename(OpsTestHelpers.Entry(path), "   ").Success);
        Assert.False(FileOperations.Rename(OpsTestHelpers.Entry(path), string.Empty).Success);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RenamingToTheSameNameDoesNothingAndSucceeds()
    {
        using var temp = new OpsTempDirectory();
        string path = temp.File("a.txt", "a");

        OperationResult result = FileOperations.Rename(OpsTestHelpers.Entry(path), "a.txt");

        Assert.True(result.Success);
        Assert.Equal("a", File.ReadAllText(path));
    }
}

public class OperationResultAndProgressTests
{
    [Fact]
    public void AFreshResultIsASuccess()
    {
        var result = new OperationResult("Copy");

        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Null(result.FirstError);
    }

    [Fact]
    public void ErrorsAndCancellationBothClearSuccess()
    {
        var result = new OperationResult("Copy");
        result.AddError("a", "broken");

        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Equal("broken", result.FirstError!.Message);
        Assert.Equal("Copy", result.FirstError.Operation);

        var cancelled = new OperationResult("Move").MarkCancelled();
        Assert.False(cancelled.Success);
        Assert.True(cancelled.Cancelled);
    }

    [Fact]
    public void ExceptionsAreDescribedInPlainLanguage()
    {
        Assert.Equal("Access denied", OperationResult.Describe(new UnauthorizedAccessException()));
        Assert.Equal("The file no longer exists", OperationResult.Describe(new FileNotFoundException()));
        Assert.Equal("The folder no longer exists", OperationResult.Describe(new DirectoryNotFoundException()));
        Assert.Equal("boom", OperationResult.Describe(new IOException("boom")));
    }

    [Fact]
    public void FractionsStayBetweenZeroAndOne()
    {
        var progress = new OperationProgress { TotalBytes = 100, DoneBytes = 250 };
        Assert.Equal(1d, progress.TotalFraction, 6);

        progress.DoneBytes = -5;
        Assert.Equal(0d, progress.TotalFraction, 6);

        progress.TotalBytes = 0;
        progress.TotalFiles = 4;
        progress.DoneFiles = 1;
        Assert.Equal(0.25d, progress.TotalFraction, 6);

        progress.TotalFiles = 0;
        Assert.Equal(0d, progress.TotalFraction, 6);
    }

    [Fact]
    public void CompletingAFileChargesWhatWasNeverStreamed()
    {
        var progress = new OperationProgress();
        progress.BeginFile("a", "b", 100);
        progress.Advance(40);
        Assert.Equal(0.4d, progress.FileFraction, 6);

        progress.CompleteFile();
        Assert.Equal(100, progress.DoneBytes);
        Assert.Equal(1, progress.DoneFiles);

        progress.Reset();
        Assert.Equal(0, progress.DoneBytes);
        Assert.False(progress.Cancelled);
    }

    [Fact]
    public void OptionsClampAndCloneIndependently()
    {
        var options = new OperationOptions { BufferSize = 1, ProgressIntervalMs = -5 };
        Assert.Equal(4096, options.BufferSize);
        Assert.Equal(0, options.ProgressIntervalMs);

        options.BufferSize = 1 << 30;
        Assert.Equal(16 * 1024 * 1024, options.BufferSize);

        OperationOptions copy = options.Clone();
        copy.UseRecycleBin = !options.UseRecycleBin;
        Assert.NotEqual(options.UseRecycleBin, copy.UseRecycleBin);
    }

    [Fact]
    public void OptionsFollowTheUserSettings()
    {
        var settings = new Settings { UseRecycleBin = false, ConfirmOverwrite = false, ConfirmDelete = false };
        OperationOptions options = OperationOptions.FromSettings(settings);

        Assert.False(options.UseRecycleBin);
        Assert.False(options.ConfirmOverwrite);
        Assert.False(options.ConfirmReadOnlyDelete);
        Assert.True(OperationOptions.FromSettings(null).UseRecycleBin);
    }
}
