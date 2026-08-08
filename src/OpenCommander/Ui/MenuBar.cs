using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Ui;

/// <summary>
/// Far's F9 pull-down system: a full-width bar on the top row carrying the top-level titles, with
/// one <see cref="PopupMenu"/> hanging below whichever title is open.
/// </summary>
/// <remarks>
/// <para>
/// Left and Right walk the titles - and, when a pull-down is open, close it and open the neighbour,
/// so holding Right sweeps through the whole menu system exactly like Far. Down or Enter opens the
/// selected pull-down, Esc closes it, and a second Esc closes the bar.
/// </para>
/// <para>
/// The bar reports the chosen leaf through <see cref="ChosenItem"/>. <see cref="RunModal"/> is the
/// one place that runs the item's <see cref="MenuItem.Action"/>, so nothing fires twice.
/// </para>
/// </remarks>
public sealed class MenuBar : IScreenComponent
{
    private readonly IReadOnlyList<MenuItem> _topLevel;
    private readonly List<Rect> _titleBounds = [];
    private PopupMenu? _popup;
    private Rect _area;
    private int _selected;

    /// <summary>Creates a menu bar.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="topLevel">
    /// The top-level titles; each one's <see cref="MenuItem.SubItems"/> is its pull-down.
    /// </param>
    public MenuBar(Theme theme, IReadOnlyList<MenuItem> topLevel)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(topLevel);

        Theme = theme;
        _topLevel = topLevel;
    }

    /// <summary>The palette this bar draws with.</summary>
    public Theme Theme { get; }

    /// <summary>The top-level titles.</summary>
    public IReadOnlyList<MenuItem> TopLevel => _topLevel;

    /// <summary>The title under the cursor.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_topLevel.Count == 0)
            {
                _selected = 0;
                return;
            }

            _selected = Math.Clamp(value, 0, _topLevel.Count - 1);
        }
    }

    /// <summary>The open pull-down, or <see langword="null"/> when only the bar is showing.</summary>
    public PopupMenu? OpenMenu => _popup;

    /// <summary><see langword="true"/> while a pull-down is open.</summary>
    public bool IsMenuOpen => _popup is not null;

    /// <summary>The leaf item the user chose, or <see langword="null"/> when the bar was cancelled.</summary>
    public MenuItem? ChosenItem { get; private set; }

    /// <summary>The top-level index the chosen item came from, or <c>-1</c>.</summary>
    public int ChosenMenuIndex { get; private set; } = -1;

    /// <summary>The index of the chosen item inside its pull-down, or <c>-1</c>.</summary>
    public int ChosenItemIndex { get; private set; } = -1;

    /// <inheritdoc/>
    public bool IsClosed { get; private set; }

    /// <summary>The bar's row in screen cells.</summary>
    public Rect Bounds { get; private set; }

    /// <summary>
    /// Puts the bar on screen through the application's modal loop and runs the chosen item's
    /// action.
    /// </summary>
    /// <param name="ctx">The application context supplying the screen and the modal loop.</param>
    /// <returns><see langword="true"/> when the user chose an item.</returns>
    public bool RunModal(IAppContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        Layout(new Rect(0, 0, ctx.Terminal.Width, ctx.Terminal.Height));
        ctx.Ui.RunModal(this);

        var chosen = ChosenItem;
        chosen?.Action?.Invoke();
        return chosen is not null;
    }

    /// <inheritdoc/>
    public void Layout(Rect area)
    {
        _area = area;
        Bounds = new Rect(area.X, area.Y, Math.Max(0, area.Width), 1);

        _titleBounds.Clear();
        int x = area.X + 1;
        foreach (var item in _topLevel)
        {
            int width = item.TextLength + 2;
            _titleBounds.Add(new Rect(x, area.Y, width, 1));
            x += width + 1;
        }

        _popup?.Layout(PullDownArea());
    }

    /// <inheritdoc/>
    public void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        buffer.Fill(Bounds, ' ', Theme.MenuBarText);

        for (int i = 0; i < _topLevel.Count && i < _titleBounds.Count; i++)
        {
            var r = _titleBounds[i];
            bool current = i == _selected;
            var style = current ? Theme.MenuBarSelected : Theme.MenuBarText;
            var hot = current ? Theme.MenuBarSelectedHighlight : Theme.MenuBarHighlight;

            buffer.Fill(r, ' ', style);
            buffer.WriteHotkey(r.X + 1, r.Y, _topLevel[i].Text, style, hot);
        }

        _popup?.Draw(buffer);
    }

    /// <inheritdoc/>
    public bool HandleInput(InputEvent ev)
    {
        switch (ev.Kind)
        {
            case InputKind.Key:
                HandleKey(ev.Key);
                break;

            case InputKind.Mouse:
                HandleMouse(ev.Mouse);
                break;

            case InputKind.Resize:
                Layout(_area);
                break;
        }

        return !IsClosed;
    }

    /// <inheritdoc/>
    public KeyBarLabels? KeyBarFor(KeyMods mods) => KeyBarLabels.Empty;

    /// <summary>Feeds one key press to the bar and, when it is open, to the pull-down.</summary>
    /// <param name="key">The key press.</param>
    /// <returns><see langword="true"/> when something consumed it.</returns>
    public bool HandleKey(KeyEvent key)
    {
        if (IsClosed)
        {
            return false;
        }

        // Left/Right always belong to the bar, even while a pull-down is open.
        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow && (key.Mods & ~KeyMods.Shift) == 0)
        {
            Step(key.Key == ConsoleKey.RightArrow ? 1 : -1);
            return true;
        }

        if (_popup is not null)
        {
            // An open pull-down is modal over the bar: everything else goes to it.
            _popup.HandleKey(key);
            if (_popup.IsClosed)
            {
                AfterPopupClosed();
            }

            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.DownArrow:
            case ConsoleKey.Enter:
                Open();
                return true;

            case ConsoleKey.Escape:
                Close();
                return true;

            case ConsoleKey.Home:
                SelectedIndex = 0;
                return true;

            case ConsoleKey.End:
                SelectedIndex = _topLevel.Count - 1;
                return true;
        }

        if (key.Ch != '\0' && !char.IsControl(key.Ch))
        {
            char c = char.ToLowerInvariant(key.Ch);
            for (int i = 0; i < _topLevel.Count; i++)
            {
                if (_topLevel[i].Hotkey == c)
                {
                    SelectedIndex = i;
                    Open();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Feeds one mouse event to the bar and, when it is open, to the pull-down.</summary>
    /// <param name="mouse">The event, in absolute screen coordinates.</param>
    /// <returns><see langword="true"/> when something consumed it.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        if (IsClosed)
        {
            return false;
        }

        if (mouse.IsPress && mouse.Y == Bounds.Y)
        {
            for (int i = 0; i < _titleBounds.Count; i++)
            {
                if (!_titleBounds[i].Contains(mouse.X, mouse.Y))
                {
                    continue;
                }

                SelectedIndex = i;
                Open();
                return true;
            }

            return true;
        }

        if (_popup is null)
        {
            if (mouse.IsPress)
            {
                Close();
            }

            return true;
        }

        bool handled = _popup.HandleMouse(mouse);
        if (_popup.IsClosed)
        {
            AfterPopupClosed();
        }

        return handled;
    }

    /// <summary>Opens the pull-down of the selected title.</summary>
    /// <returns><see langword="true"/> when a pull-down opened.</returns>
    public bool Open()
    {
        if (_topLevel.Count == 0)
        {
            return false;
        }

        var items = _topLevel[_selected].SubItems;
        if (items is null || items.Count == 0)
        {
            // A top-level entry with no children is a command in its own right.
            ChosenItem = _topLevel[_selected];
            ChosenMenuIndex = _selected;
            ChosenItemIndex = -1;
            IsClosed = true;
            return false;
        }

        var anchor = _titleBounds.Count > _selected
            ? new Rect(_titleBounds[_selected].X, Bounds.Y + 1, 0, 0)
            : new Rect(Bounds.X, Bounds.Y + 1, 0, 0);

        _popup = new PopupMenu(Theme, null, items, 0, anchor);
        _popup.Layout(PullDownArea());
        return true;
    }

    /// <summary>Closes the open pull-down, leaving the bar up.</summary>
    public void CloseMenu() => _popup = null;

    /// <summary>Closes the whole bar without a choice.</summary>
    public void Close()
    {
        _popup = null;
        IsClosed = true;
    }

    private Rect PullDownArea() =>
        Rect.FromLTRB(_area.X, _area.Y + 1, _area.Right, Math.Max(_area.Y + 2, _area.Bottom));

    private void AfterPopupClosed()
    {
        var popup = _popup;
        _popup = null;
        if (popup is null)
        {
            return;
        }

        if (popup.Result < 0)
        {
            return; // Esc inside the pull-down: back to the bar
        }

        ChosenItem = popup.ChosenItem;
        ChosenMenuIndex = _selected;
        ChosenItemIndex = popup.Result;
        IsClosed = true;
    }

    private void Step(int direction)
    {
        if (_topLevel.Count == 0)
        {
            return;
        }

        bool wasOpen = _popup is not null;
        _popup = null;

        _selected = ((_selected + direction) % _topLevel.Count + _topLevel.Count) % _topLevel.Count;

        if (wasOpen)
        {
            Open();
        }
    }
}
