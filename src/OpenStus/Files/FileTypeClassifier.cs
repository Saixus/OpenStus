namespace OpenStus.Files;

/// <summary>
/// The colour class a panel row falls into, in the classic orthodox-file-manager precedence order.
/// </summary>
/// <remarks>
/// The panel adds the two states the file system knows nothing about - the cursor bar and the tag
/// mark - on top of this, giving the full order
/// cursor &gt; tagged &gt; directory &gt; hidden &gt; archive &gt; executable &gt; temporary &gt;
/// media &gt; normal.
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

    /// <summary>A backup or temporary file: <c>*.bak</c>, <c>*.tmp</c>, <c>*~</c> and the like.</summary>
    Temporary,

    /// <summary>An image, sound or video file.</summary>
    Media,
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
        ["7z", "apk", "arj", "bz2", "cab", "deb", "ear", "gz", "iso", "jar", "lz", "lzh", "lzma",
         "nupkg", "rar", "rpm", "tar", "tbz2", "tgz", "txz", "vsix", "war", "whl", "wim", "xz", "z",
         "zip", "zst"];

    private static readonly string[] TemporaryList =
        ["$$$", "backup", "bak", "bk", "bkp", "crdownload", "dmp", "mdmp", "old", "orig", "part",
         "partial", "rej", "swo", "swp", "temp", "tmp"];

    private static readonly string[] MediaList =
        [
            // Images.
            "avif", "bmp", "gif", "heic", "ico", "jpeg", "jpg", "png", "psd", "svg", "tga", "tif",
            "tiff", "webp",

            // Sound.
            "aac", "aiff", "flac", "m4a", "mid", "midi", "mp3", "ogg", "opus", "wav", "wma",

            // Video.
            "3gp", "avi", "flv", "m4v", "mkv", "mov", "mp4", "mpeg", "mpg", "webm", "wmv",
        ];

    private static readonly HashSet<string> ExecutableSet =
        new(ExecutableList, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ArchiveSet =
        new(ArchiveList, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TemporarySet =
        new(TemporaryList, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> MediaSet =
        new(MediaList, StringComparer.OrdinalIgnoreCase);

    /// <summary>The extensions (lower case, no dot) treated as executables on Windows.</summary>
    public static IReadOnlyList<string> ExecutableExtensions => ExecutableList;

    /// <summary>The extensions (lower case, no dot) treated as archives on every platform.</summary>
    public static IReadOnlyList<string> ArchiveExtensions => ArchiveList;

    /// <summary>The extensions (lower case, no dot) treated as backup or temporary files.</summary>
    public static IReadOnlyList<string> TemporaryExtensions => TemporaryList;

    /// <summary>The extensions (lower case, no dot) treated as images, sound and video.</summary>
    public static IReadOnlyList<string> MediaExtensions => MediaList;

    /// <summary>
    /// Tests whether a file name is a backup or temporary file: a temporary extension, an editor
    /// backup ending in <c>~</c>, or an Office owner file starting with <c>~$</c>.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns><see langword="true"/> for a backup or temporary file.</returns>
    public static bool IsTemporaryName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        (name.EndsWith('~') ||
         name.StartsWith("~$", StringComparison.Ordinal) ||
         TemporarySet.Contains(ExtensionOf(name)));

    /// <summary>
    /// Tests whether <paramref name="extension"/> - written without a dot, in any case - is an
    /// image, sound or video extension.
    /// </summary>
    /// <param name="extension">The extension to test.</param>
    /// <returns><see langword="true"/> for a media extension.</returns>
    public static bool IsMediaExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && MediaSet.Contains(extension);

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

        if (entry.IsExecutable)
        {
            return FileCategory.Executable;
        }

        if (entry.IsTemporary)
        {
            return FileCategory.Temporary;
        }

        return entry.IsMedia ? FileCategory.Media : FileCategory.Normal;
    }
}
