using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenCommander.Tests")]

namespace OpenCommander.Panels;

/// <summary>
/// The panel view modes, numbered exactly like Far's Ctrl+1..Ctrl+9 accelerators.
/// </summary>
/// <remarks>
/// Modes 6..9 have no data source yet (descriptions, owners and link counts all need information the
/// file layer does not collect), so they are laid out as <see cref="Full"/>. They are kept in the
/// enum - and accepted by every accelerator - so that the key bindings, the Left/Right menus and the
/// <c>--view</c> command line switch are complete and nothing crashes when one is selected.
/// </remarks>
public enum PanelViewMode
{
    /// <summary>Three name-only columns (Ctrl+1).</summary>
    Brief = 1,

    /// <summary>Two name-only columns (Ctrl+2). The default.</summary>
    Medium = 2,

    /// <summary>One column: name, size, date and time (Ctrl+3).</summary>
    Full = 3,

    /// <summary>Two columns of name and size (Ctrl+4).</summary>
    Wide = 4,

    /// <summary>One column: name, size, date, time and attributes (Ctrl+5).</summary>
    Detailed = 5,

    /// <summary>File descriptions (Ctrl+6). Laid out as <see cref="Full"/> for now.</summary>
    Descriptions = 6,

    /// <summary>Long file descriptions (Ctrl+7). Laid out as <see cref="Full"/> for now.</summary>
    LongDescriptions = 7,

    /// <summary>File owners (Ctrl+8). Laid out as <see cref="Full"/> for now.</summary>
    FileOwners = 8,

    /// <summary>File links (Ctrl+9). Laid out as <see cref="Full"/> for now.</summary>
    Links = 9,
}

/// <summary>Helpers for turning view mode numbers, names and fallbacks into <see cref="PanelViewMode"/> values.</summary>
public static class PanelViewModes
{
    /// <summary>The mode a freshly created panel starts in.</summary>
    public const PanelViewMode Default = PanelViewMode.Medium;

    /// <summary>The lowest accelerator number.</summary>
    public const int MinNumber = 1;

    /// <summary>The highest accelerator number.</summary>
    public const int MaxNumber = 9;

    private static readonly PanelViewMode[] AllModes =
    [
        PanelViewMode.Brief,
        PanelViewMode.Medium,
        PanelViewMode.Full,
        PanelViewMode.Wide,
        PanelViewMode.Detailed,
        PanelViewMode.Descriptions,
        PanelViewMode.LongDescriptions,
        PanelViewMode.FileOwners,
        PanelViewMode.Links,
    ];

    /// <summary>Every mode, in accelerator order.</summary>
    public static IReadOnlyList<PanelViewMode> All => AllModes;

    /// <summary>
    /// Maps an accelerator number to a mode.
    /// </summary>
    /// <param name="number">The number 1..9 as typed after Ctrl.</param>
    /// <returns>The mode, or <see cref="Default"/> when the number is out of range.</returns>
    public static PanelViewMode FromNumber(int number) =>
        number is >= MinNumber and <= MaxNumber ? (PanelViewMode)number : Default;

    /// <summary>The accelerator number of a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The number 1..9.</returns>
    public static int ToNumber(PanelViewMode mode) => (int)Normalize(mode);

    /// <summary>Clamps an out-of-range value onto a real mode.</summary>
    /// <param name="mode">The value to check.</param>
    /// <returns><paramref name="mode"/>, or <see cref="Default"/> when it is not a defined mode.</returns>
    public static PanelViewMode Normalize(PanelViewMode mode) =>
        (int)mode is >= MinNumber and <= MaxNumber ? mode : Default;

    /// <summary>
    /// The mode actually used for layout: the modes whose data source does not exist yet collapse
    /// onto <see cref="PanelViewMode.Full"/>.
    /// </summary>
    /// <param name="mode">The requested mode.</param>
    /// <returns>The mode the column layout is built from.</returns>
    public static PanelViewMode Effective(PanelViewMode mode) => Normalize(mode) switch
    {
        PanelViewMode.Descriptions or PanelViewMode.LongDescriptions or
        PanelViewMode.FileOwners or PanelViewMode.Links => PanelViewMode.Full,
        var m => m,
    };

    /// <summary>The menu caption of a mode, with its <c>'&amp;'</c> hotkey marker.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The caption, e.g. <c>"Detai&amp;led"</c>.</returns>
    public static string MenuText(PanelViewMode mode) => Normalize(mode) switch
    {
        PanelViewMode.Brief => "&Brief",
        PanelViewMode.Medium => "&Medium",
        PanelViewMode.Full => "&Full",
        PanelViewMode.Wide => "&Wide",
        PanelViewMode.Detailed => "Detai&led",
        PanelViewMode.Descriptions => "&Descriptions",
        PanelViewMode.LongDescriptions => "Lon&g descriptions",
        PanelViewMode.FileOwners => "File own&ers",
        _ => "File lin&ks",
    };

    /// <summary>The plain display name of a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The name, e.g. <c>"Detailed"</c>.</returns>
    public static string DisplayName(PanelViewMode mode) =>
        MenuText(mode).Replace("&", string.Empty, StringComparison.Ordinal);
}
