using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Dvopan.Files;

namespace Dvopan.Tests;

/// <summary>
/// A throwaway directory under the system temp folder, removed on dispose.
/// </summary>
internal sealed class FsProviderTempDirectory : IDisposable
{
    public FsProviderTempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-fsp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Dir(string name)
    {
        string full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public string File(string name, string content = "x")
    {
        string full = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}

/// <summary>
/// Covers the contract <see cref="FileSystemProvider"/> promises in its own documentation: a
/// directory the user may not read comes back carrying an <see cref="DirectoryListing.Error"/>
/// rather than as a successful, empty listing.
/// </summary>
public class FileSystemProviderTests
{
    // ---------------------------------------------------------------------------------------
    // The regression this file exists for.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The defect: EnumerationOptions.IgnoreInaccessible = true swallows the
    /// UnauthorizedAccessException thrown when the enumeration opens the directory being listed,
    /// not just the ones raised for individual children. An unreadable folder then reported
    /// Error == null with only the ".." entry - byte for byte what a genuinely empty folder looks
    /// like - so the panel drew a blank list and "files: 0, folders: 0" with no hint of a problem.
    /// </summary>
    [Fact]
    public void ADirectoryTheUserMayNotReadReportsAnError()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Skipped: the denial is set up through Windows ACLs. xunit 2.9 has no dynamic skip,
            // so the repository's convention is to return early.
            return;
        }

        using var temp = new FsProviderTempDirectory();
        string denied = temp.Dir("locked");
        System.IO.File.WriteAllText(Path.Combine(denied, "secret.txt"), "hidden from us");

        FileSystemAclDenial? denial = FileSystemAclDenial.TryDenyListing(denied);
        if (denial is null)
        {
            // The environment did not honour the ACL (a container without a real security
            // subsystem, say). Nothing to assert.
            return;
        }

        using (denial)
        {
            // Precondition, established without going through the code under test: the directory
            // really is unreadable now. If this ever stops throwing the assertions below would be
            // vacuous, so prove it independently first.
            Assert.Throws<UnauthorizedAccessException>(
                () => new DirectoryInfo(denied).EnumerateFileSystemInfos("*").ToList());

            DirectoryListing listing = FileSystemProvider.Read(denied, includeHidden: true, sort: null);

            Assert.True(listing.HasError, "An unreadable directory must not read as a success.");
            Assert.NotNull(listing.Error);
            Assert.Contains("Access denied", listing.Error, StringComparison.Ordinal);

            // The user must still be able to walk back out of a folder they cannot read.
            Assert.True(listing.HasParent);
            Assert.Equal(1, listing.Count);
            Assert.Equal(0, listing.FileCount);
            Assert.Equal(0, listing.DirectoryCount);
        }

        // The ACL is restored, so the same path now reads normally.
        DirectoryListing after = FileSystemProvider.Read(denied, includeHidden: true, sort: null);
        Assert.Null(after.Error);
        Assert.Equal(1, after.FileCount);
    }

    /// <summary>
    /// The other half of the same distinction: a directory that is simply empty must keep reading
    /// as a success, otherwise the fix above would just move the lie to the other case.
    /// </summary>
    [Fact]
    public void AGenuinelyEmptyDirectoryReportsNoError()
    {
        using var temp = new FsProviderTempDirectory();
        string empty = temp.Dir("empty");

        DirectoryListing listing = FileSystemProvider.Read(empty, includeHidden: true, sort: null);

        Assert.Null(listing.Error);
        Assert.False(listing.HasError);
        Assert.True(listing.HasParent);
        Assert.Equal(1, listing.Count);
        Assert.Equal(0, listing.FileCount);
        Assert.Equal(0, listing.DirectoryCount);
    }

    /// <summary>
    /// Turning IgnoreInaccessible off must not resurrect the problem it was switched on for: the
    /// enumeration is not recursive, so an unreadable child is still listed by name and only its
    /// contents stay out of reach.
    /// </summary>
    [Fact]
    public void AnUnreadableChildDoesNotSpoilTheParentListing()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Skipped: the denial is set up through Windows ACLs.
            return;
        }

        using var temp = new FsProviderTempDirectory();
        temp.File("readable.txt");
        string child = temp.Dir("locked-child");

        FileSystemAclDenial? denial = FileSystemAclDenial.TryDenyListing(child);
        if (denial is null)
        {
            return;
        }

        using (denial)
        {
            DirectoryListing listing = FileSystemProvider.Read(temp.Path, includeHidden: true, sort: null);

            Assert.Null(listing.Error);
            Assert.Equal(1, listing.FileCount);
            Assert.Equal(1, listing.DirectoryCount);
            Assert.Contains(listing.Entries, e => e.Name == "locked-child");
            Assert.Contains(listing.Entries, e => e.Name == "readable.txt");
        }
    }

    // ---------------------------------------------------------------------------------------
    // The neighbouring outcomes, so the three cases stay told apart.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AReadableDirectoryIsUnaffected()
    {
        using var temp = new FsProviderTempDirectory();
        temp.File("a.txt", "12345");
        temp.File("b.txt", "123");
        temp.Dir("sub");

        DirectoryListing listing = FileSystemProvider.Read(temp.Path, includeHidden: true, sort: null);

        Assert.Null(listing.Error);
        Assert.Equal(2, listing.FileCount);
        Assert.Equal(1, listing.DirectoryCount);
        Assert.Equal(8, listing.TotalBytes);
        Assert.Equal(4, listing.Count);
    }

    [Fact]
    public void AMissingDirectoryReportsAnError()
    {
        using var temp = new FsProviderTempDirectory();
        string missing = Path.Combine(temp.Path, "no-such-folder");

        DirectoryListing listing = FileSystemProvider.Read(missing, includeHidden: true, sort: null);

        Assert.NotNull(listing.Error);
        Assert.Contains("Cannot find", listing.Error, StringComparison.Ordinal);
        Assert.True(listing.HasParent);
        Assert.Equal(1, listing.Count);
    }

    [Fact]
    public void AnEmptyPathReportsAnError()
    {
        DirectoryListing listing = FileSystemProvider.Read("   ", includeHidden: true, sort: null);

        Assert.NotNull(listing.Error);
        Assert.Empty(listing.Entries);
    }

    [Fact]
    public void AFailedListingIsSortedHarmlessly()
    {
        using var temp = new FsProviderTempDirectory();
        string missing = Path.Combine(temp.Path, "gone");

        DirectoryListing listing = FileSystemProvider.Read(
            missing,
            includeHidden: true,
            sort: new FileEntryComparer(
                SortMode.Name,
                reverse: false,
                directoriesFirst: true,
                numeric: false,
                caseSensitive: false));

        Assert.NotNull(listing.Error);
        Assert.Equal(1, listing.Count);
        Assert.True(listing.Entries[0].IsParent);
    }
}

/// <summary>
/// Denies the current user the right to list a directory, and puts the ACL back on dispose.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class FileSystemAclDenial : IDisposable
{
    private readonly DirectoryInfo _dir;
    private readonly FileSystemAccessRule _rule;
    private bool _restored;

    private FileSystemAclDenial(DirectoryInfo dir, FileSystemAccessRule rule)
    {
        _dir = dir;
        _rule = rule;
    }

    /// <summary>
    /// Adds an explicit Deny ACE for the current user, which outranks any inherited Allow.
    /// </summary>
    /// <param name="path">The directory to lock.</param>
    /// <returns>The handle that restores the ACL, or <see langword="null"/> when it could not be set.</returns>
    public static FileSystemAclDenial? TryDenyListing(string path)
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier? user = identity.User;
            if (user is null)
            {
                return null;
            }

            var dir = new DirectoryInfo(path);

            // ListDirectory is exactly the right the enumeration needs; ReadAttributes is left
            // alone so the path still resolves and DirectoryInfo.Exists stays true.
            var rule = new FileSystemAccessRule(
                user,
                FileSystemRights.ListDirectory,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny);

            DirectorySecurity security = dir.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(rule);
            dir.SetAccessControl(security);

            return new FileSystemAclDenial(dir, rule);
        }
        catch (Exception e) when (e is UnauthorizedAccessException
                                    or PlatformNotSupportedException
                                    or NotSupportedException
                                    or IOException
                                    or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;

        try
        {
            // The owner always keeps WRITE_DAC, so removing the deny works even though reading the
            // directory does not.
            DirectorySecurity security = _dir.GetAccessControl(AccessControlSections.Access);
            security.RemoveAccessRuleSpecific(_rule);
            _dir.SetAccessControl(security);
        }
        catch (Exception e) when (e is UnauthorizedAccessException
                                    or PlatformNotSupportedException
                                    or NotSupportedException
                                    or IOException
                                    or InvalidOperationException)
        {
            // Nothing further to try; the temp directory cleanup will report what it can.
        }
    }
}
