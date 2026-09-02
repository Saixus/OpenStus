using Dvopan.Input;
using Dvopan.Rendering;

namespace Dvopan.Ui.Controls;

/// <summary>
/// A single line text editor with a horizontally scrolling viewport.
/// </summary>
/// <remarks>
/// <para>
/// Supports Home/End, Left/Right, Ctrl+Left/Ctrl+Right word motion, Backspace, Delete, Insert to
/// toggle overwrite mode, Ctrl+Y to clear the line, selection with Shift plus any motion key, and
/// cut/copy/paste through an injectable <see cref="IClipboard"/> on both the Ctrl+C/X/V and the
/// Ctrl+Ins/Shift+Del/Shift+Ins bindings. When the focus enters the control its whole text is
/// selected - the classic behaviour that lets the first typed character replace a suggested value
/// outright.
/// </para>
/// <para>
/// When a <see cref="History"/> list is supplied, Up and Down walk it (the text being edited is
/// stashed and restored when walking back past the newest entry) and Ctrl+Down calls
/// <see cref="HistoryChooser"/> so the host can put a real popup on screen without this control
/// having to know about the modal loop.
/// </para>
/// </remarks>
public sealed class EditControl : DialogControl
{
    private string _text = string.Empty;
    private int _caret;
    private int _anchor = -1; // selection anchor; -1 means no selection
    private int _scroll;
    private int _historyIndex = -1;
    private string? _historyStash;

    /// <summary>Creates an edit control.</summary>
    /// <param name="text">The initial text; the caret starts at its end.</param>
    public EditControl(string text = "")
    {
        _text = text ?? string.Empty;
        _caret = _text.Length;
        Bounds = new Rect(0, 0, Math.Max(1, _text.Length), 1);
    }

    /// <summary>
    /// The edited text. Assigning it moves the caret to the end and drops any selection.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caret = _text.Length;
            _anchor = -1;
            _scroll = 0;
            TextChanged?.Invoke(_text);
        }
    }

    /// <summary>The caret position, in characters from the start of the text.</summary>
    public int Caret
    {
        get => _caret;
        set
        {
            _caret = Math.Clamp(value, 0, _text.Length);
            _anchor = -1;
        }
    }

    /// <summary>Maximum number of characters accepted; zero means unlimited.</summary>
    public int MaxLength { get; set; }

    /// <summary>When non-zero, every character is drawn as this glyph and copying is disabled.</summary>
    public char PasswordChar { get; set; }

    /// <summary>When set, the text cannot be modified but can still be scrolled and copied.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>When set, typing inserts; when clear, it overwrites. Toggled with Insert.</summary>
    public bool InsertMode { get; set; } = true;

    /// <summary>The clipboard cut/copy/paste talk to.</summary>
    public IClipboard Clipboard { get; set; } = Ui.Clipboard.Default;

    /// <summary>The history list Up and Down walk, oldest entry first. May be <see langword="null"/>.</summary>
    public IReadOnlyList<string>? History { get; set; }

    /// <summary>
    /// Called by Ctrl+Down with the history list; returning a non-<see langword="null"/> string
    /// replaces the text. Lets the host show a real popup without this control owning a modal loop.
    /// </summary>
    public Func<IReadOnlyList<string>, string?>? HistoryChooser { get; set; }

    /// <summary>Raised whenever the text changes.</summary>
    public Action<string>? TextChanged { get; set; }

    /// <summary>The first selected character, or the caret when nothing is selected.</summary>
    public int SelectionStart => _anchor < 0 ? _caret : Math.Min(_anchor, _caret);

    /// <summary>The number of selected characters; zero when nothing is selected.</summary>
    public int SelectionLength => _anchor < 0 ? 0 : Math.Abs(_caret - _anchor);

    /// <summary>The selected text, or an empty string.</summary>
    public string SelectedText =>
        SelectionLength == 0 ? string.Empty : _text.Substring(SelectionStart, SelectionLength);

    /// <summary>The leftmost visible character index.</summary>
    public int ScrollOffset => _scroll;

    /// <inheritdoc/>
    public override bool WantsCursor => HasFocus && Enabled;

    /// <inheritdoc/>
    public override (int X, int Y) CursorOffset => (Math.Max(0, _caret - _scroll), 0);

    /// <summary>Selects the whole line.</summary>
    public void SelectAll()
    {
        _anchor = 0;
        _caret = _text.Length;
    }

    /// <summary>
    /// Selects the whole line when the focus arrives: the first typed character then
    /// replaces the old text wholesale, and an unshifted motion key just drops the selection.
    /// </summary>
    protected internal override void OnFocusEntered()
    {
        if (_text.Length > 0)
        {
            SelectAll();
        }
    }

    /// <summary>Drops the selection, leaving the caret where it is.</summary>
    public void ClearSelection() => _anchor = -1;

    /// <summary>Deletes the selected text, if any.</summary>
    /// <returns><see langword="true"/> when something was deleted.</returns>
    public bool DeleteSelection()
    {
        if (SelectionLength == 0 || ReadOnly)
        {
            return false;
        }

        int start = SelectionStart;
        int len = SelectionLength;
        _text = _text.Remove(start, len);
        _caret = start;
        _anchor = -1;
        TextChanged?.Invoke(_text);
        return true;
    }

    /// <summary>Inserts a string at the caret, replacing the selection and honouring overwrite mode.</summary>
    /// <param name="s">The text to insert; anything from the first newline on is dropped.</param>
    public void Insert(string? s)
    {
        if (ReadOnly || string.IsNullOrEmpty(s))
        {
            return;
        }

        int cut = s.IndexOfAny(['\r', '\n']);
        string insert = cut >= 0 ? s[..cut] : s;
        if (insert.Length == 0)
        {
            return;
        }

        DeleteSelection();

        if (!InsertMode && _caret < _text.Length)
        {
            int overwrite = Math.Min(insert.Length, _text.Length - _caret);
            _text = _text.Remove(_caret, overwrite);
        }

        if (MaxLength > 0)
        {
            int room = MaxLength - _text.Length;
            if (room <= 0)
            {
                return;
            }

            if (insert.Length > room)
            {
                insert = insert[..room];
            }
        }

        _text = _text.Insert(_caret, insert);
        _caret += insert.Length;
        _anchor = -1;
        TextChanged?.Invoke(_text);
    }

    /// <inheritdoc/>
    public override void Draw(ScreenBuffer buffer, Rect client, DialogPalette palette)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(palette);

        var r = ScreenBounds(client);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        EnsureCaretVisible(r.Width);

        var normal = !Enabled || ReadOnly ? palette.EditDisabled : palette.Edit;
        var selected = palette.EditSelected;

        int selStart = SelectionStart;
        int selEnd = selStart + SelectionLength;

        for (int i = 0; i < r.Width; i++)
        {
            int index = _scroll + i;
            char ch = ' ';
            if (index < _text.Length)
            {
                ch = PasswordChar != '\0' ? PasswordChar : _text[index];
            }

            bool inSelection = SelectionLength > 0 && index >= selStart && index < selEnd;
            buffer.Set(r.X + i, r.Y, ch, inSelection ? selected : normal);
        }
    }

    /// <inheritdoc/>
    public override bool HandleKey(KeyEvent key)
    {
        if (!Enabled)
        {
            return false;
        }

        bool shift = (key.Mods & KeyMods.Shift) != 0;
        bool ctrl = (key.Mods & KeyMods.Ctrl) != 0;
        bool alt = (key.Mods & KeyMods.Alt) != 0;

        if (alt)
        {
            return false; // Alt belongs to the dialog's hotkey lookup
        }

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                MoveTo(ctrl ? PrevWord(_text, _caret) : _caret - 1, shift);
                return true;

            case ConsoleKey.RightArrow:
                MoveTo(ctrl ? NextWord(_text, _caret) : _caret + 1, shift);
                return true;

            case ConsoleKey.Home:
                MoveTo(0, shift);
                return true;

            case ConsoleKey.End:
                MoveTo(_text.Length, shift);
                return true;

            case ConsoleKey.Insert when ctrl:
                Copy();
                return true;

            case ConsoleKey.Insert when shift:
                Paste();
                return true;

            case ConsoleKey.Insert:
                InsertMode = !InsertMode;
                return true;

            case ConsoleKey.Delete when shift:
                Cut();
                return true;

            case ConsoleKey.Delete:
                DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                DeleteBackward(ctrl);
                return true;

            case ConsoleKey.UpArrow when History is { Count: > 0 }:
                StepHistory(-1);
                return true;

            case ConsoleKey.DownArrow when ctrl && History is { Count: > 0 }:
                ChooseFromHistory();
                return true;

            case ConsoleKey.DownArrow when History is { Count: > 0 }:
                StepHistory(1);
                return true;

            case ConsoleKey.A when ctrl:
                SelectAll();
                return true;

            case ConsoleKey.C when ctrl:
                Copy();
                return true;

            case ConsoleKey.X when ctrl:
                Cut();
                return true;

            case ConsoleKey.V when ctrl:
                Paste();
                return true;

            case ConsoleKey.Y when ctrl:
                Clear();
                return true;
        }

        if (key.IsPlainChar)
        {
            Insert(key.Ch.ToString());
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse, Rect client)
    {
        if (!mouse.IsPress || mouse.Button != MouseButton.Left)
        {
            return false;
        }

        var r = ScreenBounds(client);
        Caret = _scroll + (mouse.X - r.X);
        return true;
    }

    /// <inheritdoc/>
    public override bool Activate()
    {
        Owner?.SetFocus(this);
        return true;
    }

    /// <summary>Empties the line (Ctrl+Y).</summary>
    public void Clear()
    {
        if (ReadOnly || _text.Length == 0)
        {
            return;
        }

        _text = string.Empty;
        _caret = 0;
        _anchor = -1;
        _scroll = 0;
        TextChanged?.Invoke(_text);
    }

    /// <summary>Copies the selection to the clipboard. A no-op in password mode.</summary>
    /// <returns><see langword="true"/> when something was copied.</returns>
    public bool Copy()
    {
        if (PasswordChar != '\0' || SelectionLength == 0)
        {
            return false;
        }

        return Clipboard.SetText(SelectedText);
    }

    /// <summary>Copies the selection and deletes it.</summary>
    /// <returns><see langword="true"/> when something was cut.</returns>
    public bool Cut()
    {
        if (!Copy())
        {
            return false;
        }

        return DeleteSelection();
    }

    /// <summary>Inserts the clipboard text at the caret.</summary>
    /// <returns><see langword="true"/> when something was pasted.</returns>
    public bool Paste()
    {
        string? s = Clipboard.GetText();
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        int before = _text.Length;
        Insert(s);
        return _text.Length != before;
    }

    /// <summary>Scrolls so that the caret is inside a viewport of <paramref name="width"/> cells.</summary>
    /// <param name="width">The viewport width in cells.</param>
    public void EnsureCaretVisible(int width)
    {
        if (width <= 0)
        {
            _scroll = 0;
            return;
        }

        if (_caret < _scroll)
        {
            _scroll = _caret;
        }
        else if (_caret >= _scroll + width)
        {
            _scroll = _caret - width + 1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _text.Length));
    }

    private void MoveTo(int position, bool select)
    {
        if (select)
        {
            if (_anchor < 0)
            {
                _anchor = _caret;
            }
        }
        else
        {
            _anchor = -1;
        }

        _caret = Math.Clamp(position, 0, _text.Length);
        EnsureCaretVisible(Bounds.Width);
    }

    private void DeleteForward()
    {
        if (ReadOnly)
        {
            return;
        }

        if (DeleteSelection())
        {
            return;
        }

        if (_caret >= _text.Length)
        {
            return;
        }

        _text = _text.Remove(_caret, 1);
        TextChanged?.Invoke(_text);
    }

    private void DeleteBackward(bool wholeWord)
    {
        if (ReadOnly)
        {
            return;
        }

        if (DeleteSelection())
        {
            return;
        }

        if (_caret <= 0)
        {
            return;
        }

        int start = wholeWord ? PrevWord(_text, _caret) : _caret - 1;
        _text = _text.Remove(start, _caret - start);
        _caret = start;
        EnsureCaretVisible(Bounds.Width);
        TextChanged?.Invoke(_text);
    }

    private void StepHistory(int direction)
    {
        var history = History;
        if (history is null || history.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            _historyStash = _text;
            _historyIndex = history.Count;
        }

        int next = Math.Clamp(_historyIndex + direction, 0, history.Count);
        if (next == _historyIndex)
        {
            return;
        }

        _historyIndex = next;
        Text = next >= history.Count ? _historyStash ?? string.Empty : history[next];
    }

    private void ChooseFromHistory()
    {
        var history = History;
        if (history is null || history.Count == 0 || HistoryChooser is null)
        {
            return;
        }

        string? chosen = HistoryChooser(history);
        if (chosen is not null)
        {
            Text = chosen;
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int PrevWord(string s, int i)
    {
        i = Math.Clamp(i, 0, s.Length);
        while (i > 0 && !IsWordChar(s[i - 1]))
        {
            i--;
        }

        while (i > 0 && IsWordChar(s[i - 1]))
        {
            i--;
        }

        return i;
    }

    private static int NextWord(string s, int i)
    {
        i = Math.Clamp(i, 0, s.Length);
        while (i < s.Length && IsWordChar(s[i]))
        {
            i++;
        }

        while (i < s.Length && !IsWordChar(s[i]))
        {
            i++;
        }

        return i;
    }
}
