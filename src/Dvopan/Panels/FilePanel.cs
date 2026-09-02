using System.Globalization;
using Dvopan.Core;
using Dvopan.Files;
using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Theming;
using Dvopan.Ui;

namespace Dvopan.Panels;

/// <summary>
/// One of the two file panels: the directory listing, the cursor, the tag marks, and the drawing and
/// key handling that go with them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Geometry.</b> A panel of height <c>h</c> is drawn as a top frame carrying the centred path, a
/// column title row, <c>h - 5</c> file rows, a separator, the status line for the entry under the
/// cursor, and a bottom frame carrying the centred totals. Switching the status line off
/// (<see cref="Settings.ShowStatusBar"/>) hands its two rows to the file area and nothing else moves.
/// </para>
/// <para>
/// <b>Fill order.</b> Entries fill the stripes newspaper style: down the first stripe, then down the
/// next. The visible window therefore holds <c>rows * stripes</c> entries, Left and Right move the
/// cursor by one whole stripe (which in a single-stripe mode is exactly one screenful), and the panel
/// scrolls one row at a time when the cursor walks off the bottom.
/// </para>
/// <para>
/// <b>Cursor and scroll are independent.</b> Moving the cursor pulls the window along, but the mouse
/// wheel scrolls the window on its own and leaves the cursor where it is. Both are clamped on
/// every frame, so resizing the panel can never leave either one out of range.
/// </para>
/// </remarks>
public sealed class FilePanel : IFilePanel
{
    /// <summary>The narrowest panel that still gets drawn; anything smaller is painted blank.</summary>
    public const int MinWidth = 4;

    /// <summary>The shortest panel that still gets drawn; anything smaller is painted blank.</summary>
    public const int MinHeight = 5;

    /// <summary>How many rows one wheel notch scrolls.</summary>
    public const int WheelRows = 3;

    // The classic DOS-era console look draws the outer frame double and everything inside it - the
    // column dividers and the status separator - single, so the two styles are deliberately distinct.
    private const BoxStyle Frame = BoxStyle.Double;
    private const BoxStyle InnerFrame = BoxStyle.Single;
    private const string DateFormat = "MM/dd/yy";
    private const string TimeFormat = "HH:mm";

    private readonly Settings _ownSettings = new();
    private readonly PanelHistory _history = new();
    private readonly QuickSearch _quickSearch = new();

    // Tabs, each remembering a folder and the way it was being looked at. There is always at least
    // one; the strip is only drawn once there are two, so a single-tab panel keeps the classic look.
    private readonly List<PanelTab> _tabs = [new PanelTab()];
    private int _tabIndex;

    private List<FileEntry> _entries = [];
    private PanelColumnLayout? _layoutCache;
    private PanelViewMode _viewMode = PanelViewModes.Default;
    private string _path;
    private string? _error;
    private int _cursor;
    private int _top;
    private int _fileCount;
    private int _directoryCount;
    private long _totalBytes;
    private bool? _shiftSelection;

    /// <summary>Creates a panel.</summary>
    /// <param name="ctx">
    /// The application context, or <see langword="null"/> when the panel is built before the shell
    /// exists. Without it the panel falls back to its own default <see cref="Settings"/> and simply
    /// ignores the commands that need the shell (running a file, prompting for a mask).
    /// </param>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="isLeft">Whether this is the left panel.</param>
    /// <remarks>
    /// The constructor does no disk I/O: the listing stays empty until the first
    /// <see cref="Navigate"/> or <see cref="Reload"/>.
    /// </remarks>
    public FilePanel(IAppContext? ctx, Theme theme, bool isLeft)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Context = ctx;
        Theme = theme;
        IsLeft = isLeft;
        _path = SafeCurrentDirectory();
    }

    /// <summary>The application context; the shell assigns it when the panel was built before it.</summary>
    public IAppContext? Context { get; set; }

    /// <summary>The palette used for drawing.</summary>
    public Theme Theme { get; set; }

    /// <summary>Whether this is the left panel.</summary>
    public bool IsLeft { get; }

    /// <inheritdoc/>
    public Rect Bounds { get; set; }

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    /// <inheritdoc/>
    public bool IsVisible { get; set; } = true;

    /// <inheritdoc/>
    public string CurrentPath => _path;

    /// <inheritdoc/>
    public FileEntry? Current =>
        (uint)_cursor < (uint)_entries.Count ? _entries[_cursor] : null;

    /// <inheritdoc/>
    public IReadOnlyList<FileEntry> Entries => _entries;

    /// <inheritdoc/>
    public IReadOnlyList<FileEntry> SelectedOrCurrent
    {
        get
        {
            if (HasSelection)
            {
                var tagged = new List<FileEntry>();
                foreach (FileEntry e in _entries)
                {
                    if (e.Selected)
                    {
                        tagged.Add(e);
                    }
                }

                return tagged;
            }

            FileEntry? current = Current;
            return current is null || current.IsParent ? [] : [current];
        }
    }

    /// <inheritdoc/>
    public bool HasSelection
    {
        get
        {
            foreach (FileEntry e in _entries)
            {
                if (e.Selected)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The view mode (Ctrl+1..Ctrl+9).</summary>
    public PanelViewMode ViewMode
    {
        get => _viewMode;
        set => _viewMode = PanelViewModes.Normalize(value);
    }

    /// <summary>The sort key (Ctrl+F3..Ctrl+F11).</summary>
    public SortMode SortMode { get; private set; } = Files.SortMode.Name;

    /// <summary>Whether the sort key is inverted; pressing the same sort accelerator twice toggles it.</summary>
    public bool ReverseSort { get; private set; }

    /// <summary>The index of the entry under the cursor.</summary>
    public int CursorIndex
    {
        get => _cursor;
        set => SetCursor(value);
    }

    /// <summary>The index of the first entry in the visible window.</summary>
    public int TopIndex => _top;

    /// <summary>Why the directory could not be read, or <see langword="null"/>.</summary>
    public string? Error => _error;

    /// <summary>The directories this panel has shown, most recently visited first.</summary>
    public IReadOnlyList<string> History => _history.Items;

    /// <summary>The fast find state; exposed so the shell can show whether a search is running.</summary>
    public QuickSearch Search => _quickSearch;

    /// <summary>
    /// Columns at the right end of the top frame the path caption must keep clear. The shell sets
    /// this on the rightmost panel so the caption never runs underneath the clock.
    /// </summary>
    public int TitleReserve { get; set; }

    /// <summary>How many tabs the panel has; never less than one.</summary>
    public int TabCount => _tabs.Count;

    /// <summary>The index of the tab being shown.</summary>
    public int TabIndex => _tabIndex;

    /// <summary>The folder each tab is on, the current one included, in strip order.</summary>
    public IReadOnlyList<string> TabPaths
    {
        get
        {
            var paths = new string[_tabs.Count];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = i == _tabIndex ? _path : _tabs[i].Path;
            }

            return paths;
        }
    }

    /// <summary>How many file rows the panel currently has; zero when it is too short to show any.</summary>
    public int VisibleRows
    {
        get
        {
            Rect b = Bounds;
            if (b.Width < MinWidth || b.Height < RequiredHeight)
            {
                return 0;
            }

            return Math.Max(0, LastFileRow(b) - (b.Y + HeaderRows) + 1);
        }
    }

    /// <summary>
    /// The rows above the first file row: the top frame and the column titles, plus the tab strip
    /// once there is more than one tab.
    /// </summary>
    private int HeaderRows => _tabs.Count > 1 ? 3 : 2;

    /// <summary>The shortest panel that still gets drawn: <see cref="MinHeight"/> plus the tab strip when there is one.</summary>
    private int RequiredHeight => MinHeight + HeaderRows - 2;

    /// <summary>How many stripes the current view mode and width produce.</summary>
    public int VisibleStripes => LayoutFor(Math.Max(0, Bounds.Width - 2)).Stripes;

    /// <summary>How many entries fit on screen at once; never less than one.</summary>
    public int PageSize => Math.Max(1, VisibleRows * VisibleStripes);

    private Settings Settings => Context?.Settings ?? _ownSettings;

    private bool ShowStatusBar => Settings.ShowStatusBar;

    private int RowStep => Math.Max(1, VisibleRows);

    private static StringComparison NameComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Records a visit in the folder history, moving it to the front when already known.</summary>
    /// <param name="path">The directory that was visited.</param>
    public void PushHistory(string path) => _history.Push(path);

    /// <inheritdoc/>
    public void Navigate(string path, string? focusName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _path = FileSystemProvider.NormalizeDisplayPath(path);
        _tabs[_tabIndex].Path = _path;
        _quickSearch.Cancel();
        _shiftSelection = null;
        _top = 0;
        Load(focusName);
        _history.Push(_path);
    }

    /// <inheritdoc/>
    public void Reload(bool keepPosition = true)
    {
        string? focus = keepPosition ? Current?.Name : null;
        int top = _top;
        _quickSearch.Cancel();
        _shiftSelection = null;
        Load(focus);

        if (keepPosition)
        {
            _top = top;
            EnsureVisible();
        }
    }

    /// <inheritdoc/>
    public void ClearSelection()
    {
        foreach (FileEntry e in _entries)
        {
            e.Selected = false;
        }
    }

    /// <summary>
    /// Changes the sort key. Asking for the key that is already active toggles the direction, so
    /// pressing the same sort key twice reverses the order.
    /// </summary>
    /// <param name="mode">The sort key.</param>
    public void SetSort(SortMode mode)
    {
        if (mode == SortMode)
        {
            ReverseSort = !ReverseSort;
        }
        else
        {
            SortMode = mode;
            ReverseSort = false;
        }

        Resort();
    }

    /// <summary>Sets the sort key and direction outright.</summary>
    /// <param name="mode">The sort key.</param>
    /// <param name="reverse">Whether the key is inverted.</param>
    public void SetSort(SortMode mode, bool reverse)
    {
        SortMode = mode;
        ReverseSort = reverse;
        Resort();
    }

    // ------------------------------------------------------------------- tabs

    /// <summary>
    /// Opens a new tab on the current folder, right after this one, and switches to it (Ctrl+T).
    /// The new tab starts with the same sort and view, so it reads as a copy that can wander off.
    /// </summary>
    public void OpenTab()
    {
        SaveTab();
        var tab = new PanelTab
        {
            Path = _path,
            FocusName = Current?.Name,
            TopIndex = _top,
            SortMode = SortMode,
            ReverseSort = ReverseSort,
            ViewMode = _viewMode,
            Selected = SelectedNames(),
        };

        _tabs.Insert(_tabIndex + 1, tab);
        SwitchTab(_tabIndex + 1);
    }

    /// <summary>Closes the current tab and shows its left neighbour (Ctrl+W); the last tab stays.</summary>
    /// <returns><see langword="true"/> when a tab was closed.</returns>
    public bool CloseTab()
    {
        if (_tabs.Count <= 1)
        {
            return false;
        }

        _tabs.RemoveAt(_tabIndex);
        int next = Math.Max(0, _tabIndex - 1);
        _tabIndex = -1; // nothing to save: the tab being left no longer exists
        SwitchTab(next);
        return true;
    }

    /// <summary>Shows the tab to the right, wrapping round (Ctrl+Tab).</summary>
    public void NextTab() => SwitchTab((_tabIndex + 1) % _tabs.Count);

    /// <summary>Shows the tab to the left, wrapping round (Ctrl+Shift+Tab).</summary>
    public void PreviousTab() => SwitchTab((_tabIndex - 1 + _tabs.Count) % _tabs.Count);

    /// <summary>
    /// Shows one tab: the current one is put away with its folder, cursor, sort and view, and the
    /// target's are restored - its folder re-read, since it may have changed while out of sight.
    /// </summary>
    /// <param name="index">The tab to show; out of range does nothing.</param>
    public void SwitchTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        if (index == _tabIndex)
        {
            return;
        }

        SaveTab();
        _tabIndex = index;

        PanelTab tab = _tabs[index];
        SortMode = tab.SortMode;
        ReverseSort = tab.ReverseSort;
        _viewMode = PanelViewModes.Normalize(tab.ViewMode);
        Navigate(tab.Path, tab.FocusName);

        // The folder was re-read, so the tags come back by name: fifty tagged files must survive
        // a glance at another tab.
        if (tab.Selected.Count > 0)
        {
            foreach (FileEntry e in _entries)
            {
                e.Selected = !e.IsParent && tab.Selected.Contains(e.Name);
            }
        }

        _top = tab.TopIndex;
        EnsureVisible();
    }

    private HashSet<string> SelectedNames()
    {
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (FileEntry e in _entries)
        {
            if (e.Selected)
            {
                names.Add(e.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Replaces the tabs with a remembered set - one per folder that still exists - and shows the
    /// one that was showing. Folders that have gone since are dropped quietly; when none is left
    /// the panel keeps whatever it shows now.
    /// </summary>
    /// <param name="paths">The remembered folders, in strip order.</param>
    /// <param name="active">The index of the tab that was showing.</param>
    /// <returns>How many tabs were restored.</returns>
    public int RestoreTabs(IReadOnlyList<string> paths, int active)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var surviving = new List<string>();
        int activeSurviving = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            if (string.IsNullOrWhiteSpace(path) || !FileSystemProvider.DirectoryExists(path))
            {
                continue;
            }

            if (i == active)
            {
                activeSurviving = surviving.Count;
            }

            surviving.Add(FileSystemProvider.NormalizeDisplayPath(path));
        }

        if (surviving.Count == 0)
        {
            return 0;
        }

        _tabs.Clear();
        foreach (string path in surviving)
        {
            _tabs.Add(new PanelTab { Path = path });
        }

        _tabIndex = -1; // nothing to save: the old tabs are gone
        SwitchTab(Math.Clamp(activeSurviving, 0, _tabs.Count - 1));
        return _tabs.Count;
    }

    /// <summary>The caption a tab shows: the folder's own name, or the root itself for a root.</summary>
    /// <param name="path">The tab's folder.</param>
    /// <returns>The caption.</returns>
    public static string TabCaption(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? path : leaf; // a root has no leaf: "C:\" stays "C:\"
    }

    private void SaveTab()
    {
        if ((uint)_tabIndex >= (uint)_tabs.Count)
        {
            return;
        }

        PanelTab tab = _tabs[_tabIndex];
        tab.Path = _path;
        tab.FocusName = Current?.Name;
        tab.TopIndex = _top;
        tab.SortMode = SortMode;
        tab.ReverseSort = ReverseSort;
        tab.ViewMode = _viewMode;
        tab.Selected = SelectedNames();
    }

    /// <summary>Tags or untags every entry matching a mask list.</summary>
    /// <param name="maskList">A comma or semicolon separated mask list, e.g. <c>"*.cs,*.md"</c>.</param>
    /// <param name="selected">Whether to tag or untag.</param>
    /// <returns>How many entries changed.</returns>
    public int SelectByMask(string? maskList, bool selected)
    {
        if (string.IsNullOrWhiteSpace(maskList))
        {
            return 0;
        }

        int changed = 0;
        foreach (FileEntry e in _entries)
        {
            if (e.IsParent || e.IsDirectory || e.Selected == selected)
            {
                continue;
            }

            if (FileMask.IsMatchAny(e.Name, maskList))
            {
                e.Selected = selected;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>Flips the tag on every entry.</summary>
    /// <param name="includeDirectories">
    /// When set, directories are flipped too (Ctrl+Gray*); otherwise only files are (Gray*).
    /// </param>
    public void InvertSelection(bool includeDirectories)
    {
        foreach (FileEntry e in _entries)
        {
            if (e.IsParent || (!includeDirectories && e.IsDirectory))
            {
                continue;
            }

            e.Selected = !e.Selected;
        }
    }

    /// <summary>Tags every file, leaving directories and <c>".."</c> alone (Ctrl+A).</summary>
    public void SelectAllFiles()
    {
        foreach (FileEntry e in _entries)
        {
            if (!e.IsParent && !e.IsDirectory)
            {
                e.Selected = true;
            }
        }
    }

    /// <summary>Tags or untags every entry sharing the extension of the entry under the cursor.</summary>
    /// <param name="selected">Whether to tag or untag.</param>
    /// <returns>How many entries changed.</returns>
    public int SelectSameExtension(bool selected)
    {
        FileEntry? current = Current;
        if (current is null || current.IsParent)
        {
            return 0;
        }

        string extension = current.Extension;
        int changed = 0;
        foreach (FileEntry e in _entries)
        {
            if (e.IsParent || e.IsDirectory || e.Selected == selected)
            {
                continue;
            }

            if (string.Equals(e.Extension, extension, StringComparison.Ordinal))
            {
                e.Selected = selected;
                changed++;
            }
        }

        return changed;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The captions live in <see cref="KeyBarSets"/> and nowhere else: the panel only picks the row
    /// for the modifiers currently held, so the bar the shell draws and the table the help screen
    /// reads can never drift apart.
    /// </remarks>
    public KeyBarLabels? KeyBarFor(KeyMods mods) => KeyBarSets.ForPanels(mods);

    // ---------------------------------------------------------------- drawing

    /// <inheritdoc/>
    public void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (!IsVisible)
        {
            return;
        }

        Rect b = Bounds;
        if (b.IsEmpty)
        {
            return;
        }

        if (b.Width < MinWidth || b.Height < RequiredHeight)
        {
            buffer.Fill(b, ' ', Theme.PanelText);
            return;
        }

        int x = b.X;
        int y = b.Y;
        int w = b.Width;
        int h = b.Height;
        int inner = w - 2;
        int headerRow = y + HeaderRows - 1;
        int firstRow = y + HeaderRows;
        int lastRow = LastFileRow(b);
        int rows = Math.Max(0, lastRow - firstRow + 1);
        PanelColumnLayout layout = LayoutFor(inner);
        int stripes = layout.Stripes;
        int page = Math.Max(1, rows * stripes);

        ClampScroll(page);

        CellStyle box = IsActive ? Theme.PanelBoxActive : Theme.PanelBox;

        buffer.Fill(b, ' ', Theme.PanelText);
        DrawFrame(buffer, b, box);
        DrawTitle(buffer, b);

        if (_tabs.Count > 1)
        {
            DrawTabs(buffer, b, box);
        }

        char vertical = BoxChars.Vertical(InnerFrame);
        foreach (int sep in layout.Separators)
        {
            buffer.VLine(x + 1 + sep, headerRow, rows + 1, vertical, box);
        }

        foreach (PanelColumn column in layout.Columns)
        {
            buffer.WriteFixed(
                x + 1 + column.X,
                headerRow,
                column.Width,
                column.Header,
                Theme.PanelColumnTitle,
                HAlign.Center);
        }

        // The sort mode is marked with a letter in the top-left header cell: lowercase ascending,
        // uppercase descending ("n" = by name, "s" = by size, ...).
        buffer.Set(x + 1, headerRow, SortIndicator(), Theme.PanelText);

        if (rows > 0)
        {
            if (_error is not null)
            {
                buffer.WriteFixed(
                    x + 1,
                    firstRow + (rows / 2),
                    inner,
                    _error,
                    Theme.PanelEmpty,
                    HAlign.Center);
            }
            else
            {
                DrawEntries(buffer, b, layout, firstRow, rows, box);
            }

            DrawScrollBar(buffer, b, firstRow, rows, page);
        }

        if (ShowStatusBar)
        {
            DrawStatusLine(buffer, b);
        }

        DrawTotals(buffer, b);
        _quickSearch.Draw(buffer, x + 1, y + h - 1, inner, Theme.QuickSearch);
    }

    private void DrawFrame(ScreenBuffer buffer, Rect b, CellStyle box)
    {
        int x = b.X;
        int y = b.Y;
        int w = b.Width;
        int inner = w - 2;
        int right = x + w - 1;
        int bottom = y + b.Height - 1;
        char horizontal = BoxChars.Horizontal(Frame);
        char vertical = BoxChars.Vertical(Frame);

        buffer.Set(x, y, BoxChars.TopLeft(Frame), box);
        buffer.HLine(x + 1, y, inner, horizontal, box);
        buffer.Set(right, y, BoxChars.TopRight(Frame), box);

        // The double verticals run all the way down to the bottom corners - through the status
        // row too, which sits inside the frame; the ╟──╢ separator below overwrites its own
        // two edge cells with the tees.
        for (int row = y + 1; row < bottom; row++)
        {
            buffer.Set(x, row, vertical, box);
            buffer.Set(right, row, vertical, box);
        }

        if (ShowStatusBar)
        {
            // A single-line separator meeting the double vertical edges with tees: ╟────╢.
            int separator = bottom - 2;
            buffer.Set(x, separator, BoxChars.LeftTee(BoxStyle.SingleV), box);
            buffer.HLine(x + 1, separator, inner, BoxChars.Horizontal(InnerFrame), box);
            buffer.Set(right, separator, BoxChars.RightTee(BoxStyle.SingleV), box);
        }

        buffer.Set(x, bottom, BoxChars.BottomLeft(Frame), box);
        buffer.HLine(x + 1, bottom, inner, horizontal, box);
        buffer.Set(right, bottom, BoxChars.BottomRight(Frame), box);
    }

    private void DrawTitle(ScreenBuffer buffer, Rect b)
    {
        int available = b.Width - 4 - TitleReserve;
        if (available < 3)
        {
            return;
        }

        string path = FileSystemProvider.NormalizeDisplayPath(CurrentPath);
        int maxPath = available - 2;
        if (path.Length > maxPath)
        {
            // Long paths lose their head, not their tail: the folder you are in matters most.
            path = ScreenBuffer.Ellipsis + path[^(maxPath - 1)..];
        }

        string title = " " + path + " ";
        CellStyle style = IsActive ? Theme.PanelTitleActive : Theme.PanelTitle;

        // Centred, but never into the reserved corner: a long path slides left instead of running
        // underneath the clock.
        int titleX = b.X + ((b.Width - title.Length) / 2);
        int rightLimit = b.X + b.Width - 1 - TitleReserve;
        titleX = Math.Max(b.X + 1, Math.Min(titleX, rightLimit - title.Length + 1));
        buffer.Write(titleX, b.Y, title, style);
    }

    private void DrawEntries(
        ScreenBuffer buffer,
        Rect b,
        PanelColumnLayout layout,
        int firstRow,
        int rows,
        CellStyle box)
    {
        int left = b.X + 1;
        int count = _entries.Count;
        int fields = layout.FieldsPerStripe;
        char vertical = BoxChars.Vertical(InnerFrame);

        for (int s = 0; s < layout.Stripes; s++)
        {
            for (int r = 0; r < rows; r++)
            {
                int index = _top + (s * rows) + r;
                if (index >= count)
                {
                    break;
                }

                FileEntry entry = _entries[index];
                bool onCursor = IsActive && index == _cursor;
                CellStyle style = StyleFor(entry, onCursor);
                int rowY = firstRow + r;

                for (int f = 0; f < fields; f++)
                {
                    PanelColumn column = layout.Column(s, f);
                    if (f > 0)
                    {
                        // The dividers that fall inside the cursor row take the bar's colour so
                        // it reads as one unbroken block; the dividers *between* stripes keep the
                        // frame colour and are left alone.
                        buffer.Set(left + column.X - 1, rowY, vertical, onCursor ? style : box);
                    }

                    buffer.WriteFixed(
                        left + column.X,
                        rowY,
                        column.Width,
                        CellText(entry, column.Kind),
                        style,
                        column.Align);
                }
            }
        }
    }

    /// <summary>
    /// The tab strip on the row under the top frame: each tab's folder name, the shown one in the
    /// active-title colours, single dividers between. When the strip is wider than the panel it
    /// shifts right just enough for the shown tab to be on screen. The cells each caption
    /// occupies are remembered for the mouse.
    /// </summary>
    private void DrawTabs(ScreenBuffer buffer, Rect b, CellStyle box)
    {
        int row = b.Y + 1;
        foreach ((int x0, int x1, int index, string caption) in TabCells(b))
        {
            if (x0 > b.X + 1)
            {
                buffer.Set(x0 - 1, row, BoxChars.Vertical(InnerFrame), box);
            }

            CellStyle style = index == _tabIndex ? Theme.PanelTitleActive : Theme.PanelTitle;
            buffer.WriteFixed(x0, row, x1 - x0, caption, style);
        }
    }

    /// <summary>
    /// Where each visible caption sits on the strip row - computed afresh for drawing and for every
    /// click alike, so a click that lands in the same input pump as the key that changed the strip
    /// still hits what is really there.
    /// </summary>
    private List<(int X0, int X1, int Index, string Caption)> TabCells(Rect b)
    {
        var cells = new List<(int, int, int, string)>();
        int left = b.X + 1;
        int inner = b.Width - 2;
        if (inner <= 0)
        {
            return cells;
        }

        var captions = new string[_tabs.Count];
        for (int i = 0; i < captions.Length; i++)
        {
            captions[i] = " " + TabCaption(i == _tabIndex ? _path : _tabs[i].Path) + " ";
        }

        // Slide the window right until the shown tab fits.
        int first = 0;
        while (first < _tabIndex && StripWidth(captions, first, _tabIndex) > inner)
        {
            first++;
        }

        int x = left;
        for (int i = first; i < captions.Length && x < left + inner; i++)
        {
            if (i > first)
            {
                x++; // the divider
            }

            int width = Math.Min(captions[i].Length, left + inner - x);
            if (width <= 0)
            {
                break;
            }

            cells.Add((x, x + width, i, captions[i]));
            x += width;
        }

        return cells;
    }

    private static int StripWidth(string[] captions, int first, int last)
    {
        int width = 0;
        for (int i = first; i <= last; i++)
        {
            width += captions[i].Length + (i > first ? 1 : 0);
        }

        return width;
    }

    private void DrawScrollBar(ScreenBuffer buffer, Rect b, int firstRow, int rows, int page)
    {
        int count = _entries.Count;
        if (count <= page || rows <= 0)
        {
            return;
        }

        int thumb = Math.Clamp((int)((long)rows * page / count), 1, rows);
        int maxTop = count - page;
        int pos = maxTop <= 0 ? 0 : (int)((long)(rows - thumb) * _top / maxTop);
        pos = Math.Clamp(pos, 0, rows - thumb);

        int sx = b.X + b.Width - 1;
        for (int i = 0; i < rows; i++)
        {
            char glyph = i >= pos && i < pos + thumb ? BoxChars.ScrollBarThumb : BoxChars.ScrollBarTrack;
            buffer.Set(sx, firstRow + i, glyph, Theme.PanelScrollBar);
        }
    }

    private void DrawStatusLine(ScreenBuffer buffer, Rect b)
    {
        int row = b.Y + b.Height - 2;
        int inner = b.Width - 2;
        buffer.Fill(new Rect(b.X + 1, row, inner, 1), ' ', Theme.PanelStatus);

        FileEntry? current = Current;
        if (current is null || b.Width < 4)
        {
            return;
        }

        // Only the inner cells are ours: the frame's double verticals stay on both edges, so the
        // name starts flush against the left ║ - the same as the file rows - and the
        // size/date/time block ends on the second-to-last column, flush against the right ║.
        int nameX = b.X + 1;
        int lastX = b.X + b.Width - 2;
        string right = StatusRightText(current);
        int rightWidth = Math.Min(right.Length, inner);
        int rightX = lastX - rightWidth + 1;
        int nameWidth = Math.Max(0, rightX - nameX - 1);

        buffer.WriteFixed(nameX, row, nameWidth, current.Name, Theme.PanelStatusFile);
        buffer.WriteFixed(rightX, row, rightWidth, right, Theme.PanelStatus, HAlign.Right);
    }

    private void DrawTotals(ScreenBuffer buffer, Rect b)
    {
        string totals = " " + TotalsText() + " ";
        int row = b.Y + b.Height - 1;
        int available = b.Width - 2;

        // Once anything is tagged the line shows the selection, painted in yellow so that
        // pressing Ins is visible at the bottom too.
        CellStyle style = HasSelection ? Theme.PanelSelectedTotals : Theme.PanelTotals;

        if (totals.Length > available)
        {
            buffer.WriteFixed(b.X + 1, row, available, totals, style, HAlign.Center);
            return;
        }

        buffer.Write(b.X + ((b.Width - totals.Length) / 2), row, totals, style);
    }

    /// <summary>
    /// The totals string drawn on the bottom frame: the whole listing normally, and the tagged
    /// entries as soon as anything is tagged.
    /// </summary>
    /// <returns>The text, without the space padding the frame adds.</returns>
    public string TotalsText()
    {
        long bytes = 0;
        int files = 0;
        int folders = 0;
        bool selection = false;

        foreach (FileEntry e in _entries)
        {
            if (!e.Selected)
            {
                continue;
            }

            selection = true;
            if (e.IsDirectory)
            {
                folders++;
            }
            else
            {
                files++;
                bytes += e.Size;
            }
        }

        if (selection)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Selected: {SizeFormatter.Short(bytes)}, files: {files}, folders: {folders}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Bytes: {SizeFormatter.Short(_totalBytes)}, files: {_fileCount}, folders: {_directoryCount}");
    }

    /// <summary>The right hand block of the status line: size, date and time, two spaces apart.</summary>
    /// <param name="entry">The entry under the cursor.</param>
    /// <returns>The text.</returns>
    public static string StatusRightText(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string size = DirectoryMarker(entry) ?? SizeFormatter.Grouped(entry.Size);
        string date = entry.Modified.ToString(DateFormat, CultureInfo.InvariantCulture);
        string time = entry.Modified.ToString(TimeFormat, CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{size}  {date}  {time}");
    }

    /// <summary>The text one column shows for one entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="kind">The column kind.</param>
    /// <returns>The text; names start flush at the column's first cell, with no leading padding.</returns>
    public static string CellText(FileEntry entry, PanelColumnKind kind)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return kind switch
        {
            PanelColumnKind.Size => SizeCellText(entry),
            PanelColumnKind.Date => entry.Modified.ToString(DateFormat, CultureInfo.InvariantCulture),
            PanelColumnKind.Time => entry.Modified.ToString(TimeFormat, CultureInfo.InvariantCulture),
            PanelColumnKind.Attributes => entry.AttributeString,
            _ => entry.Name,
        };
    }

    /// <summary>
    /// The word a directory shows where a file shows its size: <c>"Up"</c>, <c>"Folder"</c> or
    /// <c>"Symlink"</c>, and <see langword="null"/> for anything that really has a byte count.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The marker, or <see langword="null"/> for a plain file.</returns>
    /// <remarks>
    /// The Size column and the status line both go through here, so the two can never end up
    /// disagreeing about the same entry. The panel shows the bare word in both places - no angle
    /// brackets - and the word itself is decided in exactly one place.
    /// </remarks>
    private static string? DirectoryMarker(FileEntry entry)
    {
        if (entry.IsParent)
        {
            return "Up";
        }

        if (!entry.IsDirectory)
        {
            return null;
        }

        return entry.IsReparsePoint ? "Symlink" : "Folder";
    }

    /// <summary>
    /// The sort letter shown in the top-left header cell: <c>n</c> by name, <c>x</c> by
    /// extension, <c>w</c> by write time, <c>s</c> by size, <c>u</c> unsorted, <c>c</c> by creation
    /// time, <c>a</c> by access time, <c>z</c> by description, <c>o</c> by owner - uppercase when
    /// the sort is reversed.
    /// </summary>
    /// <returns>The letter.</returns>
    public char SortIndicator()
    {
        char letter = SortMode switch
        {
            Files.SortMode.Name => 'n',
            Files.SortMode.Extension => 'x',
            Files.SortMode.Modified => 'w',
            Files.SortMode.Size => 's',
            Files.SortMode.Unsorted => 'u',
            Files.SortMode.Created => 'c',
            Files.SortMode.Accessed => 'a',
            Files.SortMode.Description => 'z',
            _ => 'o',
        };

        return ReverseSort ? char.ToUpperInvariant(letter) : letter;
    }

    private static string SizeCellText(FileEntry entry)
    {
        // The bare word, no brackets: the column is right aligned, so letting HAlign.Right do the
        // work is what keeps "Up" flush with the byte counts above and below it.
        string? marker = DirectoryMarker(entry);
        if (marker is not null)
        {
            return marker;
        }

        // The size column shows plain ungrouped digits while they fit and the compact form once
        // they do not; the grouped form belongs to the status and totals lines, not the column.
        string exact = entry.Size.ToString(CultureInfo.InvariantCulture);
        return exact.Length <= PanelColumn.SizeWidth ? exact : SizeFormatter.Short(entry.Size);
    }

    /// <summary>
    /// Picks the colour for one entry, in the fixed precedence
    /// cursor &gt; tagged &gt; hidden or system &gt; directory &gt; archive &gt; executable &gt; plain file.
    /// </summary>
    /// <remarks>
    /// Hidden and system deliberately outrank directory. A drive root's <c>$Recycle.Bin</c>,
    /// <c>ProgramData</c> and <c>System Volume Information</c> are all dim grey even though they are
    /// folders - the dimming is how the panel says "not your business", and a folder is exactly the
    /// kind of entry that most needs to recede. Ranking directory first instead made every
    /// <c>.git</c> and <c>.vs</c> in a source tree shout in bright white.
    /// </remarks>
    /// <param name="entry">The entry.</param>
    /// <param name="onCursor">Whether the entry is under the cursor of the focused panel.</param>
    /// <returns>The style.</returns>
    public CellStyle StyleFor(FileEntry entry, bool onCursor)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (onCursor)
        {
            return entry.Selected ? Theme.PanelCursorSelected : Theme.PanelCursor;
        }

        if (entry.Selected)
        {
            return Theme.PanelSelectedFile;
        }

        if (entry.IsHidden)
        {
            return Theme.PanelHidden;
        }

        if (entry.IsDirectory)
        {
            return Theme.PanelDirectory;
        }

        if (entry.IsArchive)
        {
            return Theme.PanelArchive;
        }

        return entry.IsExecutable ? Theme.PanelExecutable : Theme.PanelText;
    }

    // ------------------------------------------------------------ key handling

    /// <inheritdoc/>
    public bool HandleKey(KeyEvent key, IAppContext ctx)
    {
        IAppContext? app = ctx ?? Context;
        KeyMods mods = key.Mods;

        if ((mods & KeyMods.Shift) == 0)
        {
            // Select-versus-deselect is decided once per Shift gesture; any other key ends it.
            _shiftSelection = null;
        }

        if (HandleQuickSearchKey(key))
        {
            return true;
        }

        if (StartsQuickSearch(key))
        {
            _quickSearch.Append(key.Ch, DateTime.UtcNow);
            JumpToQuickSearchMatch(_cursor);
            return true;
        }

        bool none = mods == KeyMods.None;
        bool shift = mods == KeyMods.Shift;
        bool ctrl = mods == KeyMods.Ctrl;
        bool ctrlShift = mods == (KeyMods.Ctrl | KeyMods.Shift);
        bool ctrlAlt = mods == (KeyMods.Ctrl | KeyMods.Alt);

        if (ctrl && (key.Key == ConsoleKey.Oem5 || key.Ch == '\\' || key.Ch == '\u001c'))
        {
            Navigate(FileSystemProvider.GetRoot(CurrentPath));
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow when none:
                return MoveTo(_cursor - 1);
            case ConsoleKey.UpArrow when shift:
                return ShiftMove(_cursor - 1, inclusive: false);

            case ConsoleKey.DownArrow when none:
                return MoveTo(_cursor + 1);
            case ConsoleKey.DownArrow when shift:
                return ShiftMove(_cursor + 1, inclusive: false);

            case ConsoleKey.LeftArrow when none:
                return MoveTo(_cursor - RowStep);
            case ConsoleKey.LeftArrow when shift:
                return ShiftMove(_cursor - RowStep, inclusive: false);

            case ConsoleKey.RightArrow when none:
                return MoveTo(_cursor + RowStep);
            case ConsoleKey.RightArrow when shift:
                return ShiftMove(_cursor + RowStep, inclusive: false);

            case ConsoleKey.Home when none:
                return MoveTo(0);
            case ConsoleKey.Home when shift:
                return ShiftMove(0, inclusive: true);

            case ConsoleKey.End when none:
                return MoveTo(_entries.Count - 1);
            case ConsoleKey.End when shift:
                return ShiftMove(_entries.Count - 1, inclusive: true);

            case ConsoleKey.PageUp when none:
                return MoveTo(_cursor - PageSize);
            case ConsoleKey.PageUp when shift:
                return ShiftMove(_cursor - PageSize, inclusive: false);
            case ConsoleKey.PageUp when ctrl:
                GoToParent();
                return true;

            case ConsoleKey.PageDown when none:
                return MoveTo(_cursor + PageSize);
            case ConsoleKey.PageDown when shift:
                return ShiftMove(_cursor + PageSize, inclusive: false);
            case ConsoleKey.PageDown when ctrl:
                EnterCurrentDirectory();
                return true;

            case ConsoleKey.Enter when none:
                Activate(app);
                return true;
            case ConsoleKey.Enter when ctrl:
                InsertCurrentName(app);
                return true;

            case ConsoleKey.Backspace when none:
                GoToParent();
                return true;

            case ConsoleKey.Insert when none:
                ToggleTagAndAdvance();
                return true;

            case ConsoleKey.Add when none:
                PromptSelectByMask(app, selected: true);
                return true;
            case ConsoleKey.Add when shift:
                SetAllTags(true);
                return true;
            case ConsoleKey.Add when ctrl:
                SelectSameExtension(true);
                return true;

            case ConsoleKey.Subtract when none:
                PromptSelectByMask(app, selected: false);
                return true;
            case ConsoleKey.Subtract when shift:
                SetAllTags(false);
                return true;
            case ConsoleKey.Subtract when ctrl:
                SelectSameExtension(false);
                return true;

            case ConsoleKey.Multiply when none:
                InvertSelection(includeDirectories: false);
                return true;
            case ConsoleKey.Multiply when ctrl:
                InvertSelection(includeDirectories: true);
                return true;

            case ConsoleKey.A when ctrl:
                SelectAllFiles();
                return true;

            case ConsoleKey.R when ctrl:
                Reload();
                return true;

            case ConsoleKey.H when ctrl:
                Settings.ShowHiddenFiles = !Settings.ShowHiddenFiles;
                Reload();
                return true;

            // Tabs. Ctrl+Tab is the usual next-tab key, but Windows Terminal keeps it for its own
            // tabs, so Ctrl+Alt+Right / Ctrl+Alt+Left do the same job everywhere.
            case ConsoleKey.T when ctrl:
                OpenTab();
                return true;

            case ConsoleKey.W when ctrl:
                CloseTab();
                return true;

            case ConsoleKey.Tab when ctrl:
            case ConsoleKey.RightArrow when ctrlAlt:
                NextTab();
                return true;

            case ConsoleKey.Tab when ctrlShift:
            case ConsoleKey.LeftArrow when ctrlAlt:
                PreviousTab();
                return true;

            case >= ConsoleKey.D1 and <= ConsoleKey.D9 when ctrl:
                ViewMode = PanelViewModes.FromNumber(key.Key - ConsoleKey.D0);
                return true;

            case >= ConsoleKey.F3 and <= ConsoleKey.F11 when ctrl:
                SetSort(SortForFunctionKey(key.Key));
                return true;

            case ConsoleKey.F12 when ctrl:
                ShowSortMenu(app);
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A click in a panel that does not have the focus is <em>not</em> consumed: the method returns
    /// <see langword="false"/> so the shell can make this panel active and, if it wants to, replay
    /// the event. Everything else is handled here - the wheel scrolls the window without moving the
    /// cursor, the left button moves the cursor (and activates the entry when it was already under
    /// the cursor, or on a double click), and the right button toggles the tag and moves the cursor.
    /// </remarks>
    public bool HandleMouse(MouseEvent m, IAppContext ctx)
    {
        if (!IsVisible || !Bounds.Contains(m.X, m.Y))
        {
            return false;
        }

        if (!IsActive)
        {
            return false;
        }

        if (m.Kind == MouseKind.Wheel)
        {
            ScrollBy(-m.Wheel * WheelRows);
            return true;
        }

        if (m.Kind is not (MouseKind.Down or MouseKind.DoubleClick))
        {
            return false;
        }

        // A click on a tab caption shows that tab.
        if (_tabs.Count > 1 && m.Y == Bounds.Y + 1 && m.Button == MouseButton.Left)
        {
            foreach ((int x0, int x1, int tab, _) in TabCells(Bounds))
            {
                if (m.X >= x0 && m.X < x1)
                {
                    SwitchTab(tab);
                    return true;
                }
            }

            return true;
        }

        int index = IndexAt(m.X, m.Y);
        if (index < 0)
        {
            return false;
        }

        _shiftSelection = null;

        if (m.Button == MouseButton.Right)
        {
            FileEntry entry = _entries[index];
            entry.Selected = !entry.Selected;
            SetCursor(index);
            return true;
        }

        if (m.Button != MouseButton.Left)
        {
            return false;
        }

        bool wasCurrent = index == _cursor;
        SetCursor(index);

        if (m.Kind == MouseKind.DoubleClick || wasCurrent)
        {
            Activate(ctx ?? Context);
        }

        return true;
    }

    /// <summary>
    /// The entry under a screen cell.
    /// </summary>
    /// <param name="screenX">Screen column.</param>
    /// <param name="screenY">Screen row.</param>
    /// <returns>The entry index, or <c>-1</c> when the cell is not a file row.</returns>
    public int IndexAt(int screenX, int screenY)
    {
        Rect b = Bounds;
        if (b.Width < MinWidth || b.Height < RequiredHeight)
        {
            return -1;
        }

        int rows = VisibleRows;
        int row = screenY - (b.Y + HeaderRows);
        if (row < 0 || row >= rows)
        {
            return -1;
        }

        PanelColumnLayout layout = LayoutFor(b.Width - 2);
        int stripe = layout.StripeAt(screenX - (b.X + 1));
        if (stripe < 0)
        {
            return -1;
        }

        int index = _top + (stripe * rows) + row;
        return index >= 0 && index < _entries.Count ? index : -1;
    }

    /// <summary>Scrolls the window without moving the cursor, the way the wheel does.</summary>
    /// <param name="rows">How many rows to scroll; positive scrolls towards the end of the listing.</param>
    public void ScrollBy(int rows)
    {
        int page = PageSize;
        _top = Math.Clamp(_top + rows, 0, Math.Max(0, _entries.Count - page));
    }

    // ------------------------------------------------------------- test hooks

    /// <summary>Replaces the listing with one built in memory, bypassing the file system.</summary>
    /// <param name="path">The path the panel reports and shows in its title.</param>
    /// <param name="entries">The listing, already in display order.</param>
    internal void SetEntriesForTest(string path, IReadOnlyList<FileEntry> entries) =>
        SetEntriesForTest(path, entries, null);

    /// <summary>Replaces the listing with one built in memory and, optionally, an error.</summary>
    /// <param name="path">The path the panel reports and shows in its title.</param>
    /// <param name="entries">The listing, already in display order.</param>
    /// <param name="error">The read error to display, or <see langword="null"/>.</param>
    internal void SetEntriesForTest(string path, IReadOnlyList<FileEntry> entries, string? error)
    {
        _path = path ?? string.Empty;
        _tabs[_tabIndex].Path = _path;
        _entries = entries is null ? [] : [.. entries];
        _error = string.IsNullOrEmpty(error) ? null : error;
        _cursor = 0;
        _top = 0;
        _shiftSelection = null;
        Recount();
    }

    // --------------------------------------------------------------- internals

    private int LastFileRow(Rect b) => ShowStatusBar ? b.Y + b.Height - 4 : b.Y + b.Height - 2;

    private PanelColumnLayout LayoutFor(int innerWidth)
    {
        if (_layoutCache is null || _layoutCache.InnerWidth != innerWidth || _layoutCache.Mode != _viewMode)
        {
            _layoutCache = PanelColumnLayout.Compute(_viewMode, innerWidth);
        }

        return _layoutCache;
    }

    private void ClampScroll(int page)
    {
        int count = _entries.Count;
        if (count == 0)
        {
            _cursor = 0;
            _top = 0;
            return;
        }

        _cursor = Math.Clamp(_cursor, 0, count - 1);
        _top = Math.Clamp(_top, 0, Math.Max(0, count - page));
    }

    private void EnsureVisible()
    {
        int count = _entries.Count;
        if (count == 0)
        {
            _top = 0;
            return;
        }

        int page = PageSize;
        if (_cursor < _top)
        {
            _top = _cursor;
        }
        else if (_cursor >= _top + page)
        {
            _top = _cursor - page + 1;
        }

        _top = Math.Clamp(_top, 0, Math.Max(0, count - page));
    }

    private void SetCursor(int index)
    {
        int count = _entries.Count;
        if (count == 0)
        {
            _cursor = 0;
            _top = 0;
            return;
        }

        _cursor = Math.Clamp(index, 0, count - 1);
        EnsureVisible();
    }

    private bool MoveTo(int index)
    {
        _shiftSelection = null;
        SetCursor(index);
        return true;
    }

    private bool ShiftMove(int target, bool inclusive)
    {
        int count = _entries.Count;
        if (count == 0)
        {
            return true;
        }

        int from = Math.Clamp(_cursor, 0, count - 1);
        int to = Math.Clamp(target, 0, count - 1);

        _shiftSelection ??= !_entries[from].Selected;
        bool value = _shiftSelection.Value;

        if (from == to)
        {
            _entries[from].Selected = value;
        }
        else if (to > from)
        {
            int last = inclusive ? to : to - 1;
            for (int i = from; i <= last; i++)
            {
                _entries[i].Selected = value;
            }
        }
        else
        {
            int first = inclusive ? to : to + 1;
            for (int i = first; i <= from; i++)
            {
                _entries[i].Selected = value;
            }
        }

        SetCursor(to);
        return true;
    }

    private void ToggleTagAndAdvance()
    {
        FileEntry? current = Current;
        if (current is not null && !current.IsParent)
        {
            current.Selected = !current.Selected;
        }

        SetCursor(_cursor + 1);
    }

    private void SetAllTags(bool selected)
    {
        foreach (FileEntry e in _entries)
        {
            if (!e.IsParent && !e.IsDirectory)
            {
                e.Selected = selected;
            }
        }
    }

    private void Activate(IAppContext? app)
    {
        FileEntry? current = Current;
        if (current is null)
        {
            return;
        }

        if (current.IsParent)
        {
            GoToParent();
            return;
        }

        if (current.IsDirectory)
        {
            Navigate(current.FullPath);
            return;
        }

        app?.RunShellCommand("\"" + current.FullPath + "\"");
    }

    private void EnterCurrentDirectory()
    {
        FileEntry? current = Current;
        if (current is null || !current.IsDirectory)
        {
            return;
        }

        if (current.IsParent)
        {
            GoToParent();
            return;
        }

        Navigate(current.FullPath);
    }

    private void GoToParent()
    {
        string? parent = FileSystemProvider.GetParent(CurrentPath);
        if (parent is null)
        {
            return;
        }

        Navigate(parent, LeafName(CurrentPath));
    }

    private void InsertCurrentName(IAppContext? app)
    {
        string? name = Current?.Name;
        if (app is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        // The same quoting rule as Ctrl+J: the help screen documents the two as one command, so a
        // name with a space must arrive quoted from either key. A space is appended after each
        // Ctrl+Enter insertion, which is what lets repeated presses build up an argument list.
        string text = name.Contains(' ', StringComparison.Ordinal) ? "\"" + name + "\"" : name;
        app.InsertIntoCommandLine(text + " ");
    }

    private void PromptSelectByMask(IAppContext? app, bool selected)
    {
        string? mask = app?.Ui.Input(
            selected ? "Select" : "Deselect",
            selected ? "Select files matching mask:" : "Deselect files matching mask:",
            "*.*",
            selected ? "SelectMask" : "DeselectMask");

        if (mask is not null)
        {
            SelectByMask(mask, selected);
        }
    }

    private void ShowSortMenu(IAppContext? app)
    {
        if (app is null)
        {
            return;
        }

        var modes = SortMenuModes;
        var items = new List<MenuItem>(modes.Length);
        for (int i = 0; i < modes.Length; i++)
        {
            items.Add(new MenuItem(SortMenuText(modes[i]), SortMenuAccelerator(modes[i]))
            {
                Checked = modes[i] == SortMode,
                Tag = modes[i],
            });
        }

        int start = Array.IndexOf(modes, SortMode);
        int chosen = app.Ui.Menu("Sort by", items, Math.Max(0, start));
        if (chosen >= 0 && chosen < modes.Length)
        {
            SetSort(modes[chosen]);
        }
    }

    private bool HandleQuickSearchKey(KeyEvent key)
    {
        if (!_quickSearch.IsActive)
        {
            return false;
        }

        bool ctrl = (key.Mods & KeyMods.Ctrl) != 0;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter when !ctrl:
                // Both close the box and do nothing else; Enter activates only when pressed again.
                _quickSearch.Cancel();
                return true;

            case ConsoleKey.Enter when ctrl:
                // Ctrl+Enter walks to the next match, Ctrl+Shift+Enter back to the previous one.
                bool forward = (key.Mods & KeyMods.Shift) == 0;
                int hit = QuickSearch.Find(
                    _entries,
                    _quickSearch.Text,
                    _cursor + (forward ? 1 : -1),
                    forward);
                if (hit >= 0)
                {
                    SetCursor(hit);
                }

                return true;

            case ConsoleKey.Backspace:
                if (_quickSearch.Backspace(DateTime.UtcNow))
                {
                    JumpToQuickSearchMatch(_cursor);
                }

                return true;

            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
            case ConsoleKey.Home:
            case ConsoleKey.End:
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
                // These end the search and are then handled normally.
                _quickSearch.Cancel();
                return false;

            default:
                if (key.IsPlainChar || (StartsQuickSearch(key) && key.Ch != '\0'))
                {
                    // Plain typing continues the search; so does typing with Alt still held, so a
                    // user who keeps the modifier down never loses characters.
                    _quickSearch.Append(key.Ch, DateTime.UtcNow);
                    JumpToQuickSearchMatch(_cursor);
                    return true;
                }

                return false;
        }
    }

    private static bool StartsQuickSearch(KeyEvent key)
    {
        if ((key.Mods & KeyMods.Alt) == 0 || (key.Mods & KeyMods.Ctrl) != 0)
        {
            return false;
        }

        char c = key.Ch;
        return c != '\0' && !char.IsControl(c) && (char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '*' or '?');
    }

    private void JumpToQuickSearchMatch(int from)
    {
        int hit = QuickSearch.Find(_entries, _quickSearch.Text, from);
        if (hit >= 0)
        {
            SetCursor(hit);
        }
    }

    private void Load(string? focusName)
    {
        DirectoryListing listing = FileSystemProvider.Read(
            _path,
            Settings.ShowHiddenFiles,
            MakeComparer());

        _entries = [.. listing.Entries];
        _error = listing.Error;
        Recount();
        FocusOn(focusName);
    }

    private void Resort()
    {
        FileEntry? current = Current;
        _entries = MakeComparer().Sort(_entries);
        _cursor = current is null ? 0 : Math.Max(0, _entries.IndexOf(current));
        EnsureVisible();
    }

    private FileEntryComparer MakeComparer() =>
        FileEntryComparer.For(SortMode, ReverseSort, Settings);

    private void FocusOn(string? name)
    {
        _cursor = 0;
        if (!string.IsNullOrEmpty(name))
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].Name, name, NameComparison))
                {
                    _cursor = i;
                    break;
                }
            }
        }

        EnsureVisible();
    }

    private void Recount()
    {
        _fileCount = 0;
        _directoryCount = 0;
        _totalBytes = 0;

        foreach (FileEntry e in _entries)
        {
            if (e.IsParent)
            {
                continue;
            }

            if (e.IsDirectory)
            {
                _directoryCount++;
            }
            else
            {
                _fileCount++;
                _totalBytes += e.Size;
            }
        }
    }

    private static string LeafName(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
    }

    private static string SafeCurrentDirectory()
    {
        try
        {
            return FileSystemProvider.NormalizeDisplayPath(Environment.CurrentDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>One tab: a folder and the way it was being looked at when the user last saw it.</summary>
    private sealed class PanelTab
    {
        public string Path { get; set; } = string.Empty;

        public string? FocusName { get; set; }

        public int TopIndex { get; set; }

        public SortMode SortMode { get; set; } = Files.SortMode.Name;

        public bool ReverseSort { get; set; }

        public PanelViewMode ViewMode { get; set; } = PanelViewModes.Default;

        public HashSet<string> Selected { get; set; } = [];
    }

    private static SortMode SortForFunctionKey(ConsoleKey key) => key switch
    {
        ConsoleKey.F3 => Files.SortMode.Name,
        ConsoleKey.F4 => Files.SortMode.Extension,
        ConsoleKey.F5 => Files.SortMode.Modified,
        ConsoleKey.F6 => Files.SortMode.Size,
        ConsoleKey.F7 => Files.SortMode.Unsorted,
        ConsoleKey.F8 => Files.SortMode.Created,
        ConsoleKey.F9 => Files.SortMode.Accessed,
        ConsoleKey.F10 => Files.SortMode.Description,
        _ => Files.SortMode.Owner,
    };

    private static readonly SortMode[] SortMenuModes =
    [
        Files.SortMode.Name,
        Files.SortMode.Extension,
        Files.SortMode.Modified,
        Files.SortMode.Size,
        Files.SortMode.Unsorted,
        Files.SortMode.Created,
        Files.SortMode.Accessed,
        Files.SortMode.Description,
        Files.SortMode.Owner,
    ];

    private static string SortMenuText(SortMode mode) => mode switch
    {
        Files.SortMode.Name => "&Name",
        Files.SortMode.Extension => "&Extension",
        Files.SortMode.Modified => "&Modification time",
        Files.SortMode.Size => "&Size",
        Files.SortMode.Unsorted => "&Unsorted",
        Files.SortMode.Created => "&Creation time",
        Files.SortMode.Accessed => "&Access time",
        Files.SortMode.Description => "&Description",
        _ => "&Owner",
    };

    private static string SortMenuAccelerator(SortMode mode) => mode switch
    {
        Files.SortMode.Name => "Ctrl+F3",
        Files.SortMode.Extension => "Ctrl+F4",
        Files.SortMode.Modified => "Ctrl+F5",
        Files.SortMode.Size => "Ctrl+F6",
        Files.SortMode.Unsorted => "Ctrl+F7",
        Files.SortMode.Created => "Ctrl+F8",
        Files.SortMode.Accessed => "Ctrl+F9",
        Files.SortMode.Description => "Ctrl+F10",
        _ => "Ctrl+F11",
    };
}
