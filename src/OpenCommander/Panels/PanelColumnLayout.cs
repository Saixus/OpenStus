namespace OpenCommander.Panels;

/// <summary>
/// The resolved column geometry of one panel: how many stripes it has, how wide each of them is,
/// where every column starts, and where the vertical separators go.
/// </summary>
/// <remarks>
/// <para>
/// All coordinates are relative to the panel's <em>inner area</em> - the cells between the left and
/// right frame columns - so the screen column of a cell at offset <c>o</c> is
/// <c>panel.X + 1 + o</c> and offsets run from <c>0</c> to <see cref="InnerWidth"/> - 1.
/// </para>
/// <para>
/// The width arithmetic is: subtract the <c>stripes - 1</c> separator cells that sit between the
/// stripes, divide what is left evenly, and hand the remainder to the leftmost stripes one cell each.
/// Inside a stripe the fixed width fields are laid out from the right, each preceded by its own
/// separator cell, and the name column absorbs everything that is left. When a stripe is too narrow
/// to carry all the fields, fields are dropped from the right - the same fields in every stripe, so
/// the column titles keep lining up - until the name column has at least
/// <see cref="PanelColumn.MinNameWidth"/> cell.
/// </para>
/// <para>
/// Instances are immutable and are cached by the panel for as long as its width and view mode hold
/// still, which keeps the per-frame render path allocation free.
/// </para>
/// </remarks>
public sealed class PanelColumnLayout
{
    private static readonly PanelColumnKind[] NameOnly = [PanelColumnKind.Name];

    private static readonly PanelColumnKind[] NameAndSize =
        [PanelColumnKind.Name, PanelColumnKind.Size];

    private static readonly PanelColumnKind[] FullFields =
        [PanelColumnKind.Name, PanelColumnKind.Size, PanelColumnKind.Date, PanelColumnKind.Time];

    private static readonly PanelColumnKind[] DetailedFields =
    [
        PanelColumnKind.Name,
        PanelColumnKind.Size,
        PanelColumnKind.Date,
        PanelColumnKind.Time,
        PanelColumnKind.Attributes,
    ];

    private readonly PanelColumn[] _columns;
    private readonly int[] _separators;
    private readonly int[] _stripeStarts;
    private readonly int[] _stripeWidths;
    private readonly PanelColumnKind[] _fields;
    private readonly int[] _offsets;

    private PanelColumnLayout(
        PanelViewMode requested,
        PanelViewMode effective,
        int innerWidth,
        PanelColumnKind[] fields,
        int[] stripeStarts,
        int[] stripeWidths,
        PanelColumn[] columns,
        int[] separators)
    {
        Mode = requested;
        EffectiveMode = effective;
        InnerWidth = innerWidth;
        _fields = fields;
        _stripeStarts = stripeStarts;
        _stripeWidths = stripeWidths;
        _columns = columns;
        _separators = separators;
        _offsets = Array.ConvertAll(columns, static c => c.X);
    }

    /// <summary>The view mode this layout was asked for.</summary>
    public PanelViewMode Mode { get; }

    /// <summary>The view mode the geometry actually came from; see <see cref="PanelViewModes.Effective"/>.</summary>
    public PanelViewMode EffectiveMode { get; }

    /// <summary>The width of the panel's inner area, frame columns excluded.</summary>
    public int InnerWidth { get; }

    /// <summary>How many repeated column groups the panel is divided into.</summary>
    public int Stripes => _stripeWidths.Length;

    /// <summary>The field list every stripe repeats, left to right, starting with the name.</summary>
    public IReadOnlyList<PanelColumnKind> Fields => _fields;

    /// <summary>How many columns each stripe carries.</summary>
    public int FieldsPerStripe => _fields.Length;

    /// <summary>Every column, ordered left to right across all stripes.</summary>
    public IReadOnlyList<PanelColumn> Columns => _columns;

    /// <summary>The offsets of the separator cells, ascending. Includes both kinds of separator.</summary>
    public IReadOnlyList<int> Separators => _separators;

    /// <summary>The starting offset of every column, ordered exactly like <see cref="Columns"/>.</summary>
    public IReadOnlyList<int> Offsets => _offsets;

    /// <summary><see langword="true"/> when the panel is too narrow to show anything at all.</summary>
    public bool IsEmpty => _columns.Length == 0;

    /// <summary>The first offset of a stripe.</summary>
    /// <param name="stripe">The zero-based stripe index; out-of-range values are clamped.</param>
    /// <returns>The offset.</returns>
    public int StripeStart(int stripe) =>
        _stripeStarts.Length == 0 ? 0 : _stripeStarts[Math.Clamp(stripe, 0, _stripeStarts.Length - 1)];

    /// <summary>The width of a stripe, its internal separators included.</summary>
    /// <param name="stripe">The zero-based stripe index; out-of-range values are clamped.</param>
    /// <returns>The width in cells.</returns>
    public int StripeWidth(int stripe) =>
        _stripeWidths.Length == 0 ? 0 : _stripeWidths[Math.Clamp(stripe, 0, _stripeWidths.Length - 1)];

    /// <summary>One column of one stripe.</summary>
    /// <param name="stripe">The zero-based stripe index.</param>
    /// <param name="field">The zero-based field index inside the stripe; 0 is always the name.</param>
    /// <returns>The column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either index is outside the layout.</exception>
    public PanelColumn Column(int stripe, int field)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stripe);
        ArgumentOutOfRangeException.ThrowIfNegative(field);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stripe, Stripes);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(field, FieldsPerStripe);
        return _columns[(stripe * FieldsPerStripe) + field];
    }

    /// <summary>The columns of one stripe, left to right.</summary>
    /// <param name="stripe">The zero-based stripe index.</param>
    /// <returns>The stripe's columns; empty when the index is out of range.</returns>
    public IEnumerable<PanelColumn> StripeColumns(int stripe)
    {
        if ((uint)stripe >= (uint)Stripes)
        {
            yield break;
        }

        int first = stripe * FieldsPerStripe;
        for (int i = 0; i < FieldsPerStripe; i++)
        {
            yield return _columns[first + i];
        }
    }

    /// <summary>
    /// The separator offsets that sit <em>inside</em> one stripe, between its fields. These are the
    /// ones the cursor bar recolours; the separators between stripes keep the frame colour.
    /// </summary>
    /// <param name="stripe">The zero-based stripe index.</param>
    /// <returns>The offsets, ascending.</returns>
    public IEnumerable<int> IntraSeparators(int stripe)
    {
        if ((uint)stripe >= (uint)Stripes)
        {
            yield break;
        }

        int first = stripe * FieldsPerStripe;
        for (int i = 1; i < FieldsPerStripe; i++)
        {
            yield return _columns[first + i].X - 1;
        }
    }

    /// <summary>
    /// The stripe an offset falls in.
    /// </summary>
    /// <param name="offset">An offset inside the panel's inner area.</param>
    /// <returns>The zero-based stripe index, or <c>-1</c> when the offset lands on a separator or outside.</returns>
    public int StripeAt(int offset)
    {
        for (int s = 0; s < Stripes; s++)
        {
            if (offset >= _stripeStarts[s] && offset < _stripeStarts[s] + _stripeWidths[s])
            {
                return s;
            }
        }

        return -1;
    }

    /// <summary>How many stripes a view mode asks for, before any narrowing.</summary>
    /// <param name="mode">The view mode.</param>
    /// <returns>The stripe count.</returns>
    /// <remarks>
    /// Wide is a single stripe: Far's mode 4 is called Wide because its <em>name column</em> is
    /// wide - one column of names with a size beside it - not because the panel repeats it.
    /// </remarks>
    public static int StripesOf(PanelViewMode mode) => PanelViewModes.Effective(mode) switch
    {
        PanelViewMode.Brief => 3,
        PanelViewMode.Medium => 2,
        _ => 1,
    };

    /// <summary>The fields a view mode repeats in every stripe.</summary>
    /// <param name="mode">The view mode.</param>
    /// <returns>The field kinds, starting with the name.</returns>
    public static IReadOnlyList<PanelColumnKind> FieldsOf(PanelViewMode mode) =>
        FieldTable(PanelViewModes.Effective(mode));

    /// <summary>
    /// Computes the geometry for a mode at a given inner width.
    /// </summary>
    /// <param name="mode">The view mode.</param>
    /// <param name="innerWidth">The panel width minus its two frame columns.</param>
    /// <returns>The layout; never <see langword="null"/>.</returns>
    public static PanelColumnLayout Compute(PanelViewMode mode, int innerWidth)
    {
        PanelViewMode effective = PanelViewModes.Effective(mode);
        int inner = Math.Max(0, innerWidth);

        if (inner == 0)
        {
            return new PanelColumnLayout(mode, effective, 0, NameOnly, [], [], [], []);
        }

        // Every stripe needs at least one cell of name, plus one separator between stripes.
        int stripes = StripesOf(effective);
        while (stripes > 1 && inner - (stripes - 1) < stripes * PanelColumn.MinNameWidth)
        {
            stripes--;
        }

        int content = inner - (stripes - 1);
        int baseWidth = content / stripes;
        int remainder = content % stripes;

        var stripeWidths = new int[stripes];
        var stripeStarts = new int[stripes];
        int cursor = 0;
        for (int s = 0; s < stripes; s++)
        {
            stripeWidths[s] = baseWidth + (s < remainder ? 1 : 0);
            stripeStarts[s] = cursor;
            cursor += stripeWidths[s] + 1; // + the separator that follows this stripe
        }

        // The field list has to be the same in every stripe or the column titles stop lining up, so
        // it is trimmed against the narrowest stripe - which, with the remainder handed out on the
        // left, is always the last one.
        PanelColumnKind[] fields = TrimFields(FieldTable(effective), stripeWidths[stripes - 1]);

        int fieldsPerStripe = fields.Length;
        var columns = new PanelColumn[stripes * fieldsPerStripe];
        var separators = new List<int>(stripes * fieldsPerStripe);

        for (int s = 0; s < stripes; s++)
        {
            if (s > 0)
            {
                separators.Add(stripeStarts[s] - 1);
            }

            int fixedTotal = 0;
            for (int f = 1; f < fieldsPerStripe; f++)
            {
                fixedTotal += PanelColumn.FixedWidthOf(fields[f]) + 1; // the field and its separator
            }

            int nameWidth = Math.Max(PanelColumn.MinNameWidth, stripeWidths[s] - fixedTotal);
            int x = stripeStarts[s];

            columns[s * fieldsPerStripe] = new PanelColumn(PanelColumnKind.Name, s, x, nameWidth);
            x += nameWidth;

            for (int f = 1; f < fieldsPerStripe; f++)
            {
                separators.Add(x);
                x++;
                int width = PanelColumn.FixedWidthOf(fields[f]);
                columns[(s * fieldsPerStripe) + f] = new PanelColumn(fields[f], s, x, width);
                x += width;
            }
        }

        separators.Sort();
        return new PanelColumnLayout(
            mode,
            effective,
            inner,
            fields,
            stripeStarts,
            stripeWidths,
            columns,
            [.. separators]);
    }

    private static PanelColumnKind[] FieldTable(PanelViewMode effective) => effective switch
    {
        PanelViewMode.Wide => NameAndSize,
        PanelViewMode.Full => FullFields,
        PanelViewMode.Detailed => DetailedFields,
        _ => NameOnly,
    };

    private static PanelColumnKind[] TrimFields(PanelColumnKind[] wanted, int stripeWidth)
    {
        int count = wanted.Length;
        while (count > 1 && Needed(wanted, count) > stripeWidth)
        {
            count--;
        }

        return count == wanted.Length ? wanted : wanted[..count];
    }

    private static int Needed(PanelColumnKind[] fields, int count)
    {
        int total = PanelColumn.MinNameWidth;
        for (int f = 1; f < count; f++)
        {
            total += PanelColumn.FixedWidthOf(fields[f]) + 1;
        }

        return total;
    }
}
