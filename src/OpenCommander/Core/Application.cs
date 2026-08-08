using System.Globalization;
using OpenCommander.Editor;
using OpenCommander.Files;
using OpenCommander.Input;
using OpenCommander.Operations;
using OpenCommander.Panels;
using OpenCommander.Rendering;
using OpenCommander.Shell;
using OpenCommander.Theming;
using OpenCommander.Ui;
using OpenCommander.Viewer;

namespace OpenCommander.Core;

/// <summary>
/// The shell: it owns the terminal, the input backend, the palette, the settings, the two file
/// panels, the screen furniture and the modal component stack, and it runs the event loop.
/// </summary>
/// <remarks>
/// <para>
/// The loop is synchronous and frame paced. Each tick it re-reads the console size, drains the input
/// backend completely, then repaints once - and only when something actually changed or the clock
/// rolled over. Input is routed to the topmost modal component when there is one, and otherwise to
/// the global key table, then the command line, then the active panel.
/// </para>
/// <para>
/// A modal component is run by <see cref="RunModal"/>, which pushes it and pumps the same loop until
/// it closes. Because the pump is shared, modals nest freely: a message box raised by a copy
/// operation simply lands above the progress dialog.
/// </para>
/// </remarks>
public sealed class Application : IAppContext, IDisposable
{
    /// <summary>How long the loop sleeps when the input backend had nothing to give.</summary>
    public const int IdleSleepMs = 15;

    private const int MaxErrorLines = 8;

    private readonly IInputBackend? _input;
    private readonly UiServices _ui;
    private readonly KeyBar _keyBar;
    private readonly CommandLine _commandLine;
    private readonly ClockWidget _clock;
    private readonly CommandHistory _history;
    private readonly KeyBindings _bindings;
    private readonly List<ModalFrame> _modals = [];
    private readonly IEditorClipboard _editorClipboard = new ClipboardBridge();

    private FilePanel _left;
    private FilePanel _right;
    private bool _leftActive = true;
    private bool _quit;
    private bool _dirty = true;
    private bool _panelsHidden;
    private bool _modifierHide;
    private string _clockText = string.Empty;
    private string? _workingDirectory;

    /// <summary>
    /// Builds the shell around an already created terminal.
    /// </summary>
    /// <param name="terminal">The terminal; may be headless.</param>
    /// <param name="settings">The user settings.</param>
    /// <param name="theme">The palette.</param>
    /// <param name="input">
    /// The input backend, or <see langword="null"/> for a non-interactive shell. Without one
    /// <see cref="Run"/> returns immediately and <see cref="RunModal"/> closes its component at once,
    /// which is what makes <c>--screenshot</c> safe.
    /// </param>
    public Application(Terminal terminal, Settings settings, Theme theme, IInputBackend? input)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);

        Terminal = terminal;
        Settings = settings;
        Theme = theme;
        _input = input;

        _ui = new UiServices(this);
        _bindings = KeyBindings.Default;
        _history = input is null ? new CommandHistory() : CommandHistory.Load();
        _keyBar = new KeyBar(theme);
        _commandLine = new CommandLine(theme, _history);
        _clock = new ClockWidget();

        _left = new FilePanel(this, theme, isLeft: true) { IsActive = true };
        _right = new FilePanel(this, theme, isLeft: false);

        Layout();
    }

    /// <summary>
    /// Creates the shell from a parsed command line: terminal, settings, palette, input backend and
    /// the initial panel folders.
    /// </summary>
    /// <param name="args">The parsed command line.</param>
    /// <returns>The ready to run application.</returns>
    public static Application Create(CommandLineArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Settings settings = Settings.Load();
        Terminal terminal = Terminal.Create(args.EffectiveWidth, args.EffectiveHeight);
        Theme theme = Theme.LoadOrDefault(args.ThemePath ?? settings.ThemePath);

        IInputBackend? input = args.Screenshot || terminal.IsHeadless
            ? null
            : InputBackendFactory.Create();

        var app = new Application(terminal, settings, theme, input);
        app.Initialize(args);
        return app;
    }

    // ------------------------------------------------------------- state

    /// <inheritdoc/>
    public Theme Theme { get; }

    /// <inheritdoc/>
    public Terminal Terminal { get; }

    /// <inheritdoc/>
    public Settings Settings { get; }

    /// <inheritdoc/>
    public IUiServices Ui => _ui;

    /// <summary>The left panel.</summary>
    public FilePanel LeftFilePanel => _left;

    /// <summary>The right panel.</summary>
    public FilePanel RightFilePanel => _right;

    /// <summary>The panel with the keyboard focus.</summary>
    public FilePanel ActiveFilePanel => _leftActive ? _left : _right;

    /// <summary>The panel without the keyboard focus.</summary>
    public FilePanel PassiveFilePanel => _leftActive ? _right : _left;

    /// <inheritdoc/>
    public IFilePanel LeftPanel => _left;

    /// <inheritdoc/>
    public IFilePanel RightPanel => _right;

    /// <inheritdoc/>
    public IFilePanel ActivePanel => ActiveFilePanel;

    /// <inheritdoc/>
    public IFilePanel PassivePanel => PassiveFilePanel;

    /// <summary>The command line widget.</summary>
    public CommandLine CommandLineWidget => _commandLine;

    /// <summary>The function key bar widget.</summary>
    public KeyBar KeyBarWidget => _keyBar;

    /// <summary>The clock widget.</summary>
    public ClockWidget Clock => _clock;

    /// <summary>The shell command history.</summary>
    public CommandHistory History => _history;

    /// <summary>The global key table this shell dispatches through.</summary>
    public KeyBindings Bindings => _bindings;

    /// <summary><see langword="true"/> when the command line has no text.</summary>
    public bool CommandLineIsEmpty => !Settings.ShowCommandLine || _commandLine.IsEmpty;

    /// <summary>The modal components currently on the stack, bottom first.</summary>
    public IReadOnlyList<IScreenComponent> Modals => [.. _modals.Select(static f => f.Component)];

    /// <summary><see langword="true"/> once <see cref="RequestQuit"/> has been called.</summary>
    public bool QuitRequested => _quit;

    /// <summary>The row the command line occupies, or <c>-1</c> when it is hidden.</summary>
    public int CommandLineRow { get; private set; } = -1;

    /// <summary>The row the key bar occupies, or <c>-1</c> when it is hidden.</summary>
    public int KeyBarRow { get; private set; } = -1;

    /// <summary>The whole screen.</summary>
    public Rect ScreenArea => new(0, 0, Terminal.Width, Terminal.Height);

    /// <summary>The area a modal component may use: everything but the key bar row.</summary>
    public Rect ModalArea =>
        new(0, 0, Terminal.Width, Math.Max(1, Terminal.Height - (Settings.ShowKeyBar ? 1 : 0)));

    /// <summary><see langword="true"/> while the panels are hidden (Ctrl+O, or Ctrl+Alt+Shift held).</summary>
    public bool PanelsHidden => _panelsHidden || _modifierHide;

    // ------------------------------------------------------------- startup

    /// <summary>
    /// Applies the command line to the freshly built shell: the start folders and the view mode.
    /// </summary>
    /// <param name="args">The parsed command line.</param>
    public void Initialize(CommandLineArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string start = ResolveDirectory(args.StartPath) ?? SafeCurrentDirectory();
        _left.Navigate(ResolveDirectory(args.LeftPath) ?? start);
        _right.Navigate(ResolveDirectory(args.RightPath) ?? start);

        if (args.ViewMode is int number)
        {
            PanelViewMode mode = PanelViewModes.FromNumber(number);
            _left.ViewMode = mode;
            _right.ViewMode = mode;
        }

        Layout();
        SyncWorkingDirectory();
        _dirty = true;
    }

    // ------------------------------------------------------------- layout

    /// <summary>
    /// Recomputes the panel rectangles and the rows the command line and the key bar sit on, and
    /// re-lays out every modal component.
    /// </summary>
    public void Layout()
    {
        int w = Terminal.Width;
        int h = Terminal.Height;

        int keyRows = Settings.ShowKeyBar ? 1 : 0;
        int cmdRows = Settings.ShowCommandLine ? 1 : 0;

        KeyBarRow = keyRows == 1 ? h - 1 : -1;
        CommandLineRow = cmdRows == 1 ? h - keyRows - 1 : -1;

        int panelHeight = Math.Max(0, h - keyRows - cmdRows);

        bool leftVisible = _left.IsVisible;
        bool rightVisible = _right.IsVisible;

        if (leftVisible && rightVisible)
        {
            int split = Math.Max(1, w / 2);
            _left.Bounds = new Rect(0, 0, split, panelHeight);
            _right.Bounds = new Rect(split, 0, Math.Max(0, w - split), panelHeight);
        }
        else if (leftVisible)
        {
            _left.Bounds = new Rect(0, 0, w, panelHeight);
        }
        else if (rightVisible)
        {
            _right.Bounds = new Rect(0, 0, w, panelHeight);
        }

        Rect modalArea = ModalArea;
        foreach (ModalFrame frame in _modals)
        {
            frame.Component.Layout(modalArea);
        }
    }

    // ------------------------------------------------------------- drawing

    /// <summary>
    /// Paints one whole frame into the terminal's back buffer: the panels, the clock, the command
    /// line, the modal stack and the key bar, and places the hardware cursor.
    /// </summary>
    public void DrawFrame()
    {
        ScreenBuffer buffer = Terminal.Buffer;
        buffer.Clear(Theme.Desktop);

        bool showPanels = !PanelsHidden;
        if (showPanels)
        {
            _left.Draw(buffer);
            _right.Draw(buffer);

            if (Settings.ShowClock)
            {
                _clock.Draw(buffer, Theme, 0);
            }
        }

        if (CommandLineRow >= 0)
        {
            _commandLine.Draw(buffer, CommandLineRow, ActiveFilePanel.CurrentPath);
        }

        foreach (ModalFrame frame in _modals)
        {
            frame.Component.Draw(buffer);
        }

        if (KeyBarRow >= 0)
        {
            _keyBar.Override = CurrentKeyBar();
            _keyBar.Draw(buffer, KeyBarRow);
        }

        PlaceCursor();
    }

    /// <summary>Paints a frame and flushes it to the console.</summary>
    public void RenderNow()
    {
        DrawFrame();
        Terminal.Render();
        _dirty = false;
    }

    /// <inheritdoc/>
    public void Redraw() => _dirty = true;

    private KeyBarLabels? CurrentKeyBar()
    {
        KeyMods mods = _keyBar.Modifiers;
        return _modals.Count > 0
            ? _modals[^1].Component.KeyBarFor(mods)
            : ActiveFilePanel.KeyBarFor(mods);
    }

    private void PlaceCursor()
    {
        if (_modals.Count > 0)
        {
            IScreenComponent top = _modals[^1].Component;
            switch (top)
            {
                case Dialog { WantsCursor: true } dialog:
                    Terminal.SetCursor(dialog.CursorX, dialog.CursorY, true);
                    return;

                case FileEditor editor:
                    Terminal.SetCursor(editor.CursorScreenX, editor.CursorScreenY, true);
                    return;

                default:
                    Terminal.SetCursor(0, 0, false);
                    return;
            }
        }

        if (CommandLineRow >= 0 && !PanelsHidden)
        {
            Terminal.SetCursor(_commandLine.CaretX, _commandLine.CaretY, true);
            return;
        }

        Terminal.SetCursor(0, 0, false);
    }

    // ------------------------------------------------------------- event loop

    /// <summary>
    /// Runs the event loop until the user quits.
    /// </summary>
    /// <returns>The process exit code; always <c>0</c>.</returns>
    public int Run()
    {
        if (_input is null)
        {
            // Nothing can drive the loop: render a single frame so a redirected run still produces
            // something, and stop.
            RenderNow();
            return 0;
        }

        Layout();
        _dirty = true;

        while (!_quit)
        {
            PumpOnce();
        }

        _history.Save();
        return 0;
    }

    /// <summary>
    /// Runs one component modally: it goes on top of the stack and the loop is pumped until it
    /// closes.
    /// </summary>
    /// <param name="component">The component; <see langword="null"/> does nothing.</param>
    public void RunModal(IScreenComponent component)
    {
        if (component is null)
        {
            return;
        }

        component.Layout(ModalArea);

        var frame = new ModalFrame(component);
        _modals.Add(frame);
        _dirty = true;

        try
        {
            if (_input is null)
            {
                return; // non-interactive: nothing can answer the dialog
            }

            while (!frame.IsDone && !_quit)
            {
                PumpOnce();
            }
        }
        finally
        {
            _modals.Remove(frame);
            _dirty = true;
        }
    }

    /// <summary>Drains the input backend once, updates the widgets and repaints when needed.</summary>
    private void PumpOnce()
    {
        bool activity = false;

        if (Terminal.SyncSize())
        {
            Layout();
            Terminal.Invalidate();
            _dirty = true;
        }

        if (_input is not null)
        {
            int guard = 512;
            while (guard-- > 0 && _input.TryRead(out InputEvent ev))
            {
                activity = true;
                Dispatch(ev);

                if (_quit)
                {
                    break;
                }
            }

            KeyMods mods = _input.CurrentModifiers;
            if (mods != _keyBar.Modifiers)
            {
                _keyBar.Modifiers = mods;
                UpdateTemporaryHide(mods);
                _dirty = true;
            }
        }

        string clock = _clock.Text;
        if (!string.Equals(clock, _clockText, StringComparison.Ordinal))
        {
            _clockText = clock;
            _dirty = true;
        }

        if (_dirty)
        {
            RenderNow();
        }

        if (!activity && !_quit)
        {
            Thread.Sleep(IdleSleepMs);
        }
    }

    /// <summary>Feeds the topmost modal component while a long operation owns the main thread.</summary>
    /// <param name="component">The component that must keep responding, usually a progress dialog.</param>
    private void PumpBackground(IScreenComponent component)
    {
        if (_input is not null)
        {
            int guard = 128;
            while (guard-- > 0 && _input.TryRead(out InputEvent ev))
            {
                if (ev.Kind == InputKind.Resize)
                {
                    Terminal.SyncSize();
                    Layout();
                    Terminal.Invalidate();
                }
                else
                {
                    component.HandleInput(ev);
                }
            }
        }

        RenderNow();
    }

    /// <summary>
    /// Feeds one input event through exactly the routing the event loop uses: the topmost modal
    /// component when there is one, otherwise the global key table, then the command line, then the
    /// active panel. Exposed so the shell can be driven without a real console.
    /// </summary>
    /// <param name="ev">The event.</param>
    public void ProcessInput(InputEvent ev) => Dispatch(ev);

    private void Dispatch(InputEvent ev)
    {
        switch (ev.Kind)
        {
            case InputKind.Resize:
                Terminal.SyncSize();
                Layout();
                Terminal.Invalidate();
                _dirty = true;

                if (_modals.Count > 0)
                {
                    FeedTop(ev);
                }

                return;

            case InputKind.ModifiersChanged:
                _keyBar.Modifiers = ev.Modifiers;
                UpdateTemporaryHide(ev.Modifiers);
                _dirty = true;
                return;

            case InputKind.Key:
            case InputKind.Mouse:
                _dirty = true;

                if (_modals.Count > 0)
                {
                    FeedTop(ev);
                }
                else if (ev.Kind == InputKind.Key)
                {
                    HandleGlobalKey(ev.Key);
                }
                else
                {
                    HandleGlobalMouse(ev.Mouse);
                }

                // Whatever the event did - Enter into a folder, Backspace out of one, a menu
                // command, a cd typed on the command line - the active panel may now be somewhere
                // else, so this is the one place that has to notice.
                SyncWorkingDirectory();
                return;

            default:
                return;
        }
    }

    private void FeedTop(InputEvent ev)
    {
        ModalFrame frame = _modals[^1];
        if (!frame.Component.HandleInput(ev))
        {
            frame.Closed = true;
        }
    }

    private void UpdateTemporaryHide(KeyMods mods)
    {
        if (_input is null || !_input.SupportsModifierTracking)
        {
            return;
        }

        bool hide = mods == (KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift);
        if (hide != _modifierHide)
        {
            _modifierHide = hide;
            _dirty = true;
        }
    }

    private void HandleGlobalKey(KeyEvent key)
    {
        if (_panelsHidden)
        {
            _panelsHidden = false; // Ctrl+O showed the desktop; any key brings the panels back
            return;
        }

        if (_bindings.TryHandle(key, this))
        {
            return;
        }

        if (CommandLineRow >= 0 && _commandLine.HandleKey(key, this))
        {
            return;
        }

        ActiveFilePanel.HandleKey(key, this);
    }

    private void HandleGlobalMouse(MouseEvent mouse)
    {
        if (_panelsHidden)
        {
            _panelsHidden = false;
            return;
        }

        if (KeyBarRow >= 0 && mouse.Y == KeyBarRow && mouse.IsPress)
        {
            int index = _keyBar.HitTest(mouse.X, Terminal.Width);
            if (index >= 0)
            {
                HandleGlobalKey(new KeyEvent((ConsoleKey)((int)ConsoleKey.F1 + index), '\0', mouse.Mods));
            }

            return;
        }

        FilePanel? target = PanelAt(mouse.X, mouse.Y);
        if (target is null)
        {
            return;
        }

        if (!target.IsActive)
        {
            SetActivePanel(target);
        }

        target.HandleMouse(mouse, this);
    }

    private FilePanel? PanelAt(int x, int y)
    {
        if (_left.IsVisible && _left.Bounds.Contains(x, y))
        {
            return _left;
        }

        return _right.IsVisible && _right.Bounds.Contains(x, y) ? _right : null;
    }

    // ------------------------------------------------------------- IAppContext

    /// <inheritdoc/>
    public void SwitchPanel()
    {
        if (!_left.IsVisible || !_right.IsVisible)
        {
            return;
        }

        SetActivePanel(_leftActive ? _right : _left);
    }

    /// <inheritdoc/>
    public void SwapPanels()
    {
        (_left, _right) = (_right, _left);

        // The focus stays on the same side of the screen, so the IsActive flags have to follow the
        // sides rather than the objects that just moved.
        _left.IsActive = _leftActive;
        _right.IsActive = !_leftActive;

        Layout();
        SyncWorkingDirectory();
        _dirty = true;
    }

    /// <inheritdoc/>
    public void RequestQuit() => _quit = true;

    /// <inheritdoc/>
    public void RefreshBothPanels()
    {
        _left.Reload();
        _right.Reload();
        _dirty = true;
    }

    /// <inheritdoc/>
    public void RunShellCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _history.Add(command);

        int code = CommandExecutor.Run(command, ActiveFilePanel.CurrentPath, Terminal, out string? changeDirectory);

        if (!string.IsNullOrEmpty(changeDirectory) && FileSystemProvider.DirectoryExists(changeDirectory))
        {
            ActiveFilePanel.Navigate(changeDirectory);
        }
        else if (code != CommandExecutor.DirectoryChanged)
        {
            RefreshBothPanels();
        }

        SyncWorkingDirectory();
        Terminal.Invalidate();
        _dirty = true;
    }

    /// <inheritdoc/>
    public void InsertIntoCommandLine(string text)
    {
        _commandLine.Insert(text);
        _dirty = true;
    }

    // ------------------------------------------------------------- commands

    /// <summary>Shows the help screen (F1).</summary>
    public void ShowHelp() => RunModal(new HelpScreen(Theme));

    /// <summary>Shows the horizontal menu (F9).</summary>
    public void ShowMainMenu() => new MenuBar(Theme, MainMenu.Build(this)).RunModal(this);

    /// <summary>Shows the user menu read from <c>usermenu.json</c> (F2).</summary>
    public void ShowUserMenu()
    {
        IReadOnlyList<UserMenuEntry>? entries = MainMenu.LoadUserMenu();

        if (entries is null || entries.Count == 0)
        {
            _ui.Message(
                "User menu",
                [
                    "No user menu is defined yet.",
                    "Create the file",
                    Shorten(MainMenu.UserMenuPath, 60),
                    "with entries like",
                    "{ \"items\": [ { \"title\": \"&Build\", \"command\": \"dotnet build\" } ] }",
                ],
                MessageButtons.Ok);
            return;
        }

        List<MenuItem> items = [.. entries.Select(static e => new MenuItem(e.Title, null))];
        int index = _ui.Menu("User menu", items);

        if (index >= 0 && index < entries.Count)
        {
            RunShellCommand(entries[index].Command);
        }
    }

    /// <summary>Views the item under the cursor; a folder is measured instead (F3).</summary>
    public void ViewCurrent()
    {
        FileEntry? entry = ActiveFilePanel.Current;
        if (entry is null || entry.IsParent)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            ShowDirectorySize();
            return;
        }

        FileViewer? viewer = FileViewer.TryOpen(Theme, Ui, entry.FullPath);
        if (viewer is null)
        {
            _ui.Error("View", "Cannot open " + entry.FullPath);
            return;
        }

        using (viewer)
        {
            RunModal(viewer);
        }
    }

    /// <summary>Edits the item under the cursor (F4).</summary>
    public void EditCurrent()
    {
        FileEntry? entry = ActiveFilePanel.Current;
        if (entry is null || entry.IsParent)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            NotImplemented("File attributes");
            return;
        }

        FileEditor? editor = FileEditor.TryOpen(Theme, Ui, entry.FullPath, _editorClipboard);
        if (editor is null)
        {
            _ui.Error("Edit", "Cannot open " + entry.FullPath);
            return;
        }

        RunModal(editor);
        RefreshBothPanels();
    }

    /// <summary>Asks for a name and opens the editor on a new file (Shift+F4).</summary>
    public void EditNewFile()
    {
        FilePanel panel = ActiveFilePanel;
        string? name = _ui.Input("Edit", "Create and edit the file:", string.Empty, "newfile");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string full = ResolvePath(panel.CurrentPath, name);
        var buffer = new TextBuffer { FilePath = full };
        var editor = new FileEditor(Theme, Ui, buffer, _editorClipboard);

        RunModal(editor);
        RefreshBothPanels();
    }

    /// <summary>Copies the tagged items, or the one under the cursor (F5 / Shift+F5).</summary>
    /// <param name="currentOnly">When set, the selection is ignored and the destination folder is this one.</param>
    public void CopyFiles(bool currentOnly) => Transfer(move: false, currentOnly);

    /// <summary>Moves or renames the tagged items, or the one under the cursor (F6 / Shift+F6).</summary>
    /// <param name="currentOnly">When set, the selection is ignored and the destination folder is this one.</param>
    public void MoveFiles(bool currentOnly) => Transfer(move: true, currentOnly);

    private void Transfer(bool move, bool currentOnly)
    {
        FilePanel panel = ActiveFilePanel;
        IReadOnlyList<FileEntry> sources = SourcesFor(panel, currentOnly);
        string title = move ? "Rename or move" : "Copy";

        if (sources.Count == 0)
        {
            _ui.Message(title, ["There is nothing to " + (move ? "move" : "copy") + "."], MessageButtons.Ok);
            return;
        }

        string what = sources.Count == 1
            ? "\"" + sources[0].Name + "\""
            : sources.Count.ToString(CultureInfo.InvariantCulture) + " items";

        string initial = currentOnly
            ? Path.Combine(panel.CurrentPath, sources[0].Name)
            : PassiveFilePanel.CurrentPath;

        string? answer = _ui.Input(title, $"{title} {what} to:", initial, move ? "move" : "copy");
        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        string destination = ResolvePath(panel.CurrentPath, answer);
        OperationOptions options = OperationOptionsFor(permanent: false);

        OperationResult result = RunWithProgress(
            title,
            (progress, report, overwrite, error) => move
                ? FileOperations.Move(sources, destination, options, progress, report, overwrite, error)
                : FileOperations.Copy(sources, destination, options, progress, report, overwrite, error));

        AfterOperation(title, result);
    }

    /// <summary>Creates a folder, nested paths included (F7).</summary>
    public void MakeDirectory()
    {
        FilePanel panel = ActiveFilePanel;
        string? name = _ui.Input("Create folder", "Create the folder:", string.Empty, "mkdir");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string full = ResolvePath(panel.CurrentPath, name);
        OperationResult result = FileOperations.CreateDirectory(full);

        if (result.HasErrors)
        {
            _ui.Error("Create folder", result.FirstError!.Message);
            return;
        }

        panel.Navigate(panel.CurrentPath, FirstSegment(name));
        _dirty = true;
    }

    /// <summary>Deletes the tagged items, or the one under the cursor (F8 / Shift+F8).</summary>
    /// <param name="permanent">When set, the recycle bin is bypassed.</param>
    public void DeleteFiles(bool permanent)
    {
        FilePanel panel = ActiveFilePanel;
        IReadOnlyList<FileEntry> sources = SourcesFor(panel, currentOnly: false);

        if (sources.Count == 0)
        {
            _ui.Message("Delete", ["There is nothing to delete."], MessageButtons.Ok);
            return;
        }

        OperationOptions options = OperationOptionsFor(permanent);
        bool toBin = options.UseRecycleBin && RecycleBin.IsAvailable;

        if (Settings.ConfirmDelete)
        {
            string what = sources.Count == 1
                ? "\"" + sources[0].Name + "\""
                : sources.Count.ToString(CultureInfo.InvariantCulture) + " items";

            string[] lines = toBin
                ? ["Delete " + what, "to the recycle bin?"]
                : ["Delete " + what, "permanently?"];

            if (!_ui.Confirm("Delete", lines, warning: true))
            {
                return;
            }
        }

        OperationResult result = RunWithProgress(
            "Delete",
            (progress, report, _, error) => FileOperations.Delete(sources, options, progress, report, error));

        AfterOperation("Delete", result);
    }

    /// <summary>Asks whether to quit and, when told to, stops the loop (F10).</summary>
    public void QuitCommand()
    {
        if (_ui.Confirm("Exit", ["Do you want to quit Open Commander?"]))
        {
            RequestQuit();
        }
    }

    /// <summary>Shows the built-in extras (F11).</summary>
    public void ShowPluginsMenu()
    {
        List<MenuItem> items =
        [
            new("&File search", "Alt+F7"),
            new("&Directory size", "Ctrl+L"),
            new("&Compare directories"),
            new("&Swap panels", "Ctrl+U"),
        ];

        switch (_ui.Menu("Extras", items))
        {
            case 0:
                FindFiles();
                break;

            case 1:
                ShowDirectorySize();
                break;

            case 2:
                CompareDirectories();
                break;

            case 3:
                SwapPanels();
                break;

            default:
                break;
        }
    }

    /// <summary>Lists the screens: the panels plus every open modal component (F12).</summary>
    public void ShowScreensList()
    {
        List<MenuItem> items = [new MenuItem("&1 Panels")];

        for (int i = 0; i < _modals.Count; i++)
        {
            items.Add(new MenuItem(
                string.Create(CultureInfo.InvariantCulture, $"&{i + 2} {DescribeComponent(_modals[i].Component)}")));
        }

        _ui.Menu("Screens", items);
    }

    /// <summary>Shows the drive menu for one panel and navigates it (Alt+F1 / Alt+F2).</summary>
    /// <param name="left">Whether to change the left panel.</param>
    public void ShowDriveMenu(bool left)
    {
        FilePanel panel = left ? _left : _right;
        IReadOnlyList<DriveList.DriveItem> drives = DriveList.Get();

        if (drives.Count == 0)
        {
            _ui.Message("Drives", ["No drives were reported."], MessageButtons.Ok);
            return;
        }

        var items = new List<MenuItem>(drives.Count);
        int selected = 0;
        string current = panel.CurrentPath;

        for (int i = 0; i < drives.Count; i++)
        {
            DriveList.DriveItem drive = drives[i];
            string label = drive.Label.Length > 0 ? drive.Label : drive.FileSystem;
            string caption = "&" + drive.Letter + "  " + Escape(label);

            string right = drive.IsReady
                ? $"{drive.Type}  {SizeFormatter.Short(drive.FreeBytes)} free"
                : drive.Type.ToString();

            items.Add(new MenuItem(caption, right));

            if (current.StartsWith(drive.Root, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        int index = _ui.Menu(left ? "Left drive" : "Right drive", items, selected);
        if (index < 0 || index >= drives.Count)
        {
            return;
        }

        panel.IsVisible = true;
        Layout();
        panel.Navigate(drives[index].Root);
        _dirty = true;
    }

    /// <summary>Runs a recursive file search and jumps to what the user picks (Alt+F7).</summary>
    public void FindFiles()
    {
        FilePanel panel = ActiveFilePanel;

        string? mask = _ui.Input("Find file", "A file mask or several file masks:", "*", "findmask");
        if (mask is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(mask))
        {
            mask = "*";
        }

        string? text = _ui.Input("Find file", "Containing text (empty for any):", string.Empty, "findtext");

        var options = new SearchOptions
        {
            Mask = mask,
            Text = string.IsNullOrEmpty(text) ? null : text,
            IncludeHidden = Settings.ShowHiddenFiles,
            Recursive = true,
            CollectMatches = true,
            MaxResults = 5000,
        };

        var dialog = new ProgressDialog(Theme, "Searching");
        dialog.Layout(ModalArea);
        dialog.ShowPercent = false;

        var frame = new ModalFrame(dialog);
        _modals.Add(frame);

        using var cancellation = new CancellationTokenSource();
        SearchResult result;
        int found = 0;
        long lastTick = 0;

        try
        {
            dialog.Update(panel.CurrentPath, "Searching...", 0, null);
            RenderNow();

            result = FileSearcher.Search(
                panel.CurrentPath,
                options,
                onMatch: _ => found++,
                onDirectory: directory =>
                {
                    long now = Environment.TickCount64;
                    if (now - lastTick < 60)
                    {
                        return;
                    }

                    lastTick = now;
                    dialog.Update(
                        Shorten(directory, Math.Max(10, dialog.ClientWidth - 2)),
                        found.ToString(CultureInfo.InvariantCulture) + " found",
                        0,
                        null);

                    PumpBackground(dialog);

                    if (dialog.CancelRequested)
                    {
                        cancellation.Cancel();
                    }
                },
                cancellation.Token);
        }
        finally
        {
            _modals.Remove(frame);
            _dirty = true;
        }

        if (result.Items.Count == 0)
        {
            _ui.Message("Find file", ["Nothing matched", mask + (options.Text is null ? string.Empty : " containing \"" + options.Text + "\"")], MessageButtons.Ok);
            return;
        }

        List<string> rows = [.. result.Items.Select(static e => e.FullPath)];
        int index = _ui.List("Find file - " + rows.Count + " match(es)", rows);

        if (index < 0 || index >= result.Items.Count)
        {
            return;
        }

        FileEntry hit = result.Items[index];
        string? directoryOfHit = Path.GetDirectoryName(hit.FullPath);

        if (!string.IsNullOrEmpty(directoryOfHit))
        {
            ActiveFilePanel.Navigate(directoryOfHit, hit.Name);
        }
    }

    /// <summary>Shows the shell command history and puts the pick on the command line (Alt+F8).</summary>
    public void ShowCommandHistory()
    {
        IReadOnlyList<string> all = _history.All;
        if (all.Count == 0)
        {
            _ui.Message("Command history", ["The history is empty."], MessageButtons.Ok);
            return;
        }

        int index = _ui.List("Command history", [.. all]);
        if (index >= 0 && index < all.Count)
        {
            _commandLine.Text = all[index];
            _dirty = true;
        }
    }

    /// <summary>Shows the active panel's folder history and navigates to the pick (Alt+F12).</summary>
    public void ShowFolderHistory()
    {
        IReadOnlyList<string> all = ActiveFilePanel.History;
        if (all.Count == 0)
        {
            _ui.Message("Folders history", ["The history is empty."], MessageButtons.Ok);
            return;
        }

        int index = _ui.List("Folders history", [.. all]);
        if (index >= 0 && index < all.Count)
        {
            ActiveFilePanel.Navigate(all[index]);
            _dirty = true;
        }
    }

    /// <summary>Measures the tagged folders, or the one under the cursor (Ctrl+L).</summary>
    public void ShowDirectorySize()
    {
        FilePanel panel = ActiveFilePanel;
        List<FileEntry> targets = [.. panel.SelectedOrCurrent.Where(static e => !e.IsParent)];

        if (targets.Count == 0)
        {
            _ui.Message("Folder size", ["There is nothing to measure."], MessageButtons.Ok);
            return;
        }

        var dialog = new ProgressDialog(Theme, "Folder size");
        dialog.Layout(ModalArea);

        var frame = new ModalFrame(dialog);
        _modals.Add(frame);

        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<KeyValuePair<FileEntry, DirectorySize>> results;

        try
        {
            dialog.Update(targets[0].Name, "Measuring...", 0, null);
            RenderNow();

            int done = 0;
            results = DirectorySizeCalculator.Calculate(
                targets,
                Settings.ShowHiddenFiles,
                (entry, size) =>
                {
                    done++;
                    dialog.Update(
                        entry.Name,
                        SizeFormatter.Grouped(size.Bytes) + " bytes",
                        (double)done / targets.Count,
                        null);

                    PumpBackground(dialog);

                    if (dialog.CancelRequested)
                    {
                        cancellation.Cancel();
                    }
                },
                cancellation.Token);
        }
        finally
        {
            _modals.Remove(frame);
            _dirty = true;
        }

        DirectorySize total = DirectorySizeCalculator.Total(results);

        _ui.Message(
            "Folder size",
            [
                targets.Count == 1 ? targets[0].Name : targets.Count.ToString(CultureInfo.InvariantCulture) + " items",
                SizeFormatter.Grouped(total.Bytes) + " bytes",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{total.Files} file(s) in {total.Directories} folder(s)") +
                    (total.Complete ? string.Empty : "  (incomplete)"),
            ],
            MessageButtons.Ok);
    }

    /// <summary>Tags the entries that are missing from, or newer than, the other panel's.</summary>
    public void CompareDirectories()
    {
        MarkNewer(ActiveFilePanel, PassiveFilePanel);
        MarkNewer(PassiveFilePanel, ActiveFilePanel);
        _dirty = true;

        static void MarkNewer(FilePanel source, FilePanel other)
        {
            var map = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (FileEntry entry in other.Entries)
            {
                if (!entry.IsParent && !entry.IsDirectory)
                {
                    map[entry.Name] = entry;
                }
            }

            foreach (FileEntry entry in source.Entries)
            {
                if (entry.IsParent || entry.IsDirectory)
                {
                    entry.Selected = false;
                    continue;
                }

                entry.Selected = !map.TryGetValue(entry.Name, out FileEntry? counterpart) ||
                    entry.Modified > counterpart.Modified;
            }
        }
    }

    /// <summary>Asks for a mask and tags or untags the matching entries (Gray + / Gray -).</summary>
    /// <param name="select">Whether to tag rather than untag.</param>
    public void SelectGroup(bool select)
    {
        string? mask = _ui.Input(select ? "Select" : "Deselect", "Files matching:", "*.*", "mask");
        if (string.IsNullOrWhiteSpace(mask))
        {
            return;
        }

        ActiveFilePanel.SelectByMask(mask, select);
        _dirty = true;
    }

    /// <summary>Inverts the tags on the files of the active panel (Gray *).</summary>
    public void InvertSelection()
    {
        ActiveFilePanel.InvertSelection(includeDirectories: false);
        _dirty = true;
    }

    /// <summary>Copies the tagged names to the clipboard (Ctrl+Ins / Alt+Shift+Ins).</summary>
    /// <param name="fullPaths">When set, full paths are copied instead of bare names.</param>
    public void CopyNamesToClipboard(bool fullPaths)
    {
        FilePanel panel = ActiveFilePanel;
        IReadOnlyList<FileEntry> items = panel.SelectedOrCurrent;

        string text = items.Count == 0
            ? panel.CurrentPath
            : string.Join(Environment.NewLine, items.Select(e => fullPaths ? e.FullPath : e.Name));

        Clipboard.Default.SetText(text);
    }

    /// <summary>Puts the name under the cursor on the command line (Ctrl+J / Ctrl+F).</summary>
    /// <param name="fullPath">When set, the full path is inserted.</param>
    public void InsertName(bool fullPath)
    {
        FileEntry? entry = ActiveFilePanel.Current;
        if (entry is null)
        {
            return;
        }

        string text = fullPath ? entry.FullPath : entry.Name;
        InsertIntoCommandLine(text.Contains(' ', StringComparison.Ordinal) ? "\"" + text + "\"" : text);
    }

    /// <summary>Hides or shows one panel (Ctrl+F1 / Ctrl+F2).</summary>
    /// <param name="left">Whether to toggle the left panel.</param>
    public void TogglePanel(bool left)
    {
        FilePanel panel = left ? _left : _right;
        FilePanel other = left ? _right : _left;

        if (panel.IsVisible && !other.IsVisible)
        {
            return; // never hide both this way; Ctrl+O does that
        }

        panel.IsVisible = !panel.IsVisible;

        if (!panel.IsVisible && panel.IsActive)
        {
            SetActivePanel(other);
        }

        Layout();
        _dirty = true;
    }

    /// <summary>Hides or shows the panel without the focus (Ctrl+P).</summary>
    public void TogglePassivePanel() => TogglePanel(left: !_leftActive);

    /// <summary>Hides both panels until the next key press (Ctrl+O).</summary>
    public void HidePanelsTemporarily()
    {
        _panelsHidden = true;
        _dirty = true;
    }

    /// <summary>Shows or hides the function key bar (Ctrl+B).</summary>
    public void ToggleKeyBar()
    {
        Settings.ShowKeyBar = !Settings.ShowKeyBar;
        Layout();
        _dirty = true;
    }

    /// <summary>Flips a boolean setting from a menu item and applies the consequences.</summary>
    /// <param name="apply">Writes the new value back into the settings.</param>
    /// <param name="current">The value before the toggle.</param>
    /// <param name="reload">Whether both panels must be re-read afterwards.</param>
    public void ToggleSetting(Action<bool> apply, bool current, bool reload)
    {
        ArgumentNullException.ThrowIfNull(apply);

        apply(!current);
        Layout();

        if (reload)
        {
            RefreshBothPanels();
        }

        // Switching "Auto change folder" on has to take effect at once rather than at the next
        // navigation, and switching it off has to stop the shell following the panel.
        SyncWorkingDirectory();
        _dirty = true;
    }

    /// <summary>Sets a panel's view mode.</summary>
    /// <param name="panel">The panel.</param>
    /// <param name="mode">The new view mode.</param>
    public void SetViewMode(FilePanel panel, PanelViewMode mode)
    {
        ArgumentNullException.ThrowIfNull(panel);
        panel.ViewMode = mode;
        _dirty = true;
    }

    /// <summary>Sets a panel's sort mode.</summary>
    /// <param name="panel">The panel.</param>
    /// <param name="mode">The new sort mode.</param>
    public void SetSort(FilePanel panel, SortMode mode)
    {
        ArgumentNullException.ThrowIfNull(panel);
        panel.SetSort(mode);
        _dirty = true;
    }

    /// <summary>Re-reads one panel.</summary>
    /// <param name="panel">The panel.</param>
    public void ReloadPanel(FilePanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        panel.Reload();
        _dirty = true;
    }

    /// <summary>Writes the settings and the command history to disk (Shift+F9).</summary>
    public void SaveSettings()
    {
        string path = OpenCommander.Core.Settings.SettingsFilePath;
        bool ok = Settings.SaveTo(path);
        _history.Save();

        _ui.Message(
            "Save setup",
            ok
                ? ["The settings were saved to", Shorten(path, 60)]
                : ["The settings could not be written to", Shorten(path, 60)],
            MessageButtons.Ok,
            warning: !ok);
    }

    /// <summary>Tells the user a command exists but has not been implemented yet.</summary>
    /// <param name="feature">The feature name shown in the title.</param>
    public void NotImplemented(string feature) =>
        _ui.Message(feature, ["Not implemented in this version."], MessageButtons.Ok);

    /// <summary>
    /// Points the process working directory at the active panel's folder, when
    /// <see cref="Core.Settings.AutoChangeDirectory"/> is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shell calls this after every input event, after the start folders are applied, on every
    /// active-panel change and whenever the option itself is toggled. Doing it here rather than in
    /// <see cref="FilePanel.Navigate"/> means no navigation route can forget it: Enter, Backspace,
    /// Ctrl+PgUp, the folder history, the drive menu and a <c>cd</c> typed on the command line all
    /// end up back in the event loop.
    /// </para>
    /// <para>
    /// It is cheap to call repeatedly: the last path handed to the operating system is remembered
    /// and an unchanged path does nothing. Clearing the option forgets that path, so switching the
    /// option back on re-applies it immediately.
    /// </para>
    /// <para>
    /// A folder that cannot become the working directory - one that has just been deleted, a denied
    /// path, a dead network share - is ignored on purpose: the panel is free to show folders the
    /// process may not enter, and navigation must never fail because the working directory could not
    /// follow. The remembered path is updated before the attempt, so a folder that failed once is
    /// not retried on every keystroke while the cursor sits in it.
    /// </para>
    /// </remarks>
    public void SyncWorkingDirectory()
    {
        if (!Settings.AutoChangeDirectory)
        {
            _workingDirectory = null;
            return;
        }

        string path = ActiveFilePanel.CurrentPath;
        if (string.IsNullOrEmpty(path) || string.Equals(path, _workingDirectory, PathComparison))
        {
            return;
        }

        _workingDirectory = path;

        try
        {
            Directory.SetCurrentDirectory(path);
        }
        catch (Exception e) when (e is DirectoryNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Silent by design; see the remarks above.
        }
    }

    // ------------------------------------------------------------- operations plumbing

    private OperationResult RunWithProgress(
        string title,
        Func<OperationProgress, Action, OverwritePrompt, ErrorPrompt, OperationResult> run)
    {
        var progress = new OperationProgress { Title = title };
        var dialog = new ProgressDialog(Theme, title, showSecondary: true);
        dialog.Layout(ModalArea);

        var frame = new ModalFrame(dialog);
        _modals.Add(frame);

        try
        {
            int width = Math.Max(10, dialog.ClientWidth - 2);

            void Report()
            {
                dialog.Update(
                    Shorten(progress.CurrentSource, width),
                    Shorten(progress.CurrentTarget, width),
                    progress.TotalFraction,
                    progress.FileFraction);

                PumpBackground(dialog);

                if (dialog.CancelRequested)
                {
                    progress.Cancel();
                }
            }

            Report();
            return run(progress, Report, AskOverwrite, AskError);
        }
        finally
        {
            _modals.Remove(frame);
            _dirty = true;
        }
    }

    private DialogResult AskOverwrite(FileEntry source, FileInfo target, ref string newName)
    {
        _ = newName;

        return _ui.Message(
            "Warning",
            [
                "The destination file already exists:",
                Shorten(target?.FullName ?? string.Empty, 60),
                "Overwrite it with \"" + (source?.Name ?? string.Empty) + "\"?",
            ],
            MessageButtons.Yes | MessageButtons.No | MessageButtons.All | MessageButtons.SkipAll | MessageButtons.Cancel,
            warning: true);
    }

    private DialogResult AskError(string operation, string path, Exception error) =>
        _ui.Message(
            "Error",
            [
                operation ?? "Operation",
                Shorten(path ?? string.Empty, 60),
                error is null ? "Unknown failure" : OperationResult.Describe(error),
            ],
            MessageButtons.Retry | MessageButtons.Skip | MessageButtons.SkipAll | MessageButtons.Cancel,
            warning: true);

    private void AfterOperation(string title, OperationResult result)
    {
        if (result.HasErrors)
        {
            var lines = new List<string> { title + " finished with errors:" };

            foreach (OperationError error in result.Errors.Take(MaxErrorLines))
            {
                lines.Add(Shorten(error.Path, 50) + ": " + error.Message);
            }

            if (result.Errors.Count > MaxErrorLines)
            {
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"... and {result.Errors.Count - MaxErrorLines} more"));
            }

            _ui.Message("Error", [.. lines], MessageButtons.Ok, warning: true);
        }

        if (Settings.ClearSelectionAfterOperation && !result.Cancelled)
        {
            _left.ClearSelection();
            _right.ClearSelection();
        }

        RefreshBothPanels();
    }

    private OperationOptions OperationOptionsFor(bool permanent)
    {
        OperationOptions options = OperationOptions.FromSettings(Settings);

        if (permanent)
        {
            options.UseRecycleBin = false;
        }

        return options;
    }

    // ------------------------------------------------------------- helpers

    private void SetActivePanel(FilePanel panel)
    {
        _leftActive = ReferenceEquals(panel, _left);
        _left.IsActive = _leftActive;
        _right.IsActive = !_leftActive;
        SyncWorkingDirectory();
        _dirty = true;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static IReadOnlyList<FileEntry> SourcesFor(FilePanel panel, bool currentOnly)
    {
        if (!currentOnly)
        {
            return panel.SelectedOrCurrent;
        }

        FileEntry? current = panel.Current;
        return current is null || current.IsParent ? [] : [current];
    }

    private static string DescribeComponent(IScreenComponent component) => component switch
    {
        FileViewer viewer => "View  " + Path.GetFileName(viewer.FilePath),
        FileEditor editor => "Edit  " + Path.GetFileName(editor.FilePath),
        HelpScreen => "Help",
        MenuBar => "Menu",
        Dialog dialog => dialog.Title,
        PopupMenu menu => menu.Title ?? "Menu",
        _ => component.GetType().Name,
    };

    private static string Escape(string text) =>
        text.Contains('&', StringComparison.Ordinal)
            ? text.Replace("&", "&&", StringComparison.Ordinal)
            : text;

    private static string Shorten(string? text, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0 || text.Length <= width)
        {
            return text ?? string.Empty;
        }

        return width <= 1 ? text[^1..] : ScreenBuffer.Ellipsis + text[^(width - 1)..];
    }

    private static string FirstSegment(string name)
    {
        string trimmed = name.Trim().Trim('"');
        int slash = trimmed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return slash <= 0 ? trimmed : trimmed[..slash];
    }

    private static string ResolvePath(string baseDirectory, string input)
    {
        string text = input.Trim().Trim('"');
        if (text.Length == 0)
        {
            return baseDirectory;
        }

        bool trailing = text[^1] == Path.DirectorySeparatorChar || text[^1] == Path.AltDirectorySeparatorChar;

        string full;
        try
        {
            full = Path.GetFullPath(text, baseDirectory);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return text;
        }

        if (trailing && !full.EndsWith(Path.DirectorySeparatorChar))
        {
            full += Path.DirectorySeparatorChar;
        }

        return full;
    }

    private static string? ResolveDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            string full = Path.GetFullPath(expanded);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string SafeCurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>Restores the console and releases the input backend.</summary>
    public void Dispose()
    {
        _input?.Dispose();
        Terminal.Dispose();
    }

    /// <summary>One entry of the modal stack, so a component that answered "close me" is not pumped again.</summary>
    private sealed class ModalFrame(IScreenComponent component)
    {
        public IScreenComponent Component { get; } = component;

        public bool Closed { get; set; }

        public bool IsDone => Closed || Component.IsClosed;
    }

    /// <summary>
    /// Lets the editor use the same clipboard as the dialogs: the editor was written against its own
    /// two-method interface, and this is the three-line adapter that joins them.
    /// </summary>
    private sealed class ClipboardBridge : IEditorClipboard
    {
        public string? GetText() => Clipboard.Default.GetText();

        public bool SetText(string text) => Clipboard.Default.SetText(text);
    }
}
