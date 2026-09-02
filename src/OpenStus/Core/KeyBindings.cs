using OpenStus.Input;

namespace OpenStus.Core;

/// <summary>
/// The global key dispatch table: everything the shell itself owns, as opposed to the keys the
/// panel, the command line or a modal component handle.
/// </summary>
/// <remarks>
/// <para>
/// The table is consulted first, before the command line and before the active panel, so it must
/// only claim keys the panel does not want. In particular Ctrl+F3..Ctrl+F12 (the sort commands),
/// Ctrl+1..Ctrl+9 (the view modes), Ctrl+A, Ctrl+H, Ctrl+R, Ctrl+PgUp and Ctrl+PgDn are deliberately
/// absent - they belong to <c>FilePanel</c>.
/// </para>
/// <para>
/// A binding may carry a guard. That is how the bare Delete key can delete files while the command
/// line is empty and still reach the command line's own editing once something has been typed, and
/// how Tab switches panels on an empty line but completes a path on a typed one.
/// </para>
/// <para>
/// This table and <c>KeyBarSets</c> have to agree: every caption the key bar advertises for a
/// function key must be answered here, even if the answer is only
/// <see cref="Application.NotImplemented"/>, so that a key the user can see never does nothing at
/// all. The one exception is the Ctrl row's F3..F12, which are the panel's own sort commands and are
/// deliberately absent. A test walks both tables and fails when they drift apart.
/// </para>
/// </remarks>
public sealed class KeyBindings
{
    private readonly Dictionary<(KeyMods Mods, ConsoleKey Key), Binding> _map = [];
    private readonly List<Binding> _order = [];

    /// <summary>Creates an empty table.</summary>
    public KeyBindings()
    {
    }

    /// <summary>The shipped table.</summary>
    public static KeyBindings Default { get; } = BuildDefault();

    /// <summary>Every binding, in the order it was added.</summary>
    public IReadOnlyList<Binding> All => _order;

    /// <summary>
    /// One entry of the table.
    /// </summary>
    /// <param name="Mods">The modifiers that must be held, matched exactly.</param>
    /// <param name="Key">The key.</param>
    /// <param name="Description">What the binding does, for the documentation and the tests.</param>
    /// <param name="Action">What to run.</param>
    public sealed record Binding(KeyMods Mods, ConsoleKey Key, string Description, Action<Application> Action)
    {
        /// <summary>
        /// An optional guard. When it answers <see langword="false"/> the key is left alone and falls
        /// through to the command line and then the panel.
        /// </summary>
        public Func<Application, bool>? CanRun { get; init; }

        /// <summary>The chord as the help screen spells it, for example <c>Shift+F5</c>.</summary>
        public string Chord => new KeyEvent(Key, '\0', Mods).ToDisplayString();
    }

    /// <summary>
    /// Adds or replaces a binding.
    /// </summary>
    /// <param name="binding">The binding.</param>
    /// <returns>This table, so calls can be chained.</returns>
    public KeyBindings Add(Binding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var key = (binding.Mods, binding.Key);
        if (_map.TryGetValue(key, out Binding? existing))
        {
            _order.Remove(existing);
        }

        _map[key] = binding;
        _order.Add(binding);
        return this;
    }

    /// <summary>
    /// Adds a binding from its parts.
    /// </summary>
    /// <param name="mods">The modifiers that must be held, matched exactly.</param>
    /// <param name="key">The key.</param>
    /// <param name="description">What the binding does.</param>
    /// <param name="action">What to run.</param>
    /// <param name="canRun">An optional guard; see <see cref="Binding.CanRun"/>.</param>
    /// <returns>This table, so calls can be chained.</returns>
    public KeyBindings Add(
        KeyMods mods,
        ConsoleKey key,
        string description,
        Action<Application> action,
        Func<Application, bool>? canRun = null) =>
        Add(new Binding(mods, key, description, action) { CanRun = canRun });

    /// <summary>The binding for one chord, or <see langword="null"/>.</summary>
    /// <param name="mods">The modifiers.</param>
    /// <param name="key">The key.</param>
    /// <returns>The binding, or <see langword="null"/> when the chord is unbound.</returns>
    public Binding? Find(KeyMods mods, ConsoleKey key) =>
        _map.TryGetValue((mods, key), out Binding? binding) ? binding : null;

    /// <summary>
    /// Runs the binding for a key press, if there is one and its guard allows it.
    /// </summary>
    /// <param name="key">The key press.</param>
    /// <param name="app">The application to act on.</param>
    /// <returns><see langword="true"/> when a binding ran and the key must not travel further.</returns>
    public bool TryHandle(KeyEvent key, Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!_map.TryGetValue((key.Mods, key.Key), out Binding? binding))
        {
            return false;
        }

        if (binding.CanRun is not null && !binding.CanRun(app))
        {
            return false;
        }

        binding.Action(app);
        return true;
    }

    private static bool CommandLineEmpty(Application app) => app.CommandLineIsEmpty;

    private static KeyBindings BuildDefault()
    {
        var t = new KeyBindings();

        // --- panel focus ---------------------------------------------------
        // Tab only switches panels while the command line is empty. With something typed it has to
        // fall through to CommandLine, which completes the path under the caret like a shell does -
        // the reason for the guard: the table is consulted first, so an unguarded Tab would make
        // completion unreachable. Shift+Tab has no second job and switches unconditionally.
        t.Add(KeyMods.None, ConsoleKey.Tab, "Switch the active panel", a => a.SwitchPanel(), CommandLineEmpty);
        t.Add(KeyMods.Shift, ConsoleKey.Tab, "Switch the active panel", a => a.SwitchPanel());

        // --- the function key row ------------------------------------------
        t.Add(KeyMods.None, ConsoleKey.F1, "Help", a => a.ShowHelp());
        t.Add(KeyMods.Shift, ConsoleKey.F1, "Add to archive", a => a.NotImplemented("Add to archive"));
        t.Add(KeyMods.None, ConsoleKey.F2, "User menu", a => a.ShowUserMenu());
        t.Add(KeyMods.Shift, ConsoleKey.F2, "Extract from archive", a => a.NotImplemented("Extract from archive"));
        t.Add(KeyMods.None, ConsoleKey.F3, "View the item under the cursor", a => a.ViewCurrent());
        t.Add(KeyMods.Shift, ConsoleKey.F3, "Archive commands", a => a.NotImplemented("Archive commands"));
        t.Add(KeyMods.None, ConsoleKey.F4, "Edit the item under the cursor", a => a.EditCurrent());
        t.Add(KeyMods.Shift, ConsoleKey.F4, "Create and edit a new file", a => a.EditNewFile());
        t.Add(KeyMods.None, ConsoleKey.F5, "Copy", a => a.CopyFiles(currentOnly: false));
        t.Add(KeyMods.Shift, ConsoleKey.F5, "Copy the item under the cursor into this folder", a => a.CopyFiles(currentOnly: true));
        t.Add(KeyMods.None, ConsoleKey.F6, "Rename or move", a => a.MoveFiles(currentOnly: false));
        t.Add(KeyMods.Shift, ConsoleKey.F6, "Rename the item under the cursor", a => a.MoveFiles(currentOnly: true));
        t.Add(KeyMods.None, ConsoleKey.F7, "Create a folder", a => a.MakeDirectory());
        t.Add(KeyMods.None, ConsoleKey.F8, "Delete", a => a.DeleteFiles(permanent: false));
        t.Add(KeyMods.Shift, ConsoleKey.F8, "Delete the item under the cursor", a => a.DeleteFiles(permanent: false, currentOnly: true));
        t.Add(KeyMods.None, ConsoleKey.F9, "Open the horizontal menu", a => a.ShowMainMenu());
        t.Add(KeyMods.Shift, ConsoleKey.F9, "Save the settings", a => a.SaveSettings());
        t.Add(KeyMods.None, ConsoleKey.F10, "Quit", a => a.QuitCommand());
        t.Add(KeyMods.Shift, ConsoleKey.F10, "Last menu item", a => a.NotImplemented("Last menu item"));
        t.Add(KeyMods.None, ConsoleKey.F11, "Extras menu", a => a.ShowPluginsMenu());
        t.Add(KeyMods.Shift, ConsoleKey.F11, "Sort groups", a => a.NotImplemented("Sort groups"));
        t.Add(KeyMods.None, ConsoleKey.F12, "Screens list", a => a.ShowScreensList());
        t.Add(KeyMods.Shift, ConsoleKey.F12, "Show selected first", a => a.NotImplemented("Show selected first"));

        // --- Delete, only while the command line is empty --------------------
        t.Add(KeyMods.None, ConsoleKey.Delete, "Delete", a => a.DeleteFiles(permanent: false), CommandLineEmpty);
        t.Add(KeyMods.Shift, ConsoleKey.Delete, "Delete permanently, bypassing the recycle bin", a => a.DeleteFiles(permanent: true), CommandLineEmpty);

        // --- Alt chords ------------------------------------------------------
        t.Add(KeyMods.Alt, ConsoleKey.F1, "Change the left panel's drive", a => a.ShowDriveMenu(left: true));
        t.Add(KeyMods.Alt, ConsoleKey.F2, "Change the right panel's drive", a => a.ShowDriveMenu(left: false));
        t.Add(KeyMods.Alt, ConsoleKey.F3, "View with...", a => a.NotImplemented("View with..."));
        t.Add(KeyMods.Alt, ConsoleKey.F4, "Edit with...", a => a.NotImplemented("Edit with..."));
        t.Add(KeyMods.Alt, ConsoleKey.F5, "Print", a => a.NotImplemented("Print"));
        t.Add(KeyMods.Alt, ConsoleKey.F6, "Create link", a => a.NotImplemented("Create link"));
        t.Add(KeyMods.Alt, ConsoleKey.F7, "Find file", a => a.FindFiles());
        t.Add(KeyMods.Alt, ConsoleKey.F8, "Command history", a => a.ShowCommandHistory());
        t.Add(KeyMods.Alt, ConsoleKey.F9, "Video mode", a => a.NotImplemented("Video mode"));
        t.Add(KeyMods.Alt, ConsoleKey.F10, "Folder tree", a => a.NotImplemented("Folder tree"));
        t.Add(KeyMods.Alt, ConsoleKey.F11, "Viewer history", a => a.NotImplemented("Viewer history"));
        t.Add(KeyMods.Alt, ConsoleKey.F12, "Folders history", a => a.ShowFolderHistory());

        // --- Ctrl+Shift and Alt+Shift chords -----------------------------------
        t.Add(KeyMods.Ctrl | KeyMods.Shift, ConsoleKey.F3, "Quick view", a => a.NotImplemented("Quick view"));
        t.Add(KeyMods.Ctrl | KeyMods.Shift, ConsoleKey.F4, "Quick edit", a => a.NotImplemented("Quick edit"));
        t.Add(KeyMods.Alt | KeyMods.Shift, ConsoleKey.F9, "Plugin configuration", a => a.NotImplemented("Plugin configuration"));

        // --- panel visibility -------------------------------------------------
        t.Add(KeyMods.Ctrl, ConsoleKey.F1, "Hide or show the left panel", a => a.TogglePanel(left: true));
        t.Add(KeyMods.Ctrl, ConsoleKey.F2, "Hide or show the right panel", a => a.TogglePanel(left: false));
        t.Add(KeyMods.Ctrl, ConsoleKey.O, "Hide or show both panels; the command line stays live", a => a.HidePanelsTemporarily());
        t.Add(KeyMods.Ctrl, ConsoleKey.P, "Hide or show the passive panel", a => a.TogglePassivePanel());
        t.Add(KeyMods.Ctrl, ConsoleKey.U, "Swap the two panels", a => a.SwapPanels());
        t.Add(KeyMods.Ctrl, ConsoleKey.B, "Show or hide the key bar", a => a.ToggleKeyBar());
        t.Add(KeyMods.Ctrl, ConsoleKey.L, "Folder size of the item under the cursor", a => a.ShowDirectorySize());

        // --- clipboard and the command line -----------------------------------
        t.Add(KeyMods.Ctrl, ConsoleKey.Insert, "Copy the tagged names to the clipboard", a => a.CopyNamesToClipboard(fullPaths: false), CommandLineEmpty);
        t.Add(
            KeyMods.Ctrl | KeyMods.Shift,
            ConsoleKey.Insert,
            "Copy the tagged names to the clipboard",
            a => a.CopyNamesToClipboard(fullPaths: false));
        t.Add(
            KeyMods.Alt | KeyMods.Shift,
            ConsoleKey.Insert,
            "Copy the tagged full names to the clipboard",
            a => a.CopyNamesToClipboard(fullPaths: true));
        t.Add(KeyMods.Ctrl, ConsoleKey.J, "Insert the name under the cursor", a => a.InsertName(fullPath: false));
        t.Add(KeyMods.Ctrl, ConsoleKey.F, "Insert the full name under the cursor", a => a.InsertName(fullPath: true));

        // Ctrl+[ and Ctrl+] are the bracket keys, which the console reports as Oem4 and Oem6 on
        // the US layout. A layout with the brackets elsewhere delivers a different virtual key, so
        // Application.HandleGlobalKey also accepts the chord by its character - the same
        // arrangement FilePanel uses for Ctrl+\.
        t.Add(KeyMods.Ctrl, ConsoleKey.Oem4, "Insert the left panel's folder path", a => a.InsertPanelPath(left: true));
        t.Add(KeyMods.Ctrl, ConsoleKey.Oem6, "Insert the right panel's folder path", a => a.InsertPanelPath(left: false));

        return t;
    }
}
