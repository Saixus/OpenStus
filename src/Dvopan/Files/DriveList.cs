namespace Dvopan.Files;

/// <summary>
/// Enumerates the mounted drives for the Alt+F1 / Alt+F2 change-drive menu.
/// </summary>
/// <remarks>
/// The whole point of this type is that it cannot hang or throw. A disconnected network drive makes
/// every property of its <see cref="DriveInfo"/> block or throw, so nothing is read before
/// <see cref="DriveInfo.IsReady"/> says so, and every single property access is guarded on its own.
/// An unreadable drive still appears in the menu - with empty details - because the user may well
/// be trying to reconnect it.
/// </remarks>
public static class DriveList
{
    /// <summary>One mounted drive or mount point.</summary>
    /// <param name="Root">The root path, <c>"C:\"</c> or <c>"/"</c> or a mount point.</param>
    /// <param name="Label">The volume label, or an empty string when there is none.</param>
    /// <param name="FileSystem">The file system name (<c>"NTFS"</c>, <c>"ext4"</c>), or an empty string.</param>
    /// <param name="Type">The drive type.</param>
    /// <param name="TotalBytes">The capacity in bytes, or zero when unknown.</param>
    /// <param name="FreeBytes">The free space available to this user, or zero when unknown.</param>
    /// <param name="IsReady">Whether the drive answered at all.</param>
    public sealed record DriveItem(
        string Root,
        string Label,
        string FileSystem,
        DriveType Type,
        long TotalBytes,
        long FreeBytes,
        bool IsReady)
    {
        /// <summary>The drive letter on Windows, or the first character of the mount point elsewhere.</summary>
        public char Letter => Root.Length > 0 ? char.ToUpperInvariant(Root[0]) : ' ';

        /// <summary>The used space in bytes, never negative.</summary>
        public long UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;

        /// <summary>The root followed by the label when there is one, as the drive menu shows it.</summary>
        public string DisplayName =>
            Label.Length == 0 ? Root : $"{Root}  {Label}";
    }

    /// <summary>
    /// Every drive the runtime reports, in the order it reports them. Never throws, never blocks on
    /// a dead network drive.
    /// </summary>
    /// <returns>The drives; empty when they cannot be enumerated at all.</returns>
    public static IReadOnlyList<DriveItem> Get()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return [];
        }

        var items = new List<DriveItem>(drives.Length);
        foreach (DriveInfo drive in drives)
        {
            DriveItem? item = Describe(drive);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Finds the drive <paramref name="path"/> lives on: the matching root on Windows, the longest
    /// matching mount point elsewhere.
    /// </summary>
    /// <param name="path">The path to locate.</param>
    /// <returns>The drive, or <see langword="null"/> when none matches.</returns>
    public static DriveItem? ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return null;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        DriveItem? best = null;
        foreach (DriveItem item in Get())
        {
            if (item.Root.Length == 0 || !full.StartsWith(item.Root, comparison))
            {
                continue;
            }

            if (best is null || item.Root.Length > best.Root.Length)
            {
                best = item;
            }
        }

        return best;
    }

    private static DriveItem? Describe(DriveInfo drive)
    {
        string root;
        try
        {
            root = drive.RootDirectory.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            try
            {
                root = drive.Name;
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return null;
            }
        }

        DriveType type = DriveType.Unknown;
        try
        {
            type = drive.DriveType;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Leave it Unknown.
        }

        bool ready = false;
        try
        {
            ready = drive.IsReady;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A drive that will not even answer this is not ready by any useful definition.
        }

        if (!ready)
        {
            return new DriveItem(root, string.Empty, string.Empty, type, 0, 0, false);
        }

        string label = TryReadText(() => drive.VolumeLabel);
        string fileSystem = TryReadText(() => drive.DriveFormat);
        long total = TryReadNumber(() => drive.TotalSize);
        long free = TryReadNumber(() => drive.AvailableFreeSpace);

        return new DriveItem(root, label, fileSystem, type, total, free, true);
    }

    private static string TryReadText(Func<string?> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or PlatformNotSupportedException)
        {
            return string.Empty;
        }
    }

    private static long TryReadNumber(Func<long> read)
    {
        try
        {
            long value = read();
            return value > 0 ? value : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or PlatformNotSupportedException)
        {
            return 0;
        }
    }
}
