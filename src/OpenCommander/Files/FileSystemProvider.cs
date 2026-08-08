namespace OpenCommander.Files;

/// <summary>
/// Reads directories for the panels, and answers the path questions the panels ask.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here throws. A folder the user may not read, a disconnected drive or a path that has
/// stopped existing all come back as a <see cref="DirectoryListing"/> carrying an
/// <see cref="DirectoryListing.Error"/> and just the <c>".."</c> entry, because a file manager that
/// throws on Enter is worse than one that says "access denied".
/// </para>
/// <para>
/// Enumeration never follows links and never skips anything by attribute: hidden and system entries
/// are read and then filtered here, so that toggling Ctrl+H is a re-read rather than a special case.
/// </para>
/// </remarks>
public static class FileSystemProvider
{
    private static readonly EnumerationOptions Options = new()
    {
        // Read everything, including hidden and system entries; the caller filters.
        AttributesToSkip = 0,

        // Must stay false. IgnoreInaccessible does not only swallow per-child failures: it also
        // swallows the UnauthorizedAccessException raised when the enumeration opens the directory
        // being listed, which would turn "you may not read this folder" into a successful, empty
        // listing indistinguishable from a genuinely empty one. Because the enumeration is not
        // recursive the only access error it can surface is the root's own - exactly the one the
        // panel must report - and a child that cannot be stat'ed is already dropped on its own by
        // ToEntry's try/catch, so a single unreadable child still cannot kill the whole listing.
        IgnoreInaccessible = false,

        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.PlatformDefault,
    };

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Reads <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The directory to read.</param>
    /// <param name="includeHidden">When clear, entries carrying Hidden or System are dropped.</param>
    /// <returns>The listing; never <see langword="null"/>, never throwing.</returns>
    public static DirectoryListing Read(string path, bool includeHidden) =>
        Read(path, includeHidden, null);

    /// <summary>
    /// Reads <paramref name="path"/> and orders the result.
    /// </summary>
    /// <param name="path">The directory to read.</param>
    /// <param name="includeHidden">When clear, entries carrying Hidden or System are dropped.</param>
    /// <param name="sort">
    /// The order to apply, or <see langword="null"/> to keep the file system's own order. The
    /// <c>".."</c> entry stays first either way.
    /// </param>
    /// <returns>The listing; never <see langword="null"/>, never throwing.</returns>
    public static DirectoryListing Read(string path, bool includeHidden, IComparer<FileEntry>? sort)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DirectoryListing.Failed(path ?? string.Empty, "No folder given");
        }

        string display = NormalizeDisplayPath(path);
        bool hasParent = !IsRoot(display);

        var entries = new List<FileEntry>();
        if (hasParent)
        {
            entries.Add(FileEntry.ParentOf(display));
        }

        string? error = null;
        try
        {
            var dir = new DirectoryInfo(display);
            if (!dir.Exists)
            {
                error = $"Cannot find the folder \"{display}\"";
            }
            else
            {
                foreach (FileSystemInfo info in dir.EnumerateFileSystemInfos("*", Options))
                {
                    FileEntry? entry = ToEntry(info);
                    if (entry is null)
                    {
                        continue;
                    }

                    if (!includeHidden && entry.IsHidden)
                    {
                        continue;
                    }

                    entries.Add(entry);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            error = $"Access denied: \"{display}\"";
        }
        catch (DirectoryNotFoundException)
        {
            error = $"Cannot find the folder \"{display}\"";
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            error = e.Message;
        }

        if (error is not null)
        {
            // Whatever we managed to read before the failure is not trustworthy; keep only "..".
            entries.RemoveAll(static e => !e.IsParent);
        }
        else if (sort is not null)
        {
            entries = [.. entries.OrderBy(static e => e, sort)];
        }

        return new DirectoryListing(display, entries, error);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a drive root, a Unix root or a UNC share root - that is,
    /// whether it has no parent to walk up to.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is a root.</returns>
    public static bool IsRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string full = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            return string.Equals(TrimSeparators(full), TrimSeparators(root), PathComparison);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// The directory containing <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path to walk up from.</param>
    /// <returns>The parent in display form, or <see langword="null"/> at a root.</returns>
    public static string? GetParent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            DirectoryInfo? parent = Directory.GetParent(Path.GetFullPath(path));
            return parent is null ? null : NormalizeDisplayPath(parent.FullName);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The root of <paramref name="path"/> - <c>"C:\"</c>, <c>"/"</c> or <c>@"\\server\share\"</c>.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns>The root, or the path itself when no root can be determined.</returns>
    public static string GetRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string full = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(full);
            return string.IsNullOrEmpty(root) ? full : root;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return path;
        }
    }

    /// <summary>
    /// Puts a path into the form the panel title and the command line prompt use: absolute, with
    /// platform separators, and with a trailing separator only when it is a root (<c>"C:\"</c>).
    /// </summary>
    /// <param name="path">The path to normalise.</param>
    /// <returns>The display form, or the input unchanged when it cannot be parsed.</returns>
    public static string NormalizeDisplayPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string full = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(full);

            if (!string.IsNullOrEmpty(root) &&
                string.Equals(TrimSeparators(full), TrimSeparators(root), PathComparison))
            {
                // A root keeps its trailing separator: "C:" alone means "the current directory of
                // drive C" to every Windows API, which is not what the user is looking at.
                return root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            }

            return TrimSeparators(full);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return path;
        }
    }

    /// <summary>Whether <paramref name="path"/> names a directory that exists right now.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the directory exists.</returns>
    public static bool DirectoryExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns something the user typed - an absolute path, a relative one, <c>".."</c>, a quoted
    /// path, or <c>"~"</c> - into an absolute path, resolved against <paramref name="basePath"/>.
    /// </summary>
    /// <param name="basePath">The directory relative input is measured from, usually the active panel.</param>
    /// <param name="input">What the user typed.</param>
    /// <param name="resolved">The absolute path in display form, or an empty string on failure.</param>
    /// <returns>
    /// <see langword="true"/> when the input parsed into an absolute path. Existence is a separate
    /// question - ask <see cref="DirectoryExists"/>.
    /// </returns>
    public static bool TryResolve(string basePath, string input, out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string text = input.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1];
        }

        if (text.Length == 0)
        {
            return false;
        }

        try
        {
            if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal) ||
                (Path.DirectorySeparatorChar == '\\' && text.StartsWith("~\\", StringComparison.Ordinal)))
            {
                string home = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile,
                    Environment.SpecialFolderOption.DoNotVerify);

                if (!string.IsNullOrEmpty(home))
                {
                    text = text.Length <= 2 ? home : Path.Combine(home, text[2..]);
                }
            }

            string full;
            if (OperatingSystem.IsWindows() &&
                text.Length > 0 &&
                (text[0] == '\\' || text[0] == '/') &&
                !text.StartsWith(@"\\", StringComparison.Ordinal) &&
                !text.StartsWith("//", StringComparison.Ordinal))
            {
                // "\foo" is drive relative on Windows: anchor it to the base path's own drive
                // instead of whatever the process working directory happens to be.
                full = Path.GetFullPath(Path.Combine(GetRoot(basePath), text.TrimStart('\\', '/')));
            }
            else if (Path.IsPathRooted(text))
            {
                full = Path.GetFullPath(text);
            }
            else if (string.IsNullOrWhiteSpace(basePath))
            {
                full = Path.GetFullPath(text);
            }
            else
            {
                full = Path.GetFullPath(Path.Combine(basePath, text));
            }

            resolved = NormalizeDisplayPath(full);
            return !string.IsNullOrEmpty(resolved);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            resolved = string.Empty;
            return false;
        }
    }

    private static FileEntry? ToEntry(FileSystemInfo info)
    {
        try
        {
            FileAttributes attributes = info.Attributes;
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;

            if (!isDirectory && (attributes & FileAttributes.ReparsePoint) != 0)
            {
                // A link is shown as whatever it points at, without ever being walked into.
                isDirectory = DirectoryExists(info.FullName);
            }

            long size = 0;
            if (!isDirectory && info is FileInfo file)
            {
                try
                {
                    size = file.Length;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    size = 0;
                }
            }

            return new FileEntry
            {
                Name = info.Name,
                FullPath = info.FullName,
                IsDirectory = isDirectory,
                Size = size,
                Modified = info.LastWriteTime,
                Created = info.CreationTime,
                Accessed = info.LastAccessTime,
                Attributes = attributes,
                UnixExecutable = isDirectory ? null : UnixExecutableBit(info),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The entry vanished between the enumeration and reading it. Skip it.
            return null;
        }
    }

    private static bool? UnixExecutableBit(FileSystemInfo info)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            // Populated from the stat the enumeration already did, so this costs no extra syscall.
            return (info.UnixFileMode & UnixFileMode.UserExecute) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string TrimSeparators(string path)
    {
        int end = path.Length;
        while (end > 0 && (path[end - 1] == Path.DirectorySeparatorChar || path[end - 1] == Path.AltDirectorySeparatorChar))
        {
            end--;
        }

        return end == path.Length ? path : path[..end];
    }
}
