using OpenCommander.Core;
using OpenCommander.Input;

namespace OpenCommander.Ui;

/// <summary>
/// The function key captions shown while the file panels have the focus, one row per modifier
/// combination.
/// </summary>
/// <remarks>
/// <para>
/// Far stores these as eight consecutive blocks of twelve strings in its language file, one block
/// per modifier combination, and swaps the visible block as the user presses and releases Shift,
/// Ctrl and Alt. This type is that table, transcribed for the panel ("shell") area.
/// </para>
/// <para>
/// Captions are kept to at most <see cref="MaxCaptionLength"/> characters, which is what lets the
/// twelve cells stay readable on an eighty column console. Blank captions are meaningful: they mark
/// a key that does nothing in this context, and <see cref="KeyBar"/> draws such a cell completely
/// empty rather than showing a bare key number.
/// </para>
/// <para>
/// This type is the only copy of the table: the panels reach it through
/// <c>FilePanel.KeyBarFor</c>, which does nothing but call <see cref="ForPanels"/>.
/// </para>
/// <para>
/// A caption here is a promise that the chord answers, and the promise is kept in
/// <c>Core/KeyBindings.cs</c> - that is where every one of these keys is wired, including the ones
/// that currently answer with a "not implemented in this version" message. Change a caption and
/// change the binding with it, or the bar will advertise something the keyboard does not do. The
/// two are deliberately worded differently: the bar has six columns and says <c>"Tree"</c> for
/// Alt+F10, the binding has a whole message line and says "Folder tree".
/// </para>
/// </remarks>
public static class KeyBarSets
{
    /// <summary>
    /// The longest caption the table is allowed to contain. Far's own language template caps the
    /// captions at six characters; one more is allowed here so that a caption with a trailing
    /// ellipsis still fits.
    /// </summary>
    public const int MaxCaptionLength = 7;

    /// <summary>The ellipsis Far 3.0 appends to captions that open a dialog.</summary>
    private const string Dots = "…";

    /// <summary>No modifier: the row the reference screenshot shows.</summary>
    public static KeyBarLabels None { get; } = KeyBarLabels.Of(
        "Help", "UserMn", "View", "Edit", "Copy", "RenMov",
        "MkFold", "Delete", "ConfMn", "Quit", "Plugin", "Screen");

    /// <summary>Shift held: the archive and "ignore the selection" commands.</summary>
    public static KeyBarLabels Shift { get; } = KeyBarLabels.Of(
        "Add", "Extrct", "ArcCmd", "Edit" + Dots, "Copy", "Rename",
        string.Empty, "Delete", "Save", "Last", "Group", "SelUp");

    /// <summary>Ctrl held: panel visibility and the sort modes.</summary>
    public static KeyBarLabels Ctrl { get; } = KeyBarLabels.Of(
        "Left", "Right", "Name", "Extens", "Write", "Size",
        "Unsort", "Creatn", "Access", "Descr", "Owner", "Sort");

    /// <summary>Alt held: drives, the alternative viewer and editor, and the history lists.</summary>
    /// <remarks>
    /// Index 9 is Alt+F10, whose caption is <c>"Tree"</c>; <c>Core/KeyBindings.cs</c> binds it under
    /// the name "Folder tree".
    /// </remarks>
    public static KeyBarLabels Alt { get; } = KeyBarLabels.Of(
        "Left", "Right", "View" + Dots, "Edit" + Dots, "Print", "MkLink",
        "Find", "Histry", "Video", "Tree", "ViewHs", "FoldHs");

    /// <summary>Ctrl+Shift held: only the two "ignore the associations" commands.</summary>
    public static KeyBarLabels CtrlShift { get; } = KeyBarLabels.Of(
        string.Empty, string.Empty, "View", "Edit");

    /// <summary>Alt+Shift held: only the plugin configuration command.</summary>
    public static KeyBarLabels AltShift { get; } = KeyBarLabels.Of(
        string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, "ConfPl");

    /// <summary>Ctrl+Alt held: nothing is bound in the panel area.</summary>
    public static KeyBarLabels CtrlAlt { get; } = KeyBarLabels.Empty;

    /// <summary>Ctrl+Alt+Shift held: nothing is bound in the panel area.</summary>
    public static KeyBarLabels CtrlAltShift { get; } = KeyBarLabels.Empty;

    /// <summary>
    /// The captions for one modifier combination.
    /// </summary>
    /// <param name="mods">The modifiers currently held down.</param>
    /// <returns>The captions; never <see langword="null"/>.</returns>
    public static KeyBarLabels ForPanels(KeyMods mods) => mods switch
    {
        KeyMods.None => None,
        KeyMods.Shift => Shift,
        KeyMods.Ctrl => Ctrl,
        KeyMods.Alt => Alt,
        KeyMods.Ctrl | KeyMods.Shift => CtrlShift,
        KeyMods.Alt | KeyMods.Shift => AltShift,
        KeyMods.Ctrl | KeyMods.Alt => CtrlAlt,
        _ => CtrlAltShift,
    };

    /// <summary>Every row in the table, in the order <see cref="ForPanels"/> selects them.</summary>
    /// <returns>The eight modifier combinations paired with their captions.</returns>
    public static IReadOnlyList<(KeyMods Mods, KeyBarLabels Labels)> All { get; } =
    [
        (KeyMods.None, None),
        (KeyMods.Shift, Shift),
        (KeyMods.Ctrl, Ctrl),
        (KeyMods.Alt, Alt),
        (KeyMods.Ctrl | KeyMods.Shift, CtrlShift),
        (KeyMods.Alt | KeyMods.Shift, AltShift),
        (KeyMods.Ctrl | KeyMods.Alt, CtrlAlt),
        (KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift, CtrlAltShift),
    ];
}
