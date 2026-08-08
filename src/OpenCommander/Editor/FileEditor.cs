using System.Globalization;
using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Text;
using OpenCommander.Theming;

namespace OpenCommander.Editor;

/// <summary>
/// Far Manager's F4 editor: a full screen plain text editor with block selection, bounded undo and
/// exact preservation of the file's encoding and line terminators.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the viewer, the editor loads the whole file, so it refuses anything above
/// <see cref="MaxFileSize"/> and asks before opening something that looks binary. Everything else
/// about the file survives the round trip: a Latin-1 file stays Latin-1, a file with a BOM keeps
/// it, and a file with mixed terminators is written back mixed.
/// </para>
/// <para>
/// The component only draws and handles keys; hand it to <see cref="IUiServices.RunModal"/>.
/// </para>
/// </remarks>
public sealed class FileEditor : IScreenComponent
{
    /// <summary>The largest file the editor will open, in bytes.</summary>
    public const long MaxFileSize = 64L * 1024 * 1024;

    private static readonly KeyBarLabels BaseKeyBar = KeyBarLabels.Of(
        "Help", "Save", string.Empty, string.Empty, string.Empty, string.Empty,
        "Search", string.Empty, string.Empty, "Quit", string.Empty, "Screen");

    private readonly Theme _theme;
    private readonly IUiServices _ui;
    private readonly IEditorClipboard _clipboard;
    private readonly EditorCursor _cursor = new();

    private TextBuffer _buffer = new();
    private Rect _area;
    private int _topLine;
    private int _leftColumn;
    private string _lastSearch = string.Empty;
    private string _lastReplace = string.Empty;
    private bool _closed;

    /// <summary>
    /// Opens a file for editing, creating an empty document when it does not exist.
    /// </summary>
    /// <param name="theme">The colour scheme.</param>
    /// <param name="ui">Modal services, used for prompts, confirmations and errors.</param>
    /// <param name="path">The file to edit.</param>
    /// <param name="clipboard">
    /// The clipboard to use, or <see langword="null"/> for the process-local fallback.
    /// </param>
    /// <remarks>
    /// A file that is too large, unreadable, or binary and not confirmed leaves the editor already
    /// closed, so handing it to <see cref="IUiServices.RunModal"/> returns immediately. Use
    /// <see cref="TryOpen"/> when the caller wants to know.
    /// </remarks>
    public FileEditor(Theme theme, IUiServices ui, string path, IEditorClipboard? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(ui);

        _theme = theme;
        _ui = ui;
        _clipboard = clipboard ?? InMemoryClipboard.Shared;
        FilePath = path ?? string.Empty;

        if (!TryLoad(FilePath))
        {
            _closed = true;
        }
    }

    /// <summary>Edits a document already in memory; used by the tests and by "edit new file".</summary>
    /// <param name="theme">The colour scheme.</param>
    /// <param name="ui">Modal services.</param>
    /// <param name="buffer">The document to edit.</param>
    /// <param name="clipboard">The clipboard, or <see langword="null"/> for the fallback.</param>
    public FileEditor(Theme theme, IUiServices ui, TextBuffer buffer, IEditorClipboard? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(buffer);

        _theme = theme;
        _ui = ui;
        _clipboard = clipboard ?? InMemoryClipboard.Shared;
        _buffer = buffer;
        FilePath = buffer.FilePath ?? string.Empty;
    }

    /// <summary>
    /// Opens a file for editing, returning <see langword="null"/> when it was refused. The reason
    /// has already been shown to the user.
    /// </summary>
    /// <param name="theme">The colour scheme.</param>
    /// <param name="ui">Modal services.</param>
    /// <param name="path">The file to edit.</param>
    /// <param name="clipboard">The clipboard, or <see langword="null"/> for the fallback.</param>
    /// <returns>The editor, or <see langword="null"/>.</returns>
    public static FileEditor? TryOpen(Theme theme, IUiServices ui, string path, IEditorClipboard? clipboard = null)
    {
        var editor = new FileEditor(theme, ui, path, clipboard);
        return editor.IsClosed ? null : editor;
    }

    /// <summary>The file being edited; empty for an unnamed document.</summary>
    public string FilePath { get; private set; }

    /// <summary>The document.</summary>
    public TextBuffer Buffer => _buffer;

    /// <summary>The caret and selection.</summary>
    public EditorCursor Cursor => _cursor;

    /// <summary>Whether the document has unsaved changes.</summary>
    public bool IsModified => _buffer.IsModified;

    /// <summary>Whether typing replaces characters instead of inserting them (the Insert key).</summary>
    public bool Overwrite { get; set; }

    /// <summary>Search case insensitively. On by default, matching Far.</summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>Draw a scroll bar down the right edge when the document does not fit.</summary>
    public bool ShowScrollBar { get; set; } = true;

    /// <summary>The screen column the hardware caret should be placed at.</summary>
    public int CursorScreenX { get; private set; }

    /// <summary>The screen row the hardware caret should be placed at.</summary>
    public int CursorScreenY { get; private set; }

    /// <summary>The index of the top visible line.</summary>
    public int TopLine => _topLine;

    /// <inheritdoc/>
    public bool IsClosed => _closed;

    /// <inheritdoc/>
    public void Layout(Rect area) => _area = area;

    /// <inheritdoc/>
    public KeyBarLabels? KeyBarFor(KeyMods mods) => mods switch
    {
        KeyMods.Shift => BaseKeyBar.WithLabel(1, "SaveAs").WithLabel(6, "Next"),
        KeyMods.Ctrl => BaseKeyBar.WithLabel(6, "Replac"),
        KeyMods.Alt => BaseKeyBar.WithLabel(7, "GoTo"),
        _ => BaseKeyBar,
    };

    /// <inheritdoc/>
    public void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (_area.IsEmpty)
        {
            _area = new Rect(0, 0, buffer.Width, buffer.Height);
        }

        buffer.Fill(_area, ' ', _theme.EditorText);

        int rows = TextRows;
        int width = TextWidth;
        ScrollIntoView(rows, width);

        for (int r = 0; r < rows; r++)
        {
            DrawLine(buffer, r, _topLine + r, width);
        }

        DrawScrollBar(buffer, rows);
        DrawCaret(buffer, rows, width);

        if (_area.Height >= 2)
        {
            DrawStatus(buffer, _area.Bottom - 1);
        }
    }

    /// <inheritdoc/>
    public bool HandleInput(InputEvent ev)
    {
        if (_closed)
        {
            return false;
        }

        switch (ev.Kind)
        {
            case InputKind.Key:
                return HandleKey(ev.Key);

            case InputKind.Mouse when ev.Mouse.Kind == MouseKind.Wheel:
                _topLine = Math.Clamp(_topLine + (ev.Mouse.Wheel > 0 ? -3 : 3), 0, Math.Max(0, _buffer.LineCount - 1));
                return true;

            default:
                return true;
        }
    }

    /// <summary>Renders the editor as plain text, for tests and for <c>--screenshot</c>.</summary>
    /// <param name="width">Viewport width in cells.</param>
    /// <param name="height">Viewport height in cells.</param>
    /// <returns>The rendered rows joined by newlines.</returns>
    public string RenderToText(int width, int height)
    {
        var buffer = new ScreenBuffer(width, height);
        Layout(new Rect(0, 0, width, height));
        Draw(buffer);
        return buffer.RenderPlainText();
    }

    // ---- loading and saving ----------------------------------------------------------------------

    private bool TryLoad(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            _buffer = new TextBuffer();
            return true;
        }

        try
        {
            if (!File.Exists(path))
            {
                // Shift+F4 on a name that does not exist yet: start an empty document.
                _buffer = new TextBuffer { FilePath = path };
                return true;
            }

            var info = new FileInfo(path);
            if (info.Length > MaxFileSize)
            {
                _ui.Error(
                    "Edit",
                    $"{Path.GetFileName(path)} is {info.Length / (1024 * 1024)} MB.{Environment.NewLine}"
                    + $"The editor opens files up to {MaxFileSize / (1024 * 1024)} MB; use the viewer (F3) instead.");
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (EncodingDetector.LooksBinary(bytes)
                && !_ui.Confirm(
                    "Edit",
                    [
                        $"{Path.GetFileName(path)} appears to be a binary file.",
                        "Editing it may corrupt its content.",
                        "Open it anyway?",
                    ],
                    warning: true))
            {
                return false;
            }

            _buffer = TextBuffer.FromBytes(bytes, path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or OutOfMemoryException)
        {
            _ui.Error("Edit", $"Cannot open{Environment.NewLine}{path}{Environment.NewLine}{e.Message}");
            return false;
        }
    }

    /// <summary>Saves the document, prompting for a name when it has none.</summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public bool Save()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return SaveAs();
        }

        try
        {
            _buffer.Save(FilePath);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _ui.Error("Save", $"Cannot write{Environment.NewLine}{FilePath}{Environment.NewLine}{e.Message}");
            return false;
        }
    }

    /// <summary>Prompts for a file name and saves the document under it.</summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public bool SaveAs()
    {
        string? answer = _ui.Input("Save as", "File name", FilePath, historyKey: "EditorSaveAs");
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        string previous = FilePath;
        FilePath = answer.Trim();
        if (Save())
        {
            return true;
        }

        FilePath = previous;
        return false;
    }

    // ---- input ------------------------------------------------------------------------------------

    private bool HandleKey(KeyEvent key)
    {
        bool shift = (key.Mods & KeyMods.Shift) != 0;
        int rows = Math.Max(1, TextRows);

        switch (key.Key)
        {
            case ConsoleKey.Escape when !key.HasMods:
            case ConsoleKey.F10 when !key.HasMods:
                return !TryClose();

            case ConsoleKey.F1 when !key.HasMods:
                return true; // The help screen belongs to the shell.

            case ConsoleKey.F2 when key.Mods == KeyMods.None:
                Save();
                return true;

            case ConsoleKey.F2 when key.Mods == KeyMods.Shift:
                SaveAs();
                return true;

            case ConsoleKey.F7 when key.Mods == KeyMods.None:
                Search(fresh: true);
                return true;

            case ConsoleKey.F7 when key.Mods == KeyMods.Shift:
                Search(fresh: false);
                return true;

            case ConsoleKey.F7 when key.Mods == KeyMods.Ctrl:
                Replace();
                return true;

            case ConsoleKey.F8 when key.Mods == KeyMods.Alt:
                GoToLine();
                return true;

            case ConsoleKey.Insert when key.Mods == KeyMods.None:
                Overwrite = !Overwrite;
                return true;

            case ConsoleKey.Insert when key.Mods == KeyMods.Ctrl:
                Copy();
                return true;

            case ConsoleKey.Insert when key.Mods == KeyMods.Shift:
                Paste();
                return true;

            case ConsoleKey.Delete when key.Mods == KeyMods.Shift:
                Cut();
                return true;

            // ---- motion ----
            case ConsoleKey.LeftArrow when NoCtrlAlt(key):
                Move(() => _cursor.MoveLeft(_buffer, shift));
                return true;

            case ConsoleKey.RightArrow when NoCtrlAlt(key):
                Move(() => _cursor.MoveRight(_buffer, shift));
                return true;

            case ConsoleKey.LeftArrow when CtrlOnly(key):
                Move(() => _cursor.MoveWordLeft(_buffer, shift));
                return true;

            case ConsoleKey.RightArrow when CtrlOnly(key):
                Move(() => _cursor.MoveWordRight(_buffer, shift));
                return true;

            case ConsoleKey.UpArrow when NoCtrlAlt(key):
                Move(() => _cursor.MoveVertical(_buffer, -1, shift));
                return true;

            case ConsoleKey.DownArrow when NoCtrlAlt(key):
                Move(() => _cursor.MoveVertical(_buffer, 1, shift));
                return true;

            case ConsoleKey.UpArrow when CtrlOnly(key):
                _topLine = Math.Max(0, _topLine - 1);
                return true;

            case ConsoleKey.DownArrow when CtrlOnly(key):
                _topLine = Math.Clamp(_topLine + 1, 0, Math.Max(0, _buffer.LineCount - 1));
                return true;

            case ConsoleKey.PageUp when NoCtrlAlt(key):
                Move(() => _cursor.MoveVertical(_buffer, -rows, shift));
                return true;

            case ConsoleKey.PageDown when NoCtrlAlt(key):
                Move(() => _cursor.MoveVertical(_buffer, rows, shift));
                return true;

            case ConsoleKey.Home when CtrlOnly(key):
                Move(() => _cursor.MoveDocumentStart(_buffer, shift));
                return true;

            case ConsoleKey.End when CtrlOnly(key):
                Move(() => _cursor.MoveDocumentEnd(_buffer, shift));
                return true;

            case ConsoleKey.Home when NoCtrlAlt(key):
                Move(() => _cursor.MoveHome(_buffer, shift));
                return true;

            case ConsoleKey.End when NoCtrlAlt(key):
                Move(() => _cursor.MoveEnd(_buffer, shift));
                return true;

            // ---- editing ----
            case ConsoleKey.Enter when !key.HasMods:
                InsertNewLine();
                return true;

            case ConsoleKey.Backspace when !key.HasMods:
                Backspace();
                return true;

            case ConsoleKey.Delete when key.Mods == KeyMods.None:
                DeleteForward();
                return true;

            case ConsoleKey.Tab when key.Mods == KeyMods.None:
                Indent();
                return true;

            case ConsoleKey.Tab when key.Mods == KeyMods.Shift:
                Unindent();
                return true;

            default:
                return HandleChordOrChar(key);
        }
    }

    private bool HandleChordOrChar(KeyEvent key)
    {
        if (key.Mods == KeyMods.Ctrl)
        {
            switch (key.Key)
            {
                case ConsoleKey.Y:
                    DeleteCurrentLine();
                    return true;
                case ConsoleKey.Z:
                    UndoStep();
                    return true;
                case ConsoleKey.C:
                    Copy();
                    return true;
                case ConsoleKey.X:
                    Cut();
                    return true;
                case ConsoleKey.V:
                    Paste();
                    return true;
                case ConsoleKey.A:
                    _cursor.SelectAll(_buffer);
                    return true;
                default:
                    return true;
            }
        }

        if (key.Mods == (KeyMods.Ctrl | KeyMods.Shift))
        {
            if (key.Key == ConsoleKey.Z)
            {
                RedoStep();
            }

            return true;
        }

        if (key.IsPlainChar)
        {
            TypeCharacter(key.Ch);
        }

        return true;
    }

    private static bool NoCtrlAlt(KeyEvent key) => (key.Mods & (KeyMods.Ctrl | KeyMods.Alt)) == 0;

    private static bool CtrlOnly(KeyEvent key) => (key.Mods & (KeyMods.Ctrl | KeyMods.Alt)) == KeyMods.Ctrl;

    private void Move(Action motion)
    {
        _buffer.BreakUndoRun();
        motion();
    }

    // ---- editing operations -----------------------------------------------------------------------

    private void TypeCharacter(char ch)
    {
        if (_cursor.HasSelection)
        {
            using (_buffer.BeginGroup())
            {
                DeleteSelection();
                Apply(_buffer.InsertChar(_cursor.Line, _cursor.Column, ch, overwrite: false));
            }

            return;
        }

        Apply(_buffer.InsertChar(_cursor.Line, _cursor.Column, ch, Overwrite));
    }

    private void InsertNewLine()
    {
        using (_buffer.BeginGroup())
        {
            DeleteSelection();
            Apply(_buffer.InsertNewLine(_cursor.Line, _cursor.Column));
        }
    }

    private void Backspace()
    {
        if (DeleteSelection())
        {
            return;
        }

        Apply(_buffer.Backspace(_cursor.Line, _cursor.Column));
    }

    private void DeleteForward()
    {
        if (DeleteSelection())
        {
            return;
        }

        Apply(_buffer.DeleteCharAt(_cursor.Line, _cursor.Column));
    }

    private void DeleteCurrentLine()
    {
        _buffer.BreakUndoRun();
        _buffer.DeleteLine(_cursor.Line);
        _cursor.ClearSelection();
        _cursor.SetPosition(_buffer, _cursor.Line, 0);
    }

    private void Indent()
    {
        if (_cursor.HasSelection && _cursor.SelectionStart.Line != _cursor.SelectionEnd.Line)
        {
            _buffer.BreakUndoRun();
            _buffer.IndentLines(_cursor.SelectionStart.Line, _cursor.SelectionEnd.Line);
            _cursor.Clamp(_buffer);
            return;
        }

        using (_buffer.BeginGroup())
        {
            DeleteSelection();
            Apply(_buffer.InsertTab(_cursor.Line, _cursor.Column));
        }
    }

    private void Unindent()
    {
        _buffer.BreakUndoRun();
        if (_cursor.HasSelection)
        {
            _buffer.UnindentLines(_cursor.SelectionStart.Line, _cursor.SelectionEnd.Line);
        }
        else
        {
            _buffer.UnindentLines(_cursor.Line, _cursor.Line);
        }

        _cursor.Clamp(_buffer);
    }

    private bool DeleteSelection()
    {
        if (!_cursor.HasSelection)
        {
            return false;
        }

        (int startLine, int startColumn) = _cursor.SelectionStart;
        (int endLine, int endColumn) = _cursor.SelectionEnd;
        _buffer.BreakUndoRun();
        var at = _buffer.Delete(startLine, startColumn, endLine, endColumn);
        _cursor.ClearSelection();
        _cursor.SetPosition(_buffer, at.Line, at.Column);
        return true;
    }

    private void Apply((int Line, int Column) position)
    {
        _cursor.ClearSelection();
        _cursor.SetPosition(_buffer, position.Line, position.Column);
    }

    private void UndoStep()
    {
        if (_buffer.Undo(out int line, out int column))
        {
            _cursor.ClearSelection();
            _cursor.SetPosition(_buffer, line, column);
        }
    }

    private void RedoStep()
    {
        if (_buffer.Redo(out int line, out int column))
        {
            _cursor.ClearSelection();
            _cursor.SetPosition(_buffer, line, column);
        }
    }

    // ---- clipboard --------------------------------------------------------------------------------

    private void Copy()
    {
        string text = _cursor.HasSelection
            ? _buffer.GetRange(
                _cursor.SelectionStart.Line,
                _cursor.SelectionStart.Column,
                _cursor.SelectionEnd.Line,
                _cursor.SelectionEnd.Column)
            : _buffer.GetLine(_cursor.Line) + LineEndings.Sequence(_buffer.NewLineStyle);

        _clipboard.SetText(text);
    }

    private void Cut()
    {
        Copy();
        if (_cursor.HasSelection)
        {
            DeleteSelection();
        }
        else
        {
            DeleteCurrentLine();
        }
    }

    private void Paste()
    {
        string? text = _clipboard.GetText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using (_buffer.BeginGroup())
        {
            DeleteSelection();
            Apply(_buffer.Insert(_cursor.Line, _cursor.Column, text));
        }
    }

    // ---- search, replace, navigation ---------------------------------------------------------------

    private void Search(bool fresh)
    {
        if (fresh)
        {
            string? answer = _ui.Input("Search", "Search for", _lastSearch, historyKey: "EditorSearch");
            if (string.IsNullOrEmpty(answer))
            {
                return;
            }

            _lastSearch = answer;
        }
        else if (string.IsNullOrEmpty(_lastSearch))
        {
            Search(fresh: true);
            return;
        }

        FindFrom(_cursor.Line, _cursor.Column + 1, report: true);
    }

    private bool FindFrom(int line, int column, bool report)
    {
        if (_buffer.Find(_lastSearch, line, column, IgnoreCase, backwards: false, out int foundLine, out int foundColumn))
        {
            _buffer.BreakUndoRun();
            _cursor.MoveTo(_buffer, foundLine, foundColumn);
            _cursor.MoveTo(_buffer, foundLine, foundColumn + _lastSearch.Length, extend: true);
            return true;
        }

        if (report)
        {
            _ui.Message("Search", ["\"" + _lastSearch + "\" not found"], MessageButtons.Ok, warning: true);
        }

        return false;
    }

    private void Replace()
    {
        string? needle = _ui.Input("Replace", "Search for", _lastSearch, historyKey: "EditorSearch");
        if (string.IsNullOrEmpty(needle))
        {
            return;
        }

        string? replacement = _ui.Input("Replace", "Replace with", _lastReplace, historyKey: "EditorReplace");
        if (replacement is null)
        {
            return;
        }

        _lastSearch = needle;
        _lastReplace = replacement;

        if (_ui.Confirm("Replace", ["Replace all occurrences in the whole file?"]))
        {
            int count = _buffer.ReplaceAll(needle, replacement, IgnoreCase);
            _cursor.Clamp(_buffer);
            _ui.Message(
                "Replace",
                [count == 1 ? "1 occurrence replaced" : $"{count} occurrences replaced"],
                MessageButtons.Ok);
            return;
        }

        if (!FindFrom(_cursor.Line, _cursor.Column, report: true))
        {
            return;
        }

        using (_buffer.BeginGroup())
        {
            DeleteSelection();
            Apply(_buffer.Insert(_cursor.Line, _cursor.Column, replacement));
        }
    }

    private void GoToLine()
    {
        string? answer = _ui.Input("Go to line", "Line number", string.Empty, historyKey: "EditorGoto");
        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        if (!int.TryParse(answer.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int line))
        {
            return;
        }

        _buffer.BreakUndoRun();
        _cursor.MoveTo(_buffer, line - 1, 0);
    }

    private bool TryClose()
    {
        if (!_buffer.IsModified)
        {
            _closed = true;
            return true;
        }

        var answer = _ui.Message(
            "Editor",
            [
                string.IsNullOrEmpty(FilePath) ? "The document has been modified." : Path.GetFileName(FilePath) + " has been modified.",
                "Save the changes?",
            ],
            MessageButtons.Yes | MessageButtons.No | MessageButtons.Cancel);

        switch (answer)
        {
            case DialogResult.Yes when Save():
            case DialogResult.No:
                _closed = true;
                return true;

            default:
                return false;
        }
    }

    // ---- drawing ----------------------------------------------------------------------------------

    private int TextRows => Math.Max(1, _area.Height >= 2 ? _area.Height - 1 : _area.Height);

    private int TextWidth => Math.Max(1, _area.Width - (ShowScrollBar ? 1 : 0));

    private void ScrollIntoView(int rows, int width)
    {
        _topLine = Math.Clamp(_topLine, 0, Math.Max(0, _buffer.LineCount - 1));

        if (_cursor.Line < _topLine)
        {
            _topLine = _cursor.Line;
        }
        else if (_cursor.Line >= _topLine + rows)
        {
            _topLine = _cursor.Line - rows + 1;
        }

        int display = TextBuffer.ToDisplayColumn(_buffer.GetLine(_cursor.Line), _cursor.Column, _buffer.TabSize);
        if (display < _leftColumn)
        {
            _leftColumn = display;
        }
        else if (display >= _leftColumn + width)
        {
            _leftColumn = display - width + 1;
        }

        _topLine = Math.Max(0, _topLine);
        _leftColumn = Math.Max(0, _leftColumn);
    }

    private void DrawLine(ScreenBuffer buffer, int row, int index, int width)
    {
        int y = _area.Y + row;
        if (index >= _buffer.LineCount)
        {
            buffer.WriteFixed(_area.X, y, width, string.Empty, _theme.EditorText);
            return;
        }

        string raw = _buffer.GetLine(index);
        string expanded = TextBuffer.ExpandTabsForDisplay(raw, _buffer.TabSize);
        string slice = _leftColumn < expanded.Length
            ? expanded[_leftColumn..Math.Min(expanded.Length, _leftColumn + width)]
            : string.Empty;

        buffer.WriteFixed(_area.X, y, width, slice, _theme.EditorText);

        if (!_cursor.SelectionOnLine(index, raw.Length, out int from, out int to))
        {
            return;
        }

        // Selection is stored in character columns; the screen is in display columns.
        int displayFrom = TextBuffer.ToDisplayColumn(raw, from, _buffer.TabSize);
        int displayTo = TextBuffer.ToDisplayColumn(raw, to, _buffer.TabSize);

        int x0 = Math.Max(0, displayFrom - _leftColumn);
        int x1 = Math.Min(width, displayTo - _leftColumn);
        if (x1 > x0)
        {
            buffer.FillStyle(new Rect(_area.X + x0, y, x1 - x0, 1), _theme.EditorSelected);
        }
    }

    private void DrawScrollBar(ScreenBuffer buffer, int rows)
    {
        if (!ShowScrollBar || _area.Width < 2 || _buffer.LineCount <= rows)
        {
            return;
        }

        int x = _area.Right - 1;
        int thumb = _buffer.LineCount <= 1
            ? 0
            : (int)((long)_topLine * (rows - 1) / Math.Max(1, _buffer.LineCount - 1));

        for (int r = 0; r < rows; r++)
        {
            buffer.Set(
                x,
                _area.Y + r,
                r == Math.Clamp(thumb, 0, rows - 1) ? BoxChars.ScrollBarThumb : BoxChars.ScrollBarTrack,
                _theme.EditorScroll);
        }
    }

    private void DrawCaret(ScreenBuffer buffer, int rows, int width)
    {
        int display = TextBuffer.ToDisplayColumn(_buffer.GetLine(_cursor.Line), _cursor.Column, _buffer.TabSize);
        int x = _area.X + display - _leftColumn;
        int y = _area.Y + _cursor.Line - _topLine;

        CursorScreenX = x;
        CursorScreenY = y;

        if (display - _leftColumn < 0 || display - _leftColumn >= width ||
            _cursor.Line - _topLine < 0 || _cursor.Line - _topLine >= rows)
        {
            return;
        }

        // The hardware caret is the host's job; inverting the cell keeps the caret visible in a
        // screenshot and on a terminal that hides it.
        var under = buffer.Get(x, y);
        buffer.Set(x, y, under.Glyph, new CellStyle(under.Style.Bg, under.Style.Fg));
    }

    private void DrawStatus(ScreenBuffer buffer, int row)
    {
        int displayColumn = TextBuffer.ToDisplayColumn(_buffer.GetLine(_cursor.Line), _cursor.Column, _buffer.TabSize);

        string right = string.Format(
            CultureInfo.InvariantCulture,
            "Line {0}/{1}  Col {2}  {3}  {4}  {5}  {6}",
            _cursor.Line + 1,
            _buffer.LineCount,
            displayColumn + 1,
            _buffer.EncodingName,
            LineEndings.Name(_buffer.LineEnding),
            _buffer.IsModified ? "*" : " ",
            Overwrite ? "OVR" : "INS");

        buffer.WriteFixed(_area.X, row, _area.Width, string.Empty, _theme.EditorStatus);

        int rightStart = Math.Max(_area.X, _area.Right - right.Length - 1);
        int nameRoom = Math.Max(0, rightStart - _area.X - 2);
        if (nameRoom > 0)
        {
            string name = string.IsNullOrEmpty(FilePath) ? "(new file)" : Path.GetFileName(FilePath);
            buffer.WriteFixed(_area.X + 1, row, nameRoom, name, _theme.EditorStatus, truncateLeft: true);
        }

        buffer.WriteFixed(rightStart, row, Math.Min(right.Length, _area.Right - rightStart), right, _theme.EditorStatus);
    }
}
