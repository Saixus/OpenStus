using System.Text;

namespace Dvopan.Rendering;

/// <summary>Horizontal alignment used by <see cref="ScreenBuffer.WriteFixed"/>.</summary>
public enum HAlign
{
    /// <summary>Text hugs the left edge of the field.</summary>
    Left,

    /// <summary>Text is centred, with any odd padding cell going to the right.</summary>
    Center,

    /// <summary>Text hugs the right edge of the field.</summary>
    Right,
}

/// <summary>
/// An off-screen character grid. Every drawing primitive clips to the buffer edges instead of
/// throwing, so layout code never has to guard against a console that is smaller than expected.
/// The backing store is one flat <see cref="Cell"/> array indexed <c>y * Width + x</c>.
/// </summary>
public sealed class ScreenBuffer
{
    /// <summary>The character placed at a truncation point by <see cref="WriteFixed"/>.</summary>
    public const char Ellipsis = '…';

    /// <summary>ASCII ESC - the first character of every ANSI control sequence.</summary>
    internal const char Esc = '\u001b';

    private Cell[] _cells;
    private int _width;
    private int _height;

    /// <summary>Creates a buffer. Both dimensions are clamped to at least one cell.</summary>
    public ScreenBuffer(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _cells = new Cell[_width * _height];
        Clear(CellStyle.Default);
    }

    /// <summary>Width in cells; always at least 1.</summary>
    public int Width => _width;

    /// <summary>Height in cells; always at least 1.</summary>
    public int Height => _height;

    /// <summary>Direct access to the backing store, for the render diff. Indexed <c>y * Width + x</c>.</summary>
    internal Cell[] Cells => _cells;

    /// <summary>
    /// Resizes the grid, preserving the content of the overlapping top-left region and filling
    /// anything new with spaces in <see cref="CellStyle.Default"/>. A no-op when the size is unchanged.
    /// </summary>
    public void Resize(int width, int height)
    {
        int w = Math.Max(1, width);
        int h = Math.Max(1, height);
        if (w == _width && h == _height)
        {
            return;
        }

        var next = new Cell[w * h];
        var blank = new Cell(' ', CellStyle.Default);
        for (int i = 0; i < next.Length; i++)
        {
            next[i] = blank;
        }

        int copyW = Math.Min(w, _width);
        int copyH = Math.Min(h, _height);
        for (int y = 0; y < copyH; y++)
        {
            Array.Copy(_cells, y * _width, next, y * w, copyW);
        }

        _cells = next;
        _width = w;
        _height = h;
    }

    /// <summary>Fills the whole buffer with spaces in <paramref name="style"/>.</summary>
    public void Clear(CellStyle style)
    {
        var blank = new Cell(' ', style);
        var span = _cells.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = blank;
        }
    }

    /// <summary>Writes one cell. Coordinates outside the buffer are silently ignored.</summary>
    public void Set(int x, int y, char ch, CellStyle style)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            return;
        }

        ref var cell = ref _cells[(y * _width) + x];
        cell.Ch = ch;
        cell.Style = style;
    }

    /// <summary>
    /// Reads one cell. Coordinates outside the buffer yield a space in
    /// <see cref="CellStyle.Default"/> rather than throwing.
    /// </summary>
    public Cell Get(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            return new Cell(' ', CellStyle.Default);
        }

        return _cells[(y * _width) + x];
    }

    /// <summary>
    /// Writes <paramref name="text"/> starting at <paramref name="x"/>, clipping at the buffer
    /// edges.
    /// </summary>
    /// <returns>The number of cells that actually landed inside the buffer.</returns>
    public int Write(int x, int y, string text, CellStyle style)
    {
        if (string.IsNullOrEmpty(text) || (uint)y >= (uint)_height)
        {
            return 0;
        }

        int written = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int cx = x + i;
            if (cx < 0)
            {
                continue;
            }

            if (cx >= _width)
            {
                break;
            }

            Set(cx, y, text[i], style);
            written++;
        }

        return written;
    }

    /// <summary>
    /// Paints exactly <paramref name="width"/> cells starting at <paramref name="x"/>.
    /// Short or <see langword="null"/> text is padded with <paramref name="pad"/> according to
    /// <paramref name="align"/>; longer text is truncated and <see cref="Ellipsis"/> is placed at
    /// the cut point. With <paramref name="truncateLeft"/> the tail of the string is kept and the
    /// ellipsis goes on the left, which is what long paths want.
    /// </summary>
    public void WriteFixed(
        int x,
        int y,
        int width,
        string? text,
        CellStyle style,
        HAlign align = HAlign.Left,
        bool truncateLeft = false,
        char pad = ' ')
    {
        if (width <= 0)
        {
            return;
        }

        string s = text ?? string.Empty;
        int len = s.Length;

        if (len > width)
        {
            if (width == 1)
            {
                Set(x, y, Ellipsis, style);
                return;
            }

            if (truncateLeft)
            {
                Set(x, y, Ellipsis, style);
                int start = len - (width - 1);
                for (int i = 0; i < width - 1; i++)
                {
                    Set(x + 1 + i, y, s[start + i], style);
                }
            }
            else
            {
                for (int i = 0; i < width - 1; i++)
                {
                    Set(x + i, y, s[i], style);
                }

                Set(x + width - 1, y, Ellipsis, style);
            }

            return;
        }

        int left = align switch
        {
            HAlign.Right => width - len,
            HAlign.Center => (width - len) / 2,
            _ => 0,
        };

        for (int i = 0; i < left; i++)
        {
            Set(x + i, y, pad, style);
        }

        for (int i = 0; i < len; i++)
        {
            Set(x + left + i, y, s[i], style);
        }

        for (int i = left + len; i < width; i++)
        {
            Set(x + i, y, pad, style);
        }
    }

    /// <summary>
    /// Writes text in which <c>'&amp;'</c> marks the following character as a hotkey, drawn with
    /// <paramref name="hotStyle"/>. <c>"&amp;&amp;"</c> emits a literal <c>'&amp;'</c>; only the
    /// first marker is honoured, and a trailing lone <c>'&amp;'</c> is dropped.
    /// </summary>
    public void WriteHotkey(int x, int y, string text, CellStyle style, CellStyle hotStyle)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int cx = x;
        bool hotUsed = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '&')
            {
                Set(cx++, y, c, style);
                continue;
            }

            if (i + 1 >= text.Length)
            {
                continue; // trailing lone '&' - drop it
            }

            if (text[i + 1] == '&')
            {
                Set(cx++, y, '&', style);
                i++;
                continue;
            }

            i++;
            Set(cx++, y, text[i], hotUsed ? style : hotStyle);
            hotUsed = true;
        }
    }

    /// <summary>Display width of hotkey-marked text, i.e. its length ignoring the markers.</summary>
    public static int HotkeyTextLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                n++;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                n++;
                i++;
            }

            // A marker (or a trailing lone '&') contributes nothing; the marked character
            // is counted by the next iteration.
        }

        return n;
    }

    /// <summary>The hotkey character of hotkey-marked text, lowercased, or <see langword="null"/>.</summary>
    public static char? HotkeyOf(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                continue;
            }

            if (i + 1 >= text.Length)
            {
                return null;
            }

            if (text[i + 1] == '&')
            {
                i++;
                continue;
            }

            return char.ToLowerInvariant(text[i + 1]);
        }

        return null;
    }

    /// <summary>Fills a rectangle with one character, clipped to the buffer.</summary>
    public void Fill(Rect r, char ch, CellStyle style)
    {
        for (int y = Math.Max(0, r.Y); y < Math.Min(_height, r.Bottom); y++)
        {
            int row = y * _width;
            for (int x = Math.Max(0, r.X); x < Math.Min(_width, r.Right); x++)
            {
                ref var cell = ref _cells[row + x];
                cell.Ch = ch;
                cell.Style = style;
            }
        }
    }

    /// <summary>Recolours a rectangle without touching the characters in it.</summary>
    public void FillStyle(Rect r, CellStyle style)
    {
        for (int y = Math.Max(0, r.Y); y < Math.Min(_height, r.Bottom); y++)
        {
            int row = y * _width;
            for (int x = Math.Max(0, r.X); x < Math.Min(_width, r.Right); x++)
            {
                _cells[row + x].Style = style;
            }
        }
    }

    /// <summary>Draws a horizontal run of <paramref name="length"/> cells.</summary>
    public void HLine(int x, int y, int length, char ch, CellStyle style)
    {
        for (int i = 0; i < length; i++)
        {
            Set(x + i, y, ch, style);
        }
    }

    /// <summary>Draws a vertical run of <paramref name="length"/> cells.</summary>
    public void VLine(int x, int y, int length, char ch, CellStyle style)
    {
        for (int i = 0; i < length; i++)
        {
            Set(x, y + i, ch, style);
        }
    }

    /// <summary>
    /// Draws the frame of <paramref name="r"/>. Degenerate rectangles degrade gracefully:
    /// a one-cell-wide rectangle becomes a vertical line, a one-cell-high one a horizontal line.
    /// </summary>
    public void DrawBox(Rect r, BoxStyle style, CellStyle color)
    {
        if (r.IsEmpty)
        {
            return;
        }

        char h = BoxChars.Horizontal(style);
        char v = BoxChars.Vertical(style);

        if (r.Width == 1 && r.Height == 1)
        {
            Set(r.X, r.Y, h, color);
            return;
        }

        if (r.Width == 1)
        {
            VLine(r.X, r.Y, r.Height, v, color);
            return;
        }

        if (r.Height == 1)
        {
            HLine(r.X, r.Y, r.Width, h, color);
            return;
        }

        int right = r.Right - 1;
        int bottom = r.Bottom - 1;

        Set(r.X, r.Y, BoxChars.TopLeft(style), color);
        Set(right, r.Y, BoxChars.TopRight(style), color);
        Set(r.X, bottom, BoxChars.BottomLeft(style), color);
        Set(right, bottom, BoxChars.BottomRight(style), color);

        HLine(r.X + 1, r.Y, r.Width - 2, h, color);
        HLine(r.X + 1, bottom, r.Width - 2, h, color);
        VLine(r.X, r.Y + 1, r.Height - 2, v, color);
        VLine(right, r.Y + 1, r.Height - 2, v, color);
    }

    /// <summary>
    /// Darkens the classic two-column right / one-row bottom drop shadow of
    /// <paramref name="r"/> to DarkGray on Black, leaving the characters underneath in place.
    /// </summary>
    public void DrawShadow(Rect r)
    {
        if (r.IsEmpty)
        {
            return;
        }

        var shadow = new CellStyle(ConsoleColor.DarkGray, ConsoleColor.Black);

        for (int y = r.Y + 1; y < r.Bottom; y++)
        {
            Darken(r.Right, y, shadow);
            Darken(r.Right + 1, y, shadow);
        }

        for (int x = r.X + 2; x < r.Right + 2; x++)
        {
            Darken(x, r.Bottom, shadow);
        }
    }

    private void Darken(int x, int y, CellStyle shadow)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            return;
        }

        _cells[(y * _width) + x].Style = shadow;
    }

    /// <summary>Returns an independent copy of this buffer.</summary>
    public ScreenBuffer Clone()
    {
        var copy = new ScreenBuffer(_width, _height);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    /// <summary>
    /// Renders the buffer as plain text: rows joined by <c>"\n"</c>, with trailing spaces
    /// trimmed off each row. Deterministic; this is what <c>--screenshot</c> prints.
    /// </summary>
    public string RenderPlainText()
    {
        var sb = new StringBuilder(_width * _height);
        for (int y = 0; y < _height; y++)
        {
            if (y > 0)
            {
                sb.Append('\n');
            }

            int end = RowEnd(y);
            int row = y * _width;
            for (int x = 0; x < end; x++)
            {
                sb.Append(_cells[row + x].Glyph);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the buffer with SGR colour escapes. Rows are joined by <c>"\n"</c>, trailing
    /// spaces are trimmed, an SGR sequence is emitted only when the colour pair changes, and each
    /// non-empty row ends with a reset (<c>ESC[0m</c>). This is <c>--screenshot --ansi</c>.
    /// </summary>
    public string RenderAnsi() => RenderAnsi(ColorDepth.Indexed16, palette: null);

    /// <summary>
    /// Renders the buffer with SGR colour escapes at the given colour depth, so
    /// <c>--screenshot --ansi</c> can reproduce byte for byte what the live terminal is sent.
    /// In <see cref="ColorDepth.TrueColor"/> every style is resolved through
    /// <paramref name="palette"/> (<see cref="Palette.ClassicVga"/> when none is given) and written
    /// as <c>38;2;r;g;b</c>/<c>48;2;r;g;b</c>; otherwise the 16 indexed slots are used.
    /// </summary>
    public string RenderAnsi(ColorDepth depth, Palette? palette = null)
    {
        var sb = new StringBuilder(_width * _height * 2);
        for (int y = 0; y < _height; y++)
        {
            if (y > 0)
            {
                sb.Append('\n');
            }

            int end = RowEnd(y);
            if (end == 0)
            {
                continue;
            }

            int row = y * _width;
            CellStyle last = default;
            bool haveStyle = false;
            for (int x = 0; x < end; x++)
            {
                ref var cell = ref _cells[row + x];
                if (!haveStyle || cell.Style != last)
                {
                    AppendSgr(sb, cell.Style, depth, palette);
                    last = cell.Style;
                    haveStyle = true;
                }

                sb.Append(cell.Glyph);
            }

            sb.Append(Esc).Append("[0m");
        }

        return sb.ToString();
    }

    /// <summary>Index one past the last non-space cell of a row.</summary>
    private int RowEnd(int y)
    {
        int row = y * _width;
        int end = _width;
        while (end > 0 && _cells[row + end - 1].Glyph == ' ')
        {
            end--;
        }

        return end;
    }

    // ConsoleColor is ordered Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta,
    // DarkYellow, Gray, then the eight bright variants. ANSI is ordered black, red, green,
    // yellow, blue, magenta, cyan, white - hence the permutation table.
    private static readonly int[] AnsiIndex = [0, 4, 2, 6, 1, 5, 3, 7];

    /// <summary>Appends the indexed SGR escape sequence for a colour pair, e.g. <c>ESC[36;44m</c>.</summary>
    internal static void AppendSgr(StringBuilder sb, CellStyle style) =>
        AppendSgr(sb, style, ColorDepth.Indexed16, palette: null);

    /// <summary>
    /// Appends the SGR escape sequence for a colour pair at the given colour depth. Foreground and
    /// background always go out as one sequence - SGR takes a parameter list, and one escape per
    /// style change is what keeps the frame small.
    /// </summary>
    /// <remarks>
    /// The 24-bit form uses semicolons (<c>ESC[38;2;r;g;b;48;2;r;g;bm</c>), never the ITU colon
    /// form: no terminal requires colons, and every Windows console - conhost, ConEmu, mintty -
    /// accepts semicolons only.
    /// </remarks>
    internal static void AppendSgr(StringBuilder sb, CellStyle style, ColorDepth depth, Palette? palette)
    {
        if (depth == ColorDepth.TrueColor)
        {
            Palette p = palette ?? Palette.ClassicVga;
            Rgb fgRgb = p[style.Fg];
            Rgb bgRgb = p[style.Bg];

            sb.Append(Esc).Append("[38;2;")
              .Append(fgRgb.R).Append(';').Append(fgRgb.G).Append(';').Append(fgRgb.B)
              .Append(";48;2;")
              .Append(bgRgb.R).Append(';').Append(bgRgb.G).Append(';').Append(bgRgb.B)
              .Append('m');
            return;
        }

        int fg = (int)style.Fg & 0x0F;
        int bg = (int)style.Bg & 0x0F;
        int fgCode = fg < 8 ? 30 + AnsiIndex[fg] : 90 + AnsiIndex[fg - 8];
        int bgCode = bg < 8 ? 40 + AnsiIndex[bg] : 100 + AnsiIndex[bg - 8];
        sb.Append(Esc).Append('[').Append(fgCode).Append(';').Append(bgCode).Append('m');
    }

}
