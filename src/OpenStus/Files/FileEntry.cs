// Owned by the Files layer.

namespace OpenStus.Files;

/// <summary>
/// One row of a panel listing: a file, a directory, or the synthetic <c>".."</c> parent entry.
/// </summary>
/// <remarks>
/// Everything except <see cref="Selected"/> is immutable, because a listing is rebuilt wholesale on
/// every re-read; the tag mark is the only piece of per-entry state the user can change in place.
/// </remarks>
public sealed class FileEntry
{
    private readonly bool _isParent;
    private bool _selected;

    /// <summary>The name used to display and match the entry; <c>".."</c> for the parent entry.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The absolute path of the entry.</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>Whether the entry is a directory (the parent entry counts as one).</summary>
    public bool IsDirectory { get; init; }

    /// <summary>Whether this is the synthetic <c>".."</c> entry, which can never be tagged.</summary>
    public bool IsParent
    {
        get => _isParent;
        init
        {
            _isParent = value;
            if (value)
            {
                _selected = false;
            }
        }
    }

    /// <summary>Size in bytes; always zero for a directory.</summary>
    public long Size { get; init; }

    /// <summary>Last write time, local.</summary>
    public DateTime Modified { get; init; }

    /// <summary>Creation time, local.</summary>
    public DateTime Created { get; init; }

    /// <summary>Last access time, local.</summary>
    public DateTime Accessed { get; init; }

    /// <summary>The raw file system attributes.</summary>
    public FileAttributes Attributes { get; init; }

    /// <summary>
    /// The owner execute bit as read while the directory was enumerated, or <see langword="null"/>
    /// when it was never read - on Windows, or for an entry built by hand.
    /// </summary>
    /// <remarks>
    /// Caching it here keeps <see cref="IsExecutable"/> off the disk during rendering; when it is
    /// <see langword="null"/> on a Unix host the property falls back to a stat call.
    /// </remarks>
    public bool? UnixExecutable { get; init; }

    /// <summary>The tag mark toggled with Ins. Never set on the <c>".."</c> entry.</summary>
    public bool Selected
    {
        get => _selected;
        set => _selected = value && !IsParent;
    }

    /// <summary>Whether the entry carries the Hidden or System attribute.</summary>
    public bool IsHidden =>
        (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>Whether the entry carries the System attribute.</summary>
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;

    /// <summary>Whether the entry carries the ReadOnly attribute.</summary>
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;

    /// <summary>Whether the entry is a symlink, junction or volume mount point.</summary>
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// Whether the entry runs: a known executable extension on Windows, the owner execute bit
    /// elsewhere. Directories are never executable.
    /// </summary>
    public bool IsExecutable
    {
        get
        {
            if (IsDirectory)
            {
                return false;
            }

            return OperatingSystem.IsWindows()
                ? FileTypeClassifier.IsExecutableExtension(Extension)
                : UnixExecutable ?? FileTypeClassifier.ProbeUnixExecutable(FullPath);
        }
    }

    /// <summary>Whether the extension is one of the archive formats the panel colours differently.</summary>
    public bool IsArchive => !IsDirectory && FileTypeClassifier.IsArchiveExtension(Extension);

    /// <summary>
    /// The extension in lower case and <em>without</em> the dot, or an empty string when the name
    /// has none. A leading dot is part of the name (<c>".gitignore"</c> has no extension).
    /// </summary>
    public string Extension => FileTypeClassifier.ExtensionOf(Name);

    /// <summary>The name with its extension (and the separating dot) removed.</summary>
    public string BaseName => FileTypeClassifier.BaseNameOf(Name);

    /// <summary>
    /// The attributes rendered in the classic fixed <c>R A S H D</c> order, a dash standing in
    /// for each attribute that is absent - for example <c>"-A---"</c>. Always five characters.
    /// </summary>
    public string AttributeString
    {
        get
        {
            Span<char> buf =
            [
                IsReadOnly ? 'R' : '-',
                (Attributes & FileAttributes.Archive) != 0 ? 'A' : '-',
                IsSystem ? 'S' : '-',
                (Attributes & FileAttributes.Hidden) != 0 ? 'H' : '-',
                IsDirectory ? 'D' : '-',
            ];

            return new string(buf);
        }
    }

    /// <summary>
    /// Builds the synthetic <c>".."</c> entry for <paramref name="directory"/>. Its
    /// <see cref="FullPath"/> is the parent directory, or <paramref name="directory"/> itself when
    /// it is already a root.
    /// </summary>
    /// <param name="directory">The directory the entry belongs to.</param>
    /// <returns>The parent entry.</returns>
    public static FileEntry ParentOf(string directory)
    {
        string parent = directory;
        try
        {
            var info = Directory.GetParent(directory);
            if (info is not null)
            {
                parent = info.FullName;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A malformed or inaccessible path: point ".." back at where we already are.
        }

        // The ".." row carries the parent folder's own timestamps, so the status line and the
        // date columns read sensibly instead of showing the zero date.
        DateTime written = default;
        DateTime created = default;
        DateTime accessed = default;

        try
        {
            var info = new DirectoryInfo(parent);
            if (info.Exists)
            {
                written = info.LastWriteTime;
                created = info.CreationTime;
                accessed = info.LastAccessTime;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Timestamps are cosmetic here; the zero date is an acceptable fallback.
        }

        return new FileEntry
        {
            Name = "..",
            FullPath = parent,
            IsDirectory = true,
            IsParent = true,
            Attributes = FileAttributes.Directory,
            Modified = written,
            Created = created,
            Accessed = accessed,
        };
    }

    /// <inheritdoc/>
    public override string ToString() => IsDirectory ? Name + Path.DirectorySeparatorChar : Name;
}
