namespace OpenCommander.Rendering;

/// <summary>Line style used when drawing frames.</summary>
public enum BoxStyle
{
    /// <summary>No frame at all; every glyph is a space.</summary>
    None,

    /// <summary>Single line horizontally and vertically.</summary>
    Single,

    /// <summary>Double line horizontally and vertically.</summary>
    Double,

    /// <summary>Double horizontal lines, single vertical lines.</summary>
    SingleH,

    /// <summary>Single horizontal lines, double vertical lines.</summary>
    SingleV,
}

/// <summary>
/// The Unicode box drawing glyphs for each <see cref="BoxStyle"/>, plus the scroll bar glyphs.
/// All lookups are branch tables over a tiny enum, so they are effectively free.
/// </summary>
public static class BoxChars
{
    // Index order matches BoxStyle: None, Single, Double, SingleH, SingleV.
    //                                      None Single Double SingleH SingleV
    private static readonly char[] TopLeftT = [' ', '┌', '╔', '╒', '╓'];
    private static readonly char[] TopRightT = [' ', '┐', '╗', '╕', '╖'];
    private static readonly char[] BottomLeftT = [' ', '└', '╚', '╘', '╙'];
    private static readonly char[] BottomRightT = [' ', '┘', '╝', '╛', '╜'];
    private static readonly char[] HorizontalT = [' ', '─', '═', '═', '─'];
    private static readonly char[] VerticalT = [' ', '│', '║', '│', '║'];
    private static readonly char[] LeftTeeT = [' ', '├', '╠', '╞', '╟'];
    private static readonly char[] RightTeeT = [' ', '┤', '╣', '╡', '╢'];
    private static readonly char[] TopTeeT = [' ', '┬', '╦', '╤', '╥'];
    private static readonly char[] BottomTeeT = [' ', '┴', '╩', '╧', '╨'];
    private static readonly char[] CrossT = [' ', '┼', '╬', '╪', '╫'];

    private static char Pick(char[] table, BoxStyle s)
    {
        int i = (int)s;
        return (uint)i < (uint)table.Length ? table[i] : ' ';
    }

    /// <summary>Top-left corner glyph.</summary>
    public static char TopLeft(BoxStyle s) => Pick(TopLeftT, s);

    /// <summary>Top-right corner glyph.</summary>
    public static char TopRight(BoxStyle s) => Pick(TopRightT, s);

    /// <summary>Bottom-left corner glyph.</summary>
    public static char BottomLeft(BoxStyle s) => Pick(BottomLeftT, s);

    /// <summary>Bottom-right corner glyph.</summary>
    public static char BottomRight(BoxStyle s) => Pick(BottomRightT, s);

    /// <summary>Horizontal line glyph.</summary>
    public static char Horizontal(BoxStyle s) => Pick(HorizontalT, s);

    /// <summary>Vertical line glyph.</summary>
    public static char Vertical(BoxStyle s) => Pick(VerticalT, s);

    /// <summary>Tee opening to the right, used on a left frame edge (<c>├</c>).</summary>
    public static char LeftTee(BoxStyle s) => Pick(LeftTeeT, s);

    /// <summary>Tee opening to the left, used on a right frame edge (<c>┤</c>).</summary>
    public static char RightTee(BoxStyle s) => Pick(RightTeeT, s);

    /// <summary>Tee opening downwards, used on a top frame edge (<c>┬</c>).</summary>
    public static char TopTee(BoxStyle s) => Pick(TopTeeT, s);

    /// <summary>Tee opening upwards, used on a bottom frame edge (<c>┴</c>).</summary>
    public static char BottomTee(BoxStyle s) => Pick(BottomTeeT, s);

    /// <summary>Four-way crossing (<c>┼</c>).</summary>
    public static char Cross(BoxStyle s) => Pick(CrossT, s);

    /// <summary>Scroll bar trough glyph (light shade).</summary>
    public const char ScrollBarTrack = '░';

    /// <summary>Scroll bar thumb glyph (full block).</summary>
    public const char ScrollBarThumb = '█';

    /// <summary>Scroll bar "more above" arrow.</summary>
    public const char ScrollUpArrow = '▲';

    /// <summary>Scroll bar "more below" arrow.</summary>
    public const char ScrollDownArrow = '▼';
}
