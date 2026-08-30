using OpenCommander.Rendering;

namespace OpenCommander.Panels;

/// <summary>What a panel column shows.</summary>
public enum PanelColumnKind
{
    /// <summary>The entry name. The only elastic column: it absorbs whatever width is left over.</summary>
    Name,

    /// <summary>The size in bytes, or <c>Folder</c> / <c>Up</c> / <c>Symlink</c> for a directory.</summary>
    Size,

    /// <summary>The last write date, <c>MM/dd/yy</c>.</summary>
    Date,

    /// <summary>The last write time, <c>HH:mm</c>.</summary>
    Time,

    /// <summary>The attribute letters, <c>RASHD</c> with a dash for each attribute that is absent.</summary>
    Attributes,
}

/// <summary>
/// One resolved column of a panel: what it shows, which stripe it belongs to, and the cells it
/// occupies inside the panel's inner area.
/// </summary>
/// <remarks>
/// <para>
/// A panel is divided into <em>stripes</em> - the repeated column groups Far fills newspaper style -
/// and every stripe carries the same list of fields. <see cref="Stripe"/> says which group this
/// column is part of, and <see cref="X"/> is measured from the first cell inside the left frame, so
/// the screen column is <c>panel.X + 1 + column.X</c>.
/// </para>
/// <para>
/// Only <see cref="PanelColumnKind.Name"/> has a width that varies: every other kind has the fixed
/// width declared here, which is what keeps the two panels' columns lined up with each other.
/// </para>
/// </remarks>
/// <param name="Kind">What the column shows.</param>
/// <param name="Stripe">The zero-based stripe this column belongs to.</param>
/// <param name="X">The column's first cell, measured from the first cell inside the left frame.</param>
/// <param name="Width">The column's width in cells.</param>
public readonly record struct PanelColumn(PanelColumnKind Kind, int Stripe, int X, int Width)
{
    /// <summary>Width of a size column: enough for <c>"Symlink"</c> and for a nine digit byte count.</summary>
    public const int SizeWidth = 9;

    /// <summary>Width of a date column: exactly <c>"MM/dd/yy"</c>.</summary>
    public const int DateWidth = 8;

    /// <summary>Width of a time column: exactly <c>"HH:mm"</c>.</summary>
    public const int TimeWidth = 5;

    /// <summary>Width of an attributes column: exactly <c>"RASHD"</c>.</summary>
    public const int AttributesWidth = 5;

    /// <summary>The narrowest a name column is ever squeezed to.</summary>
    public const int MinNameWidth = 1;

    /// <summary>One past the column's last cell.</summary>
    public int Right => X + Width;

    /// <summary><see langword="true"/> for the elastic name column.</summary>
    public bool IsName => Kind == PanelColumnKind.Name;

    /// <summary>The caption drawn in the column title row.</summary>
    public string Header => HeaderOf(Kind);

    /// <summary>How the column's text is aligned inside its cells.</summary>
    public HAlign Align => AlignOf(Kind);

    /// <summary>The caption drawn in the column title row for a kind.</summary>
    /// <param name="kind">The column kind.</param>
    /// <returns>The caption.</returns>
    public static string HeaderOf(PanelColumnKind kind) => kind switch
    {
        PanelColumnKind.Size => "Size",
        PanelColumnKind.Date => "Date",
        PanelColumnKind.Time => "Time",
        PanelColumnKind.Attributes => "Attr",
        _ => "Name",
    };

    /// <summary>How a kind's text is aligned.</summary>
    /// <param name="kind">The column kind.</param>
    /// <returns>The alignment.</returns>
    /// <remarks>
    /// Sizes are right aligned so the digits line up. Everything else is left aligned; the date, time
    /// and attribute strings have a fixed length that exactly fills their column, so for them the
    /// choice is only visible when the column has been squeezed.
    /// </remarks>
    public static HAlign AlignOf(PanelColumnKind kind) =>
        kind == PanelColumnKind.Size ? HAlign.Right : HAlign.Left;

    /// <summary>
    /// The fixed width of a kind, or zero for <see cref="PanelColumnKind.Name"/>, which has none.
    /// </summary>
    /// <param name="kind">The column kind.</param>
    /// <returns>The width in cells.</returns>
    public static int FixedWidthOf(PanelColumnKind kind) => kind switch
    {
        PanelColumnKind.Size => SizeWidth,
        PanelColumnKind.Date => DateWidth,
        PanelColumnKind.Time => TimeWidth,
        PanelColumnKind.Attributes => AttributesWidth,
        _ => 0,
    };

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}#{Stripe} @{X}+{Width}";
}
