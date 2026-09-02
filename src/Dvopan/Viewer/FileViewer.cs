using System.Globalization;
using System.Text;
using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Text;
using Dvopan.Theming;

namespace Dvopan.Viewer;

/// <summary>
/// The F3 viewer: a read-only, full screen window over a file of any size, in either text or hex
/// mode.
/// </summary>
/// <remarks>
/// <para>
/// The viewport is anchored on a byte offset rather than a line number, so opening a ten gigabyte
/// log is as cheap as opening a ten byte one and jumping to the end never reads the middle. See
/// <see cref="ViewerModel"/> for how that works.
/// </para>
/// <para>
/// This type only draws and handles keys; it never runs its own loop. Hand it to
/// <see cref="IUiServices.RunModal"/>.
/// </para>
/// </remarks>
public sealed class FileViewer : IScreenComponent, IDisposable
{
    /// <summary>How many columns a horizontal scroll step moves in unwrapped text mode.</summary>
    public const int HorizontalStep = 1;

    /// <summary>How many columns Ctrl+Left and Ctrl+Right move in unwrapped text mode.</summary>
    public const int HorizontalPageStep = 20;

    private static readonly KeyBarLabels BaseKeyBar = KeyBarLabels.Of(
        "Help", "Wrap", "Quit", "Hex", "Goto", string.Empty,
        "Search", string.Empty, string.Empty, "Quit", string.Empty, "Screen");

    private readonly Theme _theme;
    private readonly IUiServices _ui;
    private readonly ViewerModel? _model;

    private Rect _area;
    private long _top;
    private int _topSubRow;
    private long _hexTop;
    private int _hScroll;
    private string _lastSearch = string.Empty;
    private bool _closed;
    private bool _disposed;

    // Syntax colouring. The viewer never holds the whole file, so there is no per-line state
    // array; instead every line drawn records the state its successor starts in, keyed by byte
    // offset. Scrolling sequentially therefore chains states exactly, and a random jump falls
    // back to "no construct open" - a block comment spanning the jump target mis-colours until
    // its closing marker, which is the honest price of never scanning gigabytes.
    private readonly Dictionary<long, Text.Syntax.SyntaxState> _syntaxStates = [];
    private readonly List<Text.Syntax.TokenSpan> _syntaxTokens = [];
    private Text.Syntax.SyntaxRules? _syntaxRules;
    private bool _syntaxResolved;

    /// <summary>
    /// Opens a file for viewing.
    /// </summary>
    /// <param name="theme">The colour scheme.</param>
    /// <param name="ui">Modal services, used for the search prompt and for error reporting.</param>
    /// <param name="path">The file to view.</param>
    /// <remarks>
    /// A file that cannot be opened is reported through <paramref name="ui"/> and the viewer starts
    /// out already closed, so a caller that hands it straight to
    /// <see cref="IUiServices.RunModal"/> simply gets an immediate return. Use
    /// <see cref="TryOpen"/> to find out up front.
    /// </remarks>
    public FileViewer(Theme theme, IUiServices ui, string path)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(ui);

        _theme = theme;
        _ui = ui;
        FilePath = path ?? string.Empty;

        try
        {
            _model = new ViewerModel(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _ui.Error("View", $"Cannot open{Environment.NewLine}{FilePath}{Environment.NewLine}{e.Message}");
            _closed = true;
            return;
        }

        _top = _model.FirstLineOffset;
    }

    /// <summary>Views content already in memory; the tests and the quick view panel use this.</summary>
    /// <param name="theme">The colour scheme.</param>
    /// <param name="ui">Modal services.</param>
    /// <param name="model">The model to view; the viewer takes ownership and disposes it.</param>
    public FileViewer(Theme theme, IUiServices ui, ViewerModel model)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(model);

        _theme = theme;
        _ui = ui;
        _model = model;
        FilePath = model.FilePath;
        _top = model.FirstLineOffset;
    }

    /// <summary>
    /// Opens a file for viewing, returning <see langword="null"/> when it cannot be read. The
    /// failure has already been reported to the user.
    /// </summary>
    /// <param name="theme">The colour scheme.</param>
    /// <param name="ui">Modal services.</param>
    /// <param name="path">The file to view.</param>
    /// <returns>The viewer, or <see langword="null"/>.</returns>
    public static FileViewer? TryOpen(Theme theme, IUiServices ui, string path)
    {
        var viewer = new FileViewer(theme, ui, path);
        if (!viewer.IsClosed)
        {
            return viewer;
        }

        viewer.Dispose();
        return null;
    }

    /// <summary>The file being viewed.</summary>
    public string FilePath { get; }

    /// <summary>The underlying byte reader, or <see langword="null"/> when the file failed to open.</summary>
    public ViewerModel? Model => _model;

    /// <summary>Wrap long lines instead of scrolling horizontally (F2). On by default.</summary>
    public bool Wrap { get; set; } = true;

    /// <summary>Show raw bytes instead of decoded text (F4).</summary>
    public bool HexMode { get; private set; }

    /// <summary>Search case insensitively. On by default.</summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>
    /// Colour the text by the file type's syntax (C#, JSON, SQL, ...). On by default; a file
    /// whose extension no rules cover - and hex mode - is drawn plain.
    /// </summary>
    public bool SyntaxHighlight { get; set; } = true;

    /// <summary>How many columns a tab character advances to. Eight, the classic console default.</summary>
    public int TabSize { get; set; } = 8;

    /// <summary>The byte offset shown at the top of the viewport.</summary>
    public long TopOffset => HexMode ? _hexTop : _top;

    /// <inheritdoc/>
    public bool IsClosed => _closed;

    /// <inheritdoc/>
    public void Layout(Rect area) => _area = area;

    /// <inheritdoc/>
    public KeyBarLabels? KeyBarFor(KeyMods mods)
    {
        var bar = BaseKeyBar
            .WithLabel(1, Wrap ? "Unwrap" : "Wrap")
            .WithLabel(3, HexMode ? "Text" : "Hex");

        return (mods & KeyMods.Shift) != 0 ? bar.WithLabel(6, "Next") : bar;
    }

    /// <inheritdoc/>
    public void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (_area.IsEmpty)
        {
            _area = new Rect(0, 0, buffer.Width, buffer.Height);
        }

        buffer.Fill(_area, ' ', _theme.ViewerText);

        // Title row, then the body, then the status row. A viewport too short for all three
        // degrades to body-only rather than drawing garbage.
        int bodyTop = _area.Y + (_area.Height >= 3 ? 1 : 0);
        int bodyRows = Math.Max(0, _area.Height - (_area.Height >= 3 ? 2 : 0));

        if (_area.Height >= 3)
        {
            buffer.WriteFixed(_area.X, _area.Y, _area.Width, Title(), _theme.ViewerStatus, HAlign.Center);
        }

        if (_model is null)
        {
            return;
        }

        if (HexMode)
        {
            DrawHex(buffer, bodyTop, bodyRows);
        }
        else
        {
            DrawText(buffer, bodyTop, bodyRows);
        }

        if (_area.Height >= 3)
        {
            DrawStatus(buffer, _area.Bottom - 1);
        }
    }

    /// <inheritdoc/>
    public bool HandleInput(InputEvent ev)
    {
        if (_closed || _model is null)
        {
            _closed = true;
            return false;
        }

        switch (ev.Kind)
        {
            case InputKind.Key:
                return HandleKey(ev.Key);

            case InputKind.Mouse when ev.Mouse.Kind == MouseKind.Wheel:
                Scroll(ev.Mouse.Wheel > 0 ? -3 : 3);
                return true;

            default:
                return true;
        }
    }

    /// <summary>Renders the viewport as plain text, which is what the screenshot tests assert on.</summary>
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model?.Dispose();
    }

    // ---- input ----------------------------------------------------------------------------------

    private bool HandleKey(KeyEvent key)
    {
        int rows = Math.Max(1, BodyRows);

        switch (key.Key)
        {
            case ConsoleKey.Escape or ConsoleKey.F10 when !key.HasMods:
            case ConsoleKey.F3 when !key.HasMods:
                _closed = true;
                return false;

            case ConsoleKey.F1 when !key.HasMods:
                return true; // The help screen is the shell's business.

            case ConsoleKey.F2 when !key.HasMods:
                ToggleWrap();
                return true;

            case ConsoleKey.F4 when !key.HasMods:
                ToggleHex();
                return true;

            case ConsoleKey.F5 when !key.HasMods:
                GoTo();
                return true;

            case ConsoleKey.F7 when key.Mods == KeyMods.None:
                Search(fresh: true);
                return true;

            case ConsoleKey.F7 when key.Mods == KeyMods.Shift:
                Search(fresh: false);
                return true;

            case ConsoleKey.UpArrow when !key.HasMods:
                Scroll(-1);
                return true;

            case ConsoleKey.DownArrow when !key.HasMods:
                Scroll(1);
                return true;

            case ConsoleKey.PageUp when !key.HasMods:
                Scroll(-rows);
                return true;

            case ConsoleKey.PageDown when !key.HasMods:
                Scroll(rows);
                return true;

            case ConsoleKey.Home when key.Mods == KeyMods.Ctrl:
                GoToStart();
                return true;

            case ConsoleKey.End when key.Mods == KeyMods.Ctrl:
                GoToEnd(rows);
                return true;

            case ConsoleKey.Home when !key.HasMods:
                if (HexMode || Wrap)
                {
                    GoToStart();
                }
                else
                {
                    _hScroll = 0;
                }

                return true;

            case ConsoleKey.End when !key.HasMods:
                if (HexMode || Wrap)
                {
                    GoToEnd(rows);
                }
                else
                {
                    _hScroll = Math.Max(0, LongestVisibleLine() - Math.Max(1, BodyWidth));
                }

                return true;

            case ConsoleKey.LeftArrow when !key.HasMods:
                ScrollHorizontally(-HorizontalStep);
                return true;

            case ConsoleKey.RightArrow when !key.HasMods:
                ScrollHorizontally(HorizontalStep);
                return true;

            case ConsoleKey.LeftArrow when key.Mods == KeyMods.Ctrl:
                ScrollHorizontally(-HorizontalPageStep);
                return true;

            case ConsoleKey.RightArrow when key.Mods == KeyMods.Ctrl:
                ScrollHorizontally(HorizontalPageStep);
                return true;

            default:
                return true;
        }
    }

    private void ToggleWrap()
    {
        Wrap = !Wrap;
        _topSubRow = 0;
        if (Wrap)
        {
            _hScroll = 0;
        }
    }

    private void ToggleHex()
    {
        if (_model is null)
        {
            return;
        }

        if (HexMode)
        {
            HexMode = false;
            _top = SnapToLineStart(_hexTop);
            _topSubRow = 0;
        }
        else
        {
            HexMode = true;
            _hexTop = _top - (_top % HexBytesPerRow);
        }
    }

    private void Scroll(int rows)
    {
        if (_model is null || rows == 0)
        {
            return;
        }

        if (HexMode)
        {
            long max = LastHexRowOffset();
            _hexTop = Math.Clamp(_hexTop + ((long)rows * HexBytesPerRow), 0, max);
            return;
        }

        if (rows > 0)
        {
            for (int i = 0; i < rows; i++)
            {
                if (!StepDown())
                {
                    break;
                }
            }
        }
        else
        {
            for (int i = 0; i > rows; i--)
            {
                if (!StepUp())
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Moves the top of the viewport down one visual row. False when the end of the file is
    /// already visible: scrolling stops once the last line reaches the bottom row rather than
    /// letting it walk up the screen with nothing but blank rows beneath.
    /// </summary>
    private bool StepDown()
    {
        if (_model is null || EndIsVisible())
        {
            return false;
        }

        if (Wrap)
        {
            int count = VisualRowCount(Expand(_model.ReadLine(_top, out long next), TabSize));
            if (_topSubRow + 1 < count)
            {
                _topSubRow++;
                return true;
            }

            if (next <= _top || next >= _model.Length)
            {
                return false;
            }

            _top = next;
            _topSubRow = 0;
            return true;
        }

        long forward = _model.NextLineOffset(_top);
        if (forward <= _top || forward >= _model.Length)
        {
            return false;
        }

        _top = forward;
        return true;
    }

    /// <summary>Moves the top of the viewport up one visual row. False when already at the start.</summary>
    private bool StepUp()
    {
        if (_model is null)
        {
            return false;
        }

        if (Wrap && _topSubRow > 0)
        {
            _topSubRow--;
            return true;
        }

        if (_top <= _model.FirstLineOffset)
        {
            return false;
        }

        long back = _model.PreviousLineOffset(_top);
        if (back >= _top)
        {
            return false;
        }

        _top = back;
        _topSubRow = Wrap ? Math.Max(0, VisualRowCount(Expand(_model.ReadLine(_top, out _), TabSize)) - 1) : 0;
        return true;
    }

    private void ScrollHorizontally(int columns)
    {
        if (HexMode || Wrap)
        {
            Scroll(columns > 0 ? 1 : -1);
            return;
        }

        _hScroll = Math.Max(0, _hScroll + columns);
    }

    private void GoToStart()
    {
        if (_model is null)
        {
            return;
        }

        _top = _model.FirstLineOffset;
        _topSubRow = 0;
        _hexTop = 0;
        _hScroll = 0;
    }

    private void GoToEnd(int rows)
    {
        if (_model is null)
        {
            return;
        }

        _hexTop = LastHexRowOffset();
        _top = _model.LastPageOffset(rows);
        _topSubRow = 0;
    }

    private void GoTo()
    {
        if (_model is null)
        {
            return;
        }

        string? answer = _ui.Input(
            "Go to",
            "Offset, 0xHEX offset or NN%",
            string.Empty,
            historyKey: "ViewerGoto");

        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        string text = answer.Trim();
        long target;

        if (text.EndsWith('%'))
        {
            if (!double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
            {
                return;
            }

            target = (long)(_model.Length * Math.Clamp(percent, 0, 100) / 100.0);
        }
        else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out target))
            {
                return;
            }
        }
        else if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out target))
        {
            return;
        }

        target = Math.Clamp(target, 0, _model.Length);
        _hexTop = Math.Clamp(target - (target % HexBytesPerRow), 0, LastHexRowOffset());
        _top = SnapToLineStart(target);
        _topSubRow = 0;
    }

    private void Search(bool fresh)
    {
        if (_model is null)
        {
            return;
        }

        if (fresh)
        {
            string? answer = _ui.Input("Search", "Search for", _lastSearch, historyKey: "ViewerSearch");
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

        long from = fresh ? _top : _model.NextLineOffset(_top);
        if (_model.Find(_lastSearch, from, IgnoreCase, backwards: false, out long at, out _))
        {
            _top = at;
            _topSubRow = 0;
            _hexTop = Math.Clamp(at - (at % HexBytesPerRow), 0, LastHexRowOffset());
            return;
        }

        _ui.Message("Search", ["\"" + _lastSearch + "\" not found"], MessageButtons.Ok, warning: true);
    }

    // ---- drawing --------------------------------------------------------------------------------

    private void DrawText(ScreenBuffer buffer, int top, int rows)
    {
        if (_model is null || rows <= 0)
        {
            return;
        }

        int width = BodyWidth;
        long offset = _top;
        int sub = Wrap ? _topSubRow : 0;
        int drawn = 0;
        bool overflowRight = false;

        Text.Syntax.SyntaxRules? rules = CurrentSyntaxRules();
        Text.Syntax.SyntaxState state = rules is not null && _syntaxStates.TryGetValue(offset, out var seeded)
            ? seeded
            : Text.Syntax.SyntaxState.None;

        while (drawn < rows && offset <= _model.Length)
        {
            string line = Expand(_model.ReadLine(offset, out long next), TabSize);

            _syntaxTokens.Clear();
            if (rules is not null)
            {
                // Tokenizing the expanded line means the spans are already in display columns.
                state = Text.Syntax.SyntaxTokenizer.TokenizeLine(line, rules, state, _syntaxTokens);
                RememberSyntaxState(next, state);
            }

            if (Wrap)
            {
                int chunks = VisualRowCount(line);
                for (; sub < chunks && drawn < rows; sub++, drawn++)
                {
                    int start = sub * width;
                    int take = Math.Min(width, Math.Max(0, line.Length - start));
                    buffer.WriteFixed(
                        _area.X,
                        top + drawn,
                        width,
                        take > 0 ? line.Substring(start, take) : string.Empty,
                        _theme.ViewerText);

                    DrawSyntaxSpans(buffer, top + drawn, windowStart: start, width);
                }

                sub = 0;
            }
            else
            {
                overflowRight |= line.Length > _hScroll + width;
                string slice = _hScroll < line.Length
                    ? line[_hScroll..Math.Min(line.Length, _hScroll + width)]
                    : string.Empty;

                buffer.WriteFixed(_area.X, top + drawn, width, slice, _theme.ViewerText);
                DrawSyntaxSpans(buffer, top + drawn, windowStart: _hScroll, width);
                drawn++;
            }

            if (next <= offset)
            {
                break;
            }

            offset = next;
            if (offset >= _model.Length)
            {
                break;
            }
        }

        if (Wrap || width <= 0)
        {
            return;
        }

        // Overlay scroll markers on the first and last body columns when text is cut off, the
        // classic console-viewer cue that there is more to the side.
        if (_hScroll > 0)
        {
            for (int r = 0; r < Math.Min(rows, drawn); r++)
            {
                buffer.Set(_area.X, top + r, '◄', _theme.ViewerArrows);
            }
        }

        if (overflowRight)
        {
            for (int r = 0; r < Math.Min(rows, drawn); r++)
            {
                buffer.Set(_area.X + width - 1, top + r, '►', _theme.ViewerArrows);
            }
        }
    }

    /// <summary>
    /// Recolours the syntax spans of one drawn body row: the spans in <see cref="_syntaxTokens"/>
    /// are in whole-line display columns, and the row shows the window
    /// <c>[windowStart, windowStart + width)</c> of that line.
    /// </summary>
    private void DrawSyntaxSpans(ScreenBuffer buffer, int y, int windowStart, int width)
    {
        foreach (Text.Syntax.TokenSpan token in _syntaxTokens)
        {
            int x0 = Math.Max(0, token.Start - windowStart);
            int x1 = Math.Min(width, token.Start + token.Length - windowStart);
            if (x1 > x0)
            {
                buffer.FillStyle(new Rect(_area.X + x0, y, x1 - x0, 1), SyntaxStyle(token.Kind));
            }
        }
    }

    /// <summary>The rules for the file being viewed, or <see langword="null"/> when off or unknown.</summary>
    private Text.Syntax.SyntaxRules? CurrentSyntaxRules()
    {
        if (!SyntaxHighlight)
        {
            return null;
        }

        if (!_syntaxResolved)
        {
            _syntaxResolved = true;
            _syntaxRules = Text.Syntax.SyntaxRegistry.ForPath(FilePath);
        }

        return _syntaxRules;
    }

    private void RememberSyntaxState(long offset, Text.Syntax.SyntaxState state)
    {
        // A bounded memory of line-start states; past the cap the map simply starts over, at the
        // cost of a fallback to "nothing open" on the next backwards jump.
        if (_syntaxStates.Count > 65536)
        {
            _syntaxStates.Clear();
        }

        _syntaxStates[offset] = state;
    }

    private CellStyle SyntaxStyle(Text.Syntax.TokenKind kind) => kind switch
    {
        Text.Syntax.TokenKind.Keyword => _theme.SyntaxKeyword,
        Text.Syntax.TokenKind.String => _theme.SyntaxString,
        Text.Syntax.TokenKind.Number => _theme.SyntaxNumber,
        Text.Syntax.TokenKind.Comment => _theme.SyntaxComment,
        Text.Syntax.TokenKind.Preprocessor => _theme.SyntaxPreprocessor,
        _ => _theme.ViewerText,
    };

    private void DrawHex(ScreenBuffer buffer, int top, int rows)
    {
        if (_model is null || rows <= 0)
        {
            return;
        }

        byte[] block = _model.ReadBlock(_hexTop, rows * HexBytesPerRow);
        var sb = new StringBuilder(80);

        for (int r = 0; r < rows; r++)
        {
            int start = r * HexBytesPerRow;
            if (start >= block.Length && !(start == 0 && block.Length == 0))
            {
                break;
            }

            int count = Math.Clamp(block.Length - start, 0, HexBytesPerRow);
            buffer.WriteFixed(
                _area.X,
                top + r,
                _area.Width,
                FormatHexRow(sb, _hexTop + start, block.AsSpan(start, count)),
                _theme.ViewerText);

            if (count < HexBytesPerRow)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Formats one hex row: an eight digit offset, sixteen bytes split into two groups of eight,
    /// and the ASCII gutter with unprintable bytes shown as dots.
    /// </summary>
    /// <param name="sb">A scratch builder, cleared on entry.</param>
    /// <param name="offset">The offset of the first byte in the row.</param>
    /// <param name="bytes">Up to <see cref="ViewerModel.HexBytesPerRow"/> bytes.</param>
    /// <returns>The formatted row.</returns>
    public static string FormatHexRow(StringBuilder sb, long offset, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(sb);

        sb.Clear();
        sb.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append(": ");

        for (int i = 0; i < HexBytesPerRow; i++)
        {
            if (i == HexBytesPerRow / 2)
            {
                sb.Append(' ');
            }

            if (i < bytes.Length)
            {
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            }
            else
            {
                sb.Append("   ");
            }
        }

        sb.Append("│ ");
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        return sb.ToString();
    }

    private void DrawStatus(ScreenBuffer buffer, int row)
    {
        if (_model is null)
        {
            return;
        }

        long offset = HexMode ? _hexTop : _top;

        // The percentage reports how much of the file the BOTTOM of the viewport reaches, so a file
        // that fits on one screen reads 100% immediately and Ctrl+End always lands on 100%.
        int percent = _model.Length <= 0
            ? 100
            : (int)(VisibleEndOffset(BodyRows) * 100 / _model.Length);

        int column = HexMode ? 0 : _hScroll;

        string right = string.Format(
            CultureInfo.InvariantCulture,
            "Col {0}  {1}  {2}%  {3}  {4}",
            column,
            offset,
            Math.Clamp(percent, 0, 100),
            _model.EncodingName,
            LineEndings.Name(_model.LineEnding));

        buffer.WriteFixed(_area.X, row, _area.Width, string.Empty, _theme.ViewerStatus);

        int rightStart = Math.Max(_area.X, _area.Right - right.Length - 1);
        int nameRoom = Math.Max(0, rightStart - _area.X - 2);
        if (nameRoom > 0)
        {
            buffer.WriteFixed(_area.X + 1, row, nameRoom, _model.FileName, _theme.ViewerStatus, truncateLeft: true);
        }

        buffer.WriteFixed(rightStart, row, Math.Min(right.Length, _area.Right - rightStart), right, _theme.ViewerStatus);
    }

    private string Title()
    {
        if (_model is null)
        {
            return FilePath;
        }

        string name = string.IsNullOrEmpty(_model.FileName) ? "(memory)" : _model.FileName;
        return HexMode ? name + " [hex]" : name;
    }

    // ---- helpers --------------------------------------------------------------------------------

    private const int HexBytesPerRow = ViewerModel.HexBytesPerRow;

    private int BodyWidth => Math.Max(1, _area.Width);

    private int BodyRows => Math.Max(1, _area.Height >= 3 ? _area.Height - 2 : _area.Height);

    private long LastHexRowOffset()
    {
        if (_model is null || _model.Length == 0)
        {
            return 0;
        }

        // The offset that puts the final row of bytes on the bottom line of the viewport.
        long lastRow = (_model.Length - 1) / HexBytesPerRow * HexBytesPerRow;
        long page = (long)Math.Max(0, BodyRows - 1) * HexBytesPerRow;
        return Math.Max(0, lastRow - page);
    }

    /// <summary>
    /// The byte offset just past the last content the viewport can show, which is what the status
    /// line's percentage is measured to. In wrap mode a line partly on screen counts whole - the
    /// cost of mapping a sub-row back to a byte offset is not worth a percent of precision.
    /// </summary>
    /// <param name="rows">How many body rows the viewport has.</param>
    private long VisibleEndOffset(int rows)
    {
        if (_model is null)
        {
            return 0;
        }

        if (HexMode)
        {
            return Math.Min(_model.Length, _hexTop + ((long)rows * HexBytesPerRow));
        }

        long offset = _top;
        int visual = Wrap ? -_topSubRow : 0;
        while (visual < rows && offset < _model.Length)
        {
            string line = _model.ReadLine(offset, out long next);
            visual += Wrap ? VisualRowCount(Expand(line, TabSize)) : 1;
            if (next <= offset)
            {
                break;
            }

            offset = next;
        }

        return offset;
    }

    /// <summary>
    /// Whether the end of the file is already on screen from the current top position, which is
    /// the point downward scrolling stops at. Bounded: the walk gives up as soon as it has
    /// counted one screenful of rows.
    /// </summary>
    private bool EndIsVisible()
    {
        if (_model is null)
        {
            return true;
        }

        int rows = BodyRows;
        int visual = Wrap ? -_topSubRow : 0;
        long offset = _top;
        while (offset < _model.Length)
        {
            string line = _model.ReadLine(offset, out long next);
            visual += Wrap ? VisualRowCount(Expand(line, TabSize)) : 1;
            if (visual > rows)
            {
                return false;
            }

            if (next <= offset)
            {
                break;
            }

            offset = next;
        }

        return true;
    }

    private long SnapToLineStart(long offset)
    {
        if (_model is null)
        {
            return 0;
        }

        long o = _model.Clamp(offset);
        return o <= _model.FirstLineOffset ? _model.FirstLineOffset : _model.PreviousLineOffset(o + 1);
    }

    private int VisualRowCount(string expandedLine)
    {
        int width = BodyWidth;
        return expandedLine.Length <= width ? 1 : ((expandedLine.Length + width - 1) / width);
    }

    private int LongestVisibleLine()
    {
        if (_model is null)
        {
            return 0;
        }

        int longest = 0;
        long offset = _top;
        for (int i = 0; i < BodyRows && offset < _model.Length; i++)
        {
            longest = Math.Max(longest, Expand(_model.ReadLine(offset, out long next), TabSize).Length);
            if (next <= offset)
            {
                break;
            }

            offset = next;
        }

        return longest;
    }

    /// <summary>Replaces tabs with the spaces that advance to the next tab stop.</summary>
    /// <param name="line">The raw line.</param>
    /// <param name="tabSize">Columns per tab stop; values below one are treated as one.</param>
    /// <returns>The line with every tab expanded, and other control characters shown as dots.</returns>
    public static string Expand(string line, int tabSize)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        int stop = Math.Max(1, tabSize);
        if (line.IndexOf('\t') < 0 && !ContainsControl(line))
        {
            return line;
        }

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

    private static bool ContainsControl(string line)
    {
        foreach (char c in line)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
