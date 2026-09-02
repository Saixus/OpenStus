using Dvopan.Core;

namespace Dvopan.Files;

/// <summary>
/// Orders panel entries the way Far does.
/// </summary>
/// <remarks>
/// <para>
/// The order is decided in three steps. The <c>".."</c> entry always comes first, whatever else is
/// asked for. Directories then come before files when
/// <see cref="DirectoriesFirst"/> is set. Only after that does the sort key matter, and
/// <see cref="Reverse"/> flips that key alone - never the <c>".."</c> or directory grouping.
/// </para>
/// <para>
/// Entries with equal keys fall back to their names, ascending, so the order stays stable and
/// predictable even in a descending sort. <see cref="SortMode.Unsorted"/> skips that fallback so
/// that a stable sort - <see cref="Sort"/>, or <c>Enumerable.OrderBy</c> - preserves the order the
/// file system produced.
/// </para>
/// </remarks>
public sealed class FileEntryComparer : IComparer<FileEntry>
{
    /// <summary>Creates a comparer.</summary>
    /// <param name="mode">The sort key.</param>
    /// <param name="reverse">Invert the key comparison.</param>
    /// <param name="directoriesFirst">Group directories ahead of files.</param>
    /// <param name="numeric">Compare digit runs inside names by value.</param>
    /// <param name="caseSensitive">Compare names by ordinal instead of case-insensitively.</param>
    public FileEntryComparer(
        SortMode mode,
        bool reverse,
        bool directoriesFirst,
        bool numeric,
        bool caseSensitive)
    {
        Mode = mode;
        Reverse = reverse;
        DirectoriesFirst = directoriesFirst;
        Numeric = numeric;
        CaseSensitive = caseSensitive;
    }

    /// <summary>The sort key.</summary>
    public SortMode Mode { get; }

    /// <summary>Whether the key comparison is inverted.</summary>
    public bool Reverse { get; }

    /// <summary>Whether directories are grouped ahead of files.</summary>
    public bool DirectoriesFirst { get; }

    /// <summary>Whether digit runs inside names compare by value.</summary>
    public bool Numeric { get; }

    /// <summary>Whether names compare case sensitively.</summary>
    public bool CaseSensitive { get; }

    /// <summary>
    /// Builds a comparer from the user's persisted preferences.
    /// </summary>
    /// <param name="mode">The sort key.</param>
    /// <param name="reverse">Invert the key comparison.</param>
    /// <param name="settings">The settings supplying the grouping and name comparison flags.</param>
    /// <returns>The comparer.</returns>
    public static FileEntryComparer For(SortMode mode, bool reverse, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new FileEntryComparer(
            mode,
            reverse,
            settings.DirectoriesFirst,
            settings.NumericSort,
            settings.CaseSensitiveSort);
    }

    /// <summary>
    /// Returns <paramref name="entries"/> in sorted order using a stable sort, so entries this
    /// comparer calls equal keep their input order.
    /// </summary>
    /// <param name="entries">The entries to order.</param>
    /// <returns>A new list in display order.</returns>
    public List<FileEntry> Sort(IEnumerable<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. entries.OrderBy(static e => e, this)];
    }

    /// <inheritdoc/>
    public int Compare(FileEntry? x, FileEntry? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        if (x.IsParent || y.IsParent)
        {
            if (x.IsParent && y.IsParent)
            {
                return 0;
            }

            return x.IsParent ? -1 : 1;
        }

        if (DirectoriesFirst && x.IsDirectory != y.IsDirectory)
        {
            return x.IsDirectory ? -1 : 1;
        }

        int key = CompareKey(x, y);
        if (Reverse)
        {
            key = -key;
        }

        if (key != 0)
        {
            return key;
        }

        if (Mode == SortMode.Unsorted)
        {
            return 0;
        }

        int byName = CompareNames(x.Name, y.Name);
        return byName != 0 ? byName : string.CompareOrdinal(x.Name, y.Name);
    }

    private int CompareKey(FileEntry x, FileEntry y) => Mode switch
    {
        SortMode.Unsorted => 0,
        SortMode.Extension => CompareNames(SortExtension(x), SortExtension(y)),
        SortMode.Modified => x.Modified.CompareTo(y.Modified),
        SortMode.Created => x.Created.CompareTo(y.Created),
        SortMode.Accessed => x.Accessed.CompareTo(y.Accessed),
        SortMode.Size => x.Size.CompareTo(y.Size),

        // Name, and the two modes whose data source does not exist yet.
        _ => CompareNames(x.Name, y.Name),
    };

    // A folder named "archive.old" is not an "old" file, and Far's default agrees: sorting by
    // extension treats a directory's extension as empty, so directories order among themselves
    // by the name fallback alone.
    private static string SortExtension(FileEntry e) => e.IsDirectory ? string.Empty : e.Extension;

    private int CompareNames(string a, string b)
    {
        if (Numeric)
        {
            return NaturalComparer.Compare(a, b, CaseSensitive);
        }

        return CaseSensitive
            ? string.CompareOrdinal(a, b)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
