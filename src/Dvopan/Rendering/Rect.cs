namespace Dvopan.Rendering;

/// <summary>
/// An axis-aligned rectangle in character cells. <see cref="Right"/> and <see cref="Bottom"/>
/// are exclusive, so a rectangle covers the columns <c>[X, Right)</c> and rows <c>[Y, Bottom)</c>.
/// </summary>
/// <param name="X">Left-most column (inclusive).</param>
/// <param name="Y">Top-most row (inclusive).</param>
/// <param name="Width">Width in cells; may be zero or negative, in which case the rectangle is empty.</param>
/// <param name="Height">Height in cells; may be zero or negative, in which case the rectangle is empty.</param>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>One past the right-most column.</summary>
    public int Right => X + Width;

    /// <summary>One past the bottom-most row.</summary>
    public int Bottom => Y + Height;

    /// <summary><see langword="true"/> when the rectangle covers no cells at all.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Tests whether the cell at <paramref name="x"/>, <paramref name="y"/> lies inside the rectangle.</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>
    /// Grows the rectangle by <paramref name="dx"/> cells on the left and right and by
    /// <paramref name="dy"/> cells on the top and bottom. Negative values shrink it.
    /// </summary>
    public Rect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + (dx * 2), Height + (dy * 2));

    /// <summary>Returns the same rectangle translated by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public Rect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Builds a rectangle from exclusive right/bottom edges.</summary>
    public static Rect FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    /// <inheritdoc/>
    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}
