using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCommander.Files;
using OpenCommander.Panels;

namespace OpenCommander.Core;

/// <summary>
/// The definition of the horizontal (F9) menu: Left, Files, Commands, Options, Right.
/// </summary>
/// <remarks>
/// The structure follows Far Manager's own menu arrays, minus the entries whose feature is not in
/// this version. Every leaf carries the accelerator hint on its right and an action bound to the
/// application, so <c>MenuBar.RunModal</c> can simply invoke the chosen item.
/// </remarks>
public static class MainMenu
{
    /// <summary>The file the F2 user menu is read from, in the settings folder.</summary>
    public const string UserMenuFileName = "usermenu.json";

    /// <summary>Builds the whole bar.</summary>
    /// <param name="app">The application the actions act on.</param>
    /// <returns>The five top-level items, each with its pull-down.</returns>
    public static IReadOnlyList<MenuItem> Build(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return
        [
            new MenuItem("&Left") { SubItems = PanelMenu(app, left: true) },
            new MenuItem("&Files") { SubItems = FilesMenu(app) },
            new MenuItem("&Commands") { SubItems = CommandsMenu(app) },
            new MenuItem("&Options") { SubItems = OptionsMenu(app) },
            new MenuItem("&Right") { SubItems = PanelMenu(app, left: false) },
        ];
    }

    /// <summary>Builds the Left or Right pull-down.</summary>
    /// <param name="app">The application the actions act on.</param>
    /// <param name="left">Whether this is the Left menu.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<MenuItem> PanelMenu(Application app, bool left)
    {
        ArgumentNullException.ThrowIfNull(app);

        FilePanel panel = left ? app.LeftFilePanel : app.RightFilePanel;

        MenuItem Mode(string text, PanelViewMode mode, string accel) =>
            new(text, accel, () => app.SetViewMode(panel, mode)) { Checked = panel.ViewMode == mode };

        return
        [
            Mode("&Brief", PanelViewMode.Brief, "Ctrl+1"),
            Mode("&Medium", PanelViewMode.Medium, "Ctrl+2"),
            Mode("&Full", PanelViewMode.Full, "Ctrl+3"),
            Mode("&Wide", PanelViewMode.Wide, "Ctrl+4"),
            Mode("Detai&led", PanelViewMode.Detailed, "Ctrl+5"),
            MenuItem.Separator(),
            new MenuItem("&Sort by name", "Ctrl+F3", () => app.SetSort(panel, SortMode.Name)),
            new MenuItem("Sort by e&xtension", "Ctrl+F4", () => app.SetSort(panel, SortMode.Extension)),
            new MenuItem("Sort by &write time", "Ctrl+F5", () => app.SetSort(panel, SortMode.Modified)),
            new MenuItem("Sort by si&ze", "Ctrl+F6", () => app.SetSort(panel, SortMode.Size)),
            new MenuItem("&Unsorted", "Ctrl+F7", () => app.SetSort(panel, SortMode.Unsorted)),
            MenuItem.Separator(),
            new MenuItem("Panel &On/Off", left ? "Ctrl+F1" : "Ctrl+F2", () => app.TogglePanel(left)),
            new MenuItem("&Re-read", "Ctrl+R", () => app.ReloadPanel(panel)),
            new MenuItem("&Change drive", left ? "Alt+F1" : "Alt+F2", () => app.ShowDriveMenu(left)),
        ];
    }

    /// <summary>Builds the Files pull-down.</summary>
    /// <param name="app">The application the actions act on.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<MenuItem> FilesMenu(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return
        [
            new MenuItem("&View", "F3", app.ViewCurrent),
            new MenuItem("&Edit", "F4", app.EditCurrent),
            new MenuItem("&Copy", "F5", () => app.CopyFiles(currentOnly: false)),
            new MenuItem("&Rename or move", "F6", () => app.MoveFiles(currentOnly: false)),
            new MenuItem("&Make folder", "F7", app.MakeDirectory),
            new MenuItem("&Delete", "F8", () => app.DeleteFiles(permanent: false)),
            new MenuItem("Delete &permanently", "Shift+Del", () => app.DeleteFiles(permanent: true)),
            MenuItem.Separator(),
            new MenuItem("&Folder size", "Ctrl+L", app.ShowDirectorySize),
            new MenuItem("Copy &names to clipboard", "Ctrl+Ins", () => app.CopyNamesToClipboard(fullPaths: false)),
            MenuItem.Separator(),
            new MenuItem("Select &group", "Gray +", () => app.SelectGroup(select: true)),
            new MenuItem("U&nselect group", "Gray -", () => app.SelectGroup(select: false)),
            new MenuItem("&Invert selection", "Gray *", app.InvertSelection),
        ];
    }

    /// <summary>Builds the Commands pull-down.</summary>
    /// <param name="app">The application the actions act on.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<MenuItem> CommandsMenu(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return
        [
            new MenuItem("&Find file", "Alt+F7", app.FindFiles),
            new MenuItem("&History", "Alt+F8", app.ShowCommandHistory),
            new MenuItem("F&olders history", "Alt+F12", app.ShowFolderHistory),
            MenuItem.Separator(),
            new MenuItem("&Swap panels", "Ctrl+U", app.SwapPanels),
            new MenuItem("&Panels On/Off", "Ctrl+O", app.HidePanelsTemporarily),
            new MenuItem("&Compare folders", null, app.CompareDirectories),
            MenuItem.Separator(),
            new MenuItem("&User menu", "F2", app.ShowUserMenu),
            new MenuItem("&Extras", "F11", app.ShowPluginsMenu),
            new MenuItem("Sc&reens list", "F12", app.ShowScreensList),
        ];
    }

    /// <summary>Builds the Options pull-down.</summary>
    /// <param name="app">The application the actions act on.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<MenuItem> OptionsMenu(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Settings s = app.Settings;

        return
        [
            new MenuItem("Show &hidden files", "Ctrl+H", () => app.ToggleSetting(v => s.ShowHiddenFiles = v, s.ShowHiddenFiles, reload: true))
            {
                Checked = s.ShowHiddenFiles,
            },
            new MenuItem("&Directories first", null, () => app.ToggleSetting(v => s.DirectoriesFirst = v, s.DirectoriesFirst, reload: true))
            {
                Checked = s.DirectoriesFirst,
            },
            new MenuItem("&Numeric sort", null, () => app.ToggleSetting(v => s.NumericSort = v, s.NumericSort, reload: true))
            {
                Checked = s.NumericSort,
            },
            new MenuItem("&Auto change folder", null, () => app.ToggleSetting(v => s.AutoChangeDirectory = v, s.AutoChangeDirectory, reload: false))
            {
                Checked = s.AutoChangeDirectory,
            },
            MenuItem.Separator(),
            new MenuItem("Show &key bar", "Ctrl+B", () => app.ToggleSetting(v => s.ShowKeyBar = v, s.ShowKeyBar, reload: false))
            {
                Checked = s.ShowKeyBar,
            },
            new MenuItem("Show &command line", null, () => app.ToggleSetting(v => s.ShowCommandLine = v, s.ShowCommandLine, reload: false))
            {
                Checked = s.ShowCommandLine,
            },
            new MenuItem("Show &status bar", null, () => app.ToggleSetting(v => s.ShowStatusBar = v, s.ShowStatusBar, reload: false))
            {
                Checked = s.ShowStatusBar,
            },
            new MenuItem("Show c&lock", null, () => app.ToggleSetting(v => s.ShowClock = v, s.ShowClock, reload: false))
            {
                Checked = s.ShowClock,
            },
            new MenuItem("Synta&x highlighting", null, () => app.ToggleSetting(v => s.SyntaxHighlight = v, s.SyntaxHighlight, reload: false))
            {
                Checked = s.SyntaxHighlight,
            },
            MenuItem.Separator(),
            new MenuItem("Confirm de&lete", null, () => app.ToggleSetting(v => s.ConfirmDelete = v, s.ConfirmDelete, reload: false))
            {
                Checked = s.ConfirmDelete,
            },
            new MenuItem("Confirm delete of non-empt&y folders", null, () => app.ToggleSetting(v => s.ConfirmDeleteNonEmptyFolders = v, s.ConfirmDeleteNonEmptyFolders, reload: false))
            {
                Checked = s.ConfirmDeleteNonEmptyFolders,
            },
            new MenuItem("Use the &recycle bin", null, () => app.ToggleSetting(v => s.UseRecycleBin = v, s.UseRecycleBin, reload: false))
            {
                Checked = s.UseRecycleBin,
            },
            MenuItem.Separator(),
            new MenuItem("&Save setup", "Shift+F9", app.SaveSettings),
        ];
    }

    /// <summary>The path the F2 user menu is read from.</summary>
    public static string UserMenuPath => Path.Combine(Settings.ConfigDirectory, UserMenuFileName);

    /// <summary>
    /// Loads the user menu. Never throws: a missing or malformed file yields
    /// <see langword="null"/> so the caller can tell the user there is no menu yet.
    /// </summary>
    /// <param name="path">The file to read, or <see langword="null"/> for <see cref="UserMenuPath"/>.</param>
    /// <returns>The entries, or <see langword="null"/> when there is no usable file.</returns>
    public static IReadOnlyList<UserMenuEntry>? LoadUserMenu(string? path = null)
    {
        string file = string.IsNullOrWhiteSpace(path) ? UserMenuPath : path;

        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            string json = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            UserMenuFile? parsed = JsonSerializer.Deserialize(json, UserMenuJsonContext.Default.UserMenuFile);
            if (parsed?.Items is not { Count: > 0 })
            {
                return null;
            }

            return [.. parsed.Items.Where(static i => i is not null && !string.IsNullOrWhiteSpace(i.Title))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>One entry of the F2 user menu.</summary>
public sealed class UserMenuEntry
{
    /// <summary>The caption; a <c>'&amp;'</c> marks the hotkey.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The command line to run in the active panel's folder.</summary>
    public string Command { get; set; } = string.Empty;
}

/// <summary>The shape of <c>usermenu.json</c>.</summary>
public sealed class UserMenuFile
{
    /// <summary>The entries, in display order.</summary>
    public List<UserMenuEntry> Items { get; set; } = [];
}

/// <summary>Source-generated serialisation metadata for the user menu file.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(UserMenuFile))]
public sealed partial class UserMenuJsonContext : JsonSerializerContext
{
}
