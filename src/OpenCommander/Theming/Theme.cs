using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OpenCommander.Rendering;

namespace OpenCommander.Theming;

/// <summary>
/// The complete colour palette of the application. Every visual element takes its
/// <see cref="CellStyle"/> from exactly one property here, so a theme file can restyle the whole
/// UI without touching any drawing code.
/// </summary>
/// <remarks>
/// A freshly constructed <see cref="Theme"/> already holds the Far Manager default palette, which
/// means a theme file only has to specify the entries it wants to change.
/// </remarks>
public sealed class Theme
{
    /// <summary>Creates a theme pre-filled with the Far Manager default palette.</summary>
    public Theme() => ApplyFarDefault(this);

    /// <summary>Human readable theme name.</summary>
    public string Name { get; set; } = "Far Default";

    private Rendering.Palette _palette = Rendering.Palette.ClassicVga;

    /// <summary>
    /// The RGB values this theme wants behind the 16 console slots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The colour table above never changes with colour depth - it always names console slots, and
    /// a slot is a slot whether it is written as <c>SGR 96;44</c> or as
    /// <c>SGR 38;2;85;255;255;48;2;0;0;170</c>. What changes is this palette: in
    /// <see cref="ColorDepth.Indexed16"/> the terminal's own scheme decides what a slot looks like
    /// and this table is ignored, and in <see cref="ColorDepth.TrueColor"/> it is what pins the
    /// classic look regardless of how the terminal is themed. That is why there is no separate
    /// "true colour" theme: one theme, two depths, and the palette is the only variable.
    /// </para>
    /// <para>
    /// A theme file may carry its own <c>"palette"</c> block, which is how a user retheme can go
    /// all the way down to the RGB without touching their terminal settings.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A null palette was assigned.</exception>
    public Rendering.Palette Palette
    {
        get => _palette;
        set => _palette = value ?? throw new ArgumentNullException(nameof(value));
    }

    // ---- Panels ---------------------------------------------------------------------------

    /// <summary>Panel frame of the inactive panel.</summary>
    public CellStyle PanelBox { get; set; }

    /// <summary>Panel frame of the active panel.</summary>
    public CellStyle PanelBoxActive { get; set; }

    /// <summary>Path caption in the top frame of the inactive panel.</summary>
    public CellStyle PanelTitle { get; set; }

    /// <summary>Path caption in the top frame of the active panel.</summary>
    public CellStyle PanelTitleActive { get; set; }

    /// <summary>Column header captions ("Name", "Size", ...).</summary>
    public CellStyle PanelColumnTitle { get; set; }

    /// <summary>An ordinary file.</summary>
    public CellStyle PanelText { get; set; }

    /// <summary>A directory.</summary>
    public CellStyle PanelDirectory { get; set; }

    /// <summary>A hidden or system entry.</summary>
    public CellStyle PanelHidden { get; set; }

    /// <summary>An executable file.</summary>
    public CellStyle PanelExecutable { get; set; }

    /// <summary>An archive file.</summary>
    public CellStyle PanelArchive { get; set; }

    /// <summary>A file tagged with Ins.</summary>
    public CellStyle PanelSelectedFile { get; set; }

    /// <summary>The cursor bar.</summary>
    public CellStyle PanelCursor { get; set; }

    /// <summary>The cursor bar over a tagged file.</summary>
    public CellStyle PanelCursorSelected { get; set; }

    /// <summary>Panel status bar background.</summary>
    public CellStyle PanelStatus { get; set; }

    /// <summary>Panel status bar text describing the item under the cursor.</summary>
    public CellStyle PanelStatusFile { get; set; }

    /// <summary>Totals line in the bottom frame.</summary>
    public CellStyle PanelTotals { get; set; }

    /// <summary>
    /// The totals line while it shows the tagged selection - yellow in Far, so pressing Ins is
    /// visible at the bottom of the panel too.
    /// </summary>
    public CellStyle PanelSelectedTotals { get; set; }

    /// <summary>Panel scroll bar.</summary>
    public CellStyle PanelScrollBar { get; set; }

    /// <summary>Empty area below the last entry.</summary>
    public CellStyle PanelEmpty { get; set; }

    /// <summary>Drive / free space information.</summary>
    public CellStyle PanelDriveInfo { get; set; }

    /// <summary>The Alt+letter quick search box.</summary>
    public CellStyle QuickSearch { get; set; }

    // ---- Screen chrome --------------------------------------------------------------------

    /// <summary>The screen behind everything else.</summary>
    public CellStyle Desktop { get; set; }

    /// <summary>The clock in the top-right corner.</summary>
    public CellStyle Clock { get; set; }

    /// <summary>The F-number digits in the key bar.</summary>
    public CellStyle KeyBarNum { get; set; }

    /// <summary>The captions in the key bar.</summary>
    public CellStyle KeyBarText { get; set; }

    /// <summary>The gaps between key bar cells.</summary>
    public CellStyle KeyBarBackground { get; set; }

    /// <summary>The "C:\path&gt;" prompt on the command line.</summary>
    public CellStyle CommandLinePrefix { get; set; }

    /// <summary>Typed text on the command line.</summary>
    public CellStyle CommandLineText { get; set; }

    /// <summary>Selected text on the command line.</summary>
    public CellStyle CommandLineSelected { get; set; }

    // ---- Horizontal (F9) menu bar ----------------------------------------------------------

    /// <summary>Menu bar text.</summary>
    public CellStyle MenuBarText { get; set; }

    /// <summary>Menu bar hotkey letter.</summary>
    public CellStyle MenuBarHighlight { get; set; }

    /// <summary>Menu bar item under the cursor.</summary>
    public CellStyle MenuBarSelected { get; set; }

    /// <summary>Menu bar hotkey letter of the item under the cursor.</summary>
    public CellStyle MenuBarSelectedHighlight { get; set; }

    // ---- Popup / vertical menus -------------------------------------------------------------

    /// <summary>Popup menu frame.</summary>
    public CellStyle MenuBox { get; set; }

    /// <summary>Popup menu title.</summary>
    public CellStyle MenuTitle { get; set; }

    /// <summary>Popup menu item text.</summary>
    public CellStyle MenuText { get; set; }

    /// <summary>Popup menu hotkey letter.</summary>
    public CellStyle MenuHighlight { get; set; }

    /// <summary>Popup menu item under the cursor.</summary>
    public CellStyle MenuSelected { get; set; }

    /// <summary>Popup menu hotkey letter of the item under the cursor.</summary>
    public CellStyle MenuSelectedHighlight { get; set; }

    /// <summary>Disabled popup menu item.</summary>
    public CellStyle MenuDisabled { get; set; }

    /// <summary>Popup menu separator line.</summary>
    public CellStyle MenuSeparator { get; set; }

    /// <summary>Popup menu scroll bar.</summary>
    public CellStyle MenuScroll { get; set; }

    // ---- Dialogs -----------------------------------------------------------------------------

    /// <summary>Dialog frame and body.</summary>
    public CellStyle DialogBox { get; set; }

    /// <summary>Dialog title.</summary>
    public CellStyle DialogBoxTitle { get; set; }

    /// <summary>Dialog label text.</summary>
    public CellStyle DialogText { get; set; }

    /// <summary>Dialog hotkey letter.</summary>
    public CellStyle DialogHighlight { get; set; }

    /// <summary>Dialog edit field.</summary>
    public CellStyle DialogEdit { get; set; }

    /// <summary>Selected text inside a dialog edit field.</summary>
    public CellStyle DialogEditSelected { get; set; }

    /// <summary>Disabled dialog edit field.</summary>
    public CellStyle DialogEditDisabled { get; set; }

    /// <summary>Dialog button.</summary>
    public CellStyle DialogButton { get; set; }

    /// <summary>Dialog button hotkey letter.</summary>
    public CellStyle DialogButtonHighlight { get; set; }

    /// <summary>Focused dialog button.</summary>
    public CellStyle DialogButtonSelected { get; set; }

    /// <summary>Focused dialog button hotkey letter.</summary>
    public CellStyle DialogButtonSelectedHighlight { get; set; }

    /// <summary>List box item inside a dialog.</summary>
    public CellStyle DialogListText { get; set; }

    /// <summary>List box hotkey letter inside a dialog.</summary>
    public CellStyle DialogListHighlight { get; set; }

    /// <summary>List box item under the cursor inside a dialog.</summary>
    public CellStyle DialogListSelected { get; set; }

    /// <summary>List box hotkey letter of the item under the cursor inside a dialog.</summary>
    public CellStyle DialogListSelectedHighlight { get; set; }

    // ---- Warning dialogs ----------------------------------------------------------------------

    /// <summary>Warning dialog frame and body.</summary>
    public CellStyle WarnDialogBox { get; set; }

    /// <summary>Warning dialog title.</summary>
    public CellStyle WarnDialogBoxTitle { get; set; }

    /// <summary>Warning dialog label text.</summary>
    public CellStyle WarnDialogText { get; set; }

    /// <summary>Warning dialog hotkey letter.</summary>
    public CellStyle WarnDialogHighlight { get; set; }

    /// <summary>Warning dialog button.</summary>
    public CellStyle WarnDialogButton { get; set; }

    /// <summary>Warning dialog button hotkey letter.</summary>
    public CellStyle WarnDialogButtonHighlight { get; set; }

    /// <summary>Focused warning dialog button.</summary>
    public CellStyle WarnDialogButtonSelected { get; set; }

    /// <summary>Focused warning dialog button hotkey letter.</summary>
    public CellStyle WarnDialogButtonSelectedHighlight { get; set; }

    // ---- Viewer / editor ------------------------------------------------------------------------

    /// <summary>Viewer body text.</summary>
    public CellStyle ViewerText { get; set; }

    /// <summary>Selected text in the viewer.</summary>
    public CellStyle ViewerSelected { get; set; }

    /// <summary>Viewer status bar.</summary>
    public CellStyle ViewerStatus { get; set; }

    /// <summary>Viewer horizontal scroll arrows.</summary>
    public CellStyle ViewerArrows { get; set; }

    /// <summary>Editor body text.</summary>
    public CellStyle EditorText { get; set; }

    /// <summary>Selected text in the editor.</summary>
    public CellStyle EditorSelected { get; set; }

    /// <summary>Editor status bar.</summary>
    public CellStyle EditorStatus { get; set; }

    /// <summary>Editor scroll bar.</summary>
    public CellStyle EditorScroll { get; set; }

    // ---- Progress --------------------------------------------------------------------------------

    /// <summary>Filled part of a progress bar.</summary>
    public CellStyle ProgressBar { get; set; }

    /// <summary>Unfilled part of a progress bar.</summary>
    public CellStyle ProgressBarEmpty { get; set; }

    // =============================================================================================

    /// <summary>
    /// The canonical Far Manager palette over the classic DOS RGB table
    /// (<see cref="Rendering.Palette.ClassicVga"/>).
    /// </summary>
    public static Theme FarDefault() => new();

    /// <summary>
    /// The same colour table over the legacy Windows NT console RGB
    /// (<see cref="Rendering.Palette.WindowsNt"/>) - the values Far Manager itself installs into the
    /// console at startup, so this is what a classic Far screenshot is actually showing.
    /// </summary>
    /// <remarks>
    /// The only difference from <see cref="FarDefault"/> is the palette: a deeper
    /// <c>#000080</c> blue and a pure <c>#00FFFF</c> cyan instead of the DOS <c>#0000AA</c> and
    /// <c>#55FFFF</c>, which lifts the dominant panel pair from 10.84:1 to 12.77:1. Every one of the
    /// 74 entries below is identical.
    /// </remarks>
    public static Theme FarNt() => new() { Name = "Far NT", Palette = Rendering.Palette.WindowsNt };

    /// <summary>The names of the built-in themes, in the order they are offered.</summary>
    public static IReadOnlyList<string> BuiltInNames { get; } = ["Far Default", "Far NT"];

    /// <summary>
    /// Resolves a built-in theme by name, ignoring case and any separators. Accepts the display
    /// names in <see cref="BuiltInNames"/> as well as the short forms <c>far</c>, <c>default</c>,
    /// <c>nt</c> and <c>windowsnt</c>.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="name"/> named a built-in theme.</returns>
    public static bool TryGetBuiltIn(string? name, [NotNullWhen(true)] out Theme? theme)
    {
        theme = ThemePalette.Normalize(name) switch
        {
            "fardefault" or "far" or "default" or "classic" => FarDefault(),
            "farnt" or "nt" or "windowsnt" => FarNt(),
            _ => null,
        };

        return theme is not null;
    }

    /// <summary>
    /// Fills <paramref name="t"/> with Far Manager's default palette.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every entry is transcribed from <c>far/palette.cpp</c>, whose line number is quoted on the
    /// right. Far spells each default as <c>F_&lt;fg&gt;|B_&lt;bg&gt;</c>, and the two halves do
    /// <em>not</em> come from the same set of constants: <c>F_LIGHTCYAN</c> is
    /// <c>C_LIGHTCYAN</c> = index 11 = <see cref="ConsoleColor.Cyan"/>, while <c>B_CYAN</c> is
    /// <c>C_CYAN</c> = index 3 = <see cref="ConsoleColor.DarkCyan"/> (<c>far/palette.hpp</c> lines
    /// 89, 98, 120 and 129 - <c>B_LIGHTCYAN</c> exists but Far never uses it in the defaults).
    /// Collapsing the two onto one constant made every cyan bar render as <c>SGR 106</c> where Far
    /// writes <c>SGR 46</c>: too loud rather than washed out, and no palette change can fix it.
    /// The locals below keep the halves apart, which is why the background names are prefixed.
    /// </para>
    /// <para>
    /// A handful of entries have no counterpart in Far's palette at all - file-type colours come
    /// from <c>Highlight.farconfig</c>, and the panel status line, drive info and progress bars are
    /// this application's own. Those are marked and left as they were rather than invented.
    /// </para>
    /// </remarks>
    private static void ApplyFarDefault(Theme t)
    {
        // Foregrounds - Far's F_* macros.
        const ConsoleColor black = ConsoleColor.Black;          // F_BLACK      index 0
        const ConsoleColor darkGray = ConsoleColor.DarkGray;    // F_DARKGRAY   index 8
        const ConsoleColor lightGray = ConsoleColor.Gray;       // F_LIGHTGRAY  index 7
        const ConsoleColor darkCyan = ConsoleColor.DarkCyan;    // F_CYAN       index 3
        const ConsoleColor lightCyan = ConsoleColor.Cyan;       // F_LIGHTCYAN  index 11
        const ConsoleColor lightGreen = ConsoleColor.Green;     // F_LIGHTGREEN index 10
        const ConsoleColor yellow = ConsoleColor.Yellow;        // F_YELLOW     index 14
        const ConsoleColor white = ConsoleColor.White;          // F_WHITE      index 15

        // Backgrounds - Far's B_* macros. bCyan is the one that bites: index 3, not 11.
        const ConsoleColor bBlack = ConsoleColor.Black;         // B_BLACK      index 0
        const ConsoleColor bBlue = ConsoleColor.DarkBlue;       // B_BLUE       index 1
        const ConsoleColor bCyan = ConsoleColor.DarkCyan;       // B_CYAN       index 3
        const ConsoleColor bRed = ConsoleColor.DarkRed;         // B_RED        index 4
        const ConsoleColor bLightGray = ConsoleColor.Gray;      // B_LIGHTGRAY  index 7

        t.Name = "Far Default";
        t.Palette = Rendering.Palette.ClassicVga;

        // ---- Panels -------------------------------------------------------------------------
        // Far paints the frame, the column separators, the file text, the path caption, the totals
        // line and the scroll bar with ONE attribute: F_LIGHTCYAN|B_BLUE. COL_PANELBOX and
        // COL_PANELTEXT are distinct enum constants carrying the same default, so the near
        // monochrome panel is faithful rather than a bug, and the contrast has to come from the
        // palette (10.84:1 on ClassicVga) instead of from spending more colours on the frame.
        t.PanelBox = new CellStyle(lightCyan, bBlue);            // COL_PANELBOX             :132
        t.PanelBoxActive = new CellStyle(lightCyan, bBlue);      // Far has no active-frame colour
        t.PanelTitle = new CellStyle(lightCyan, bBlue);          // COL_PANELTITLE           :82
        t.PanelTitleActive = new CellStyle(black, bCyan);        // COL_PANELSELECTEDTITLE   :83
        t.PanelColumnTitle = new CellStyle(yellow, bBlue);       // COL_PANELCOLUMNTITLE     :84
        t.PanelText = new CellStyle(lightCyan, bBlue);           // COL_PANELTEXT            :76

        // File-type colours: not palette entries in Far, they come from Highlight.farconfig.
        // Directories are white, the <exec> group (*.exe, *.com, *.bat, *.cmd) light green, and
        // hidden or system entries dark cyan - which is what keeps executables distinguishable
        // from folders on a stock Far screen.
        t.PanelDirectory = new CellStyle(white, bBlue);          // Highlight.farconfig: <folder>
        t.PanelHidden = new CellStyle(darkCyan, bBlue);          // Highlight.farconfig: hidden/system
        t.PanelExecutable = new CellStyle(lightGreen, bBlue);    // Highlight.farconfig: <exec>
        t.PanelArchive = new CellStyle(ConsoleColor.Magenta, bBlue); // Highlight.farconfig: <arc>

        t.PanelSelectedFile = new CellStyle(yellow, bBlue);      // COL_PANELSELECTEDTEXT    :77
        t.PanelCursor = new CellStyle(black, bCyan);             // COL_PANELCURSOR          :80
        t.PanelCursorSelected = new CellStyle(yellow, bCyan);    // COL_PANELSELECTEDCURSOR  :81
        t.PanelStatus = new CellStyle(white, bBlue);             // no Far palette entry
        t.PanelStatusFile = new CellStyle(white, bBlue);         // no Far palette entry
        t.PanelTotals = new CellStyle(lightCyan, bBlue);         // COL_PANELTOTALINFO       :85
        t.PanelSelectedTotals = new CellStyle(yellow, bBlue);    // COL_PANELSELECTEDINFO    :86
        t.PanelScrollBar = new CellStyle(lightCyan, bBlue);      // COL_PANELSCROLLBAR       :130
        t.PanelEmpty = new CellStyle(lightCyan, bBlue);          // the panel's own ground = COL_PANELTEXT
        t.PanelDriveInfo = new CellStyle(white, bBlue);          // no Far palette entry
        t.QuickSearch = new CellStyle(black, bCyan);             // no Far entry; follows the B_CYAN idiom

        // ---- Screen chrome ------------------------------------------------------------------
        // COL_COMMANDLINE and COL_COMMANDLINEPREFIX are the sentinel ColorsInit::Default (:114,
        // :140), which resolves to colors::default_color(): Far deliberately inherits the
        // terminal's own default pair there so the command line matches the shell it runs. A
        // CellStyle cannot say "inherit", and this application's stand-in for an untouched console
        // is CellStyle.Default - which is also what Desktop paints, so the command line now sits
        // flush on its backdrop instead of floating on a blue strip.
        t.Desktop = CellStyle.Default;                           // no Far entry; the user screen
        t.Clock = new CellStyle(black, bCyan);                   // COL_CLOCK                :115
        t.KeyBarNum = new CellStyle(lightGray, bBlack);          // COL_KEYBARNUM            :111
        t.KeyBarText = new CellStyle(black, bCyan);              // COL_KEYBARTEXT           :112
        t.KeyBarBackground = new CellStyle(lightGray, bBlack);   // COL_KEYBARBACKGROUND     :113
        // Far's COL_COMMANDLINE is the ColorsInit::Default sentinel, i.e. "inherit the console's
        // own default pair". On a real Far session that pair resolves to the blue backdrop Far put
        // there, which is why the command line sits on the same blue as the panels in every Far
        // screenshot - so blue is what faithfulness means here, not the black of a bare console.
        // Desktop below stays CellStyle.Default: that one really is the untouched user screen.
        t.CommandLinePrefix = new CellStyle(lightGray, bBlue);   // COL_COMMANDLINEPREFIX    :140
        t.CommandLineText = new CellStyle(lightGray, bBlue);     // COL_COMMANDLINE          :114
        t.CommandLineSelected = new CellStyle(black, bCyan);     // COL_COMMANDLINESELECTED  :135

        // ---- Horizontal (F9) menu bar - Far's HMenu.* ---------------------------------------
        t.MenuBarText = new CellStyle(black, bCyan);             // COL_HMENUTEXT            :72
        t.MenuBarHighlight = new CellStyle(yellow, bCyan);       // COL_HMENUHIGHLIGHT       :74
        t.MenuBarSelected = new CellStyle(white, bBlack);        // COL_HMENUSELECTEDTEXT    :73
        t.MenuBarSelectedHighlight = new CellStyle(yellow, bBlack); // COL_HMENUSELECTEDHIGHLIGHT :75

        // ---- Popup menus - Far's Menu.*. These are WHITE on dark cyan, not black. ------------
        t.MenuBox = new CellStyle(white, bCyan);                 // COL_MENUBOX              :70
        t.MenuTitle = new CellStyle(white, bCyan);               // COL_MENUTITLE            :71
        t.MenuText = new CellStyle(white, bCyan);                // COL_MENUTEXT             :66
        t.MenuHighlight = new CellStyle(yellow, bCyan);          // COL_MENUHIGHLIGHT        :68
        t.MenuSelected = new CellStyle(white, bBlack);           // COL_MENUSELECTEDTEXT     :67
        t.MenuSelectedHighlight = new CellStyle(yellow, bBlack); // COL_MENUSELECTEDHIGHLIGHT :69
        t.MenuDisabled = new CellStyle(darkGray, bCyan);         // COL_MENUDISABLEDTEXT     :147
        t.MenuSeparator = new CellStyle(white, bCyan);           // no entry; drawn with COL_MENUBOX
        t.MenuScroll = new CellStyle(white, bCyan);              // COL_MENUSCROLLBAR        :138

        // ---- Dialogs ------------------------------------------------------------------------
        t.DialogBox = new CellStyle(black, bLightGray);          // COL_DIALOGBOX            :89
        t.DialogBoxTitle = new CellStyle(black, bLightGray);     // COL_DIALOGBOXTITLE       :90
        t.DialogText = new CellStyle(black, bLightGray);         // COL_DIALOGTEXT           :87
        t.DialogHighlight = new CellStyle(yellow, bLightGray);   // COL_DIALOGHIGHLIGHTTEXT  :88
        t.DialogEdit = new CellStyle(black, bCyan);              // COL_DIALOGEDIT           :92
        t.DialogEditSelected = new CellStyle(white, bBlack);     // COL_DIALOGEDITSELECTED   :134
        t.DialogEditDisabled = new CellStyle(darkGray, bCyan);   // COL_DIALOGEDITDISABLED   :142
        t.DialogButton = new CellStyle(black, bLightGray);       // COL_DIALOGBUTTON         :93
        t.DialogButtonHighlight = new CellStyle(yellow, bLightGray);        // COL_DIALOGHIGHLIGHTBUTTON :95
        t.DialogButtonSelected = new CellStyle(black, bCyan);               // COL_DIALOGSELECTEDBUTTON  :94
        t.DialogButtonSelectedHighlight = new CellStyle(yellow, bCyan);     // COL_DIALOGHIGHLIGHTSELECTEDBUTTON :96
        t.DialogListText = new CellStyle(black, bLightGray);                // COL_DIALOGLISTTEXT        :97
        t.DialogListHighlight = new CellStyle(yellow, bLightGray);          // COL_DIALOGLISTHIGHLIGHT   :99
        t.DialogListSelected = new CellStyle(white, bBlack);                // COL_DIALOGLISTSELECTEDTEXT :98
        t.DialogListSelectedHighlight = new CellStyle(yellow, bBlack);      // COL_DIALOGLISTSELECTEDHIGHLIGHT :100

        // ---- Warning dialogs ----------------------------------------------------------------
        t.WarnDialogBox = new CellStyle(white, bRed);            // COL_WARNDIALOGBOX        :103
        t.WarnDialogBoxTitle = new CellStyle(white, bRed);       // COL_WARNDIALOGBOXTITLE   :104
        t.WarnDialogText = new CellStyle(white, bRed);           // COL_WARNDIALOGTEXT       :101
        t.WarnDialogHighlight = new CellStyle(yellow, bRed);     // COL_WARNDIALOGHIGHLIGHTTEXT :102
        t.WarnDialogButton = new CellStyle(white, bRed);         // COL_WARNDIALOGBUTTON     :107
        t.WarnDialogButtonHighlight = new CellStyle(yellow, bRed);          // COL_WARNDIALOGHIGHLIGHTBUTTON :109
        t.WarnDialogButtonSelected = new CellStyle(black, bLightGray);      // COL_WARNDIALOGSELECTEDBUTTON  :108
        t.WarnDialogButtonSelectedHighlight = new CellStyle(yellow, bLightGray); // COL_WARNDIALOGHIGHLIGHTSELECTEDBUTTON :110

        // ---- Viewer / editor ----------------------------------------------------------------
        t.ViewerText = new CellStyle(lightCyan, bBlue);          // COL_VIEWERTEXT           :116
        t.ViewerSelected = new CellStyle(black, bCyan);          // COL_VIEWERSELECTEDTEXT   :117
        t.ViewerStatus = new CellStyle(black, bCyan);            // COL_VIEWERSTATUS         :118
        t.ViewerArrows = new CellStyle(yellow, bBlue);           // COL_VIEWERARROWS         :136
        t.EditorText = new CellStyle(lightCyan, bBlue);          // COL_EDITORTEXT           :119
        t.EditorSelected = new CellStyle(black, bCyan);          // COL_EDITORSELECTEDTEXT   :120
        t.EditorStatus = new CellStyle(black, bCyan);            // COL_EDITORSTATUS         :121
        t.EditorScroll = new CellStyle(lightCyan, bBlue);        // COL_EDITORSCROLLBAR      :193

        // ---- Progress. No Far counterpart: Far draws copy progress with dialog colours. ------
        t.ProgressBar = new CellStyle(lightCyan, bBlue);         // no Far palette entry
        t.ProgressBarEmpty = new CellStyle(darkGray, bBlue);     // no Far palette entry
    }

    // =============================================================================================

    /// <summary>One addressable palette entry: its name plus a getter and a setter.</summary>
    internal readonly record struct Slot(string Name, Func<Theme, CellStyle> Get, Action<Theme, CellStyle> Set);

    /// <summary>Every palette entry, in the order themes are written to disk.</summary>
    internal static IReadOnlyList<Slot> Slots => AllSlots;

    private static readonly Slot[] AllSlots =
    [
        new("PanelBox", t => t.PanelBox, (t, v) => t.PanelBox = v),
        new("PanelBoxActive", t => t.PanelBoxActive, (t, v) => t.PanelBoxActive = v),
        new("PanelTitle", t => t.PanelTitle, (t, v) => t.PanelTitle = v),
        new("PanelTitleActive", t => t.PanelTitleActive, (t, v) => t.PanelTitleActive = v),
        new("PanelColumnTitle", t => t.PanelColumnTitle, (t, v) => t.PanelColumnTitle = v),
        new("PanelText", t => t.PanelText, (t, v) => t.PanelText = v),
        new("PanelDirectory", t => t.PanelDirectory, (t, v) => t.PanelDirectory = v),
        new("PanelHidden", t => t.PanelHidden, (t, v) => t.PanelHidden = v),
        new("PanelExecutable", t => t.PanelExecutable, (t, v) => t.PanelExecutable = v),
        new("PanelArchive", t => t.PanelArchive, (t, v) => t.PanelArchive = v),
        new("PanelSelectedFile", t => t.PanelSelectedFile, (t, v) => t.PanelSelectedFile = v),
        new("PanelCursor", t => t.PanelCursor, (t, v) => t.PanelCursor = v),
        new("PanelCursorSelected", t => t.PanelCursorSelected, (t, v) => t.PanelCursorSelected = v),
        new("PanelStatus", t => t.PanelStatus, (t, v) => t.PanelStatus = v),
        new("PanelStatusFile", t => t.PanelStatusFile, (t, v) => t.PanelStatusFile = v),
        new("PanelTotals", t => t.PanelTotals, (t, v) => t.PanelTotals = v),
        new("PanelSelectedTotals", t => t.PanelSelectedTotals, (t, v) => t.PanelSelectedTotals = v),
        new("PanelScrollBar", t => t.PanelScrollBar, (t, v) => t.PanelScrollBar = v),
        new("PanelEmpty", t => t.PanelEmpty, (t, v) => t.PanelEmpty = v),
        new("PanelDriveInfo", t => t.PanelDriveInfo, (t, v) => t.PanelDriveInfo = v),
        new("QuickSearch", t => t.QuickSearch, (t, v) => t.QuickSearch = v),

        new("Desktop", t => t.Desktop, (t, v) => t.Desktop = v),
        new("Clock", t => t.Clock, (t, v) => t.Clock = v),
        new("KeyBarNum", t => t.KeyBarNum, (t, v) => t.KeyBarNum = v),
        new("KeyBarText", t => t.KeyBarText, (t, v) => t.KeyBarText = v),
        new("KeyBarBackground", t => t.KeyBarBackground, (t, v) => t.KeyBarBackground = v),
        new("CommandLinePrefix", t => t.CommandLinePrefix, (t, v) => t.CommandLinePrefix = v),
        new("CommandLineText", t => t.CommandLineText, (t, v) => t.CommandLineText = v),
        new("CommandLineSelected", t => t.CommandLineSelected, (t, v) => t.CommandLineSelected = v),

        new("MenuBarText", t => t.MenuBarText, (t, v) => t.MenuBarText = v),
        new("MenuBarHighlight", t => t.MenuBarHighlight, (t, v) => t.MenuBarHighlight = v),
        new("MenuBarSelected", t => t.MenuBarSelected, (t, v) => t.MenuBarSelected = v),
        new("MenuBarSelectedHighlight", t => t.MenuBarSelectedHighlight, (t, v) => t.MenuBarSelectedHighlight = v),

        new("MenuBox", t => t.MenuBox, (t, v) => t.MenuBox = v),
        new("MenuTitle", t => t.MenuTitle, (t, v) => t.MenuTitle = v),
        new("MenuText", t => t.MenuText, (t, v) => t.MenuText = v),
        new("MenuHighlight", t => t.MenuHighlight, (t, v) => t.MenuHighlight = v),
        new("MenuSelected", t => t.MenuSelected, (t, v) => t.MenuSelected = v),
        new("MenuSelectedHighlight", t => t.MenuSelectedHighlight, (t, v) => t.MenuSelectedHighlight = v),
        new("MenuDisabled", t => t.MenuDisabled, (t, v) => t.MenuDisabled = v),
        new("MenuSeparator", t => t.MenuSeparator, (t, v) => t.MenuSeparator = v),
        new("MenuScroll", t => t.MenuScroll, (t, v) => t.MenuScroll = v),

        new("DialogBox", t => t.DialogBox, (t, v) => t.DialogBox = v),
        new("DialogBoxTitle", t => t.DialogBoxTitle, (t, v) => t.DialogBoxTitle = v),
        new("DialogText", t => t.DialogText, (t, v) => t.DialogText = v),
        new("DialogHighlight", t => t.DialogHighlight, (t, v) => t.DialogHighlight = v),
        new("DialogEdit", t => t.DialogEdit, (t, v) => t.DialogEdit = v),
        new("DialogEditSelected", t => t.DialogEditSelected, (t, v) => t.DialogEditSelected = v),
        new("DialogEditDisabled", t => t.DialogEditDisabled, (t, v) => t.DialogEditDisabled = v),
        new("DialogButton", t => t.DialogButton, (t, v) => t.DialogButton = v),
        new("DialogButtonHighlight", t => t.DialogButtonHighlight, (t, v) => t.DialogButtonHighlight = v),
        new("DialogButtonSelected", t => t.DialogButtonSelected, (t, v) => t.DialogButtonSelected = v),
        new("DialogButtonSelectedHighlight", t => t.DialogButtonSelectedHighlight, (t, v) => t.DialogButtonSelectedHighlight = v),
        new("DialogListText", t => t.DialogListText, (t, v) => t.DialogListText = v),
        new("DialogListHighlight", t => t.DialogListHighlight, (t, v) => t.DialogListHighlight = v),
        new("DialogListSelected", t => t.DialogListSelected, (t, v) => t.DialogListSelected = v),
        new("DialogListSelectedHighlight", t => t.DialogListSelectedHighlight, (t, v) => t.DialogListSelectedHighlight = v),

        new("WarnDialogBox", t => t.WarnDialogBox, (t, v) => t.WarnDialogBox = v),
        new("WarnDialogBoxTitle", t => t.WarnDialogBoxTitle, (t, v) => t.WarnDialogBoxTitle = v),
        new("WarnDialogText", t => t.WarnDialogText, (t, v) => t.WarnDialogText = v),
        new("WarnDialogHighlight", t => t.WarnDialogHighlight, (t, v) => t.WarnDialogHighlight = v),
        new("WarnDialogButton", t => t.WarnDialogButton, (t, v) => t.WarnDialogButton = v),
        new("WarnDialogButtonHighlight", t => t.WarnDialogButtonHighlight, (t, v) => t.WarnDialogButtonHighlight = v),
        new("WarnDialogButtonSelected", t => t.WarnDialogButtonSelected, (t, v) => t.WarnDialogButtonSelected = v),
        new("WarnDialogButtonSelectedHighlight", t => t.WarnDialogButtonSelectedHighlight, (t, v) => t.WarnDialogButtonSelectedHighlight = v),

        new("ViewerText", t => t.ViewerText, (t, v) => t.ViewerText = v),
        new("ViewerSelected", t => t.ViewerSelected, (t, v) => t.ViewerSelected = v),
        new("ViewerStatus", t => t.ViewerStatus, (t, v) => t.ViewerStatus = v),
        new("ViewerArrows", t => t.ViewerArrows, (t, v) => t.ViewerArrows = v),
        new("EditorText", t => t.EditorText, (t, v) => t.EditorText = v),
        new("EditorSelected", t => t.EditorSelected, (t, v) => t.EditorSelected = v),
        new("EditorStatus", t => t.EditorStatus, (t, v) => t.EditorStatus = v),
        new("EditorScroll", t => t.EditorScroll, (t, v) => t.EditorScroll = v),

        new("ProgressBar", t => t.ProgressBar, (t, v) => t.ProgressBar = v),
        new("ProgressBarEmpty", t => t.ProgressBarEmpty, (t, v) => t.ProgressBarEmpty = v),
    ];

    private static readonly Dictionary<string, Slot> SlotsByName = BuildIndex();

    private static Dictionary<string, Slot> BuildIndex()
    {
        var map = new Dictionary<string, Slot>(AllSlots.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var slot in AllSlots)
        {
            map[slot.Name] = slot;

            // Also accept the dotted Far spelling, e.g. "Panel.Title.Selected" style keys with
            // separators stripped, so hand-written files are forgiving.
            map[slot.Name.Replace(".", string.Empty, StringComparison.Ordinal)] = slot;
        }

        return map;
    }

    /// <summary>Looks up a palette entry by name, ignoring case and any <c>.</c>/<c>_</c>/<c>-</c> separators.</summary>
    public bool TrySetByName(string name, CellStyle style)
    {
        if (TryFindSlot(name, out var slot))
        {
            slot.Set(this, style);
            return true;
        }

        return false;
    }

    /// <summary>Reads a palette entry by name, ignoring case and separators.</summary>
    public bool TryGetByName(string name, out CellStyle style)
    {
        if (TryFindSlot(name, out var slot))
        {
            style = slot.Get(this);
            return true;
        }

        style = CellStyle.Default;
        return false;
    }

    private static bool TryFindSlot(string name, out Slot slot)
    {
        if (SlotsByName.TryGetValue(name, out slot))
        {
            return true;
        }

        string cleaned = name
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return SlotsByName.TryGetValue(cleaned, out slot);
    }

    // =============================================================================================

    /// <summary>
    /// Loads a theme from a JSON file. Missing entries keep their Far default value and unknown
    /// entries are ignored, so a partial or slightly out-of-date file still loads cleanly.
    /// Throws only when the file cannot be read or is not valid JSON.
    /// </summary>
    /// <remarks>
    /// An optional <c>"palette"</c> block sets <see cref="Palette"/>. A file written before that
    /// block existed simply keeps <see cref="Rendering.Palette.ClassicVga"/>, so old themes load
    /// unchanged.
    /// </remarks>
    public static Theme LoadFromJson(string path)
    {
        string json = File.ReadAllText(path);
        var theme = FarDefault();

        var file = JsonSerializer.Deserialize(json, ThemeJsonContext.Default.ThemeFile);
        if (file is null)
        {
            return theme;
        }

        if (!string.IsNullOrWhiteSpace(file.Name))
        {
            theme.Name = file.Name.Trim();
        }
        else
        {
            theme.Name = Path.GetFileNameWithoutExtension(path);
        }

        // The flat form first, so an explicit "colors" block wins over a stray root-level key.
        Apply(theme, file.Extra);
        Apply(theme, file.Colors);

        if (file.Palette is { } block && ThemePalette.TryParse(block, theme.Palette, out var palette))
        {
            theme.Palette = palette;
        }

        return theme;
    }

    private static void Apply(Theme theme, Dictionary<string, JsonElement>? entries)
    {
        if (entries is null)
        {
            return;
        }

        foreach (var (key, value) in entries)
        {
            if (!TryFindSlot(key, out var slot))
            {
                continue;
            }

            if (ThemeColor.TryParseStyle(value, out var style))
            {
                slot.Set(theme, style);
            }
        }
    }

    /// <summary>
    /// Writes the complete palette to a JSON file, creating the directory if needed. The
    /// <c>"palette"</c> block is written in the same shape a standalone palette file uses, so it
    /// can be moved between the two by copy and paste.
    /// </summary>
    public void SaveToJson(string path)
    {
        var colors = new Dictionary<string, string>(AllSlots.Length, StringComparer.Ordinal);
        foreach (var slot in AllSlots)
        {
            colors[slot.Name] = ThemeColor.Format(slot.Get(this));
        }

        var rgb = new Dictionary<string, string>(Rendering.Palette.Size, StringComparer.Ordinal);
        for (int i = 0; i < Rendering.Palette.Size; i++)
        {
            rgb[((ConsoleColor)i).ToString()] = Palette[i].ToHex();
        }

        var dto = new ThemeFileOut
        {
            Name = Name,
            Colors = colors,
            Palette = new ThemePaletteOut { Name = Palette.Name, Colors = rgb },
        };

        string json = JsonSerializer.Serialize(dto, ThemeJsonContext.Default.ThemeFileOut);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads <paramref name="path"/> when it points at a readable theme file, resolves it as a
    /// built-in theme name when it does not, and falls back to <see cref="FarDefault"/> for
    /// anything else. Never throws.
    /// </summary>
    public static Theme LoadOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FarDefault();
        }

        try
        {
            if (File.Exists(path))
            {
                return LoadFromJson(path);
            }
        }
        catch
        {
            return FarDefault();
        }

        // Not a file - a bare built-in name is worth honouring before giving up on it.
        return TryGetBuiltIn(path, out var builtIn) ? builtIn : FarDefault();
    }
}
