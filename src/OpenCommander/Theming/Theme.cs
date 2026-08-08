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

    /// <summary>The canonical Far Manager palette.</summary>
    public static Theme FarDefault() => new();

    private static void ApplyFarDefault(Theme t)
    {
        // Far's palette source spells these C_BLUE and C_LIGHTCYAN; in ConsoleColor terms that is
        // DarkBlue (index 1) and Cyan (index 11).
        const ConsoleColor blue = ConsoleColor.DarkBlue;
        const ConsoleColor cyan = ConsoleColor.Cyan;

        t.Name = "Far Default";

        t.PanelBox = new CellStyle(cyan, blue);
        t.PanelBoxActive = new CellStyle(cyan, blue);
        t.PanelTitle = new CellStyle(cyan, blue);
        t.PanelTitleActive = new CellStyle(ConsoleColor.Black, cyan);
        t.PanelColumnTitle = new CellStyle(ConsoleColor.Yellow, blue);
        t.PanelText = new CellStyle(cyan, blue);
        t.PanelDirectory = new CellStyle(ConsoleColor.White, blue);
        t.PanelHidden = new CellStyle(ConsoleColor.DarkGray, blue);
        t.PanelExecutable = new CellStyle(ConsoleColor.White, blue);
        t.PanelArchive = new CellStyle(ConsoleColor.Magenta, blue);
        t.PanelSelectedFile = new CellStyle(ConsoleColor.Yellow, blue);
        t.PanelCursor = new CellStyle(ConsoleColor.Black, cyan);
        t.PanelCursorSelected = new CellStyle(ConsoleColor.Yellow, cyan);
        t.PanelStatus = new CellStyle(ConsoleColor.White, blue);
        t.PanelStatusFile = new CellStyle(ConsoleColor.White, blue);
        t.PanelTotals = new CellStyle(ConsoleColor.Yellow, blue);
        t.PanelScrollBar = new CellStyle(cyan, blue);
        t.PanelEmpty = new CellStyle(cyan, blue);
        t.PanelDriveInfo = new CellStyle(ConsoleColor.White, blue);
        t.QuickSearch = new CellStyle(ConsoleColor.Black, cyan);

        t.Desktop = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        t.Clock = new CellStyle(ConsoleColor.Yellow, blue);
        t.KeyBarNum = new CellStyle(ConsoleColor.White, ConsoleColor.Black);
        t.KeyBarText = new CellStyle(ConsoleColor.Black, cyan);
        t.KeyBarBackground = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        t.CommandLinePrefix = new CellStyle(ConsoleColor.Gray, blue);
        t.CommandLineText = new CellStyle(ConsoleColor.White, blue);
        t.CommandLineSelected = new CellStyle(ConsoleColor.Black, cyan);

        t.MenuBarText = new CellStyle(ConsoleColor.Black, cyan);
        t.MenuBarHighlight = new CellStyle(ConsoleColor.DarkRed, cyan);
        t.MenuBarSelected = new CellStyle(ConsoleColor.White, ConsoleColor.Black);
        t.MenuBarSelectedHighlight = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black);

        t.MenuBox = new CellStyle(ConsoleColor.Black, cyan);
        t.MenuTitle = new CellStyle(ConsoleColor.Black, cyan);
        t.MenuText = new CellStyle(ConsoleColor.Black, cyan);
        t.MenuHighlight = new CellStyle(ConsoleColor.DarkRed, cyan);
        t.MenuSelected = new CellStyle(ConsoleColor.White, ConsoleColor.Black);
        t.MenuSelectedHighlight = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black);
        t.MenuDisabled = new CellStyle(ConsoleColor.DarkGray, cyan);
        t.MenuSeparator = new CellStyle(ConsoleColor.Black, cyan);
        t.MenuScroll = new CellStyle(ConsoleColor.Black, cyan);

        t.DialogBox = new CellStyle(ConsoleColor.Black, ConsoleColor.Gray);
        t.DialogBoxTitle = new CellStyle(ConsoleColor.Black, ConsoleColor.Gray);
        t.DialogText = new CellStyle(ConsoleColor.Black, ConsoleColor.Gray);
        t.DialogHighlight = new CellStyle(ConsoleColor.DarkRed, ConsoleColor.Gray);
        t.DialogEdit = new CellStyle(ConsoleColor.Black, cyan);
        t.DialogEditSelected = new CellStyle(ConsoleColor.White, blue);
        t.DialogEditDisabled = new CellStyle(ConsoleColor.DarkGray, ConsoleColor.Gray);
        t.DialogButton = new CellStyle(ConsoleColor.Black, ConsoleColor.Gray);
        t.DialogButtonHighlight = new CellStyle(ConsoleColor.DarkRed, ConsoleColor.Gray);
        t.DialogButtonSelected = new CellStyle(ConsoleColor.Black, cyan);
        t.DialogButtonSelectedHighlight = new CellStyle(ConsoleColor.DarkRed, cyan);
        t.DialogListText = new CellStyle(ConsoleColor.Black, ConsoleColor.Gray);
        t.DialogListHighlight = new CellStyle(ConsoleColor.DarkRed, ConsoleColor.Gray);
        t.DialogListSelected = new CellStyle(ConsoleColor.Black, cyan);
        t.DialogListSelectedHighlight = new CellStyle(ConsoleColor.DarkRed, cyan);

        t.WarnDialogBox = new CellStyle(ConsoleColor.White, ConsoleColor.DarkRed);
        t.WarnDialogBoxTitle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkRed);
        t.WarnDialogText = new CellStyle(ConsoleColor.White, ConsoleColor.DarkRed);
        t.WarnDialogHighlight = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkRed);
        t.WarnDialogButton = new CellStyle(ConsoleColor.White, ConsoleColor.DarkRed);
        t.WarnDialogButtonHighlight = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkRed);
        t.WarnDialogButtonSelected = new CellStyle(ConsoleColor.Black, ConsoleColor.Gray);
        t.WarnDialogButtonSelectedHighlight = new CellStyle(ConsoleColor.DarkRed, ConsoleColor.Gray);

        t.ViewerText = new CellStyle(ConsoleColor.Gray, blue);
        t.ViewerSelected = new CellStyle(ConsoleColor.Black, cyan);
        t.ViewerStatus = new CellStyle(ConsoleColor.Black, cyan);
        t.ViewerArrows = new CellStyle(ConsoleColor.Yellow, blue);
        t.EditorText = new CellStyle(ConsoleColor.Gray, blue);
        t.EditorSelected = new CellStyle(ConsoleColor.Black, cyan);
        t.EditorStatus = new CellStyle(ConsoleColor.Black, cyan);
        t.EditorScroll = new CellStyle(cyan, blue);

        t.ProgressBar = new CellStyle(cyan, blue);
        t.ProgressBarEmpty = new CellStyle(ConsoleColor.DarkGray, blue);
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

    /// <summary>Writes the complete palette to a JSON file, creating the directory if needed.</summary>
    public void SaveToJson(string path)
    {
        var colors = new Dictionary<string, string>(AllSlots.Length, StringComparer.Ordinal);
        foreach (var slot in AllSlots)
        {
            colors[slot.Name] = ThemeColor.Format(slot.Get(this));
        }

        var dto = new ThemeFileOut { Name = Name, Colors = colors };
        string json = JsonSerializer.Serialize(dto, ThemeJsonContext.Default.ThemeFileOut);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads <paramref name="path"/> when it points at a readable theme file, and falls back to
    /// <see cref="FarDefault"/> for anything else. Never throws.
    /// </summary>
    public static Theme LoadOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FarDefault();
        }

        try
        {
            return File.Exists(path) ? LoadFromJson(path) : FarDefault();
        }
        catch
        {
            return FarDefault();
        }
    }
}
