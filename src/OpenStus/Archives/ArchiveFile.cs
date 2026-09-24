using System.Formats.Tar;
using System.IO.Compression;
using OpenStus.Operations;

namespace OpenStus.Archives;

/// <summary>The archive formats the panel can walk into.</summary>
public enum ArchiveKind
{
    /// <summary>A zip file, and the formats built on it: jar, nupkg, vsix, whl and friends.</summary>
    Zip,

    /// <summary>An uncompressed tar file.</summary>
    Tar,

    /// <summary>A gzip-compressed tar file (<c>.tar.gz</c>, <c>.tgz</c>).</summary>
    TarGzip,

    /// <summary>A single gzip-compressed file (<c>.gz</c> that is not a tar).</summary>
    Gzip,
}

/// <summary>One file or folder inside an archive.</summary>
public sealed class ArchiveItem
{
    /// <summary>
    /// The path inside the archive: <c>/</c> separated, with no leading or trailing separator -
    /// <c>"docs/images/logo.png"</c>.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>The last segment of <see cref="Path"/>.</summary>
    public string Name => ArchiveFile.LeafOf(Path);

    /// <summary>Whether this is a folder.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>The uncompressed size in bytes; zero for a folder.</summary>
    public long Size { get; init; }

    /// <summary>The modification time, local.</summary>
    public DateTime Modified { get; init; }

    /// <summary>The DOS attributes the archive recorded, when it recorded any.</summary>
    public FileAttributes Attributes { get; init; }
}

/// <summary>
/// The catalogue of one archive - every file and folder in it - plus extraction. Read-only: the
/// panel browses an archive and copies out of it, but never writes into one.
/// </summary>
/// <remarks>
/// <para>
/// Only formats the runtime reads natively are supported: zip and its relatives, tar, gzip'd tar
/// and a lone gzip'd file. Anything else - 7z, rar, cab, iso - is left to the program the system
/// associates with it.
/// </para>
/// <para>
/// Entry names are sanitised before anything else sees them: backslashes become slashes, empty
/// and <c>.</c> segments go, and an entry that climbs out with <c>..</c> or starts from a drive or
/// a root is dropped entirely, so nothing extracted can ever land outside the chosen folder.
/// Folders the archive only implies (a file <c>a/b/c.txt</c> with no <c>a/</c> entry) are
/// synthesised, so every level can be walked into.
/// </para>
/// </remarks>
public sealed class ArchiveFile
{
    private const int CopyBlock = 81920;
    private const int ReportIntervalMs = 33;

    private static readonly Dictionary<string, ArchiveKind> KindByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = ArchiveKind.Zip,
        [".jar"] = ArchiveKind.Zip,
        [".war"] = ArchiveKind.Zip,
        [".ear"] = ArchiveKind.Zip,
        [".nupkg"] = ArchiveKind.Zip,
        [".snupkg"] = ArchiveKind.Zip,
        [".vsix"] = ArchiveKind.Zip,
        [".whl"] = ArchiveKind.Zip,
        [".xpi"] = ArchiveKind.Zip,
        [".apk"] = ArchiveKind.Zip,
        [".tar"] = ArchiveKind.Tar,
        [".tgz"] = ArchiveKind.TarGzip,
        [".gz"] = ArchiveKind.Gzip,
    };

    private readonly Dictionary<string, ArchiveItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ArchiveItem>> _children = new(StringComparer.Ordinal);

    private ArchiveFile(string filePath, ArchiveKind kind, long length, DateTime stamp)
    {
        FilePath = filePath;
        Kind = kind;
        FileLength = length;
        FileStamp = stamp;
        _children[string.Empty] = [];
    }

    /// <summary>The archive file on disk.</summary>
    public string FilePath { get; }

    /// <summary>The format.</summary>
    public ArchiveKind Kind { get; }

    /// <summary>The archive's size when the catalogue was read.</summary>
    public long FileLength { get; }

    /// <summary>The archive's modification time when the catalogue was read.</summary>
    public DateTime FileStamp { get; }

    /// <summary>Every file and folder, synthesised folders included.</summary>
    public IReadOnlyCollection<ArchiveItem> Items => _items.Values;

    /// <summary>The format a file name says it is, or <see langword="null"/> for anything unsupported.</summary>
    /// <param name="path">The file path or bare name.</param>
    /// <returns>The format, or <see langword="null"/>.</returns>
    public static ArchiveKind? KindOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string name = System.IO.Path.GetFileName(path);
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.TarGzip;
        }

        string extension = System.IO.Path.GetExtension(name);
        return KindByExtension.TryGetValue(extension, out ArchiveKind kind) ? kind : null;
    }

    /// <summary>Whether a file name is one of the formats the panel can walk into.</summary>
    /// <param name="path">The file path or bare name.</param>
    /// <returns><see langword="true"/> for a supported archive.</returns>
    public static bool CanOpen(string? path) => KindOf(path) is not null;

    /// <summary>Reads the catalogue of an archive.</summary>
    /// <param name="path">The archive file.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid archive of its kind.</exception>
    /// <exception cref="NotSupportedException">The file name is not a supported archive.</exception>
    public static ArchiveFile Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        ArchiveKind kind = KindOf(path) ?? throw new NotSupportedException("Not a supported archive: " + path);
        var info = new FileInfo(path);
        var archive = new ArchiveFile(info.FullName, kind, info.Length, info.LastWriteTime);

        switch (kind)
        {
            case ArchiveKind.Zip:
                archive.ReadZipCatalogue();
                break;

            case ArchiveKind.Tar:
            case ArchiveKind.TarGzip:
                archive.ReadTarCatalogue();
                break;

            default:
                archive.ReadGzipCatalogue(info);
                break;
        }

        return archive;
    }

    /// <summary>
    /// Whether the file on disk is still the one the catalogue was read from - same size, same
    /// time stamp - so a panel re-read can skip decompressing a big tarball that has not changed.
    /// </summary>
    /// <returns><see langword="true"/> when the catalogue is still current.</returns>
    public bool IsCurrent()
    {
        try
        {
            var info = new FileInfo(FilePath);
            return info.Exists && info.Length == FileLength && info.LastWriteTime == FileStamp;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The item at an inner path, or <see langword="null"/>.</summary>
    /// <param name="innerPath">The <c>/</c> separated path inside the archive.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public ArchiveItem? Find(string innerPath) =>
        _items.TryGetValue(innerPath ?? string.Empty, out ArchiveItem? item) ? item : null;

    /// <summary>Whether an inner path is a folder; the empty path - the archive's root - always is.</summary>
    /// <param name="innerPath">The <c>/</c> separated path inside the archive.</param>
    /// <returns><see langword="true"/> for a folder.</returns>
    public bool IsDirectory(string innerPath) =>
        string.IsNullOrEmpty(innerPath) || Find(innerPath) is { IsDirectory: true };

    /// <summary>The files and folders directly inside one folder of the archive.</summary>
    /// <param name="innerPath">The folder; empty for the archive's root.</param>
    /// <returns>The items, in catalogue order; empty for a path that is not a folder.</returns>
    public IReadOnlyList<ArchiveItem> List(string innerPath) =>
        _children.TryGetValue(innerPath ?? string.Empty, out List<ArchiveItem>? list) ? list : [];

    /// <summary>What a folder of the archive holds, counted from the catalogue.</summary>
    /// <param name="innerPath">The file or folder; a file counts as itself.</param>
    /// <returns>The size of the files, and how many files and folders sit underneath.</returns>
    public DirectorySize Measure(string innerPath)
    {
        ArchiveItem? item = Find(innerPath);
        if (item is { IsDirectory: false })
        {
            return new DirectorySize(item.Size, 1, 0, true);
        }

        long bytes = 0;
        long files = 0;
        long folders = 0;
        foreach (ArchiveItem child in _items.Values)
        {
            if (!IsUnder(child.Path, innerPath))
            {
                continue;
            }

            if (child.IsDirectory)
            {
                folders++;
            }
            else
            {
                files++;
                bytes += child.Size;
            }
        }

        return new DirectorySize(bytes, files, folders, true);
    }

    /// <summary>
    /// Extracts files and folders into a folder on disk, each under its own name - a folder with
    /// everything inside it. Never throws: every failure is recorded in the result.
    /// </summary>
    /// <param name="innerPaths">What to extract: paths inside the archive, all in one folder.</param>
    /// <param name="destination">The folder to extract into; created when missing.</param>
    /// <param name="progress">Filled in as the extraction runs, and polled for cancellation.</param>
    /// <param name="onProgress">Called now and then while the extraction runs.</param>
    /// <returns>What happened.</returns>
    public OperationResult Extract(
        IReadOnlyList<string> innerPaths,
        string destination,
        OperationProgress? progress = null,
        Action? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(innerPaths);
        ArgumentNullException.ThrowIfNull(destination);

        var result = new OperationResult("Extract");
        progress ??= new OperationProgress();
        var run = new Extraction(this, innerPaths, destination, progress, onProgress, result);

        try
        {
            run.Prepare();

            switch (Kind)
            {
                case ArchiveKind.Zip:
                    ExtractZip(run);
                    break;

                case ArchiveKind.Tar:
                case ArchiveKind.TarGzip:
                    ExtractTar(run);
                    break;

                default:
                    ExtractGzip(run);
                    break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException or NotSupportedException)
        {
            result.AddError(FilePath, e);
        }

        if (progress.Cancelled)
        {
            result.MarkCancelled();
        }

        run.Report(force: true);
        return result;
    }

    // ------------------------------------------------------------------ catalogue readers

    private void ReadZipCatalogue()
    {
        using ZipArchive zip = ZipFile.OpenRead(FilePath);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string? path = Sanitize(entry.FullName);
            if (path is null)
            {
                continue;
            }

            bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            Add(path, directory, directory ? 0 : entry.Length, SafeLocal(entry.LastWriteTime), DosAttributes(entry.ExternalAttributes));
        }
    }

    private void ReadTarCatalogue()
    {
        using Stream stream = OpenTarStream();
        using var reader = new TarReader(stream);

        while (reader.GetNextEntry(copyData: false) is TarEntry entry)
        {
            if (!IsTarContent(entry.EntryType, out bool directory))
            {
                continue;
            }

            string? path = Sanitize(entry.Name);
            if (path is not null)
            {
                Add(path, directory, directory ? 0 : entry.Length, entry.ModificationTime.LocalDateTime, 0);
            }
        }
    }

    private void ReadGzipCatalogue(FileInfo info)
    {
        // A lone gzip'd file holds exactly one member: the archive's own name without ".gz". Its
        // size is the ISIZE trailer - the length modulo 2^32, exact for anything under 4 GB.
        long size = 0;
        using (FileStream stream = File.OpenRead(FilePath))
        {
            if (stream.Length >= 18)
            {
                Span<byte> trailer = stackalloc byte[4];
                stream.Seek(-4, SeekOrigin.End);
                stream.ReadExactly(trailer);
                size = BitConverter.ToUInt32(trailer);
            }
        }

        Add(GzipMemberName(), directory: false, size, info.LastWriteTime, 0);
    }

    private string GzipMemberName()
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(FilePath);
        return string.IsNullOrEmpty(name) ? "content" : name;
    }

    private void Add(string path, bool directory, long size, DateTime modified, FileAttributes attributes)
    {
        EnsureParents(path, modified);

        var item = new ArchiveItem
        {
            Path = path,
            IsDirectory = directory,
            Size = size,
            Modified = modified,
            Attributes = directory ? FileAttributes.Directory | attributes : attributes,
        };

        string parent = ParentOf(path);
        if (_items.TryGetValue(path, out ArchiveItem? existing))
        {
            // A later entry for the same name wins, as tar extracts it - and an explicit folder
            // entry replaces the one synthesised for its children, keeping its own time stamp.
            List<ArchiveItem> siblings = _children[parent];
            siblings[siblings.IndexOf(existing)] = item;
        }
        else
        {
            _children[parent].Add(item);
        }

        _items[path] = item;

        if (directory && !_children.ContainsKey(path))
        {
            _children[path] = [];
        }
    }

    private void EnsureParents(string path, DateTime modified)
    {
        string parent = ParentOf(path);
        if (parent.Length == 0 || _items.ContainsKey(parent))
        {
            return;
        }

        EnsureParents(parent, modified);

        var folder = new ArchiveItem
        {
            Path = parent,
            IsDirectory = true,
            Modified = modified,
            Attributes = FileAttributes.Directory,
        };

        _items[parent] = folder;
        _children[ParentOf(parent)].Add(folder);
        _children[parent] = [];
    }

    // ------------------------------------------------------------------ extraction

    private void ExtractZip(Extraction run)
    {
        using ZipArchive zip = ZipFile.OpenRead(FilePath);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (run.Cancelled)
            {
                return;
            }

            string? path = Sanitize(entry.FullName);
            if (path is null || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            run.ExtractFile(path, entry.Length, SafeLocal(entry.LastWriteTime), entry.Open);
        }
    }

    private void ExtractTar(Extraction run)
    {
        using Stream stream = OpenTarStream();
        using var reader = new TarReader(stream);

        while (!run.Cancelled && reader.GetNextEntry(copyData: false) is TarEntry entry)
        {
            if (!IsTarContent(entry.EntryType, out bool directory) || directory)
            {
                continue;
            }

            string? path = Sanitize(entry.Name);
            if (path is null)
            {
                continue;
            }

            // The data stream belongs to the reader, which skips whatever is left of it on the next
            // entry; disposing it here would make that skip throw.
            run.ExtractFile(path, entry.Length, entry.ModificationTime.LocalDateTime, () => entry.DataStream ?? Stream.Null, ownsStream: false);
        }
    }

    private void ExtractGzip(Extraction run)
    {
        string name = GzipMemberName();
        ArchiveItem? item = Find(name);
        run.ExtractFile(
            name,
            item?.Size ?? 0,
            item?.Modified ?? FileStamp,
            () => new GZipStream(File.OpenRead(FilePath), CompressionMode.Decompress));
    }

    private Stream OpenTarStream()
    {
        FileStream file = File.OpenRead(FilePath);
        return Kind == ArchiveKind.TarGzip ? new GZipStream(file, CompressionMode.Decompress) : file;
    }

    private static bool IsTarContent(TarEntryType type, out bool directory)
    {
        directory = type == TarEntryType.Directory;
        return directory || type is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;
    }

    /// <summary>One extraction run: which items were asked for, where they go, and the counters.</summary>
    private sealed class Extraction
    {
        private readonly ArchiveFile _archive;
        private readonly IReadOnlyList<string> _roots;
        private readonly string _destination;
        private readonly OperationProgress _progress;
        private readonly Action? _onProgress;
        private readonly OperationResult _result;
        private long _lastReport;

        public Extraction(
            ArchiveFile archive,
            IReadOnlyList<string> roots,
            string destination,
            OperationProgress progress,
            Action? onProgress,
            OperationResult result)
        {
            _archive = archive;
            _roots = [.. roots.Where(static r => !string.IsNullOrEmpty(r))];
            _destination = System.IO.Path.GetFullPath(destination);
            _progress = progress;
            _onProgress = onProgress;
            _result = result;
        }

        public bool Cancelled => _progress.Cancelled;

        /// <summary>Counts the totals from the catalogue and creates every folder asked for, empty ones included.</summary>
        public void Prepare()
        {
            Directory.CreateDirectory(_destination);

            long files = 0;
            long bytes = 0;
            foreach (ArchiveItem item in _archive.Items)
            {
                string? target = TargetFor(item.Path);
                if (target is null)
                {
                    continue;
                }

                if (item.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    files++;
                    bytes += item.Size;
                }
            }

            _progress.TotalFiles = files;
            _progress.TotalBytes = bytes;
            _progress.TotalsKnown = true;
            Report(force: true);
        }

        public void ExtractFile(string path, long size, DateTime modified, Func<Stream> open, bool ownsStream = true)
        {
            string? target = TargetFor(path);
            if (target is null)
            {
                return;
            }

            _progress.BeginFile(ArchivePath.Combine(_archive.FilePath, path), target, size);
            Report(force: true);

            try
            {
                string? folder = System.IO.Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                Stream source = open();
                try
                {
                    using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    byte[] block = new byte[CopyBlock];
                    int read;
                    while ((read = source.Read(block, 0, block.Length)) > 0)
                    {
                        output.Write(block, 0, read);
                        _progress.Advance(read);
                        Report(force: false);

                        if (Cancelled)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    if (ownsStream)
                    {
                        source.Dispose();
                    }
                }

                if (Cancelled)
                {
                    TryDelete(target);
                    return;
                }

                TrySetTime(target, modified);
                _progress.CompleteFile();
                _result.FilesProcessed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException or NotSupportedException)
            {
                _progress.CompleteFile();
                _result.AddError(ArchivePath.Combine(_archive.FilePath, path), e);
            }
        }

        public void Report(bool force)
        {
            long now = Environment.TickCount64;
            if (!force && now - _lastReport < ReportIntervalMs)
            {
                return;
            }

            _lastReport = now;
            _onProgress?.Invoke();
        }

        /// <summary>
        /// Where an item lands: under the destination by its path relative to the folder the roots
        /// sit in, or <see langword="null"/> when it is not one of the roots nor inside one.
        /// </summary>
        private string? TargetFor(string path)
        {
            foreach (string root in _roots)
            {
                if (!IsUnder(path, root) && !string.Equals(path, root, StringComparison.Ordinal))
                {
                    continue;
                }

                string parent = ParentOf(root);
                string relative = parent.Length == 0 ? path : path[(parent.Length + 1)..];
                string target = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(_destination, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

                // The names were sanitised when read; this is the belt to that pair of braces.
                string prefix = _destination.EndsWith(System.IO.Path.DirectorySeparatorChar)
                    ? _destination
                    : _destination + System.IO.Path.DirectorySeparatorChar;
                return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? target : null;
            }

            return null;
        }

        private static void TrySetTime(string path, DateTime modified)
        {
            if (modified == default)
            {
                return;
            }

            try
            {
                File.SetLastWriteTime(path, modified);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A time stamp is not worth failing an extraction over.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A half-written file left behind by a cancel is untidy, not harmful.
            }
        }
    }

    // ------------------------------------------------------------------ names

    /// <summary>
    /// Normalises an entry name to the catalogue's form, or <see langword="null"/> for one that must
    /// never be extracted: empty, rooted, or climbing out with <c>..</c>.
    /// </summary>
    /// <param name="name">The name as the archive stores it.</param>
    /// <returns>The <c>/</c> separated path, or <see langword="null"/>.</returns>
    public static string? Sanitize(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string[] parts = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == ".." || part.Contains(':', StringComparison.Ordinal))
            {
                return null;
            }

            kept.Add(part);
        }

        if (kept.Count == 0 || name[0] is '/' or '\\')
        {
            return null;
        }

        return string.Join('/', kept);
    }

    /// <summary>The folder an inner path sits in; empty at the archive's root.</summary>
    /// <param name="path">The <c>/</c> separated path.</param>
    /// <returns>The parent path.</returns>
    public static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>The last segment of an inner path.</summary>
    /// <param name="path">The <c>/</c> separated path.</param>
    /// <returns>The name.</returns>
    public static string LeafOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>Joins a folder and a name inside the archive.</summary>
    /// <param name="folder">The folder; empty for the root.</param>
    /// <param name="name">The name.</param>
    /// <returns>The joined path.</returns>
    public static string Join(string folder, string name) =>
        string.IsNullOrEmpty(folder) ? name : folder + "/" + name;

    private static bool IsUnder(string path, string folder) =>
        folder.Length == 0
            ? path.Length > 0
            : path.Length > folder.Length + 1 &&
              path.StartsWith(folder, StringComparison.Ordinal) &&
              path[folder.Length] == '/';

    private static FileAttributes DosAttributes(int external)
    {
        // The low byte holds DOS attributes when the archive was made on Windows; a Unix-made one
        // keeps its mode in the high word and leaves the low byte zero.
        const FileAttributes kept = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
        return (FileAttributes)(external & 0xFF) & kept;
    }

    private static DateTime SafeLocal(DateTimeOffset stamp)
    {
        try
        {
            return stamp.LocalDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return default;
        }
    }
}
