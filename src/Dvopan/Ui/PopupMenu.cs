using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Theming;

namespace Dvopan.Ui;

/// <summary>
/// The bordered popup behind <see cref="IUiServices.Menu"/>: a single-line frame in the menu
/// palette, an optional centred title, separators, disabled and checked items, right-aligned
/// accelerator hints, hotkeys, wrap-around arrow navigation and incremental type-search.
/// </summary>
/// <remarks>
/// <para>
/// The menu is a pure selector: it reports the chosen index through <see cref="Result"/> and does
/// <em>not</em> run <see cref="MenuItem.Action"/> unless <see cref="InvokeActions"/> is set. That
/// keeps "which item did the user pick" separate from "what happens next", which is what
/// <see cref="MenuBar"/> needs.
/// </para>
/// <para>
/// A letter that matches exactly one item's <c>'&amp;'</c> hotkey chooses that item outright.
/// Any other letter extends the type-search prefix and only moves the cursor.
/// </para>
/// </remarks>
public sealed class PopupMenu : IScreenComponent
{
    /// <summary>The check column, its trailing space and the two frame columns.</summary>
    private const int Decoration = 4;

    /// <summary>The glyph marking a checked item.</summary>
    public const char CheckGlyph = '√';

    private readonly IReadOnlyList<MenuItem> _items;
    private readonly Rect? _requested;
    private int _selected;
    private int _top;
    private string _search = string.Empty;
    private Rect _area;

    /// <summary>Creates a popup menu.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="title">An optional title centred on the top frame line.</param>
    /// <param name="items">The items; separators and disabled entries are skipped when moving.</param>
    /// <param name="selected">The index the cursor starts on.</param>
    /// <param name="position">
    /// Where to put the menu. A rectangle with a positive width and height is used as the menu's
    /// bounds; one with a zero size anchors its top-left corner; <see langword="null"/> centres it.
    /// </param>
    public PopupMenu(
        Theme theme,
        string? title,
        IReadOnlyList<MenuItem> items,
        int selected = 0,
        Rect? position = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(items);

        Theme = theme;
        Title = title;
        _items = items;
        _requested = position;
        _selected = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1));

        if (!IsSelectable(_selected))
        {
            _selected = NextSelectable(_selected, 1, wrap: true);
        }
    }

    /// <summary>The palette this menu draws with.</summary>
    public Theme Theme { get; }

    /// <summary>The title centred on the top frame line, or <see langword="null"/>.</summary>
    public string? Title { get; set; }

    /// <summary>The items.</summary>
    public IReadOnlyList<MenuItem> Items => _items;

    /// <summary>The index under the cursor, or <c>-1</c> when nothing is selectable.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_items.Count == 0)
            {
                _selected = -1;
                return;
            }

            int next = Math.Clamp(value, 0, _items.Count - 1);
            _selected = IsSelectable(next) ? next : NextSelectable(next, 1, wrap: true);
            ScrollIntoView();
        }
    }

    /// <summary>The item under the cursor, or <see langword="null"/>.</summary>
    public MenuItem? SelectedItem =>
        _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    /// <summary>The chosen index, or <c>-1</c> when the menu was cancelled or is still open.</summary>
    public int Result { get; private set; } = -1;

    /// <summary>The chosen item, or <see langword="null"/>.</summary>
    public MenuItem? ChosenItem =>
        Result >= 0 && Result < _items.Count ? _items[Result] : null;

    /// <inheritdoc/>
    public bool IsClosed { get; private set; }

    /// <summary><see langword="true"/> when the menu closed without a choice.</summary>
    public bool Cancelled => IsClosed && Result < 0;

    /// <summary>When set, choosing an item also runs its <see cref="MenuItem.Action"/>.</summary>
    public bool InvokeActions { get; set; }

    /// <summary>The menu's rectangle in screen cells, as computed by the last <see cref="Layout"/>.</summary>
    public Rect Bounds { get; private set; }

    /// <summary>When set, the classic drop shadow is painted.</summary>
    public bool HasShadow { get; set; } = true;

    /// <summary>The first visible item.</summary>
    public int TopIndex => _top;

    /// <summary>The type-search prefix accumulated so far.</summary>
    public string SearchPrefix => _search;

    /// <inheritdoc/>
    public void Layout(Rect area)
    {
        _area = area;

        int width;
        int height;
        int x;
        int y;

        if (_requested is Rect r && r.Width > 0 && r.Height > 0)
        {
            width = Math.Min(r.Width, area.Width);
            height = Math.Min(r.Height, area.Height);
            x = r.X;
            y = r.Y;
        }
        else
        {
            width = Math.Min(MeasureWidth(), Math.Max(3, area.Width));
            height = Math.Min(_items.Count + 2, Math.Max(3, area.Height));

            if (_requested is Rect anchor)
            {
                x = anchor.X;
                y = anchor.Y;
            }
            else
            {
                x = area.X + Math.Max(0, (area.Width - width) / 2);
                y = area.Y + Math.Max(0, (area.Height - height) / 2);
            }
        }

        // Keep the whole menu on screen even when the anchor sits near an edge.
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));

        Bounds = new Rect(x, y, Math.Max(3, width), Math.Max(3, height));
        ScrollIntoView();
    }

    /// <inheritdoc/>
    public void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var box = Theme.MenuBox;
        var r = Bounds;

        buffer.Fill(r, ' ', Theme.MenuText);
        buffer.DrawBox(r, BoxStyle.Single, box);

        if (!string.IsNullOrEmpty(Title) && r.Width > 4)
        {
            string t = " " + Title + " ";
            int max = r.Width - 2;
            int tx = r.X + 1 + Math.Max(0, (max - t.Length) / 2);
            buffer.WriteFixed(tx, r.Y, Math.Min(t.Length, max), t, Theme.MenuTitle);
        }

        int rows = Math.Max(0, r.Height - 2);
        for (int row = 0; row < rows; row++)
        {
            DrawRow(buffer, r, row, _top + row);
        }

        if (_items.Count > rows && rows > 0)
        {
            DrawScrollBar(buffer, new Rect(r.Right - 1, r.Y + 1, 1, rows));
        }

        if (HasShadow)
        {
            buffer.DrawShadow(r);
        }
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

    /// <summary>Feeds one key press to the menu.</summary>
    /// <param name="key">The key press.</param>
    /// <returns><see langword="true"/> when the menu consumed it.</returns>
    public bool HandleKey(KeyEvent key)
    {
        if (IsClosed)
        {
            return false;
        }

        int page = Math.Max(1, Bounds.Height - 2);

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _search = string.Empty;
                Move(-1, wrap: true);
                return true;

            case ConsoleKey.DownArrow:
                _search = string.Empty;
                Move(1, wrap: true);
                return true;

            case ConsoleKey.PageUp:
                _search = string.Empty;
                MoveBy(-page);
                return true;

            case ConsoleKey.PageDown:
                _search = string.Empty;
                MoveBy(page);
                return true;

            case ConsoleKey.Home:
                _search = string.Empty;
                SelectFirst();
                return true;

            case ConsoleKey.End:
                _search = string.Empty;
                SelectLast();
                return true;

            case ConsoleKey.Enter:
                Choose(_selected);
                return true;

            case ConsoleKey.Escape:
                Cancel();
                return true;

            case ConsoleKey.Backspace when _search.Length > 0:
                _search = _search[..^1];
                return true;
        }

        if (key.IsPlainChar || ((key.Mods & KeyMods.Alt) != 0 && key.Ch != '\0'))
        {
            char c = char.ToLowerInvariant(key.Ch);

            int hot = FindHotkey(c);
            if (hot >= 0)
            {
                SelectedIndex = hot;
                Choose(hot);
                return true;
            }

            if (key.IsPlainChar)
            {
                return ExtendSearch(key.Ch);
            }
        }

        return false;
    }

    /// <summary>Feeds one mouse event to the menu.</summary>
    /// <param name="mouse">The event, in absolute screen coordinates.</param>
    /// <returns><see langword="true"/> when the menu consumed it.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        if (IsClosed)
        {
            return false;
        }

        if (mouse.Kind == MouseKind.Wheel)
        {
            MoveBy(-mouse.Wheel);
            return true;
        }

        if (!mouse.IsPress)
        {
            return false;
        }

        if (!Bounds.Contains(mouse.X, mouse.Y))
        {
            Cancel(); // a click outside a popup dismisses it
            return true;
        }

        int row = mouse.Y - Bounds.Y - 1;
        if (row < 0 || row >= Bounds.Height - 2)
        {
            return true;
        }

        int index = _top + row;
        if (index < 0 || index >= _items.Count || !IsSelectable(index))
        {
            return true;
        }

        SelectedIndex = index;
        Choose(index);
        return true;
    }

    /// <summary>Chooses an item and closes the menu.</summary>
    /// <param name="index">The item index; a separator or disabled item is ignored.</param>
    /// <returns><see langword="true"/> when the menu closed with a choice.</returns>
    public bool Choose(int index)
    {
        if (index < 0 || index >= _items.Count || !IsSelectable(index))
        {
            return false;
        }

        Result = index;
        IsClosed = true;

        if (InvokeActions)
        {
            _items[index].Action?.Invoke();
        }

        return true;
    }

    /// <summary>Closes the menu without a choice.</summary>
    public void Cancel()
    {
        Result = -1;
        IsClosed = true;
    }

    /// <summary>The natural width of the menu, frame included.</summary>
    /// <returns>The width in cells.</returns>
    public int MeasureWidth()
    {
        int text = 0;
        int right = 0;

        foreach (var item in _items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            text = Math.Max(text, item.TextLength);
            right = Math.Max(right, item.RightText?.Length ?? 0);
        }

        int inner = text + (right > 0 ? right + 2 : 0);
        int width = inner + Decoration + 1;

        if (!string.IsNullOrEmpty(Title))
        {
            width = Math.Max(width, Title.Length + 4);
        }

        return Math.Max(8, width);
    }

    private void DrawRow(ScreenBuffer buffer, Rect r, int row, int index)
    {
        int y = r.Y + 1 + row;
        int contentX = r.X + 1;
        int contentWidth = r.Width - 2;
        if (contentWidth <= 0)
        {
            return;
        }

        if (index < 0 || index >= _items.Count)
        {
            buffer.Fill(new Rect(contentX, y, contentWidth, 1), ' ', Theme.MenuText);
            return;
        }

        var item = _items[index];

        if (item.IsSeparator)
        {
            buffer.Set(r.X, y, BoxChars.LeftTee(BoxStyle.Single), Theme.MenuBox);
            buffer.HLine(contentX, y, contentWidth, BoxChars.Horizontal(BoxStyle.Single), Theme.MenuSeparator);
            buffer.Set(r.Right - 1, y, BoxChars.RightTee(BoxStyle.Single), Theme.MenuBox);
            return;
        }

        bool current = index == _selected;
        var style = current ? Theme.MenuSelected : item.Enabled ? Theme.MenuText : Theme.MenuDisabled;
        var hot = current ? Theme.MenuSelectedHighlight : Theme.MenuHighlight;

        buffer.Fill(new Rect(contentX, y, contentWidth, 1), ' ', style);

        if (item.Checked)
        {
            buffer.Set(contentX, y, CheckGlyph, style);
        }

        int textX = contentX + 2;
        int available = Math.Max(0, contentWidth - 2);
        if (available > 0)
        {
            if (item.TextLength <= available)
            {
                buffer.WriteHotkey(textX, y, item.Text, style, item.Enabled ? hot : style);
            }
            else
            {
                buffer.WriteFixed(textX, y, available, Controls.LabelControl.StripMarkers(item.Text), style);
            }
        }

        if (!string.IsNullOrEmpty(item.RightText))
        {
            int len = item.RightText.Length;
            int rx = r.Right - 2 - len;
            if (rx > textX)
            {
                buffer.WriteFixed(rx, y, len, item.RightText, style);
            }
        }
    }

    private void DrawScrollBar(ScreenBuffer buffer, Rect bar)
    {
        var style = Theme.MenuScroll;
        buffer.Fill(bar, BoxChars.ScrollBarTrack, style);

        if (_items.Count <= 0 || bar.Height <= 0)
        {
            return;
        }

        int thumb = Math.Clamp(bar.Height * bar.Height / _items.Count, 1, bar.Height);
        int maxTop = Math.Max(1, _items.Count - bar.Height);
        int offset = bar.Height - thumb <= 0 ? 0 : _top * (bar.Height - thumb) / maxTop;

        for (int i = 0; i < thumb; i++)
        {
            buffer.Set(bar.X, bar.Y + Math.Min(bar.Height - 1, offset + i), BoxChars.ScrollBarThumb, style);
        }
    }

    private bool IsSelectable(int index) =>
        index >= 0 && index < _items.Count && _items[index].IsSelectable;

    private int NextSelectable(int from, int step, bool wrap)
    {
        if (_items.Count == 0)
        {
            return -1;
        }

        int index = from;
        for (int i = 0; i < _items.Count; i++)
        {
            index += step;
            if (index < 0 || index >= _items.Count)
            {
                if (!wrap)
                {
                    return IsSelectable(from) ? from : -1;
                }

                index = ((index % _items.Count) + _items.Count) % _items.Count;
            }

            if (IsSelectable(index))
            {
                return index;
            }
        }

        return IsSelectable(from) ? from : -1;
    }

    private void Move(int step, bool wrap)
    {
        int next = NextSelectable(_selected, step, wrap);
        if (next >= 0)
        {
            _selected = next;
            ScrollIntoView();
        }
    }

    private void MoveBy(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        int target = Math.Clamp(_selected + delta, 0, _items.Count - 1);
        if (IsSelectable(target))
        {
            _selected = target;
        }
        else
        {
            int step = delta >= 0 ? 1 : -1;
            int found = NextSelectable(target, step, wrap: false);
            _selected = found >= 0 ? found : NextSelectable(target, -step, wrap: false);
        }

        ScrollIntoView();
    }

    private void SelectFirst()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (IsSelectable(i))
            {
                _selected = i;
                ScrollIntoView();
                return;
            }
        }
    }

    private void SelectLast()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (IsSelectable(i))
            {
                _selected = i;
                ScrollIntoView();
                return;
            }
        }
    }

    private int FindHotkey(char c)
    {
        int found = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.IsSelectable && item.Hotkey == c)
            {
                if (found >= 0)
                {
                    return -1; // ambiguous: fall back to the type-search
                }

                found = i;
            }
        }

        return found;
    }

    private bool ExtendSearch(char c)
    {
        string candidate = _search + c;
        for (int i = 0; i < _items.Count; i++)
        {
            int index = (Math.Max(0, _selected) + i) % _items.Count;
            var item = _items[index];
            if (!item.IsSelectable)
            {
                continue;
            }

            string plain = Controls.LabelControl.StripMarkers(item.Text);
            if (plain.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                _search = candidate;
                _selected = index;
                ScrollIntoView();
                return true;
            }
        }

        return true; // consumed; the prefix is simply not extended
    }

    private void ScrollIntoView()
    {
        int rows = Math.Max(1, Bounds.Height - 2);
        int max = Math.Max(0, _items.Count - rows);

        if (_selected >= 0)
        {
            if (_selected < _top)
            {
                _top = _selected;
            }
            else if (_selected >= _top + rows)
            {
                _top = _selected - rows + 1;
            }
        }

        _top = Math.Clamp(_top, 0, max);
    }
}
