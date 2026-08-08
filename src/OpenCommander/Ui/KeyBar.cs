using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Ui;

/// <summary>
/// The twelve function key cells drawn on the bottom screen row.
/// </summary>
/// <remarks>
/// <para>
/// The row is laid out demand first: every cell asks for exactly the columns its own content needs -
/// the key number plus its caption - and only what is left over is shared out as padding, one column
/// at a time to the leftmost cells. That is what keeps the two digit numbers of F10..F12 from eating
/// their captions on an eighty column console, where an even <c>width / 12</c> split leaves the last
/// three cells two columns short. The twelve cells always tile the row exactly, so no column is drawn
/// twice and none is left behind.
/// </para>
/// <para>
/// Captions are only truncated when the row genuinely cannot hold them all, and then the widest cells
/// give up columns first, so a narrow console loses a little from every long caption instead of
/// wiping out the last few.
/// </para>
/// <para>
/// Each cell is the key number in <see cref="Theme.KeyBarNum"/> immediately followed by the caption in
/// <see cref="Theme.KeyBarText"/>. The padding columns after the caption stay in
/// <see cref="Theme.KeyBarBackground"/>: they are the gap between the coloured caption blocks in Far.
/// A cell whose caption is empty is drawn entirely in the background style - an unbound key shows no
/// number.
/// </para>
/// <para>
/// <see cref="Modifiers"/> is fed from <see cref="IInputBackend.CurrentModifiers"/>, which is what
/// makes the captions change live while a modifier is merely held down.
/// </para>
/// </remarks>
public sealed class KeyBar
{
    private readonly Theme _theme;

    /// <summary>Creates a key bar drawn with <paramref name="theme"/>.</summary>
    /// <param name="theme">The palette; must not be <see langword="null"/>.</param>
    public KeyBar(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    /// <summary>The modifiers currently held down, which choose the caption row.</summary>
    public KeyMods Modifiers { get; set; } = KeyMods.None;

    /// <summary>
    /// Captions supplied by whatever owns the screen - a dialog, the viewer, the editor - or
    /// <see langword="null"/> to use the panel captions for <see cref="Modifiers"/>.
    /// </summary>
    public KeyBarLabels? Override { get; set; }

    /// <summary>The captions that would be drawn right now.</summary>
    public KeyBarLabels Current => Override ?? KeyBarSets.ForPanels(Modifiers);

    /// <summary>
    /// Draws the bar across the full width of <paramref name="buf"/> on row <paramref name="y"/>.
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
        KeyBarLabels labels = Current;

        // The whole row starts as background, so the padding columns between two caption blocks - and
        // a cell too narrow to hold anything - end up painted rather than showing the previous frame.
        buf.HLine(0, y, width, ' ', _theme.KeyBarBackground);

        Span<int> widths = stackalloc int[KeyBarLabels.KeyCount];
        Layout(labels, width, widths);

        int start = 0;
        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            int cellStart = start;
            int cellWidth = widths[i];
            start += cellWidth;

            if (cellWidth <= 0)
            {
                continue;
            }

            string caption = labels[i];
            if (string.IsNullOrEmpty(caption))
            {
                continue; // unbound key: leave the cell blank
            }

            string number = NumberOf(i);
            if (cellWidth <= number.Length)
            {
                // Pathological width: the number alone does not fit, so show what we can of it.
                buf.WriteFixed(cellStart, y, cellWidth, number, _theme.KeyBarNum);
                continue;
            }

            buf.Write(cellStart, y, number, _theme.KeyBarNum);

            int fieldStart = cellStart + number.Length;
            int fieldWidth = cellWidth - number.Length;

            if (caption.Length <= fieldWidth)
            {
                // Exactly the caption is coloured; the columns after it are the gap to the next block
                // and keep the background style the row was primed with.
                buf.Write(fieldStart, y, caption, _theme.KeyBarText);
            }
            else
            {
                buf.WriteFixed(fieldStart, y, fieldWidth, caption, _theme.KeyBarText);
            }
        }
    }

    /// <summary>
    /// Maps a screen column to the function key whose cell covers it.
    /// </summary>
    /// <param name="x">The screen column.</param>
    /// <param name="width">The width the bar was drawn at.</param>
    /// <returns>The zero-based key index (0 == F1), or <c>-1</c> when the column is off the bar.</returns>
    public int HitTest(int x, int width)
    {
        if (width <= 0 || x < 0 || x >= width)
        {
            return -1;
        }

        Span<int> widths = stackalloc int[KeyBarLabels.KeyCount];
        Layout(Current, width, widths);

        int start = 0;
        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            if (widths[i] > 0 && x >= start && x < start + widths[i])
            {
                return i;
            }

            start += widths[i];
        }

        return -1;
    }

    /// <summary>
    /// The column span of one cell of the unmodified panel row, <see cref="KeyBarSets.None"/>.
    /// </summary>
    /// <param name="index">The zero-based key index, 0 == F1.</param>
    /// <param name="width">The width the bar is drawn at.</param>
    /// <returns>The first column of the cell and its width; the width is zero when it does not fit.</returns>
    public static (int Start, int Width) CellBounds(int index, int width) =>
        CellBounds(index, width, KeyBarSets.None);

    /// <summary>
    /// The column span of one cell for a given caption row. The layout depends on the captions
    /// because each cell is sized from what it has to show.
    /// </summary>
    /// <param name="index">The zero-based key index, 0 == F1.</param>
    /// <param name="width">The width the bar is drawn at.</param>
    /// <param name="labels">The captions being drawn; <see langword="null"/> means the panel row.</param>
    /// <returns>The first column of the cell and its width; the width is zero when it does not fit.</returns>
    public static (int Start, int Width) CellBounds(int index, int width, KeyBarLabels? labels)
    {
        if ((uint)index >= KeyBarLabels.KeyCount || width <= 0)
        {
            return (0, 0);
        }

        Span<int> widths = stackalloc int[KeyBarLabels.KeyCount];
        Layout(labels ?? KeyBarSets.None, width, widths);

        int start = 0;
        for (int i = 0; i < index; i++)
        {
            start += widths[i];
        }

        return (start, widths[index]);
    }

    /// <summary>The key number drawn in a cell, e.g. <c>"1"</c> for F1 and <c>"12"</c> for F12.</summary>
    /// <param name="index">The zero-based key index, 0 == F1.</param>
    /// <returns>The number as text.</returns>
    public static string NumberOf(int index) => Numbers[Math.Clamp(index, 0, KeyBarLabels.KeyCount - 1)];

    /// <summary>The columns one cell needs to show its number and caption in full; zero when unbound.</summary>
    /// <param name="index">The zero-based key index, 0 == F1.</param>
    /// <param name="caption">The caption for that key.</param>
    /// <returns>The demanded width in columns.</returns>
    public static int DemandOf(int index, string? caption) =>
        string.IsNullOrEmpty(caption) ? 0 : NumberOf(index).Length + caption.Length;

    /// <summary>
    /// Fills <paramref name="widths"/> with the width of every cell. The widths always sum to
    /// <paramref name="width"/> exactly, which is what makes the bar tile the row.
    /// </summary>
    private static void Layout(KeyBarLabels labels, int width, Span<int> widths)
    {
        Span<int> demand = stackalloc int[KeyBarLabels.KeyCount];
        int total = 0;

        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            demand[i] = DemandOf(i, labels[i]);
            total += demand[i];
        }

        if (total > width)
        {
            Shrink(demand, width, widths);
            return;
        }

        // Everything fits: hand the spare columns out as the trailing gaps between the caption
        // blocks, evenly, with the odd column going to the leftmost cells.
        int spare = width - total;
        int gap = spare / KeyBarLabels.KeyCount;
        int extra = spare % KeyBarLabels.KeyCount;

        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            widths[i] = demand[i] + gap + (i < extra ? 1 : 0);
        }
    }

    /// <summary>
    /// Fits the demand into a row too narrow for it by capping the widest cells: every cell keeps
    /// <c>min(demand, cap)</c> columns for the largest cap that fits, and the few columns still
    /// spare go to the capped cells, leftmost first.
    /// </summary>
    private static void Shrink(ReadOnlySpan<int> demand, int width, Span<int> widths)
    {
        int max = 0;
        for (int i = 0; i < demand.Length; i++)
        {
            max = Math.Max(max, demand[i]);
        }

        // Largest cap whose total still fits. `max` itself never fits here - the caller checked.
        int lo = 0;
        int hi = max;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo + 1) / 2);
            if (TotalCappedAt(demand, mid) <= width)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        int cap = lo;
        int used = 0;
        for (int i = 0; i < KeyBarLabels.KeyCount; i++)
        {
            widths[i] = Math.Min(demand[i], cap);
            used += widths[i];
        }

        // Raising the cap by one would overflow the row, so fewer than one column per capped cell is
        // left: handing them out leftmost first still tiles the row exactly.
        int spare = width - used;
        for (int i = 0; i < KeyBarLabels.KeyCount && spare > 0; i++)
        {
            if (demand[i] > cap)
            {
                widths[i]++;
                spare--;
            }
        }
    }

    /// <summary>The row width the demand would take if no cell were allowed past <paramref name="cap"/>.</summary>
    private static int TotalCappedAt(ReadOnlySpan<int> demand, int cap)
    {
        int total = 0;
        for (int i = 0; i < demand.Length; i++)
        {
            total += Math.Min(demand[i], cap);
        }

        return total;
    }

    private static readonly string[] Numbers =
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"];
}
