using System.Text;
using OpenCommander.Text;

namespace OpenCommander.Editor;

/// <summary>
/// One line of the editor document: its text, and the terminator that followed it in the file.
/// </summary>
/// <remarks>
/// Storing the terminator per line rather than per document is what lets a file that arrives with
/// mixed line endings be saved back exactly as it was. The final line of a document always carries
/// an empty terminator, so a file ending in a newline is represented as a trailing empty line and
/// <c>Text + Ending</c> concatenated over every line reproduces the original character for
/// character.
/// </remarks>
/// <param name="Text">The line content, with no terminator.</param>
/// <param name="Ending">The terminator: <c>""</c>, <c>"\n"</c>, <c>"\r\n"</c> or <c>"\r"</c>.</param>
public readonly record struct TextLine(string Text, string Ending)
{
    /// <summary>The number of characters in the line, excluding its terminator.</summary>
    public int Length => Text.Length;

    /// <inheritdoc/>
    public override string ToString() => Text;
}

/// <summary>
/// The editor's document: a list of lines with insert, delete, split and join operations, bounded
/// grouped undo, dirty tracking, and exact round-tripping of the original encoding and line
/// terminators.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation goes through a single primitive - replace a run of lines with another run - which
/// is also exactly what an undo record stores. That keeps undo correct by construction: there is no
/// operation whose inverse has to be derived separately.
/// </para>
/// <para>
/// Consecutive single-line edits merge into one undo record while the caret moves contiguously, so
/// undo steps back over a typing run rather than over one character. Call
/// <see cref="BreakUndoRun"/> when the caret moves for any other reason.
/// </para>
/// </remarks>
public sealed class TextBuffer
{
    /// <summary>The tab stop width used when none is configured.</summary>
    public const int DefaultTabSize = 4;

    /// <summary>How many undo records are kept before the oldest are dropped.</summary>
    public const int DefaultUndoLimit = 1000;

    private readonly List<TextLine> _lines = [];
    private readonly List<UndoEntry> _undo = [];
    private readonly List<UndoEntry> _redo = [];

    private int _crlfCount;
    private int _lfCount;
    private int _crCount;

    private UndoEntry? _group;
    private int _groupDepth;
    private bool _mergeArmed;
    private bool _dirty;
    private long _nextId = 1;
    private long _savedId;
    private int _tabSize = DefaultTabSize;
    private int _undoLimit = DefaultUndoLimit;

    /// <summary>Creates an empty document: one blank line, UTF-8 with no BOM.</summary>
    public TextBuffer()
    {
        _lines.Add(new TextLine(string.Empty, string.Empty));
        Encoding = EncodingDetector.Utf8NoBom;
        NewLineStyle = LineEndings.Platform;
    }

    /// <summary>Builds a document from text already in memory.</summary>
    /// <param name="text">The document text; its terminators are preserved exactly.</param>
    /// <param name="encoding">The encoding to save with; defaults to UTF-8 without a BOM.</param>
    /// <param name="hasBom">Whether a byte order mark should be written on save.</param>
    /// <returns>The document, with no undo history and not modified.</returns>
    public static TextBuffer FromText(string? text, Encoding? encoding = null, bool hasBom = false)
    {
        var buffer = new TextBuffer();
        buffer.SetContent(text ?? string.Empty, encoding ?? EncodingDetector.Utf8NoBom, hasBom);
        return buffer;
    }

    /// <summary>Builds a document from raw file bytes, detecting the encoding and skipping any BOM.</summary>
    /// <param name="bytes">The file content.</param>
    /// <param name="path">The path to remember, or <see langword="null"/> for an unnamed document.</param>
    /// <returns>The document.</returns>
    public static TextBuffer FromBytes(ReadOnlySpan<byte> bytes, string? path = null)
    {
        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(bytes);
        var buffer = new TextBuffer();
        buffer.SetContent(EncodingDetector.DecodeSkippingBom(bytes, encoding), encoding, hasBom);
        buffer.FilePath = path;
        return buffer;
    }

    /// <summary>Reads a file into a document.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The document, with <see cref="FilePath"/> set.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static TextBuffer Load(string path) => FromBytes(File.ReadAllBytes(path), path);

    /// <summary>The file this document was loaded from or last saved to.</summary>
    public string? FilePath { get; set; }

    /// <summary>The encoding used on save.</summary>
    public Encoding Encoding { get; set; }

    /// <summary>Whether a byte order mark is written on save.</summary>
    public bool HasBom { get; set; }

    /// <summary>The encoding name for the status line, for example <c>"UTF-8 BOM"</c>.</summary>
    public string EncodingName => EncodingDetector.DisplayName(Encoding, HasBom);

    /// <summary>
    /// The terminator convention currently in the document, which is
    /// <see cref="LineEndingStyle.Mixed"/> when more than one is present.
    /// </summary>
    public LineEndingStyle LineEnding
    {
        get
        {
            int kinds = (_crlfCount > 0 ? 1 : 0) + (_lfCount > 0 ? 1 : 0) + (_crCount > 0 ? 1 : 0);
            return kinds switch
            {
                0 => LineEndingStyle.None,
                1 => _crlfCount > 0 ? LineEndingStyle.Crlf : _lfCount > 0 ? LineEndingStyle.Lf : LineEndingStyle.Cr,
                _ => LineEndingStyle.Mixed,
            };
        }
    }

    /// <summary>The terminator given to lines the user creates. Never <see cref="LineEndingStyle.Mixed"/>.</summary>
    public LineEndingStyle NewLineStyle { get; set; }

    /// <summary>How many columns a tab advances to. Clamped to at least one.</summary>
    public int TabSize
    {
        get => _tabSize;
        set => _tabSize = Math.Max(1, value);
    }

    /// <summary>Insert spaces instead of a tab character when Tab is pressed.</summary>
    public bool ExpandTabs { get; set; }

    /// <summary>How many undo records to keep. Clamped to at least one.</summary>
    public int UndoLimit
    {
        get => _undoLimit;
        set
        {
            _undoLimit = Math.Max(1, value);
            TrimUndo();
        }
    }

    /// <summary>The number of lines; always at least one.</summary>
    public int LineCount => _lines.Count;

    /// <summary>The text of one line, without its terminator. Out-of-range indices read as empty.</summary>
    /// <param name="index">Zero based line index.</param>
    public string this[int index] => GetLine(index);

    /// <summary>The text of one line, without its terminator.</summary>
    /// <param name="index">Zero based line index; out of range reads as empty.</param>
    /// <returns>The line text.</returns>
    public string GetLine(int index) =>
        (uint)index < (uint)_lines.Count ? _lines[index].Text : string.Empty;

    /// <summary>The terminator that follows one line.</summary>
    /// <param name="index">Zero based line index; out of range reads as empty.</param>
    /// <returns>The terminator characters, empty for the last line.</returns>
    public string GetLineEnding(int index) =>
        (uint)index < (uint)_lines.Count ? _lines[index].Ending : string.Empty;

    /// <summary>The length of one line in characters, excluding its terminator.</summary>
    /// <param name="index">Zero based line index.</param>
    /// <returns>The length, or zero when out of range.</returns>
    public int LineLength(int index) =>
        (uint)index < (uint)_lines.Count ? _lines[index].Text.Length : 0;

    /// <summary>A snapshot of every line's text.</summary>
    /// <returns>A fresh list; mutating it does not affect the document.</returns>
    public IReadOnlyList<string> ToLineList()
    {
        var list = new List<string>(_lines.Count);
        foreach (var line in _lines)
        {
            list.Add(line.Text);
        }

        return list;
    }

    /// <summary>Whether the document has changed since it was loaded or last saved.</summary>
    /// <remarks>
    /// Undoing back to the state the file was saved in clears the flag again, because each undo
    /// record carries an identity that survives being undone and redone.
    /// </remarks>
    public bool IsModified => _dirty;

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _undo.Count > 0 || _groupDepth > 0;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The number of undo records currently held.</summary>
    public int UndoDepth => _undo.Count;

    // ---- positions ------------------------------------------------------------------------------

    /// <summary>Clamps a line index into the document.</summary>
    /// <param name="line">The index to clamp.</param>
    /// <returns>A valid line index.</returns>
    public int ClampLine(int line) => Math.Clamp(line, 0, _lines.Count - 1);

    /// <summary>Clamps a column into a line.</summary>
    /// <param name="line">The line the column belongs to.</param>
    /// <param name="column">The column to clamp.</param>
    /// <returns>A column between zero and the line length inclusive.</returns>
    public int ClampColumn(int line, int column) =>
        Math.Clamp(column, 0, LineLength(ClampLine(line)));

    /// <summary>Clamps a caret position into the document.</summary>
    /// <param name="line">The line index.</param>
    /// <param name="column">The column.</param>
    /// <returns>A valid position.</returns>
    public (int Line, int Column) Clamp(int line, int column)
    {
        int l = ClampLine(line);
        return (l, ClampColumn(l, column));
    }

    /// <summary>The position one past the last character of the document.</summary>
    public (int Line, int Column) EndPosition => (_lines.Count - 1, _lines[^1].Text.Length);

    // ---- reading --------------------------------------------------------------------------------

    /// <summary>
    /// Extracts a character range, including the real terminators of the lines it spans.
    /// </summary>
    /// <param name="startLine">Start line.</param>
    /// <param name="startColumn">Start column.</param>
    /// <param name="endLine">End line.</param>
    /// <param name="endColumn">End column.</param>
    /// <returns>The text; empty when the range is empty. The range is normalised if reversed.</returns>
    public string GetRange(int startLine, int startColumn, int endLine, int endColumn)
    {
        Normalize(ref startLine, ref startColumn, ref endLine, ref endColumn);
        if (startLine == endLine && startColumn == endColumn)
        {
            return string.Empty;
        }

        if (startLine == endLine)
        {
            return _lines[startLine].Text[startColumn..endColumn];
        }

        var sb = new StringBuilder();
        sb.Append(_lines[startLine].Text[startColumn..]).Append(_lines[startLine].Ending);
        for (int i = startLine + 1; i < endLine; i++)
        {
            sb.Append(_lines[i].Text).Append(_lines[i].Ending);
        }

        sb.Append(_lines[endLine].Text[..endColumn]);
        return sb.ToString();
    }

    /// <summary>The whole document, terminators included, exactly as it will be written.</summary>
    /// <returns>The document text.</returns>
    public string GetText()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.Append(line.Text).Append(line.Ending);
        }

        return sb.ToString();
    }

    /// <summary>The whole document encoded, byte order mark included when <see cref="HasBom"/> is set.</summary>
    /// <returns>The bytes that <see cref="Save"/> would write.</returns>
    public byte[] GetBytes()
    {
        using var stream = new MemoryStream();
        WriteTo(stream);
        return stream.ToArray();
    }

    // ---- editing --------------------------------------------------------------------------------

    /// <summary>
    /// Inserts text, which may contain line breaks. Breaks in the inserted text are rewritten to
    /// <see cref="NewLineStyle"/>, so a paste never contaminates the document with a second
    /// convention.
    /// </summary>
    /// <param name="line">The line to insert at.</param>
    /// <param name="column">The column to insert at.</param>
    /// <param name="text">The text to insert.</param>
    /// <returns>The position just past the inserted text.</returns>
    public (int Line, int Column) Insert(int line, int column, string? text)
    {
        (line, column) = Clamp(line, column);
        if (string.IsNullOrEmpty(text))
        {
            return (line, column);
        }

        var current = _lines[line];
        string head = current.Text[..column];
        string tail = current.Text[column..];
        string newline = LineEndings.Sequence(NewLineStyle);
        var pieces = SplitIntoLines(text, newline);

        if (pieces.Count == 1)
        {
            var replacement = new[] { new TextLine(head + pieces[0].Text + tail, current.Ending) };
            int end = column + pieces[0].Text.Length;
            ApplySplice(line, 1, replacement, line, column, line, end, mergeable: true);
            return (line, end);
        }

        var lines = new TextLine[pieces.Count];
        lines[0] = new TextLine(head + pieces[0].Text, pieces[0].Ending);
        for (int i = 1; i < pieces.Count - 1; i++)
        {
            lines[i] = pieces[i];
        }

        int last = pieces.Count - 1;
        lines[last] = new TextLine(pieces[last].Text + tail, current.Ending);

        int endLine = line + last;
        int endColumn = pieces[last].Text.Length;
        ApplySplice(line, 1, lines, line, column, endLine, endColumn, mergeable: false);
        return (endLine, endColumn);
    }

    /// <summary>Inserts one character.</summary>
    /// <param name="line">The line to insert at.</param>
    /// <param name="column">The column to insert at.</param>
    /// <param name="ch">The character.</param>
    /// <param name="overwrite">Replace the character already there instead of pushing it right.</param>
    /// <returns>The position just past the character.</returns>
    public (int Line, int Column) InsertChar(int line, int column, char ch, bool overwrite = false)
    {
        (line, column) = Clamp(line, column);
        if (overwrite && column < _lines[line].Text.Length)
        {
            var current = _lines[line];
            string text = string.Concat(current.Text.AsSpan(0, column), ch.ToString(), current.Text.AsSpan(column + 1));
            ApplySplice(line, 1, [new TextLine(text, current.Ending)], line, column, line, column + 1, mergeable: true);
            return (line, column + 1);
        }

        return Insert(line, column, ch.ToString());
    }

    /// <summary>Splits a line in two at the caret.</summary>
    /// <param name="line">The line to split.</param>
    /// <param name="column">Where to split it.</param>
    /// <returns>The start of the new second line.</returns>
    public (int Line, int Column) InsertNewLine(int line, int column)
    {
        (line, column) = Clamp(line, column);
        var current = _lines[line];
        string newline = LineEndings.Sequence(NewLineStyle);

        TextLine[] replacement =
        [
            new TextLine(current.Text[..column], newline),
            new TextLine(current.Text[column..], current.Ending),
        ];

        ApplySplice(line, 1, replacement, line, column, line + 1, 0, mergeable: false);
        return (line + 1, 0);
    }

    /// <summary>Joins a line with the one below it.</summary>
    /// <param name="line">The upper line; nothing happens when it is the last one.</param>
    /// <returns>The position the two lines were joined at.</returns>
    public (int Line, int Column) JoinLines(int line)
    {
        line = ClampLine(line);
        if (line >= _lines.Count - 1)
        {
            return (line, LineLength(line));
        }

        int column = _lines[line].Text.Length;
        return Delete(line, column, line + 1, 0);
    }

    /// <summary>
    /// Deletes a character range. The range is normalised, so the two ends may be given in either
    /// order.
    /// </summary>
    /// <param name="startLine">One end's line.</param>
    /// <param name="startColumn">One end's column.</param>
    /// <param name="endLine">The other end's line.</param>
    /// <param name="endColumn">The other end's column.</param>
    /// <returns>The position the deleted text used to start at.</returns>
    public (int Line, int Column) Delete(int startLine, int startColumn, int endLine, int endColumn)
    {
        Normalize(ref startLine, ref startColumn, ref endLine, ref endColumn);
        if (startLine == endLine && startColumn == endColumn)
        {
            return (startLine, startColumn);
        }

        var first = _lines[startLine];
        var last = _lines[endLine];
        var merged = new TextLine(first.Text[..startColumn] + last.Text[endColumn..], last.Ending);

        ApplySplice(
            startLine,
            endLine - startLine + 1,
            [merged],
            endLine,
            endColumn,
            startLine,
            startColumn,
            mergeable: startLine == endLine);

        return (startLine, startColumn);
    }

    /// <summary>Deletes the character before the caret, joining lines when the caret is at column zero.</summary>
    /// <param name="line">The caret line.</param>
    /// <param name="column">The caret column.</param>
    /// <returns>The new caret position.</returns>
    public (int Line, int Column) Backspace(int line, int column)
    {
        (line, column) = Clamp(line, column);
        if (column > 0)
        {
            return Delete(line, column - 1, line, column);
        }

        return line == 0 ? (line, column) : Delete(line - 1, LineLength(line - 1), line, 0);
    }

    /// <summary>Deletes the character at the caret, joining lines when the caret is at end of line.</summary>
    /// <param name="line">The caret line.</param>
    /// <param name="column">The caret column.</param>
    /// <returns>The caret position, which does not move.</returns>
    public (int Line, int Column) DeleteCharAt(int line, int column)
    {
        (line, column) = Clamp(line, column);
        if (column < LineLength(line))
        {
            return Delete(line, column, line, column + 1);
        }

        return line >= _lines.Count - 1 ? (line, column) : Delete(line, column, line + 1, 0);
    }

    /// <summary>Deletes a whole line, terminator included (Ctrl+Y).</summary>
    /// <param name="index">The line to delete.</param>
    /// <remarks>Deleting the only line leaves the document with one empty line rather than none.</remarks>
    public void DeleteLine(int index)
    {
        index = ClampLine(index);

        if (_lines.Count == 1)
        {
            if (_lines[0].Text.Length == 0)
            {
                return;
            }

            ApplySplice(0, 1, [new TextLine(string.Empty, string.Empty)], 0, _lines[0].Text.Length, 0, 0, mergeable: false);
            return;
        }

        if (index == _lines.Count - 1)
        {
            // Removing the last line makes its predecessor the last, so that line loses its
            // terminator; splice both at once to keep the invariant.
            ApplySplice(
                index - 1,
                2,
                [new TextLine(_lines[index - 1].Text, string.Empty)],
                index,
                0,
                index - 1,
                _lines[index - 1].Text.Length,
                mergeable: false);
            return;
        }

        ApplySplice(index, 1, [], index, 0, index, 0, mergeable: false);
    }

    /// <summary>Replaces the text of one line, keeping its terminator.</summary>
    /// <param name="index">The line to replace.</param>
    /// <param name="text">The new text.</param>
    public void ReplaceLine(int index, string text)
    {
        index = ClampLine(index);
        var current = _lines[index];
        string value = text ?? string.Empty;
        if (string.Equals(current.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        ApplySplice(
            index,
            1,
            [new TextLine(value, current.Ending)],
            index,
            current.Text.Length,
            index,
            value.Length,
            mergeable: false);
    }

    /// <summary>Inserts a whole new line.</summary>
    /// <param name="index">Where to insert it; <see cref="LineCount"/> appends at the end.</param>
    /// <param name="text">The line text.</param>
    public void InsertLine(int index, string text)
    {
        index = Math.Clamp(index, 0, _lines.Count);
        string value = text ?? string.Empty;
        string newline = LineEndings.Sequence(NewLineStyle);

        if (index == _lines.Count)
        {
            int lastIndex = _lines.Count - 1;
            var last = _lines[lastIndex];
            ApplySplice(
                lastIndex,
                1,
                [new TextLine(last.Text, newline), new TextLine(value, string.Empty)],
                lastIndex,
                last.Text.Length,
                index,
                value.Length,
                mergeable: false);
            return;
        }

        ApplySplice(index, 0, [new TextLine(value, newline)], index, 0, index, value.Length, mergeable: false);
    }

    /// <summary>Inserts a tab, as a tab character or as spaces depending on <see cref="ExpandTabs"/>.</summary>
    /// <param name="line">The caret line.</param>
    /// <param name="column">The caret column.</param>
    /// <returns>The new caret position.</returns>
    public (int Line, int Column) InsertTab(int line, int column)
    {
        (line, column) = Clamp(line, column);
        if (!ExpandTabs)
        {
            return Insert(line, column, "\t");
        }

        int display = ToDisplayColumn(_lines[line].Text, column, TabSize);
        int spaces = TabSize - (display % TabSize);
        return Insert(line, column, new string(' ', spaces));
    }

    /// <summary>Adds one indent level to a run of lines, as a single undo step.</summary>
    /// <param name="firstLine">First line of the run.</param>
    /// <param name="lastLine">Last line of the run, inclusive.</param>
    /// <remarks>Empty lines are left alone, so indenting a block does not create trailing whitespace.</remarks>
    public void IndentLines(int firstLine, int lastLine)
    {
        int first = ClampLine(Math.Min(firstLine, lastLine));
        int last = ClampLine(Math.Max(firstLine, lastLine));
        string indent = ExpandTabs ? new string(' ', TabSize) : "\t";

        using (BeginGroup())
        {
            for (int i = first; i <= last; i++)
            {
                if (_lines[i].Text.Length > 0)
                {
                    ReplaceLine(i, indent + _lines[i].Text);
                }
            }
        }
    }

    /// <summary>Removes one indent level from a run of lines, as a single undo step.</summary>
    /// <param name="firstLine">First line of the run.</param>
    /// <param name="lastLine">Last line of the run, inclusive.</param>
    /// <remarks>
    /// A leading tab is removed whole; otherwise up to <see cref="TabSize"/> leading spaces go.
    /// Lines with no leading whitespace are untouched.
    /// </remarks>
    public void UnindentLines(int firstLine, int lastLine)
    {
        int first = ClampLine(Math.Min(firstLine, lastLine));
        int last = ClampLine(Math.Max(firstLine, lastLine));

        using (BeginGroup())
        {
            for (int i = first; i <= last; i++)
            {
                string text = _lines[i].Text;
                if (text.Length == 0)
                {
                    continue;
                }

                if (text[0] == '\t')
                {
                    ReplaceLine(i, text[1..]);
                    continue;
                }

                int strip = 0;
                while (strip < TabSize && strip < text.Length && text[strip] == ' ')
                {
                    strip++;
                }

                if (strip > 0)
                {
                    ReplaceLine(i, text[strip..]);
                }
            }
        }
    }

    // ---- undo -----------------------------------------------------------------------------------

    /// <summary>
    /// Starts an undo group: every edit until the returned token is disposed becomes one undo step.
    /// Groups nest.
    /// </summary>
    /// <returns>A token to dispose at the end of the group.</returns>
    public IDisposable BeginGroup()
    {
        if (_groupDepth++ == 0)
        {
            _group = new UndoEntry(_nextId++);
        }

        return new GroupScope(this);
    }

    /// <summary>Ends the innermost undo group opened by <see cref="BeginGroup"/>.</summary>
    public void EndGroup()
    {
        if (_groupDepth == 0)
        {
            return;
        }

        if (--_groupDepth > 0)
        {
            return;
        }

        var group = _group;
        _group = null;
        if (group is null || group.Splices.Count == 0)
        {
            return;
        }

        _undo.Add(group);
        _redo.Clear();
        _mergeArmed = false;
        TrimUndo();
    }

    /// <summary>
    /// Stops the next edit merging into the previous undo record. The editor calls this whenever the
    /// caret moves for a reason other than typing, so undo steps line up with what the user did.
    /// </summary>
    public void BreakUndoRun() => _mergeArmed = false;

    /// <summary>Undoes one step.</summary>
    /// <param name="line">Receives the caret line to restore.</param>
    /// <param name="column">Receives the caret column to restore.</param>
    /// <returns><see langword="false"/> when there was nothing to undo.</returns>
    public bool Undo(out int line, out int column)
    {
        while (_groupDepth > 0)
        {
            EndGroup();
        }

        line = 0;
        column = 0;
        if (_undo.Count == 0)
        {
            return false;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        for (int i = entry.Splices.Count - 1; i >= 0; i--)
        {
            ApplyBackward(entry.Splices[i]);
        }

        _redo.Add(entry);
        _mergeArmed = false;
        _dirty = CurrentId != _savedId;
        (line, column) = Clamp(entry.CaretBeforeLine, entry.CaretBeforeColumn);
        return true;
    }

    /// <summary>Redoes one step.</summary>
    /// <param name="line">Receives the caret line to restore.</param>
    /// <param name="column">Receives the caret column to restore.</param>
    /// <returns><see langword="false"/> when there was nothing to redo.</returns>
    public bool Redo(out int line, out int column)
    {
        line = 0;
        column = 0;
        if (_redo.Count == 0)
        {
            return false;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        foreach (var splice in entry.Splices)
        {
            ApplyForward(splice);
        }

        _undo.Add(entry);
        _mergeArmed = false;
        _dirty = CurrentId != _savedId;
        (line, column) = Clamp(entry.CaretAfterLine, entry.CaretAfterColumn);
        return true;
    }

    /// <summary>Throws the undo and redo history away, leaving the content alone.</summary>
    public void ClearUndo()
    {
        _undo.Clear();
        _redo.Clear();
        _group = null;
        _groupDepth = 0;
        _mergeArmed = false;
        _savedId = 0;
    }

    /// <summary>
    /// Records the document as clean at its current state. The undo run is broken too, so the next
    /// keystroke starts a fresh record rather than folding into the one that was just saved.
    /// </summary>
    public void MarkSaved()
    {
        _savedId = CurrentId;
        _dirty = false;
        _mergeArmed = false;
    }

    // ---- saving ---------------------------------------------------------------------------------

    /// <summary>Writes the document to a file in its original encoding and terminators.</summary>
    /// <param name="path">Where to write; defaults to <see cref="FilePath"/>.</param>
    /// <exception cref="InvalidOperationException">No path was given and the document is unnamed.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(string? path = null)
    {
        string target = path ?? FilePath
            ?? throw new InvalidOperationException("The document has no file name; supply one.");

        using (var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            WriteTo(stream);
        }

        FilePath = target;
        MarkSaved();
    }

    /// <summary>Writes the document to a stream, byte order mark first when <see cref="HasBom"/> is set.</summary>
    /// <param name="stream">The stream to write to; it is left open.</param>
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (HasBom)
        {
            byte[] preamble = Encoding.GetPreamble();
            if (preamble.Length == 0)
            {
                preamble = WithBom(Encoding).GetPreamble();
            }

            stream.Write(preamble, 0, preamble.Length);
        }

        // The preamble is written by hand above, so the writer must use an encoding that has none;
        // otherwise a BOM file gets two marks and a non-BOM file gets one it should not have.
        using var writer = new StreamWriter(stream, WithoutBom(Encoding), 64 * 1024, leaveOpen: true);
        foreach (var line in _lines)
        {
            writer.Write(line.Text);
            if (line.Ending.Length > 0)
            {
                writer.Write(line.Ending);
            }
        }
    }

    // ---- searching ------------------------------------------------------------------------------

    /// <summary>Finds text in the document.</summary>
    /// <param name="needle">The text to find; empty never matches.</param>
    /// <param name="fromLine">The line to start from.</param>
    /// <param name="fromColumn">The column to start from.</param>
    /// <param name="ignoreCase">Compare case insensitively.</param>
    /// <param name="backwards">Search towards the start of the document.</param>
    /// <param name="line">Receives the line of the match.</param>
    /// <param name="column">Receives the column of the match.</param>
    /// <returns><see langword="true"/> when a match was found.</returns>
    public bool Find(
        string needle,
        int fromLine,
        int fromColumn,
        bool ignoreCase,
        bool backwards,
        out int line,
        out int column)
    {
        line = ClampLine(fromLine);
        column = ClampColumn(line, fromColumn);
        if (string.IsNullOrEmpty(needle))
        {
            return false;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!backwards)
        {
            for (int i = line; i < _lines.Count; i++)
            {
                int from = i == line ? column : 0;
                string text = _lines[i].Text;
                if (from > text.Length)
                {
                    continue;
                }

                int at = text.IndexOf(needle, from, comparison);
                if (at >= 0)
                {
                    line = i;
                    column = at;
                    return true;
                }
            }

            return false;
        }

        for (int i = line; i >= 0; i--)
        {
            string text = _lines[i].Text;
            int limit = i == line ? Math.Min(column, text.Length) : text.Length;
            if (limit <= 0)
            {
                continue;
            }

            int at = text[..limit].LastIndexOf(needle, comparison);
            if (at >= 0)
            {
                line = i;
                column = at;
                return true;
            }
        }

        return false;
    }

    /// <summary>Replaces every occurrence of <paramref name="needle"/>, as a single undo step.</summary>
    /// <param name="needle">The text to replace; empty does nothing.</param>
    /// <param name="replacement">The replacement text; line breaks in it are not supported.</param>
    /// <param name="ignoreCase">Compare case insensitively.</param>
    /// <returns>How many occurrences were replaced.</returns>
    public int ReplaceAll(string needle, string? replacement, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return 0;
        }

        string value = replacement ?? string.Empty;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int total = 0;

        using (BeginGroup())
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                string text = _lines[i].Text;
                int count = CountOccurrences(text, needle, comparison);
                if (count == 0)
                {
                    continue;
                }

                ReplaceLine(i, text.Replace(needle, value, comparison));
                total += count;
            }
        }

        return total;
    }

    // ---- static text helpers ---------------------------------------------------------------------

    /// <summary>
    /// Expands tabs to the next tab stop and shows other control characters as dots, so that one
    /// source character always occupies exactly one screen cell after expansion.
    /// </summary>
    /// <param name="line">The raw line.</param>
    /// <param name="tabSize">Columns per tab stop.</param>
    /// <returns>The display form of the line.</returns>
    public static string ExpandTabsForDisplay(string? line, int tabSize)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        int stop = Math.Max(1, tabSize);
        var sb = new StringBuilder(line.Length + stop);
        foreach (char c in line)
        {
            if (c == '\t')
            {
                sb.Append(' ', stop - (sb.Length % stop));
            }
            else
            {
                sb.Append(char.IsControl(c) ? '.' : c);
            }
        }

        return sb.ToString();
    }

    /// <summary>The screen column a character index lands on once tabs are expanded.</summary>
    /// <param name="line">The raw line.</param>
    /// <param name="column">The character index.</param>
    /// <param name="tabSize">Columns per tab stop.</param>
    /// <returns>The display column.</returns>
    public static int ToDisplayColumn(string? line, int column, int tabSize)
    {
        int stop = Math.Max(1, tabSize);
        int display = 0;
        int limit = Math.Min(column, line?.Length ?? 0);

        for (int i = 0; i < limit; i++)
        {
            display += line![i] == '\t' ? stop - (display % stop) : 1;
        }

        // A caret past the end of the line sits in virtual space one column per character.
        return display + Math.Max(0, column - limit);
    }

    /// <summary>The character index that a screen column corresponds to once tabs are expanded.</summary>
    /// <param name="line">The raw line.</param>
    /// <param name="displayColumn">The display column.</param>
    /// <param name="tabSize">Columns per tab stop.</param>
    /// <returns>The character index; clamped to the line length.</returns>
    public static int FromDisplayColumn(string? line, int displayColumn, int tabSize)
    {
        if (string.IsNullOrEmpty(line) || displayColumn <= 0)
        {
            return Math.Max(0, displayColumn);
        }

        int stop = Math.Max(1, tabSize);
        int display = 0;
        for (int i = 0; i < line.Length; i++)
        {
            int width = line[i] == '\t' ? stop - (display % stop) : 1;
            if (display + width > displayColumn)
            {
                return i;
            }

            display += width;
            if (display == displayColumn)
            {
                return i + 1;
            }
        }

        return line.Length;
    }

    /// <summary>The start of the word to the left of a column (Ctrl+Left).</summary>
    /// <param name="line">The line text.</param>
    /// <param name="column">The caret column.</param>
    /// <returns>The new column; zero when there is nothing to the left.</returns>
    public static int WordLeft(string? line, int column)
    {
        if (string.IsNullOrEmpty(line))
        {
            return 0;
        }

        int i = Math.Clamp(column, 0, line.Length);
        while (i > 0 && char.IsWhiteSpace(line[i - 1]))
        {
            i--;
        }

        if (i == 0)
        {
            return 0;
        }

        bool word = IsWordChar(line[i - 1]);
        while (i > 0 && !char.IsWhiteSpace(line[i - 1]) && IsWordChar(line[i - 1]) == word)
        {
            i--;
        }

        return i;
    }

    /// <summary>The start of the word to the right of a column (Ctrl+Right).</summary>
    /// <param name="line">The line text.</param>
    /// <param name="column">The caret column.</param>
    /// <returns>The new column; the line length when there is nothing further right.</returns>
    public static int WordRight(string? line, int column)
    {
        if (string.IsNullOrEmpty(line))
        {
            return 0;
        }

        int i = Math.Clamp(column, 0, line.Length);
        if (i >= line.Length)
        {
            return line.Length;
        }

        bool word = IsWordChar(line[i]);
        if (!char.IsWhiteSpace(line[i]))
        {
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && IsWordChar(line[i]) == word)
            {
                i++;
            }
        }

        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>The display width of a line's leading whitespace.</summary>
    /// <param name="line">The line text.</param>
    /// <param name="tabSize">Columns per tab stop.</param>
    /// <returns>The indent width in columns.</returns>
    public static int IndentWidth(string? line, int tabSize)
    {
        if (string.IsNullOrEmpty(line))
        {
            return 0;
        }

        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        return ToDisplayColumn(line, i, tabSize);
    }

    /// <summary>Whether a character belongs to a word for the purposes of Ctrl+Left and Ctrl+Right.</summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for letters, digits and underscore.</returns>
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ---- internals ------------------------------------------------------------------------------

    private long CurrentId => _undo.Count > 0 ? _undo[^1].Id : 0;

    private void SetContent(string text, Encoding encoding, bool hasBom)
    {
        _lines.Clear();
        _crlfCount = 0;
        _lfCount = 0;
        _crCount = 0;

        foreach (var line in SplitIntoLines(text, forceEnding: null))
        {
            _lines.Add(line);
            CountEnding(line.Ending, 1);
        }

        if (_lines.Count == 0)
        {
            _lines.Add(new TextLine(string.Empty, string.Empty));
        }

        Encoding = encoding;
        HasBom = hasBom;

        var detected = LineEnding;
        NewLineStyle = detected switch
        {
            LineEndingStyle.None => LineEndings.Platform,
            LineEndingStyle.Mixed => LineEndings.Dominant(text),
            _ => detected,
        };

        _undo.Clear();
        _redo.Clear();
        _group = null;
        _groupDepth = 0;
        _mergeArmed = false;
        _savedId = 0;
        _dirty = false;
    }

    /// <summary>
    /// Splits text into lines, keeping each terminator with the line it followed. The result always
    /// has at least one element and its last element always has an empty terminator.
    /// </summary>
    private static List<TextLine> SplitIntoLines(string text, string? forceEnding)
    {
        var lines = new List<TextLine>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                bool crlf = i + 1 < text.Length && text[i + 1] == '\n';
                lines.Add(new TextLine(text[start..i], forceEnding ?? (crlf ? LineEndings.Crlf : LineEndings.Cr)));
                if (crlf)
                {
                    i++;
                }

                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(new TextLine(text[start..i], forceEnding ?? LineEndings.Lf));
                start = i + 1;
            }
        }

        lines.Add(new TextLine(text[start..], string.Empty));
        return lines;
    }

    private static void Normalize(ref int startLine, ref int startColumn, ref int endLine, ref int endColumn)
    {
        if (endLine < startLine || (endLine == startLine && endColumn < startColumn))
        {
            (startLine, endLine) = (endLine, startLine);
            (startColumn, endColumn) = (endColumn, startColumn);
        }
    }

    private void ApplySplice(
        int start,
        int removeCount,
        TextLine[] replacement,
        int caretBeforeLine,
        int caretBeforeColumn,
        int caretAfterLine,
        int caretAfterColumn,
        bool mergeable)
    {
        var before = new TextLine[removeCount];
        _lines.CopyTo(start, before, 0, removeCount);

        var splice = new Splice(start, before, replacement);
        ApplyForward(splice);
        _dirty = true;

        if (_groupDepth > 0 && _group is not null)
        {
            if (_group.Splices.Count == 0)
            {
                _group.CaretBeforeLine = caretBeforeLine;
                _group.CaretBeforeColumn = caretBeforeColumn;
            }

            _group.Splices.Add(splice);
            _group.CaretAfterLine = caretAfterLine;
            _group.CaretAfterColumn = caretAfterColumn;
            return;
        }

        if (mergeable && _mergeArmed && TryMergeIntoLast(splice, caretBeforeLine, caretBeforeColumn, caretAfterLine, caretAfterColumn))
        {
            _redo.Clear();
            return;
        }

        var entry = new UndoEntry(_nextId++)
        {
            CaretBeforeLine = caretBeforeLine,
            CaretBeforeColumn = caretBeforeColumn,
            CaretAfterLine = caretAfterLine,
            CaretAfterColumn = caretAfterColumn,
            Mergeable = mergeable,
        };

        entry.Splices.Add(splice);
        _undo.Add(entry);
        _redo.Clear();
        _mergeArmed = mergeable;
        TrimUndo();
    }

    /// <summary>
    /// Folds a single-line edit into the previous record when it continues from exactly where that
    /// one left off - a typing run, or a run of backspaces.
    /// </summary>
    private bool TryMergeIntoLast(Splice splice, int caretBeforeLine, int caretBeforeColumn, int caretAfterLine, int caretAfterColumn)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var last = _undo[^1];
        if (!last.Mergeable || last.Splices.Count != 1)
        {
            return false;
        }

        var previous = last.Splices[0];
        if (previous.Start != splice.Start ||
            previous.Before.Length != 1 || previous.After.Length != 1 ||
            splice.Before.Length != 1 || splice.After.Length != 1)
        {
            return false;
        }

        if (last.CaretAfterLine != caretBeforeLine || last.CaretAfterColumn != caretBeforeColumn)
        {
            return false;
        }

        last.Splices[0] = new Splice(previous.Start, previous.Before, splice.After);
        last.CaretAfterLine = caretAfterLine;
        last.CaretAfterColumn = caretAfterColumn;
        return true;
    }

    private void ApplyForward(Splice splice)
    {
        for (int i = 0; i < splice.Before.Length; i++)
        {
            CountEnding(splice.Before[i].Ending, -1);
        }

        _lines.RemoveRange(splice.Start, splice.Before.Length);
        _lines.InsertRange(splice.Start, splice.After);

        for (int i = 0; i < splice.After.Length; i++)
        {
            CountEnding(splice.After[i].Ending, 1);
        }
    }

    private void ApplyBackward(Splice splice)
    {
        for (int i = 0; i < splice.After.Length; i++)
        {
            CountEnding(splice.After[i].Ending, -1);
        }

        _lines.RemoveRange(splice.Start, splice.After.Length);
        _lines.InsertRange(splice.Start, splice.Before);

        for (int i = 0; i < splice.Before.Length; i++)
        {
            CountEnding(splice.Before[i].Ending, 1);
        }
    }

    private void CountEnding(string ending, int delta)
    {
        switch (ending)
        {
            case LineEndings.Crlf:
                _crlfCount += delta;
                break;
            case LineEndings.Lf:
                _lfCount += delta;
                break;
            case LineEndings.Cr:
                _crCount += delta;
                break;
        }
    }

    private void TrimUndo()
    {
        int excess = _undo.Count - _undoLimit;
        if (excess > 0)
        {
            _undo.RemoveRange(0, excess);
        }
    }

    private static int CountOccurrences(string haystack, string needle, StringComparison comparison)
    {
        int count = 0;
        int i = 0;
        while (i <= haystack.Length - needle.Length)
        {
            int at = haystack.IndexOf(needle, i, comparison);
            if (at < 0)
            {
                break;
            }

            count++;
            i = at + needle.Length;
        }

        return count;
    }

    private static Encoding WithoutBom(Encoding encoding) => encoding.CodePage switch
    {
        65001 => EncodingDetector.Utf8NoBom,
        1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
        1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
        12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: false),
        12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: false),
        _ => encoding,
    };

    private static Encoding WithBom(Encoding encoding) => encoding.CodePage switch
    {
        65001 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
        12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
        _ => encoding,
    };

    private readonly record struct Splice(int Start, TextLine[] Before, TextLine[] After);

    private sealed class UndoEntry(long id)
    {
        public long Id { get; } = id;

        public List<Splice> Splices { get; } = [];

        public int CaretBeforeLine { get; set; }

        public int CaretBeforeColumn { get; set; }

        public int CaretAfterLine { get; set; }

        public int CaretAfterColumn { get; set; }

        public bool Mergeable { get; set; }
    }

    private sealed class GroupScope(TextBuffer owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            owner.EndGroup();
        }
    }
}
