using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Dvopan.Rendering;

namespace Dvopan.Theming;

/// <summary>
/// The complete colour palette of the application. Every visual element takes its
/// <see cref="CellStyle"/> from exactly one property here, so a theme file can restyle the whole
/// UI without touching any drawing code.
/// </summary>
/// <remarks>
/// A freshly constructed <see cref="Theme"/> already holds the classic default palette, which
/// means a theme file only has to specify the entries it wants to change.
/// </remarks>
public sealed class Theme
{
    /// <summary>Creates a theme pre-filled with the classic default palette.</summary>
    public Theme() => ApplyClassic(this);

    /// <summary>Human readable theme name.</summary>
    public string Name { get; set; } = "Classic";

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
    /// The totals line while it shows the tagged selection - yellow, like the tagged entries
    /// themselves, so pressing Ins is visible at the bottom of the panel too.
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

    /// <summary>The command word on the command line - the first word, and the first after a pipe.</summary>
    public CellStyle CommandLineCommand { get; set; }

    /// <summary>An option on the command line (<c>-v</c>, <c>--all</c>, <c>/s</c>).</summary>
    public CellStyle CommandLineOption { get; set; }

    /// <summary>A quoted string on the command line.</summary>
    public CellStyle CommandLineString { get; set; }

    /// <summary>A variable on the command line (<c>$x</c>, <c>%PATH%</c>).</summary>
    public CellStyle CommandLineVariable { get; set; }

    /// <summary>The ghost completion drawn after the caret, taken from the history.</summary>
    public CellStyle CommandLineSuggestion { get; set; }

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

    // ---- Syntax colouring (editor and viewer) ---------------------------------------------

    /// <summary>A language keyword, or a JSON object key.</summary>
    public CellStyle SyntaxKeyword { get; set; }

    /// <summary>A string or character literal.</summary>
    public CellStyle SyntaxString { get; set; }

    /// <summary>A numeric literal.</summary>
    public CellStyle SyntaxNumber { get; set; }

    /// <summary>A comment.</summary>
    public CellStyle SyntaxComment { get; set; }

    /// <summary>A preprocessor directive.</summary>
    public CellStyle SyntaxPreprocessor { get; set; }

    // ---- Progress --------------------------------------------------------------------------------

    /// <summary>Filled part of a progress bar.</summary>
    public CellStyle ProgressBar { get; set; }

    /// <summary>Unfilled part of a progress bar.</summary>
    public CellStyle ProgressBarEmpty { get; set; }

    // =============================================================================================

    /// <summary>
    /// The classic blue-panel colour table over the DOS-era RGB values
    /// (<see cref="Rendering.Palette.ClassicVga"/>).
    /// </summary>
    public static Theme Classic() => new();

    /// <summary>
    /// The same colour table over the legacy Windows NT console RGB
    /// (<see cref="Rendering.Palette.WindowsNt"/>) - the values the Windows console used before
    /// Windows Terminal, so this is what the classic look was actually showing on Windows.
    /// </summary>
    /// <remarks>
    /// The only difference from <see cref="Classic"/> is the palette: a deeper
    /// <c>#000080</c> blue and a pure <c>#00FFFF</c> cyan instead of the DOS <c>#0000AA</c> and
    /// <c>#55FFFF</c>, which lifts the dominant panel pair from 10.84:1 to 12.77:1. Every one of the
    /// 74 entries below is identical.
    /// </remarks>
    public static Theme ClassicNt() => new() { Name = "Classic NT", Palette = Rendering.Palette.WindowsNt };

    /// <summary>The names of the built-in themes, in the order they are offered.</summary>
    public static IReadOnlyList<string> BuiltInNames { get; } = ["Classic", "Classic NT"];

    /// <summary>
    /// Resolves a built-in theme by name, ignoring case and any separators. Accepts the display
    /// names in <see cref="BuiltInNames"/> as well as the short forms <c>default</c>, <c>classic</c>,
    /// <c>nt</c> and <c>windowsnt</c>.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="name"/> named a built-in theme.</returns>
    public static bool TryGetBuiltIn(string? name, [NotNullWhen(true)] out Theme? theme)
    {
        theme = ThemePalette.Normalize(name) switch
        {
            "default" or "classic" => Classic(),
            "classicnt" or "nt" or "windowsnt" => ClassicNt(),
            _ => null,
        };

        return theme is not null;
    }

    /// <summary>
    /// Fills <paramref name="t"/> with the classic default palette.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every entry is a foreground/background pair over the 16 console slots, described on the
    /// right by what it colours. The two halves of the classic look deliberately do <em>not</em>
    /// share one cyan: the cyan used as a foreground is the bright slot, index 11 =
    /// <see cref="ConsoleColor.Cyan"/>, while the cyan used as a background is the dark slot,
    /// index 3 = <see cref="ConsoleColor.DarkCyan"/> - the bright slot is never used as a
    /// background anywhere in the table. Collapsing the two onto one constant made every cyan bar
    /// render as <c>SGR 106</c> where the classic look writes <c>SGR 46</c>: too loud rather than
    /// washed out, and no palette change can fix it. The locals below keep the halves apart,
    /// which is why the background names are prefixed.
    /// </para>
    /// <para>
    /// A handful of entries have no counterpart in the classic colour table at all - file-type
    /// colours traditionally live in a separate highlighting configuration, and the panel status
    /// line, drive info and progress bars are this application's own. Those are marked and left
    /// as they were rather than invented.
    /// </para>
    /// </remarks>
    private static void ApplyClassic(Theme t)
    {
        // Foregrounds.
        const ConsoleColor black = ConsoleColor.Black;          // index 0
        const ConsoleColor darkGray = ConsoleColor.DarkGray;    // index 8
        const ConsoleColor lightGray = ConsoleColor.Gray;       // index 7
        const ConsoleColor darkCyan = ConsoleColor.DarkCyan;    // index 3
        const ConsoleColor lightCyan = ConsoleColor.Cyan;       // index 11
        const ConsoleColor lightGreen = ConsoleColor.Green;     // index 10
        const ConsoleColor yellow = ConsoleColor.Yellow;        // index 14
        const ConsoleColor white = ConsoleColor.White;          // index 15

        // Backgrounds. bCyan is the one that bites: index 3, not 11.
        const ConsoleColor bBlack = ConsoleColor.Black;         // index 0
        const ConsoleColor bBlue = ConsoleColor.DarkBlue;       // index 1
        const ConsoleColor bCyan = ConsoleColor.DarkCyan;       // index 3
        const ConsoleColor bRed = ConsoleColor.DarkRed;         // index 4
        const ConsoleColor bLightGray = ConsoleColor.Gray;      // index 7

        t.Name = "Classic";
        t.Palette = Rendering.Palette.ClassicVga;

        // ---- Panels -------------------------------------------------------------------------
        // The classic look paints the frame, the column separators, the file text, the path
        // caption, the totals line and the scroll bar with ONE attribute: bright cyan on blue.
        // The frame and the file text are distinct slots carrying the same default, so the near
        // monochrome panel is faithful rather than a bug, and the contrast has to come from the
        // palette (10.84:1 on ClassicVga) instead of from spending more colours on the frame.
        t.PanelBox = new CellStyle(lightCyan, bBlue);            // frame and column separators
        t.PanelBoxActive = new CellStyle(lightCyan, bBlue);      // the classic look has no separate active-frame colour
        t.PanelTitle = new CellStyle(lightCyan, bBlue);          // path caption
        t.PanelTitleActive = new CellStyle(black, bCyan);        // path caption of the active panel
        t.PanelColumnTitle = new CellStyle(yellow, bBlue);       // column headings
        t.PanelText = new CellStyle(lightCyan, bBlue);           // plain file entries

        // File-type colours: traditionally not palette entries but a separate highlighting
        // configuration. Directories are white, the executable group (*.exe, *.com, *.bat, *.cmd)
        // light green, and hidden or system entries dark cyan - which is what keeps executables
        // distinguishable from folders in the classic look.
        t.PanelDirectory = new CellStyle(white, bBlue);          // directories
        t.PanelHidden = new CellStyle(darkCyan, bBlue);          // hidden or system entries
        t.PanelExecutable = new CellStyle(lightGreen, bBlue);    // executables
        t.PanelArchive = new CellStyle(ConsoleColor.Magenta, bBlue); // archives

        t.PanelSelectedFile = new CellStyle(yellow, bBlue);      // tagged entries
        t.PanelCursor = new CellStyle(black, bCyan);             // cursor bar
        t.PanelCursorSelected = new CellStyle(yellow, bCyan);    // cursor bar over a tagged entry
        t.PanelStatus = new CellStyle(white, bBlue);             // this application's own
        t.PanelStatusFile = new CellStyle(white, bBlue);         // this application's own
        t.PanelTotals = new CellStyle(lightCyan, bBlue);         // totals line
        t.PanelSelectedTotals = new CellStyle(yellow, bBlue);    // totals line showing the tagged selection
        t.PanelScrollBar = new CellStyle(lightCyan, bBlue);      // scroll bar
        t.PanelEmpty = new CellStyle(lightCyan, bBlue);          // the panel's own ground = the file text
        t.PanelDriveInfo = new CellStyle(white, bBlue);          // this application's own
        t.QuickSearch = new CellStyle(black, bCyan);             // this application's own; the usual black-on-dark-cyan bar

        // ---- Screen chrome ------------------------------------------------------------------
        t.Desktop = CellStyle.Default;                           // this application's own; the user screen
        t.Clock = new CellStyle(black, bCyan);                   // clock in the top right corner
        t.KeyBarNum = new CellStyle(lightGray, bBlack);          // F-key numbers in the key bar
        t.KeyBarText = new CellStyle(black, bCyan);              // F-key labels in the key bar
        t.KeyBarBackground = new CellStyle(lightGray, bBlack);   // gaps between the key bar labels
        // The command line inherits the console's own colours, and the console is black: it is a
        // strip of terminal, light grey on black, never the panel blue - which is exactly what
        // the classic look shows. The colouring of the typed command follows the conventions of
        // PSReadLine and fish on the same black.
        t.CommandLinePrefix = new CellStyle(lightGray, bBlack);       // the prompt
        t.CommandLineText = new CellStyle(lightGray, bBlack);         // the typed text
        t.CommandLineCommand = new CellStyle(yellow, bBlack);         // this application's own; PSReadLine's Command
        t.CommandLineOption = new CellStyle(darkGray, bBlack);        // this application's own; PSReadLine's Parameter
        t.CommandLineString = new CellStyle(lightCyan, bBlack);       // this application's own; PSReadLine's String
        t.CommandLineVariable = new CellStyle(lightGreen, bBlack);    // this application's own; PSReadLine's Variable
        t.CommandLineSuggestion = new CellStyle(darkGray, bBlack);    // this application's own; PSReadLine's InlinePrediction
        t.CommandLineSelected = new CellStyle(black, bCyan);     // selected text in the command line

        // ---- Horizontal (F9) menu bar -------------------------------------------------------
        t.MenuBarText = new CellStyle(black, bCyan);             // menu bar items
        t.MenuBarHighlight = new CellStyle(yellow, bCyan);       // hot letters in the menu bar
        t.MenuBarSelected = new CellStyle(white, bBlack);        // the open menu's title
        t.MenuBarSelectedHighlight = new CellStyle(yellow, bBlack); // hot letter of the open menu's title

        // ---- Popup menus. These are WHITE on dark cyan, not black. ---------------------------
        t.MenuBox = new CellStyle(white, bCyan);                 // frame
        t.MenuTitle = new CellStyle(white, bCyan);               // title
        t.MenuText = new CellStyle(white, bCyan);                // items
        t.MenuHighlight = new CellStyle(yellow, bCyan);          // hot letters
        t.MenuSelected = new CellStyle(white, bBlack);           // the item under the cursor
        t.MenuSelectedHighlight = new CellStyle(yellow, bBlack); // hot letter of the item under the cursor
        t.MenuDisabled = new CellStyle(darkGray, bCyan);         // disabled items
        t.MenuSeparator = new CellStyle(white, bCyan);           // drawn with the frame colour
        t.MenuScroll = new CellStyle(white, bCyan);              // scroll bar

        // ---- Dialogs ------------------------------------------------------------------------
        t.DialogBox = new CellStyle(black, bLightGray);          // frame
        t.DialogBoxTitle = new CellStyle(black, bLightGray);     // title
        t.DialogText = new CellStyle(black, bLightGray);         // plain text
        t.DialogHighlight = new CellStyle(yellow, bLightGray);   // hot letters in text
        t.DialogEdit = new CellStyle(black, bCyan);              // edit fields
        t.DialogEditSelected = new CellStyle(white, bBlack);     // selected text in an edit field
        t.DialogEditDisabled = new CellStyle(darkGray, bCyan);   // disabled edit fields
        t.DialogButton = new CellStyle(black, bLightGray);       // buttons
        t.DialogButtonHighlight = new CellStyle(yellow, bLightGray);        // hot letters on buttons
        t.DialogButtonSelected = new CellStyle(black, bCyan);               // the focused button
        t.DialogButtonSelectedHighlight = new CellStyle(yellow, bCyan);     // hot letter on the focused button
        t.DialogListText = new CellStyle(black, bLightGray);                // list box items
        t.DialogListHighlight = new CellStyle(yellow, bLightGray);          // hot letters in list boxes
        t.DialogListSelected = new CellStyle(white, bBlack);                // list box item under the cursor
        t.DialogListSelectedHighlight = new CellStyle(yellow, bBlack);      // hot letter of the list box item under the cursor

        // ---- Warning dialogs ----------------------------------------------------------------
        t.WarnDialogBox = new CellStyle(white, bRed);            // frame
        t.WarnDialogBoxTitle = new CellStyle(white, bRed);       // title
        t.WarnDialogText = new CellStyle(white, bRed);           // plain text
        t.WarnDialogHighlight = new CellStyle(yellow, bRed);     // hot letters in text
        t.WarnDialogButton = new CellStyle(white, bRed);         // buttons
        t.WarnDialogButtonHighlight = new CellStyle(yellow, bRed);          // hot letters on buttons
        t.WarnDialogButtonSelected = new CellStyle(black, bLightGray);      // the focused button
        t.WarnDialogButtonSelectedHighlight = new CellStyle(yellow, bLightGray); // hot letter on the focused button

        // ---- Viewer / editor ----------------------------------------------------------------
        t.ViewerText = new CellStyle(lightCyan, bBlue);          // viewer text
        t.ViewerSelected = new CellStyle(black, bCyan);          // selected text in the viewer
        t.ViewerStatus = new CellStyle(black, bCyan);            // viewer status line
        t.ViewerArrows = new CellStyle(yellow, bBlue);           // the arrows marking a horizontally scrolled line
        t.EditorText = new CellStyle(lightCyan, bBlue);          // editor text
        t.EditorSelected = new CellStyle(black, bCyan);          // selected text in the editor
        t.EditorStatus = new CellStyle(black, bCyan);            // editor status line
        t.EditorScroll = new CellStyle(lightCyan, bBlue);        // editor scroll bar

        // ---- Syntax colouring. Not part of the classic colour table; this mirrors the usual
        // highlighter scheme on the blue editor - grey comments, yellow strings,
        // white keywords. Both surfaces (editor and viewer) share the blue background, so one set
        // of slots serves them both.
        t.SyntaxKeyword = new CellStyle(white, bBlue);
        t.SyntaxString = new CellStyle(yellow, bBlue);
        t.SyntaxNumber = new CellStyle(ConsoleColor.Magenta, bBlue);
        t.SyntaxComment = new CellStyle(darkGray, bBlue);
        t.SyntaxPreprocessor = new CellStyle(lightGreen, bBlue);

        // ---- Progress. Classic managers drew copy progress in dialog colours. ----------------
        t.ProgressBar = new CellStyle(lightCyan, bBlue);         // this application's own
        t.ProgressBarEmpty = new CellStyle(darkGray, bBlue);     // this application's own
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
        new("CommandLineCommand", t => t.CommandLineCommand, (t, v) => t.CommandLineCommand = v),
        new("CommandLineOption", t => t.CommandLineOption, (t, v) => t.CommandLineOption = v),
        new("CommandLineString", t => t.CommandLineString, (t, v) => t.CommandLineString = v),
        new("CommandLineVariable", t => t.CommandLineVariable, (t, v) => t.CommandLineVariable = v),
        new("CommandLineSuggestion", t => t.CommandLineSuggestion, (t, v) => t.CommandLineSuggestion = v),
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

        new("SyntaxKeyword", t => t.SyntaxKeyword, (t, v) => t.SyntaxKeyword = v),
        new("SyntaxString", t => t.SyntaxString, (t, v) => t.SyntaxString = v),
        new("SyntaxNumber", t => t.SyntaxNumber, (t, v) => t.SyntaxNumber = v),
        new("SyntaxComment", t => t.SyntaxComment, (t, v) => t.SyntaxComment = v),
        new("SyntaxPreprocessor", t => t.SyntaxPreprocessor, (t, v) => t.SyntaxPreprocessor = v),

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

            // Also accept the dotted spelling, e.g. "Panel.Title.Selected" style keys with
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
    /// Loads a theme from a JSON file. Missing entries keep their classic default value and unknown
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
        var theme = Classic();

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
    /// built-in theme name when it does not, and falls back to <see cref="Classic"/> for
    /// anything else. Never throws.
    /// </summary>
    public static Theme LoadOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Classic();
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
            return Classic();
        }

        // Not a file - a bare built-in name is worth honouring before giving up on it.
        return TryGetBuiltIn(path, out var builtIn) ? builtIn : Classic();
    }
}
