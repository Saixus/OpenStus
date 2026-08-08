namespace OpenCommander.Files;

/// <summary>
/// The result of reading one directory: its entries, and the reason it could not be read in full.
/// </summary>
/// <remarks>
/// A listing is always usable. When <see cref="Error"/> is set the panel still gets a listing - one
/// holding just the <c>".."</c> entry - so the user can always walk back out of a folder they are
/// not allowed to read.
/// </remarks>
public sealed class DirectoryListing
{
    /// <summary>Creates a listing.</summary>
    /// <param name="path">The directory that was read.</param>
    /// <param name="entries">The entries, in display order.</param>
    /// <param name="error">Why the directory could not be read, or <see langword="null"/> on success.</param>
    public DirectoryListing(string path, IReadOnlyList<FileEntry> entries, string? error = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Path = path ?? string.Empty;
        Entries = entries;
        Error = string.IsNullOrEmpty(error) ? null : error;

        foreach (FileEntry e in entries)
        {
            if (e.IsParent)
            {
                continue;
            }

            if (e.IsDirectory)
            {
                DirectoryCount++;
            }
            else
            {
                FileCount++;
                TotalBytes += e.Size;
            }
        }
    }

    /// <summary>The directory that was read, in display form.</summary>
    public string Path { get; }

    /// <summary>The entries in display order, <c>".."</c> included when the directory has a parent.</summary>
    public IReadOnlyList<FileEntry> Entries { get; }

    /// <summary>Why the directory could not be read - access denied and the like - or <see langword="null"/>.</summary>
    public string? Error { get; }

    /// <summary><see langword="true"/> when <see cref="Error"/> is set.</summary>
    public bool HasError => Error is not null;

    /// <summary>The number of entries, the <c>".."</c> entry included.</summary>
    public int Count => Entries.Count;

    /// <summary>The number of files, excluding directories and <c>".."</c>.</summary>
    public int FileCount { get; }

    /// <summary>The number of directories, excluding <c>".."</c>.</summary>
    public int DirectoryCount { get; }

    /// <summary>The summed size of the files, which is what the panel's totals line reports.</summary>
    public long TotalBytes { get; }

    /// <summary><see langword="true"/> when the listing carries a <c>".."</c> entry.</summary>
    public bool HasParent => Entries.Count > 0 && Entries[0].IsParent;

    /// <summary>The <c>".."</c> entry, or <see langword="null"/> at a root.</summary>
    public FileEntry? Parent => HasParent ? Entries[0] : null;

    /// <summary>An empty listing for a directory that could not be read at all.</summary>
    /// <param name="path">The directory that was read.</param>
    /// <param name="error">The reason.</param>
    /// <returns>The listing.</returns>
    public static DirectoryListing Failed(string path, string error) =>
        new(path, [], error);

    /// <inheritdoc/>
    public override string ToString() =>
        HasError ? $"{Path} ({Error})" : $"{Path} ({Count} entries)";
}
