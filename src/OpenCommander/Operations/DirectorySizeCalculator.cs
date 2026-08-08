using OpenCommander.Files;

namespace OpenCommander.Operations;

/// <summary>
/// The result of walking a folder: what is in it, and whether the walk saw all of it.
/// </summary>
/// <param name="Bytes">The total size of every file underneath.</param>
/// <param name="Files">How many files there are.</param>
/// <param name="Directories">How many folders there are, not counting the one asked about.</param>
/// <param name="Complete">
/// <see langword="false"/> when the walk was cancelled or hit something it could not read, so the
/// numbers are a lower bound. Far shows such a total with a leading <c>&gt;</c>.
/// </param>
public readonly record struct DirectorySize(long Bytes, long Files, long Directories, bool Complete)
{
    /// <summary>An empty, complete result.</summary>
    public static DirectorySize Empty => new(0, 0, 0, true);

    /// <summary>Whether the folder held nothing at all.</summary>
    public bool IsEmpty => Bytes == 0 && Files == 0 && Directories == 0;

    /// <summary>Adds two results, keeping <see cref="Complete"/> only when both were complete.</summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns>The sum.</returns>
    public static DirectorySize operator +(DirectorySize left, DirectorySize right) =>
        new(left.Bytes + right.Bytes,
            left.Files + right.Files,
            left.Directories + right.Directories,
            left.Complete && right.Complete);

    /// <summary>Adds two results.</summary>
    /// <param name="other">The result to add.</param>
    /// <returns>The sum.</returns>
    public DirectorySize Add(DirectorySize other) => this + other;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{(Complete ? string.Empty : ">")}{Bytes} bytes, {Files} file(s), {Directories} folder(s)";
}

/// <summary>
/// Adds up the recursive size of folders - what Far puts on the panel when the user presses the
/// space bar on a folder, and what the folder-size column shows.
/// </summary>
/// <remarks>
/// The walk is iterative rather than recursive so a pathological tree cannot blow the stack, it never
/// follows reparse points (a junction pointing at its own ancestor would otherwise run forever), and
/// it never throws: a folder it may not read simply clears
/// <see cref="DirectorySize.Complete"/> on the result.
/// </remarks>
public static class DirectorySizeCalculator
{
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.PlatformDefault,
    };

    /// <summary>
    /// Adds up everything under <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The folder to measure. A file measures as itself.</param>
    /// <param name="includeHidden">Whether to count hidden and system entries.</param>
    /// <param name="cancellationToken">Stops the walk; the partial total comes back incomplete.</param>
    /// <returns>The total. Never throws.</returns>
    public static DirectorySize Calculate(
        string path,
        bool includeHidden = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Empty();
        }

        try
        {
            if (!Directory.Exists(path))
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    return new DirectorySize(0, 0, 0, false);
                }

                return new DirectorySize(SafeLength(file), 1, 0, true);
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return new DirectorySize(0, 0, 0, false);
        }

        long bytes = 0;
        long files = 0;
        long directories = 0;
        bool complete = true;

        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                complete = false;
                break;
            }

            string current = pending.Pop();

            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(current).EnumerateFileSystemInfos("*", Options);
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                complete = false;
                continue;
            }

            try
            {
                foreach (FileSystemInfo info in children)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        complete = false;
                        break;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = info.Attributes;
                    }
                    catch (Exception e) when (IsFileSystemException(e))
                    {
                        complete = false;
                        continue;
                    }

                    if (!includeHidden && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories++;

                        // Never walk into a link: a junction pointing at an ancestor is a loop, and
                        // its contents are somebody else's bytes anyway.
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(info.FullName);
                        }
                    }
                    else
                    {
                        files++;
                        if (info is FileInfo file)
                        {
                            bytes += SafeLength(file);
                        }
                    }
                }
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                // EnumerateFileSystemInfos is lazy: a failure can surface here rather than above.
                complete = false;
            }
        }

        return new DirectorySize(bytes, files, directories, complete);
    }

    /// <summary>
    /// Adds up everything under one panel entry. The <c>".."</c> entry measures as empty.
    /// </summary>
    /// <param name="entry">The entry to measure.</param>
    /// <param name="includeHidden">Whether to count hidden and system entries.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>The total. Never throws.</returns>
    public static DirectorySize Calculate(
        FileEntry entry,
        bool includeHidden = true,
        CancellationToken cancellationToken = default)
    {
        if (entry is null || entry.IsParent || string.IsNullOrWhiteSpace(entry.FullPath))
        {
            return Empty();
        }

        if (!entry.IsDirectory)
        {
            return new DirectorySize(entry.Size, 1, 0, true);
        }

        return Calculate(entry.FullPath, includeHidden, cancellationToken);
    }

    /// <summary>
    /// Measures several entries, reporting each one as it finishes so the panel can redraw a row at
    /// a time.
    /// </summary>
    /// <param name="entries">The entries to measure. <c>".."</c> entries are skipped.</param>
    /// <param name="includeHidden">Whether to count hidden and system entries.</param>
    /// <param name="onEach">Called with each entry and its total as soon as it is known.</param>
    /// <param name="cancellationToken">Stops the walk; entries not yet reached are left out.</param>
    /// <returns>
    /// The per-entry totals, in the order the entries were given. Pass the list to
    /// <see cref="Total"/> for the grand total.
    /// </returns>
    public static IReadOnlyList<KeyValuePair<FileEntry, DirectorySize>> Calculate(
        IReadOnlyList<FileEntry> entries,
        bool includeHidden = true,
        Action<FileEntry, DirectorySize>? onEach = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<KeyValuePair<FileEntry, DirectorySize>>();
        if (entries is null)
        {
            return results;
        }

        foreach (FileEntry entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (entry is null || entry.IsParent)
            {
                continue;
            }

            DirectorySize size = Calculate(entry, includeHidden, cancellationToken);
            results.Add(new KeyValuePair<FileEntry, DirectorySize>(entry, size));

            try
            {
                onEach?.Invoke(entry, size);
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                // A callback that throws must not abandon the rest of the measurement.
            }
        }

        return results;
    }

    /// <summary>
    /// Adds up the per-entry totals produced by
    /// <see cref="Calculate(IReadOnlyList{FileEntry}, bool, Action{FileEntry, DirectorySize}, CancellationToken)"/>.
    /// </summary>
    /// <param name="results">The per-entry totals.</param>
    /// <returns>The grand total.</returns>
    public static DirectorySize Total(IReadOnlyList<KeyValuePair<FileEntry, DirectorySize>> results)
    {
        DirectorySize total = DirectorySize.Empty;
        if (results is null)
        {
            return total;
        }

        foreach (KeyValuePair<FileEntry, DirectorySize> pair in results)
        {
            total += pair.Value;
        }

        return total;
    }

    private static DirectorySize Empty() => DirectorySize.Empty;

    private static long SafeLength(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return 0;
        }
    }

    private static bool IsFileSystemException(Exception e) =>
        e is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;
}
