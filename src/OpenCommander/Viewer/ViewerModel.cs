using System.Text;
using OpenCommander.Text;

namespace OpenCommander.Viewer;

/// <summary>
/// Random access over the bytes of a file being viewed, without ever holding more than a window of
/// it in memory.
/// </summary>
/// <remarks>
/// <para>
/// A position in the file is a <em>byte offset</em>, never a line number - which is exactly how Far
/// Manager's viewer works, and the only design that opens a multi-gigabyte log instantly. Moving up
/// or down a line is a bounded scan around the current offset, so memory use is constant no matter
/// how large the file is and no matter where in it the user jumps.
/// </para>
/// <para>
/// On top of that, a forward index of line starts is grown lazily as the user scrolls down. When it
/// happens to cover the offset being asked about, moving backwards becomes an exact binary search
/// instead of a scan. It is a cache and nothing depends on it being present, so jumping straight to
/// the end of a huge file simply skips it.
/// </para>
/// <para>
/// A "line" is capped at <see cref="MaxLineBytes"/>. A file with no terminators at all - a binary,
/// typically - therefore reads as a sequence of fixed size chunks rather than as one enormous
/// string. Because the cap is a fixed stride from the start of the line, stepping back over such a
/// chunk lands on exactly the offset stepping forward came from.
/// </para>
/// </remarks>
public sealed class ViewerModel : IDisposable
{
    /// <summary>The longest run of bytes presented as a single line, and the backward scan window.</summary>
    public const int MaxLineBytes = 64 * 1024;

    /// <summary>How many bytes a hex mode row shows.</summary>
    public const int HexBytesPerRow = 16;

    /// <summary>How much of the file the line ending sniff looks at.</summary>
    public const int LineEndingSampleSize = 64 * 1024;

    /// <summary>How far back a backwards search will scan before giving up, in bytes.</summary>
    public const long BackwardSearchLimit = 64L * 1024 * 1024;

    /// <summary>How many line starts the lazy forward index will remember at most.</summary>
    public const int MaxIndexedLines = 1 << 20;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly byte[] _window = new byte[MaxLineBytes];
    private readonly List<long> _lineStarts = [];
    private readonly int _codeUnit;
    private readonly bool _bigEndian;
    private readonly long _length;
    private bool _disposed;

    /// <summary>Opens a file for viewing. The file stays shareable, so it can still be written to.</summary>
    /// <param name="path">The file to open.</param>
    /// <exception cref="IOException">The file could not be opened.</exception>
    public ViewerModel(string path)
        : this(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            path,
            ownsStream: true)
    {
    }

    /// <summary>
    /// Views an already open stream, which is how the tests drive the model without touching disk.
    /// </summary>
    /// <param name="stream">A readable, seekable stream.</param>
    /// <param name="path">The name to show on the status line.</param>
    /// <param name="ownsStream">When set, disposing the model disposes the stream too.</param>
    /// <exception cref="ArgumentException">The stream is not readable and seekable.</exception>
    public ViewerModel(Stream stream, string path, bool ownsStream = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The viewer needs a readable, seekable stream.", nameof(stream));
        }

        _stream = stream;
        _ownsStream = ownsStream;
        FilePath = path ?? string.Empty;
        _length = stream.Length;

        byte[] sample = EncodingDetector.ReadSample(stream, LineEndingSampleSize);
        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(sample);
        Encoding = encoding;
        HasBom = hasBom;
        _codeUnit = EncodingDetector.CodeUnitSize(encoding);
        _bigEndian = EncodingDetector.IsBigEndian(encoding);
        FirstLineOffset = EncodingDetector.BomLength(sample);
        LineEnding = LineEndings.Detect(EncodingDetector.DecodeSkippingBom(sample, encoding));
        IsBinary = EncodingDetector.LooksBinary(sample);

        if (_length > FirstLineOffset)
        {
            _lineStarts.Add(FirstLineOffset);
        }
    }

    /// <summary>Creates a model over an in-memory copy of the given bytes.</summary>
    /// <param name="data">The content.</param>
    /// <param name="path">The name to show on the status line.</param>
    /// <returns>A model that owns its backing stream.</returns>
    public static ViewerModel FromBytes(byte[] data, string path = "") =>
        new(new MemoryStream(data ?? [], writable: false), path, ownsStream: true);

    /// <summary>The path the file was opened from.</summary>
    public string FilePath { get; }

    /// <summary>The file name alone, for the title and status lines.</summary>
    public string FileName =>
        string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);

    /// <summary>The file size in bytes.</summary>
    public long Length => _length;

    /// <summary>The encoding the content is decoded with.</summary>
    public Encoding Encoding { get; }

    /// <summary>Whether the file starts with a byte order mark.</summary>
    public bool HasBom { get; }

    /// <summary>The encoding name shown on the status line, for example <c>"UTF-8 BOM"</c>.</summary>
    public string EncodingName => EncodingDetector.DisplayName(Encoding, HasBom);

    /// <summary>The line terminator convention detected in the head of the file.</summary>
    public LineEndingStyle LineEnding { get; }

    /// <summary>Whether the head of the file contains NUL bytes.</summary>
    public bool IsBinary { get; }

    /// <summary>The offset of the first line, i.e. just past any byte order mark.</summary>
    public long FirstLineOffset { get; }

    /// <summary>How many line starts the lazy forward index currently holds.</summary>
    public int IndexedLineCount => _lineStarts.Count;

    /// <summary>Whether the lazy forward index has reached the end of the file.</summary>
    public bool IsFullyIndexed => _lineStarts.Count > 0 && IndexedThrough >= _length;

    /// <summary>The offset the lazy forward index has scanned up to.</summary>
    public long IndexedThrough { get; private set; }

    /// <summary>Clamps an arbitrary offset into the addressable range of the file.</summary>
    /// <param name="offset">The offset to clamp.</param>
    /// <returns>An offset between <see cref="FirstLineOffset"/> and <see cref="Length"/> inclusive.</returns>
    public long Clamp(long offset) => Math.Clamp(offset, FirstLineOffset, _length);

    /// <summary>
    /// Reads the line starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">A line start offset; it is clamped into range first.</param>
    /// <param name="next">
    /// Receives the offset of the following line, which is <see cref="Length"/> at the end of the
    /// file. It is always greater than <paramref name="offset"/> unless the file is exhausted, so
    /// a caller can loop on it safely.
    /// </param>
    /// <returns>The line text with its terminator stripped.</returns>
    public string ReadLine(long offset, out long next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        offset = Clamp(offset);
        if (offset >= _length)
        {
            next = _length;
            return string.Empty;
        }

        int want = (int)Math.Min(MaxLineBytes, _length - offset);
        int have = ReadAt(offset, _window, want);
        if (have <= 0)
        {
            next = _length;
            return string.Empty;
        }

        int unit = _codeUnit;
        int end = -1;
        int skip = 0;

        for (int i = 0; i + unit <= have; i += unit)
        {
            int cu = CodeUnitAt(_window, i);
            if (cu == '\n')
            {
                end = i;
                skip = unit;
                break;
            }

            if (cu != '\r')
            {
                continue;
            }

            end = i;
            skip = unit;

            if (i + (2 * unit) <= have)
            {
                if (CodeUnitAt(_window, i + unit) == '\n')
                {
                    skip = 2 * unit;
                }
            }
            else if (offset + i + unit < _length && PeekIsLineFeed(offset + i + unit))
            {
                // The CR sat on the very edge of the window; the LF is the next unit in the file.
                skip = 2 * unit;
            }

            break;
        }

        if (end < 0)
        {
            // No terminator inside the window: either the file ended, or the line is longer than
            // MaxLineBytes and gets a hard break here.
            end = have - (have % unit);
            skip = 0;
            if (offset + have < _length)
            {
                end = TrimIncompleteTail(_window, end);
            }
        }

        next = offset + end + skip;
        if (next <= offset)
        {
            next = Math.Min(_length, offset + unit);
        }

        RecordLineStart(offset, next);
        return Encoding.GetString(_window, 0, end);
    }

    /// <summary>The offset of the line after the one starting at <paramref name="offset"/>.</summary>
    /// <param name="offset">A line start offset.</param>
    /// <returns>The next line start, or <see cref="Length"/> at the end of the file.</returns>
    public long NextLineOffset(long offset)
    {
        ReadLine(offset, out long next);
        return next;
    }

    /// <summary>
    /// The offset of the line before the one starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">A line start offset.</param>
    /// <returns>
    /// The previous line start, or <see cref="FirstLineOffset"/> when already at the top. Answered
    /// from the lazy index when it covers the offset, otherwise by scanning one
    /// <see cref="MaxLineBytes"/> window backwards.
    /// </returns>
    public long PreviousLineOffset(long offset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        offset = Clamp(offset);
        if (offset <= FirstLineOffset)
        {
            return FirstLineOffset;
        }

        if (TryPreviousFromIndex(offset, out long indexed))
        {
            return indexed;
        }

        long start = Math.Max(FirstLineOffset, offset - MaxLineBytes);
        int want = (int)(offset - start);
        int have = ReadAt(start, _window, want);
        if (have <= 0)
        {
            return FirstLineOffset;
        }

        int unit = _codeUnit;

        // Walk backwards over the window looking for the last terminator that ends before offset.
        // Its end position is the start of the line we are after.
        for (int i = have - unit; i >= 0; i -= unit)
        {
            int cu = CodeUnitAt(_window, i);
            if (cu != '\n' && cu != '\r')
            {
                continue;
            }

            long terminatorEnd = start + i + unit;
            if (cu == '\r' && terminatorEnd < _length && PeekIsLineFeed(terminatorEnd))
            {
                terminatorEnd += unit;
            }

            if (terminatorEnd < offset)
            {
                return terminatorEnd;
            }
        }

        // A line longer than the window. MaxLineBytes is both the forward break stride and the
        // backward window, so stepping back exactly one window lands on the previous break.
        return start;
    }

    /// <summary>Moves <paramref name="lines"/> lines from an offset; negative counts move up.</summary>
    /// <param name="offset">The offset to move from.</param>
    /// <param name="lines">How many lines to move.</param>
    /// <returns>The resulting line start offset, clamped to the file.</returns>
    public long Advance(long offset, int lines)
    {
        long pos = Clamp(offset);
        if (lines > 0)
        {
            for (int i = 0; i < lines && pos < _length; i++)
            {
                pos = NextLineOffset(pos);
            }
        }
        else
        {
            for (int i = 0; i > lines && pos > FirstLineOffset; i--)
            {
                pos = PreviousLineOffset(pos);
            }
        }

        return pos;
    }

    /// <summary>
    /// The offset the top of the screen should sit at to show the end of the file.
    /// </summary>
    /// <param name="rows">How many rows the viewport has.</param>
    /// <returns>The line start <paramref name="rows"/> lines back from the end of the file.</returns>
    public long LastPageOffset(int rows) => Advance(_length, -Math.Max(1, rows));

    /// <summary>Reads raw bytes, which is what hex mode draws.</summary>
    /// <param name="offset">The offset to read from; clamped into range.</param>
    /// <param name="count">How many bytes to read.</param>
    /// <returns>The bytes read; shorter than requested at the end of the file.</returns>
    public byte[] ReadBlock(long offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        offset = Math.Clamp(offset, 0, _length);
        count = (int)Math.Clamp((long)Math.Max(0, count), 0, _length - offset);
        if (count == 0)
        {
            return [];
        }

        var buffer = new byte[count];
        int read = ReadAt(offset, buffer, count);
        return read == count ? buffer : buffer[..read];
    }

    /// <summary>
    /// Grows the lazy forward line index until it covers <paramref name="throughOffset"/>.
    /// </summary>
    /// <param name="throughOffset">The offset the index should reach.</param>
    /// <remarks>
    /// Purely an optimisation for scrolling back up; it is capped at
    /// <see cref="MaxIndexedLines"/> entries and it is always safe to skip.
    /// </remarks>
    public void EnsureIndexed(long throughOffset)
    {
        if (_lineStarts.Count == 0 || _lineStarts.Count >= MaxIndexedLines)
        {
            return;
        }

        long limit = Clamp(throughOffset);
        while (IndexedThrough < limit && _lineStarts.Count < MaxIndexedLines)
        {
            long from = _lineStarts[^1];
            long next = NextLineOffset(from);
            if (next <= from || next >= _length)
            {
                IndexedThrough = _length;
                return;
            }
        }
    }

    /// <summary>
    /// The one-based line number of an offset, when the lazy index happens to cover it.
    /// </summary>
    /// <param name="offset">The line start offset to look up.</param>
    /// <param name="lineNumber">Receives the one-based line number.</param>
    /// <returns><see langword="false"/> when the index does not reach that far.</returns>
    public bool TryGetLineNumber(long offset, out int lineNumber)
    {
        lineNumber = 0;
        offset = Clamp(offset);
        if (_lineStarts.Count == 0 || offset > IndexedThrough)
        {
            return false;
        }

        int i = _lineStarts.BinarySearch(offset);
        if (i < 0)
        {
            i = ~i - 1;
            if (i < 0)
            {
                return false;
            }
        }

        lineNumber = i + 1;
        return true;
    }

    /// <summary>
    /// Searches for text, line by line.
    /// </summary>
    /// <param name="needle">The text to find; an empty needle never matches.</param>
    /// <param name="fromOffset">The line start to begin at.</param>
    /// <param name="ignoreCase">Compare case insensitively.</param>
    /// <param name="backwards">Search towards the start of the file instead of the end.</param>
    /// <param name="lineOffset">Receives the start offset of the line the match sits on.</param>
    /// <param name="column">Receives the zero based character index of the match within that line.</param>
    /// <returns><see langword="true"/> when a match was found.</returns>
    /// <remarks>
    /// A forward search reads only as far as the first hit. A backwards search has to rescan from
    /// the start of the file (or from <see cref="BackwardSearchLimit"/> bytes back, whichever is
    /// closer) because lines cannot be decoded in reverse.
    /// </remarks>
    public bool Find(
        string needle,
        long fromOffset,
        bool ignoreCase,
        bool backwards,
        out long lineOffset,
        out int column)
    {
        lineOffset = Clamp(fromOffset);
        column = 0;
        if (string.IsNullOrEmpty(needle))
        {
            return false;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!backwards)
        {
            long pos = Clamp(fromOffset);
            while (pos < _length)
            {
                string line = ReadLine(pos, out long next);
                int at = line.IndexOf(needle, comparison);
                if (at >= 0)
                {
                    lineOffset = pos;
                    column = at;
                    return true;
                }

                if (next <= pos)
                {
                    break;
                }

                pos = next;
            }

            return false;
        }

        long stop = Clamp(fromOffset);
        long scan = Math.Max(FirstLineOffset, stop - BackwardSearchLimit);
        if (scan > FirstLineOffset)
        {
            // Re-align onto a real line start after the arbitrary byte cut.
            scan = NextLineOffset(PreviousLineOffset(scan));
        }

        bool found = false;
        while (scan < stop)
        {
            string line = ReadLine(scan, out long next);
            int at = line.IndexOf(needle, comparison);
            if (at >= 0)
            {
                lineOffset = scan;
                column = at;
                found = true;
            }

            if (next <= scan)
            {
                break;
            }

            scan = next;
        }

        return found;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }

    private int ReadAt(long offset, byte[] destination, int count)
    {
        if (offset >= _length || count <= 0)
        {
            return 0;
        }

        count = (int)Math.Min(count, _length - offset);
        _stream.Position = offset;
        return _stream.ReadAtLeast(destination.AsSpan(0, count), count, throwOnEndOfStream: false);
    }

    private bool PeekIsLineFeed(long offset)
    {
        Span<byte> unit = stackalloc byte[4];
        if (offset + _codeUnit > _length)
        {
            return false;
        }

        _stream.Position = offset;
        int read = _stream.ReadAtLeast(unit[.._codeUnit], _codeUnit, throwOnEndOfStream: false);
        return read == _codeUnit && CodeUnitAt(unit, 0) == '\n';
    }

    /// <summary>The value of the code unit at a byte index, honouring the encoding's width and order.</summary>
    private int CodeUnitAt(ReadOnlySpan<byte> data, int index) => _codeUnit switch
    {
        1 => data[index],
        2 => _bigEndian ? (data[index] << 8) | data[index + 1] : data[index] | (data[index + 1] << 8),
        _ => _bigEndian
            ? (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3]
            : data[index] | (data[index + 1] << 8) | (data[index + 2] << 16) | (data[index + 3] << 24),
    };

    /// <summary>
    /// Pulls a hard line break back off an incomplete trailing UTF-8 sequence, so a chunked
    /// pathological line does not sprout a replacement character at every break.
    /// </summary>
    private int TrimIncompleteTail(ReadOnlySpan<byte> data, int end)
    {
        if (_codeUnit != 1 || Encoding.CodePage != 65001 || end <= 0)
        {
            return end;
        }

        int lead = end;
        while (lead > 0 && (data[lead - 1] & 0xC0) == 0x80)
        {
            lead--;
        }

        if (lead == 0 || lead == end)
        {
            return end;
        }

        byte b = data[lead - 1];
        int need = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
        return (lead - 1) + need > end ? lead - 1 : end;
    }

    /// <summary>Appends to the lazy index when a read walked off its current tail.</summary>
    private void RecordLineStart(long offset, long next)
    {
        if (_lineStarts.Count == 0 || _lineStarts.Count >= MaxIndexedLines)
        {
            return;
        }

        if (offset != _lineStarts[^1] || next <= offset)
        {
            return;
        }

        IndexedThrough = next;
        if (next < _length)
        {
            _lineStarts.Add(next);
        }
    }

    private bool TryPreviousFromIndex(long offset, out long previous)
    {
        previous = FirstLineOffset;
        if (_lineStarts.Count == 0 || offset > IndexedThrough)
        {
            return false;
        }

        int i = _lineStarts.BinarySearch(offset);
        if (i < 0)
        {
            i = ~i;
        }

        if (i <= 0)
        {
            return false;
        }

        previous = _lineStarts[i - 1];
        return true;
    }
}
