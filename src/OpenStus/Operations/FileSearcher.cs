using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using OpenStus.Files;

namespace OpenStus.Operations;

/// <summary>
/// What Alt+F7 is looking for.
/// </summary>
/// <remarks>
/// A search with no <see cref="Text"/> matches on the name alone. With <see cref="Text"/> set, a file
/// has to match the mask <em>and</em> contain the text; the mask is therefore the cheap filter that
/// keeps the expensive content pass off most of the tree.
/// </remarks>
public sealed class SearchOptions
{
    /// <summary>
    /// The file mask list, comma or semicolon separated, with <c>!</c> prefixes excluding - the same
    /// syntax <see cref="FileMask"/> uses everywhere else. Empty matches everything.
    /// </summary>
    public string Mask { get; set; } = "*";

    /// <summary>The text to look for inside each file, or <see langword="null"/> to search names only.</summary>
    public string? Text { get; set; }

    /// <summary>Treat <see cref="Text"/> as a .NET regular expression rather than a literal.</summary>
    public bool UseRegex { get; set; }

    /// <summary>Match <see cref="Text"/> case sensitively.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Require <see cref="Text"/> to sit on word boundaries.</summary>
    public bool WholeWords { get; set; }

    /// <summary>Look inside hidden and system entries.</summary>
    public bool IncludeHidden { get; set; } = true;

    /// <summary>Descend into subfolders.</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// How deep to descend: 0 visits only the folder the search started in, 1 also visits its
    /// children, and so on.
    /// </summary>
    public int MaxDepth { get; set; } = int.MaxValue;

    /// <summary>
    /// Report folders whose name matches the mask as results too. Never combined with a content
    /// search - a folder has no content to search.
    /// </summary>
    public bool MatchDirectories { get; set; }

    /// <summary>
    /// Descend into reparse points. Off by default: a junction pointing at one of its own ancestors
    /// makes the walk run forever.
    /// </summary>
    public bool FollowLinks { get; set; }

    /// <summary>
    /// Files larger than this are not opened for a content search and therefore never match. Zero
    /// means no limit.
    /// </summary>
    public long MaxContentBytes { get; set; }

    /// <summary>Stop after this many matches. Zero means no limit.</summary>
    public int MaxResults { get; set; }

    /// <summary>Keep the matches in <see cref="SearchResult.Items"/> as well as reporting them.</summary>
    public bool CollectMatches { get; set; } = true;

    /// <summary>
    /// Force a text encoding for the content search instead of detecting one per file.
    /// </summary>
    public Encoding? Encoding { get; set; }

    /// <summary>Whether a content search is being asked for at all.</summary>
    public bool HasContentSearch => !string.IsNullOrEmpty(Text);

    /// <summary>Returns an independent copy of these options.</summary>
    /// <returns>The copy.</returns>
    public SearchOptions Clone() => (SearchOptions)MemberwiseClone();
}

/// <summary>
/// What a search found and how much ground it covered.
/// </summary>
public sealed class SearchResult
{
    private readonly List<FileEntry> _items = [];
    private readonly List<OperationError> _errors = [];

    /// <summary>Whether the search stopped early - cancelled, or capped by <see cref="SearchOptions.MaxResults"/>.</summary>
    public bool Cancelled { get; set; }

    /// <summary>How many files were considered.</summary>
    public int FilesScanned { get; set; }

    /// <summary>How many folders were opened.</summary>
    public int DirectoriesScanned { get; set; }

    /// <summary>How many matches were reported.</summary>
    public int Matches { get; set; }

    /// <summary>
    /// The matches, when <see cref="SearchOptions.CollectMatches"/> is set; empty otherwise.
    /// </summary>
    public IReadOnlyList<FileEntry> Items => _items;

    /// <summary>Folders and files the search could not read.</summary>
    public IReadOnlyList<OperationError> Errors => _errors;

    /// <summary>Whether anything could not be read.</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Adds a match to <see cref="Items"/>.</summary>
    /// <param name="entry">The matching entry.</param>
    public void Add(FileEntry entry) => _items.Add(entry);

    /// <summary>Records something the search could not read.</summary>
    /// <param name="path">The path.</param>
    /// <param name="error">The failure.</param>
    public void AddError(string path, Exception error) =>
        _errors.Add(new OperationError("Search", path, OperationResult.Describe(error)) { Exception = error });

    /// <inheritdoc/>
    public override string ToString() =>
        $"Search: {Matches} match(es) in {FilesScanned} file(s), {DirectoriesScanned} folder(s)" +
        (Cancelled ? ", cancelled" : string.Empty);
}

/// <summary>
/// The recursive file finder behind Alt+F7: by name mask, and optionally by the text inside the file.
/// </summary>
/// <remarks>
/// <para>
/// The walk is iterative, never follows reparse points unless asked to, and never throws - an
/// unreadable folder lands in <see cref="SearchResult.Errors"/> and the walk carries on. Matches are
/// pushed to a callback as they are found so a dialog can fill its list while the search runs, and
/// every folder is announced through a second callback so the dialog can show where it is.
/// </para>
/// <para>
/// The content search detects the file's encoding from its byte order mark, or failing that from the
/// bytes themselves (UTF-16 by its NUL pattern, UTF-8 by strict validation), and falls back to
/// Latin-1 so that a literal search still works over arbitrary 8-bit data. Files are streamed in
/// 64 K character chunks with an overlap, so a match is found even when it straddles a chunk
/// boundary - up to the overlap length.
/// </para>
/// </remarks>
public static class FileSearcher
{
    /// <summary>How many characters are decoded at a time during a content search.</summary>
    public const int ContentChunkChars = 64 * 1024;

    /// <summary>
    /// The longest match a content search can find across a chunk boundary. A literal term uses its
    /// own length instead when that is shorter.
    /// </summary>
    public const int ContentOverlapChars = 4 * 1024;

    private const int EncodingSampleBytes = 8 * 1024;

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
    /// Searches one folder tree.
    /// </summary>
    /// <param name="root">The folder to start from.</param>
    /// <param name="options">What to look for, or <see langword="null"/> for "every file, by name".</param>
    /// <param name="onMatch">Called with each match as it is found.</param>
    /// <param name="onDirectory">Called with each folder as it is opened, for a progress line.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>What was found. Never <see langword="null"/>, never throwing.</returns>
    public static SearchResult Search(
        string root,
        SearchOptions? options = null,
        Action<FileEntry>? onMatch = null,
        Action<string>? onDirectory = null,
        CancellationToken cancellationToken = default) =>
        Search(root is null ? [] : [root], options, onMatch, onDirectory, cancellationToken);

    /// <summary>
    /// Searches several folder trees in one pass, sharing the match cap and the result list.
    /// </summary>
    /// <param name="roots">The folders to start from.</param>
    /// <param name="options">What to look for, or <see langword="null"/> for "every file, by name".</param>
    /// <param name="onMatch">Called with each match as it is found.</param>
    /// <param name="onDirectory">Called with each folder as it is opened, for a progress line.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>What was found. Never <see langword="null"/>, never throwing.</returns>
    public static SearchResult Search(
        IReadOnlyList<string> roots,
        SearchOptions? options = null,
        Action<FileEntry>? onMatch = null,
        Action<string>? onDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var result = new SearchResult();
        options ??= new SearchOptions();

        try
        {
            Regex? regex = null;
            if (options.HasContentSearch && options.UseRegex)
            {
                if (!TryBuildRegex(options, out regex))
                {
                    result.AddError(string.Empty, new ArgumentException($"\"{options.Text}\" is not a valid regular expression"));
                    return result;
                }
            }

            foreach (string root in roots ?? [])
            {
                if (result.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string start;
                try
                {
                    start = Path.GetFullPath(root);
                }
                catch (Exception e) when (IsFileSystemException(e))
                {
                    result.AddError(root, e);
                    continue;
                }

                Walk(start, options, regex, result, onMatch, onDirectory, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
            }
        }
        catch (Exception e)
        {
            result.AddError(string.Empty, e);
        }

        return result;
    }

    /// <summary>
    /// Whether a file contains a piece of text, deciding the encoding for itself.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="text">The literal or pattern to look for.</param>
    /// <param name="useRegex">Treat <paramref name="text"/> as a regular expression.</param>
    /// <param name="caseSensitive">Match case sensitively.</param>
    /// <param name="wholeWords">Require word boundaries around the match.</param>
    /// <param name="maxBytes">Refuse to open files larger than this; zero means no limit.</param>
    /// <param name="encoding">Force an encoding instead of detecting one.</param>
    /// <returns><see langword="true"/> when the file contains the text. Never throws.</returns>
    public static bool ContainsText(
        string path,
        string text,
        bool useRegex = false,
        bool caseSensitive = false,
        bool wholeWords = false,
        long maxBytes = 0,
        Encoding? encoding = null)
    {
        var options = new SearchOptions
        {
            Text = text,
            UseRegex = useRegex,
            CaseSensitive = caseSensitive,
            WholeWords = wholeWords,
            MaxContentBytes = maxBytes,
            Encoding = encoding,
        };

        Regex? regex = null;
        if (useRegex && !TryBuildRegex(options, out regex))
        {
            return false;
        }

        return FileContains(path, options, regex, CancellationToken.None);
    }

    /// <summary>
    /// Works out which encoding a file's bytes are in.
    /// </summary>
    /// <remarks>
    /// A byte order mark wins outright. Failing that, a strong pattern of NUL bytes on one side of
    /// each pair means UTF-16, bytes that decode strictly as UTF-8 mean UTF-8, and anything else is
    /// read as Latin-1 so that every byte survives the round trip and a literal search still works.
    /// </remarks>
    /// <param name="path">The file to sample.</param>
    /// <returns>The encoding to read the file with. Never throws; defaults to UTF-8.</returns>
    public static Encoding DetectEncoding(string path)
    {
        try
        {
            using FileStream stream = File.Open(
                path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete });

            byte[] buffer = ArrayPool<byte>.Shared.Rent(EncodingSampleBytes);
            try
            {
                int read = stream.Read(buffer, 0, EncodingSampleBytes);
                return DetectEncoding(buffer.AsSpan(0, Math.Max(read, 0)));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// Works out which encoding a block of bytes is in.
    /// </summary>
    /// <param name="sample">The first few kilobytes of the file.</param>
    /// <returns>The encoding.</returns>
    public static Encoding DetectEncoding(ReadOnlySpan<byte> sample)
    {
        if (sample.Length >= 4 && sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }

        if (sample.Length >= 4 && sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0xFE && sample[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        if (sample.Length == 0)
        {
            return Encoding.UTF8;
        }

        int evenNuls = 0;
        int oddNuls = 0;
        int pairs = Math.Min(sample.Length, EncodingSampleBytes) / 2 * 2;
        for (int i = 0; i < pairs; i += 2)
        {
            if (sample[i] == 0)
            {
                evenNuls++;
            }

            if (sample[i + 1] == 0)
            {
                oddNuls++;
            }
        }

        int half = pairs / 2;
        if (half > 0)
        {
            // Latin text in UTF-16 puts a NUL in every high byte: little endian fills the odd
            // positions, big endian the even ones.
            if (oddNuls > half * 3 / 4 && evenNuls == 0)
            {
                return Encoding.Unicode;
            }

            if (evenNuls > half * 3 / 4 && oddNuls == 0)
            {
                return Encoding.BigEndianUnicode;
            }
        }

        return IsValidUtf8(sample) ? Encoding.UTF8 : Encoding.Latin1;
    }

    private static void Walk(
        string root,
        SearchOptions options,
        Regex? regex,
        SearchResult result,
        Action<FileEntry>? onMatch,
        Action<string>? onDirectory,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            if (result.Cancelled || cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                return;
            }

            (string current, int depth) = pending.Pop();

            result.DirectoriesScanned++;
            Notify(onDirectory, current);

            List<FileSystemInfo> children;
            try
            {
                children = [.. new DirectoryInfo(current).EnumerateFileSystemInfos("*", Options)];
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                result.AddError(current, e);
                continue;
            }

            foreach (FileSystemInfo info in children)
            {
                if (result.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    return;
                }

                FileAttributes attributes;
                try
                {
                    attributes = info.Attributes;
                }
                catch (Exception e) when (IsFileSystemException(e))
                {
                    continue;
                }

                bool hidden = (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                if (!options.IncludeHidden && hidden)
                {
                    continue;
                }

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isLink = (attributes & FileAttributes.ReparsePoint) != 0;

                if (isDirectory)
                {
                    if (options.MatchDirectories && !options.HasContentSearch &&
                        FileMask.IsMatchAny(info.Name, options.Mask))
                    {
                        Report(ToEntry(info, attributes, isDirectory: true), options, result, onMatch);
                    }

                    if (options.Recursive && depth < options.MaxDepth && (!isLink || options.FollowLinks))
                    {
                        pending.Push((info.FullName, depth + 1));
                    }

                    continue;
                }

                result.FilesScanned++;

                if (!FileMask.IsMatchAny(info.Name, options.Mask))
                {
                    continue;
                }

                if (options.HasContentSearch && !FileContains(info.FullName, options, regex, cancellationToken))
                {
                    continue;
                }

                Report(ToEntry(info, attributes, isDirectory: false), options, result, onMatch);
            }
        }
    }

    private static void Report(FileEntry entry, SearchOptions options, SearchResult result, Action<FileEntry>? onMatch)
    {
        result.Matches++;

        if (options.CollectMatches)
        {
            result.Add(entry);
        }

        if (onMatch is not null)
        {
            try
            {
                onMatch(entry);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                return;
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                result.AddError(entry.FullPath, e);
            }
        }

        if (options.MaxResults > 0 && result.Matches >= options.MaxResults)
        {
            result.Cancelled = true;
        }
    }

    private static void Notify(Action<string>? onDirectory, string path)
    {
        if (onDirectory is null)
        {
            return;
        }

        try
        {
            onDirectory(path);
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // A progress callback is never allowed to stop the search.
        }
    }

    private static bool TryBuildRegex(SearchOptions options, out Regex? regex)
    {
        regex = null;
        if (string.IsNullOrEmpty(options.Text))
        {
            return false;
        }

        RegexOptions regexOptions = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (!options.CaseSensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        string pattern = options.WholeWords ? $@"\b(?:{options.Text})\b" : options.Text;

        try
        {
            regex = new Regex(pattern, regexOptions, TimeSpan.FromSeconds(5));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool FileContains(string path, SearchOptions options, Regex? regex, CancellationToken cancellationToken)
    {
        string term = options.Text ?? string.Empty;
        if (term.Length == 0)
        {
            return true;
        }

        char[]? buffer = null;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            long length = info.Length;
            if (options.MaxContentBytes > 0 && length > options.MaxContentBytes)
            {
                return false;
            }

            if (length == 0)
            {
                return false;
            }

            Encoding encoding = options.Encoding ?? DetectEncoding(path);

            using FileStream stream = File.Open(
                path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete });

            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024);

            int overlap = regex is null
                ? Math.Min(Math.Max(term.Length - 1, 0), ContentOverlapChars)
                : ContentOverlapChars;

            buffer = ArrayPool<char>.Shared.Rent(ContentChunkChars + overlap);
            int carried = 0;

            StringComparison comparison = options.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                int read = reader.Read(buffer, carried, ContentChunkChars);
                if (read <= 0)
                {
                    return false;
                }

                int total = carried + read;
                var window = new ReadOnlySpan<char>(buffer, 0, total);

                if (regex is not null)
                {
                    if (regex.IsMatch(window))
                    {
                        return true;
                    }
                }
                else if (Contains(window, term, comparison, options.WholeWords))
                {
                    return true;
                }

                carried = Math.Min(overlap, total);
                if (carried > 0)
                {
                    Array.Copy(buffer, total - carried, buffer, 0, carried);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (Exception e) when (IsFileSystemException(e) || e is DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
    }

    private static bool Contains(ReadOnlySpan<char> window, string term, StringComparison comparison, bool wholeWords)
    {
        if (!wholeWords)
        {
            return window.IndexOf(term, comparison) >= 0;
        }

        int offset = 0;
        while (offset < window.Length)
        {
            int found = window[offset..].IndexOf(term, comparison);
            if (found < 0)
            {
                return false;
            }

            int start = offset + found;
            int end = start + term.Length;

            bool leftOk = start == 0 || !IsWordChar(window[start - 1]);
            bool rightOk = end >= window.Length || !IsWordChar(window[end]);

            if (leftOk && rightOk)
            {
                return true;
            }

            offset = start + 1;
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsValidUtf8(ReadOnlySpan<byte> sample)
    {
        // Drop a multi-byte sequence the sample cut in half, or a perfectly good file would be
        // mistaken for binary because of where the 8 KB boundary fell.
        int end = sample.Length;
        for (int back = 0; back < 4 && end > 0; back++)
        {
            byte b = sample[end - 1];
            if ((b & 0x80) == 0)
            {
                break;
            }

            end--;

            if ((b & 0xC0) == 0xC0)
            {
                break;
            }
        }

        if (end <= 0)
        {
            return true;
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            strict.GetCharCount(sample[..end]);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static FileEntry ToEntry(FileSystemInfo info, FileAttributes attributes, bool isDirectory)
    {
        long size = 0;
        DateTime modified = default;
        DateTime created = default;
        DateTime accessed = default;

        try
        {
            modified = info.LastWriteTime;
            created = info.CreationTime;
            accessed = info.LastAccessTime;

            if (!isDirectory && info is FileInfo file)
            {
                size = file.Length;
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // A file that vanished mid-search is still worth reporting by name.
        }

        return new FileEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = isDirectory,
            Size = size,
            Modified = modified,
            Created = created,
            Accessed = accessed,
            Attributes = attributes,
        };
    }

    private static bool IsFileSystemException(Exception e) =>
        e is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;
}
