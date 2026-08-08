using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Shell;
using OpenCommander.Theming;

namespace OpenCommander.Ui;

/// <summary>
/// The single-line shell prompt drawn between the panels and the key bar.
/// </summary>
/// <remarks>
/// <para>
/// The interesting part of this class is not the editing, it is the routing. In Far the command line
/// and the panel share a keyboard: an empty command line means the arrow keys, Home, End, PageUp,
/// PageDown, Insert and every function key belong to the panel, and the moment there is text on the
/// line the keys that could plausibly edit it - Left, Right, Home, End, Backspace, Delete - switch
/// sides. <see cref="HandleKey"/> returns <see langword="false"/> for everything the panel should
/// see, and the application feeds the panel whatever comes back unhandled.
/// </para>
/// <para>
/// The grey keypad keys are the exception that has to be spelled out: Gray+, Gray- and Gray* are the
/// panel's selection commands in Far whether or not a command is half typed, so they are handed over
/// even though the Windows backend delivers them carrying a printable character. Gray/ is not in that
/// group - the panel does nothing with it and a slash has to stay typeable into a path.
/// </para>
/// <para>
/// The visible text scrolls horizontally so the caret is always on screen, and the prompt is
/// truncated from the left when the current directory is too long, so the tail of the path - the
/// part that actually tells you where you are - survives.
/// </para>
/// </remarks>
public sealed class CommandLine
{
    /// <summary>The character that separates the prompt from the typed text.</summary>
    public const char PromptSuffix = '>';

    /// <summary>The prompt never eats more of the row than this, leaving room to type.</summary>
    private const int MinTextWidth = 16;

    private readonly Theme _theme;
    private readonly PathCompletion _completion = new();

    private string _text = string.Empty;
    private int _caret;
    private int _scroll;
    private string? _pendingLine;

    /// <summary>
    /// Creates a command line.
    /// </summary>
    /// <param name="theme">The palette; must not be <see langword="null"/>.</param>
    /// <param name="history">The command history recall walks; must not be <see langword="null"/>.</param>
    public CommandLine(Theme theme, CommandHistory history)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(history);

        _theme = theme;
        History = history;
    }

    /// <summary>The history Up, Down, Ctrl+E and Ctrl+X walk, and that Enter appends to.</summary>
    public CommandHistory History { get; }

    /// <summary>
    /// The directory shown before the <c>&gt;</c>, which is also the directory Tab completion and
    /// <c>cd</c> resolve relative paths against. The application keeps this in step with the active
    /// panel.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>The text on the line. Setting it puts the caret at the end.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caret = _text.Length;
            _completion.Reset();
        }
    }

    /// <summary>The caret position, from 0 to the length of <see cref="Text"/>.</summary>
    public int Caret => _caret;

    /// <summary><see langword="true"/> when there is nothing on the line.</summary>
    public bool IsEmpty => _text.Length == 0;

    /// <summary>The screen column the hardware cursor belongs at, valid after <see cref="Draw"/>.</summary>
    public int CaretX { get; private set; }

    /// <summary>The screen row the hardware cursor belongs at, valid after <see cref="Draw"/>.</summary>
    public int CaretY { get; private set; }

    /// <summary>
    /// Draws the prompt and the text across the full width of <paramref name="buf"/>, and works out
    /// where the hardware cursor goes.
    /// </summary>
    /// <param name="buf">The back buffer.</param>
    /// <param name="y">The screen row; rows outside the buffer are ignored.</param>
    public void Draw(ScreenBuffer buf, int y)
    {
        ArgumentNullException.ThrowIfNull(buf);

        if ((uint)y >= (uint)buf.Height)
        {
            return;
        }

        int width = buf.Width;
        string prompt = Prefix + PromptSuffix;

        int promptWidth = Math.Min(prompt.Length, Math.Max(0, width - MinTextWidth));
        if (promptWidth <= 0 && width > 0)
        {
            // A console too narrow for both: keep at least the ">" so the row still reads as a prompt.
            promptWidth = Math.Min(width, 1);
            prompt = PromptSuffix.ToString();
        }

        // truncateLeft: the end of a path is what tells you where you are.
        buf.WriteFixed(0, y, promptWidth, prompt, _theme.CommandLinePrefix, HAlign.Left, truncateLeft: true);

        int textWidth = Math.Max(0, width - promptWidth);
        ScrollToCaret(textWidth);

        string visible = _scroll < _text.Length
            ? _text[_scroll..Math.Min(_text.Length, _scroll + textWidth)]
            : string.Empty;

        buf.WriteFixed(promptWidth, y, textWidth, visible, _theme.CommandLineText);

        CaretY = y;
        CaretX = Math.Clamp(promptWidth + (_caret - _scroll), 0, Math.Max(0, width - 1));
    }

    /// <summary>
    /// Draws the prompt using an explicit directory, which is set as <see cref="Prefix"/> first.
    /// </summary>
    /// <param name="buf">The back buffer.</param>
    /// <param name="y">The screen row.</param>
    /// <param name="currentDirectory">The directory to show, usually the active panel's.</param>
    public void Draw(ScreenBuffer buf, int y, string currentDirectory)
    {
        Prefix = currentDirectory ?? string.Empty;
        Draw(buf, y);
    }

    /// <summary>
    /// Inserts text at the caret, which is where Ctrl+Enter, Ctrl+F and the other "put the file name
    /// on the command line" bindings land.
    /// </summary>
    /// <param name="text">The text to insert; <see langword="null"/> or empty does nothing.</param>
    public void Insert(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _text = _text.Insert(_caret, text);
        _caret += text.Length;
        _completion.Reset();
        History.ResetCursor();
        _pendingLine = null;
    }

    /// <summary>Empties the line and puts the history cursor back on it (Ctrl+Y, Esc).</summary>
    public void Clear()
    {
        _text = string.Empty;
        _caret = 0;
        _scroll = 0;
        _completion.Reset();
        History.ResetCursor();
        _pendingLine = null;
    }

    /// <summary>
    /// Handles one key press.
    /// </summary>
    /// <param name="key">The key press.</param>
    /// <param name="ctx">The application context, used to run the command on Enter.</param>
    /// <returns>
    /// <see langword="true"/> when the command line consumed the key, <see langword="false"/> when
    /// the panel should get it instead.
    /// </returns>
    public bool HandleKey(KeyEvent key, IAppContext ctx)
    {
        bool empty = IsEmpty;

        // Ctrl chords first: several of them work whether or not the line has text, and the rest
        // have to fall through to the panel (Ctrl+R, Ctrl+U and friends live there).
        if ((key.Mods & KeyMods.Ctrl) != 0 && (key.Mods & KeyMods.Alt) == 0)
        {
            switch (key.Key)
            {
                case ConsoleKey.Y:
                    Clear();
                    return true;

                case ConsoleKey.E:
                    return RecallPrevious();

                case ConsoleKey.X:
                    return RecallNext();

                case ConsoleKey.LeftArrow when !empty:
                    MoveCaret(PreviousWord(_caret));
                    return true;

                case ConsoleKey.RightArrow when !empty:
                    MoveCaret(NextWord(_caret));
                    return true;

                default:
                    // Everything else - Ctrl+Enter, Ctrl+J, Ctrl+F, Ctrl+R - belongs to the panel,
                    // which calls Insert() when it wants text put on the line.
                    return false;
            }
        }

        if ((key.Mods & KeyMods.Alt) != 0)
        {
            return false; // Alt is quick search and the Alt+F-key commands
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return Execute(ctx);

            case ConsoleKey.Escape:
                if (empty)
                {
                    return false;
                }

                Clear();
                return true;

            case ConsoleKey.Tab:
                if (empty)
                {
                    return false; // an empty line means Tab switches panels
                }

                return CompletePath();

            case ConsoleKey.Backspace:
                if (empty || _caret == 0)
                {
                    return !empty;
                }

                _text = _text.Remove(_caret - 1, 1);
                MoveCaret(_caret - 1);
                return true;

            case ConsoleKey.Delete:
                if (empty)
                {
                    return false;
                }

                if (_caret < _text.Length)
                {
                    _text = _text.Remove(_caret, 1);
                    AfterEdit();
                }

                return true;

            case ConsoleKey.LeftArrow:
                if (empty)
                {
                    return false;
                }

                MoveCaret(_caret - 1);
                return true;

            case ConsoleKey.RightArrow:
                if (empty)
                {
                    return false;
                }

                MoveCaret(_caret + 1);
                return true;

            case ConsoleKey.Home:
                if (empty)
                {
                    return false;
                }

                MoveCaret(0);
                return true;

            case ConsoleKey.End:
                if (empty)
                {
                    return false;
                }

                MoveCaret(_text.Length);
                return true;

            case ConsoleKey.UpArrow:
                return !empty && RecallPrevious();

            case ConsoleKey.DownArrow:
                return !empty && RecallNext();

            // Panel navigation and the function keys are never the command line's business.
            //
            // Gray+, Gray- and Gray* join them: they are select-by-mask, deselect-by-mask and invert
            // selection, and the Windows backend reports them as ConsoleKey.Add/Subtract/Multiply
            // carrying '+', '-' and '*', so without these three arms the trailing IsPlainChar case
            // below would swallow them and the panel would never see the keys at all. Gray/ is left
            // out on purpose: the panel binds nothing to it, and typing a slash into a path has to
            // keep working. The '+' on the main keyboard row arrives as OemPlus, not Add, so it is
            // still inserted as text.
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
            case ConsoleKey.Insert:
            case ConsoleKey.Add:
            case ConsoleKey.Subtract:
            case ConsoleKey.Multiply:
                return false;
        }

        if (key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F24)
        {
            return false;
        }

        if (key.IsPlainChar)
        {
            Insert(key.Ch.ToString());
            return true;
        }

        return false;
    }

    /// <summary>Runs the line, remembers it and clears the field.</summary>
    /// <param name="ctx">The application context, or <see langword="null"/> to only edit the history.</param>
    /// <returns><see langword="false"/> for an empty line, which lets the panel see Enter.</returns>
    private bool Execute(IAppContext? ctx)
    {
        if (IsEmpty)
        {
            return false;
        }

        string command = _text;
        Clear();
        History.Add(command);
        ctx?.RunShellCommand(command);
        return true;
    }

    private bool CompletePath()
    {
        string baseDirectory = string.IsNullOrWhiteSpace(Prefix) ? Environment.CurrentDirectory : Prefix;
        if (!_completion.TryComplete(_text, _caret, baseDirectory, out string text, out int caret))
        {
            return true; // nothing matched, but Tab still belongs to the command line
        }

        _text = text;
        _caret = Math.Clamp(caret, 0, _text.Length);
        _pendingLine = null;
        return true;
    }

    private bool RecallPrevious()
    {
        // Remember the half-typed line so stepping back down restores it, exactly like a shell.
        _pendingLine ??= _text;

        string? entry = History.Previous();
        if (entry is null)
        {
            return true;
        }

        SetRecalled(entry);
        return true;
    }

    private bool RecallNext()
    {
        string? entry = History.Next();
        if (entry is not null)
        {
            SetRecalled(entry);
            return true;
        }

        if (History.Cursor < 0 && _pendingLine is not null)
        {
            SetRecalled(_pendingLine);
            _pendingLine = null;
        }

        return true;
    }

    private void SetRecalled(string entry)
    {
        _text = entry;
        _caret = entry.Length;
        _completion.Reset();
    }

    private void MoveCaret(int position)
    {
        _caret = Math.Clamp(position, 0, _text.Length);
        AfterEdit();
    }

    private void AfterEdit()
    {
        _caret = Math.Clamp(_caret, 0, _text.Length);
        _completion.Reset();
        History.ResetCursor();
        _pendingLine = null;
    }

    private void ScrollToCaret(int textWidth)
    {
        if (textWidth <= 0)
        {
            _scroll = 0;
            return;
        }

        if (_caret < _scroll)
        {
            _scroll = _caret;
        }
        else if (_caret >= _scroll + textWidth)
        {
            _scroll = _caret - textWidth + 1;
        }

        // Do not leave a gap at the right when the text got shorter than the window.
        int maxScroll = Math.Max(0, _text.Length - textWidth + 1);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
    }

    /// <summary>The caret position one word to the left, Far style: skip separators, then the word.</summary>
    private int PreviousWord(int from)
    {
        int i = Math.Clamp(from, 0, _text.Length);
        while (i > 0 && IsSeparator(_text[i - 1]))
        {
            i--;
        }

        while (i > 0 && !IsSeparator(_text[i - 1]))
        {
            i--;
        }

        return i;
    }

    /// <summary>The caret position one word to the right: skip the word, then the separators.</summary>
    private int NextWord(int from)
    {
        int i = Math.Clamp(from, 0, _text.Length);
        while (i < _text.Length && !IsSeparator(_text[i]))
        {
            i++;
        }

        while (i < _text.Length && IsSeparator(_text[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c is '\\' or '/' or '.' or ',' or ';' or ':' or '=' or '"';
}
