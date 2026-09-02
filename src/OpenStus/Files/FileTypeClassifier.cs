namespace OpenStus.Files;

/// <summary>
/// The colour class a panel row falls into, in the classic orthodox-file-manager precedence order.
/// </summary>
/// <remarks>
/// The panel adds the two states the file system knows nothing about - the cursor bar and the tag
/// mark - on top of this, giving the full order
/// cursor &gt; tagged &gt; directory &gt; hidden &gt; archive &gt; executable &gt; normal.
/// </remarks>
public enum FileCategory
{
    /// <summary>An ordinary file.</summary>
    Normal,

    /// <summary>A directory, including the <c>".."</c> entry.</summary>
    Directory,

    /// <summary>A file carrying the Hidden or System attribute.</summary>
    Hidden,

    /// <summary>A file with a known archive extension.</summary>
    Archive,

    /// <summary>A file that runs.</summary>
    Executable,
}

/// <summary>
/// Decides what kind of thing a name refers to: which extensions run, which are archives, and which
/// colour class a listing entry belongs to.
/// </summary>
/// <remarks>
/// Extension tests are pure string work so that they can be used from the render path without
/// touching the disk. The one exception is the Unix execute bit, which needs a stat call; the
/// provider reads it once while enumerating and stores it on the entry.
/// </remarks>
public static class FileTypeClassifier
{
    private static readonly string[] ExecutableList =
        ["bat", "cmd", "com", "exe", "msi", "ps1"];

    private static readonly string[] ArchiveList =
        ["7z", "bz2", "cab", "gz", "iso", "rar", "tar", "xz", "zip"];

    private static readonly HashSet<string> ExecutableSet =
        new(ExecutableList, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ArchiveSet =
        new(ArchiveList, StringComparer.OrdinalIgnoreCase);

    /// <summary>The extensions (lower case, no dot) treated as executables on Windows.</summary>
    public static IReadOnlyList<string> ExecutableExtensions => ExecutableList;

    /// <summary>The extensions (lower case, no dot) treated as archives on every platform.</summary>
    public static IReadOnlyList<string> ArchiveExtensions => ArchiveList;

    /// <summary>
    /// Tests whether <paramref name="extension"/> - written without a dot, in any case - is one of
    /// the Windows executable extensions.
    /// </summary>
    /// <param name="extension">The extension to test.</param>
    /// <returns><see langword="true"/> for an executable extension.</returns>
    public static bool IsExecutableExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && ExecutableSet.Contains(extension);

    /// <summary>
    /// Tests whether <paramref name="extension"/> - written without a dot, in any case - is one of
    /// the archive extensions.
    /// </summary>
    /// <param name="extension">The extension to test.</param>
    /// <returns><see langword="true"/> for an archive extension.</returns>
    public static bool IsArchiveExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && ArchiveSet.Contains(extension);

    /// <summary>
    /// The extension of <paramref name="name"/> in lower case and without the dot, or an empty
    /// string when there is none. A leading dot belongs to the name, so <c>".gitignore"</c> has no
    /// extension, and a trailing dot is not one either.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The extension, or an empty string.</returns>
    public static string ExtensionOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1
            ? string.Empty
            : name[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>
    /// The part of <paramref name="name"/> before the extension, with the separating dot removed.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The base name.</returns>
    public static string BaseNameOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? name : name[..dot];
    }

    /// <summary>
    /// Reads the owner execute bit of <paramref name="fullPath"/>. Never throws.
    /// </summary>
    /// <param name="fullPath">The file to stat.</param>
    /// <returns>
    /// <see langword="true"/> when the file is executable by its owner; <see langword="false"/> on
    /// Windows, for a missing file, or when the mode cannot be read.
    /// </returns>
    public static bool ProbeUnixExecutable(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return (File.GetUnixFileMode(fullPath) & UnixFileMode.UserExecute) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="entry"/> runs: a known extension on Windows, the owner execute bit
    /// elsewhere. Directories never run.
    /// </summary>
    /// <param name="entry">The entry to classify.</param>
    /// <returns><see langword="true"/> when the entry is executable.</returns>
    public static bool IsExecutable(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsExecutable;
    }

    /// <summary>Whether <paramref name="entry"/> is a file with an archive extension.</summary>
    /// <param name="entry">The entry to classify.</param>
    /// <returns><see langword="true"/> when the entry is an archive.</returns>
    public static bool IsArchive(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsArchive;
    }

    /// <summary>
    /// Picks the colour class for <paramref name="entry"/>, testing the categories in the same
    /// order the panel paints them.
    /// </summary>
    /// <param name="entry">The entry to classify.</param>
    /// <returns>The category.</returns>
    public static FileCategory Classify(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsDirectory)
        {
            return FileCategory.Directory;
        }

        if (entry.IsHidden)
        {
            return FileCategory.Hidden;
        }

        if (entry.IsArchive)
        {
            return FileCategory.Archive;
        }

        return entry.IsExecutable ? FileCategory.Executable : FileCategory.Normal;
    }
}
