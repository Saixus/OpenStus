using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Shell;
using Dvopan.Theming;

namespace Dvopan.Ui;

/// <summary>
/// The single-line shell prompt drawn between the panels and the key bar.
/// </summary>
/// <remarks>
/// <para>
/// The interesting part of this class is not the editing, it is the routing. The command line and
/// the panel share a keyboard: an empty command line means the arrow keys, Home, End, PageUp,
/// PageDown, Insert and every function key belong to the panel, and the moment there is text on the
/// line the keys that could plausibly edit it - Left, Right, Home, End, Backspace, Delete, and Up
/// and Down for the history - switch sides. Shifted keys stay out - Shift plus the arrows, Home or
/// End is the panel's select-and-move family - so the caret motions only claim a key when no
/// modifier is held. <see cref="HandleKey"/> returns <see langword="false"/> for everything the
/// panel should see, and the application feeds the panel whatever comes back unhandled.
/// </para>
/// <para>
/// The terminal habits are here too. Up and Down walk only the history entries that start like
/// the typed text (Ctrl+E and Ctrl+X walk all of it), the newest matching entry is shown greyed
/// out after the caret and accepted with Right or End - Ctrl+Right takes it one word at a time -
/// Ctrl+Backspace and Ctrl+Delete remove a word, and the line is coloured as it is typed: the
/// command word, options, strings and variables each in their own colour.
/// </para>
/// <para>
/// The grey keypad keys are the exception that has to be spelled out: Gray+, Gray- and Gray* are the
/// panel's selection commands whether or not a command is half typed, so they are handed over
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
    private readonly List<CommandToken> _tokens = [];

    private string _text = string.Empty;
    private int _caret;
    private int _scroll;
    private string? _pendingLine;

    // Reverse search (Ctrl+R): the query typed so far, the history index of the current match or
    // -1, and the line as it was before the search began, so Escape can put it back.
    private bool _searching;
    private string _searchQuery = string.Empty;
    private int _searchIndex = -1;
    private string _searchOriginal = string.Empty;

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
    /// The history entry offered as a ghost completion - the newest one that starts with the typed
    /// text and goes on past it - or <see langword="null"/>. Only offered while the caret sits at
    /// the end of a line the user is typing, never in the middle of a recall.
    /// </summary>
    public string? Suggestion =>
        !_searching && _text.Length > 0 && _caret == _text.Length && History.Cursor < 0 ? History.Suggest(_text) : null;

    /// <summary><see langword="true"/> while a Ctrl+R reverse search is running.</summary>
    public bool IsSearching => _searching;

    /// <summary>The reverse-search query typed so far; empty when not searching.</summary>
    public string SearchQuery => _searching ? _searchQuery : string.Empty;

    /// <summary>
    /// Starts a reverse search through the history, bash style: the prompt turns into
    /// <c>(reverse-i-search)'query':</c>, every typed character narrows the query, Ctrl+R steps to
    /// an older match and Ctrl+S to a newer one, Enter keeps the match on the line, Escape puts
    /// the original line back, and any other key keeps the match and is handled as usual. Whatever
    /// is on the line when the search starts becomes the initial query.
    /// </summary>
    public void StartReverseSearch()
    {
        if (_searching)
        {
            return;
        }

        _searching = true;
        _searchOriginal = _text;
        _searchQuery = _text;
        _searchIndex = -1;
        History.ResetCursor();
        _pendingLine = null;

        SearchFrom(0, step: 1);
    }

    /// <summary>
    /// The clipboard Shift+Ins and Ctrl+V paste from. Assignable so a test can substitute an
    /// in-memory implementation and never touch the machine clipboard.
    /// </summary>
    public IClipboard Clipboard { get; set; } = Ui.Clipboard.Default;

    /// <summary>
    /// The directory shown before the <c>&gt;</c>, which is also the directory Tab completion and
    /// <c>cd</c> resolve relative paths against. The application keeps this in step with the active
    /// panel.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// The text on the line. Setting it puts the caret at the end and, like every other edit, ends
    /// any history walk in progress - the Alt+F8 pick must not be undone by the next Down.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caret = _text.Length;
            History.ResetCursor();
            _pendingLine = null;
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
        string prompt = _searching
            ? "(reverse-i-search)'" + _searchQuery + "': "
            : Prefix + PromptSuffix;

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
        DrawColouring(buf, y, promptWidth, textWidth);
        DrawSuggestion(buf, y, promptWidth, textWidth);
        DrawSearchMatch(buf, y, promptWidth, textWidth);

        CaretY = y;
        CaretX = Math.Clamp(promptWidth + (_caret - _scroll), 0, Math.Max(0, width - 1));
    }

    /// <summary>Highlights where the reverse-search query sits inside the matched command.</summary>
    private void DrawSearchMatch(ScreenBuffer buf, int y, int promptWidth, int textWidth)
    {
        if (!_searching || _searchIndex < 0 || _searchQuery.Length == 0)
        {
            return;
        }

        int at = _text.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return;
        }

        int x0 = Math.Max(0, at - _scroll);
        int x1 = Math.Min(textWidth, at + _searchQuery.Length - _scroll);
        if (x1 > x0)
        {
            buf.FillStyle(new Rect(promptWidth + x0, y, x1 - x0, 1), _theme.CommandLineSelected);
        }
    }

    /// <summary>Recolours the command, options, strings and variables of the visible text.</summary>
    private void DrawColouring(ScreenBuffer buf, int y, int promptWidth, int textWidth)
    {
        _tokens.Clear();
        CommandLineSyntax.Tokenize(_text, _tokens);

        foreach (CommandToken token in _tokens)
        {
            int x0 = Math.Max(0, token.Start - _scroll);
            int x1 = Math.Min(textWidth, token.Start + token.Length - _scroll);
            if (x1 > x0)
            {
                buf.FillStyle(new Rect(promptWidth + x0, y, x1 - x0, 1), StyleFor(_theme, token.Kind));
            }
        }
    }

    /// <summary>Draws the ghost remainder of <see cref="Suggestion"/> after the typed text.</summary>
    private void DrawSuggestion(ScreenBuffer buf, int y, int promptWidth, int textWidth)
    {
        string? suggestion = Suggestion;
        if (suggestion is null)
        {
            return;
        }

        int x = _text.Length - _scroll;
        int room = textWidth - x;
        if (room <= 0)
        {
            return;
        }

        buf.WriteFixed(promptWidth + x, y, room, suggestion[_text.Length..], _theme.CommandLineSuggestion);
    }

    /// <summary>The colour a kind of command-line token is drawn in; the shell's user-screen echo uses the same table.</summary>
    /// <param name="theme">The palette.</param>
    /// <param name="kind">The token kind.</param>
    /// <returns>The style.</returns>
    public static CellStyle StyleFor(Theme theme, CommandTokenKind kind)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return kind switch
        {
            CommandTokenKind.Command => theme.CommandLineCommand,
            CommandTokenKind.Option => theme.CommandLineOption,
            CommandTokenKind.String => theme.CommandLineString,
            CommandTokenKind.Variable => theme.CommandLineVariable,
            _ => theme.CommandLineText,
        };
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
        History.ResetCursor();
        _pendingLine = null;
    }

    /// <summary>Empties the line and puts the history cursor back on it (Ctrl+Y, Esc).</summary>
    public void Clear()
    {
        _text = string.Empty;
        _caret = 0;
        _scroll = 0;
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
        if (_searching && HandleSearchKey(key))
        {
            return true;
        }

        bool empty = IsEmpty;

        // Ctrl chords first: several of them work whether or not the line has text, and the rest
        // have to fall through to the panel (Ctrl+U and friends live there).
        if ((key.Mods & KeyMods.Ctrl) != 0 && (key.Mods & KeyMods.Alt) == 0)
        {
            switch (key.Key)
            {
                case ConsoleKey.Y:
                    Clear();
                    return true;

                // Ctrl+R searches the history when there is text to search with; on an empty line
                // it stays the panel's re-read command (the shell starts a search there itself while
                // Ctrl+O hides the panels, when there is no panel to re-read).
                case ConsoleKey.R when !empty:
                    StartReverseSearch();
                    return true;

                case ConsoleKey.E:
                    return RecallPrevious(byPrefix: false);

                case ConsoleKey.X:
                    return RecallNext(byPrefix: false);

                case ConsoleKey.V:
                    Paste();
                    return true;

                case ConsoleKey.LeftArrow when !empty:
                    MoveCaret(PreviousWord(_caret));
                    return true;

                case ConsoleKey.RightArrow when !empty:
                    if (_caret == _text.Length && Suggestion is string byWord)
                    {
                        AcceptSuggestion(byWord, wholeLine: false);
                        return true;
                    }

                    MoveCaret(NextWord(_caret));
                    return true;

                case ConsoleKey.Backspace when !empty:
                    DeleteRange(PreviousWord(_caret), _caret);
                    return true;

                case ConsoleKey.Delete when !empty:
                    DeleteRange(_caret, NextWord(_caret));
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

        // Only Shift can still be down here - Ctrl and Alt were dealt with above - and a shifted
        // key is the panel's: Shift+Up/Down and friends are select-and-move, and Shift+Enter is not
        // Enter. The guards below spell that out; the trailing IsPlainChar case still accepts Shift
        // so capitals keep typing.
        switch (key.Key)
        {
            case ConsoleKey.Enter when key.Mods == KeyMods.None:
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

                return CompletePath(ctx);

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

            case ConsoleKey.LeftArrow when key.Mods == KeyMods.None:
                if (empty)
                {
                    return false;
                }

                MoveCaret(_caret - 1);
                return true;

            case ConsoleKey.RightArrow when key.Mods == KeyMods.None:
                if (empty)
                {
                    return false;
                }

                // At the end of the line Right takes the ghost suggestion, as in every shell that
                // offers one; anywhere else it is just a caret move.
                if (_caret == _text.Length && Suggestion is string accepted)
                {
                    AcceptSuggestion(accepted, wholeLine: true);
                    return true;
                }

                MoveCaret(_caret + 1);
                return true;

            case ConsoleKey.Home when key.Mods == KeyMods.None:
                if (empty)
                {
                    return false;
                }

                MoveCaret(0);
                return true;

            case ConsoleKey.End when key.Mods == KeyMods.None:
                if (empty)
                {
                    return false;
                }

                if (_caret == _text.Length && Suggestion is string acceptedAtEnd)
                {
                    AcceptSuggestion(acceptedAtEnd, wholeLine: true);
                    return true;
                }

                MoveCaret(_text.Length);
                return true;

            // With text on the line Up and Down walk the history the way a shell does: only the
            // entries starting like what was typed, the half-typed line coming back at the end.
            // On an empty line they stay with the panel, which needs them to move its cursor.
            case ConsoleKey.UpArrow when key.Mods == KeyMods.None:
                return !empty && RecallPrevious(byPrefix: true);

            case ConsoleKey.DownArrow when key.Mods == KeyMods.None:
                return !empty && RecallNext(byPrefix: true);

            // Shift+Ins pastes, as in every edit field; the plain key below stays with the panel.
            case ConsoleKey.Insert when key.Mods == KeyMods.Shift:
                Paste();
                return true;

            // Panel navigation and the function keys are never the command line's business; a
            // shifted Up or Down lands here too and goes to the panel's selection.
            //
            // Gray+, Gray- and Gray* join them: they are select-by-mask, deselect-by-mask and invert
            // selection, and the Windows backend reports them as ConsoleKey.Add/Subtract/Multiply
            // carrying '+', '-' and '*', so without these three arms the trailing IsPlainChar case
            // below would swallow them and the panel would never see the keys at all. Gray/ is left
            // out on purpose: the panel binds nothing to it, and typing a slash into a path has to
            // keep working. The '+' on the main keyboard row arrives as OemPlus, not Add, so it is
            // still inserted as text.
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
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

    /// <summary>
    /// Steps through the history regardless of what is on the line. The shell uses this while the
    /// panels are hidden (Ctrl+O), when Up and Down on an empty line have no panel cursor to move.
    /// </summary>
    /// <param name="previous">Whether to step backwards rather than forwards.</param>
    /// <returns>Always <see langword="true"/>; the key is consumed either way.</returns>
    public bool RecallHistory(bool previous) => previous ? RecallPrevious(byPrefix: false) : RecallNext(byPrefix: false);

    /// <summary>
    /// The keys that mean something inside a reverse search. Returns <see langword="false"/> for a
    /// key that ends the search and must then be handled as usual - an arrow, Home, End, Tab.
    /// </summary>
    private bool HandleSearchKey(KeyEvent key)
    {
        bool ctrl = (key.Mods & KeyMods.Ctrl) != 0;

        if (ctrl && key.Key == ConsoleKey.R)
        {
            SearchFrom(_searchIndex + 1, step: 1);
            return true;
        }

        if (ctrl && key.Key == ConsoleKey.S)
        {
            SearchFrom(_searchIndex - 1, step: -1);
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                EndSearch(keepMatch: false);
                return true;

            case ConsoleKey.Enter:
                EndSearch(keepMatch: true);
                return true;

            case ConsoleKey.Backspace when !ctrl:
                if (_searchQuery.Length > 0)
                {
                    _searchQuery = _searchQuery[..^1];
                    SearchFrom(0, step: 1);
                }

                return true;

            default:
                if (key.IsPlainChar)
                {
                    // Narrowing: the current match is tried first, exactly like bash, so the line
                    // only jumps when it has to.
                    _searchQuery += key.Ch;
                    SearchFrom(Math.Max(0, _searchIndex), step: 1);
                    return true;
                }

                EndSearch(keepMatch: true);
                return false;
        }
    }

    /// <summary>
    /// Moves to the nearest history entry containing the query, scanning from
    /// <paramref name="from"/> towards older (<c>step</c> 1) or newer (<c>step</c> -1) entries.
    /// A miss leaves the current match where it is, as bash's "failing" search does.
    /// </summary>
    private void SearchFrom(int from, int step)
    {
        IReadOnlyList<string> all = History.All;
        for (int i = from; i >= 0 && i < all.Count; i += step)
        {
            if (all[i].Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                _searchIndex = i;
                _text = all[i];
                _caret = _text.Length;
                return;
            }
        }

        if (_searchIndex < 0)
        {
            // Nothing has matched yet: keep showing the line the search started from.
            _text = _searchOriginal;
            _caret = _text.Length;
        }
    }

    private void EndSearch(bool keepMatch)
    {
        _searching = false;

        if (!keepMatch)
        {
            _text = _searchOriginal;
            _caret = _text.Length;
        }

        _searchQuery = string.Empty;
        _searchIndex = -1;
        _searchOriginal = string.Empty;
        History.ResetCursor();
        _pendingLine = null;
    }

    /// <summary>
    /// Takes the ghost suggestion onto the line - all of it, or just its next word.
    /// </summary>
    private void AcceptSuggestion(string suggestion, bool wholeLine)
    {
        int end = suggestion.Length;
        if (!wholeLine)
        {
            // One word of the remainder: to the end of the next run of non-separators.
            int i = _text.Length;
            while (i < suggestion.Length && IsSeparator(suggestion[i]))
            {
                i++;
            }

            while (i < suggestion.Length && !IsSeparator(suggestion[i]))
            {
                i++;
            }

            end = i;
        }

        _text = suggestion[..end];
        _caret = _text.Length;
        History.ResetCursor();
        _pendingLine = null;
    }

    /// <summary>Removes <c>[from, to)</c> and leaves the caret at <paramref name="from"/>.</summary>
    private void DeleteRange(int from, int to)
    {
        from = Math.Clamp(from, 0, _text.Length);
        to = Math.Clamp(to, from, _text.Length);
        if (to == from)
        {
            return;
        }

        _text = _text.Remove(from, to - from);
        MoveCaret(from);
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

    /// <summary>
    /// Tab: completes the file or folder name under the caret against the active panel's folder,
    /// the way a shell does. One match is taken outright; several are first narrowed to what they
    /// all share, and when nothing more can be shared a list opens right above the token so the
    /// pick is a matter of arrows and Enter.
    /// </summary>
    private bool CompletePath(IAppContext? ctx)
    {
        string baseDirectory = string.IsNullOrWhiteSpace(Prefix) ? Environment.CurrentDirectory : Prefix;

        (int start, int length) = PathCompletion.TokenAt(_text, _caret);
        string token = _text.Substring(start, length);
        IReadOnlyList<string> matches = PathCompletion.Matches(token, baseDirectory);

        if (matches.Count == 0)
        {
            return true; // nothing matched, but Tab still belongs to the command line
        }

        if (matches.Count == 1)
        {
            ReplaceToken(start, length, matches[0]);
            return true;
        }

        string common = PathCompletion.CommonPrefix(matches);
        if (common.Length > 0 && !string.Equals(common, token, StringComparison.Ordinal) &&
            common.Length > token.Length)
        {
            ReplaceToken(start, length, common);
            return true;
        }

        if (ctx is null)
        {
            return true;
        }

        var items = new List<MenuItem>(matches.Count);
        foreach (string match in matches)
        {
            items.Add(new MenuItem(match.Replace("&", "&&", StringComparison.Ordinal)));
        }

        // Anchored on the token, opening upwards from the command line; the menu keeps itself on
        // screen when that would not fit.
        int x = Math.Max(0, CaretX - (_caret - start));
        int y = Math.Max(0, CaretY - items.Count - 2);
        int pick = ctx.Ui.Menu("Complete", items, 0, new Rect(x, y, 0, 0));
        if (pick >= 0 && pick < matches.Count)
        {
            ReplaceToken(start, length, matches[pick]);
        }

        return true;
    }

    /// <summary>Replaces <c>[start, start + length)</c> and puts the caret after the replacement.</summary>
    private void ReplaceToken(int start, int length, string replacement)
    {
        _text = string.Concat(_text.AsSpan(0, start), replacement, _text.AsSpan(start + length));
        _caret = start + replacement.Length;
        History.ResetCursor();
        _pendingLine = null;
    }

    /// <summary>
    /// Inserts the clipboard text at the caret (Shift+Ins as well as Ctrl+V). The command line
    /// is a single-line field, so everything from the first line break on is dropped.
    /// </summary>
    private void Paste()
    {
        string? text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int cut = text.IndexOfAny(['\r', '\n']);
        Insert(cut >= 0 ? text[..cut] : text);
    }

    /// <summary>
    /// Steps to an older entry. With <paramref name="byPrefix"/> - the Up key - only entries
    /// starting like the half-typed line count, so "git" plus Up visits only the git commands;
    /// without it - Ctrl+E - the whole history is walked.
    /// </summary>
    private bool RecallPrevious(bool byPrefix)
    {
        // Remember the half-typed line so stepping back down restores it, exactly like a shell.
        _pendingLine ??= _text;

        string? entry = History.Previous(byPrefix ? _pendingLine : string.Empty);
        if (entry is null)
        {
            return true;
        }

        SetRecalled(entry);
        return true;
    }

    private bool RecallNext(bool byPrefix)
    {
        string? entry = History.Next(byPrefix ? _pendingLine : string.Empty);
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
    }

    private void MoveCaret(int position)
    {
        _caret = Math.Clamp(position, 0, _text.Length);
        AfterEdit();
    }

    private void AfterEdit()
    {
        _caret = Math.Clamp(_caret, 0, _text.Length);
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

    /// <summary>The caret position one word to the left: skip separators, then the word.</summary>
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
