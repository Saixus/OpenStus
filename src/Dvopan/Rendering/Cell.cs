namespace Dvopan.Rendering;

/// <summary>
/// One character cell of the screen: the glyph plus its colour pair.
/// Kept as a mutable struct with public fields so <see cref="ScreenBuffer"/> can update it
/// in place through a <c>ref</c> without copying, which keeps the render path allocation free.
/// </summary>
public struct Cell
{
    /// <summary>The glyph. <c>'\0'</c> is treated as a space everywhere.</summary>
    public char Ch;

    /// <summary>The colour pair the glyph is drawn with.</summary>
    public CellStyle Style;

    /// <summary>Creates a cell.</summary>
    public Cell(char ch, CellStyle style)
    {
        Ch = ch;
        Style = style;
    }

    /// <summary>The glyph normalised so that <c>'\0'</c> reads as a space.</summary>
    public readonly char Glyph => Ch == '\0' ? ' ' : Ch;

    /// <summary>Value equality on both the glyph and the style; used by the render diff.</summary>
    public readonly bool SameAs(in Cell other) => Ch == other.Ch && Style == other.Style;

    /// <inheritdoc/>
    public readonly override string ToString() => $"'{Glyph}' {Style}";
}
