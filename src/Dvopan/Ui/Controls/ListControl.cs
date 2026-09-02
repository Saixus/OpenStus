using Dvopan.Input;
using Dvopan.Rendering;

namespace Dvopan.Ui.Controls;

/// <summary>
/// A scrollable single-select list with a scroll bar and incremental type-search.
/// </summary>
/// <remarks>
/// Typing letters extends the search prefix and jumps to the first row that starts with it; any
/// navigation key, Esc or Enter resets the prefix. A character that would make the prefix match
/// nothing is ignored, so the search never gets stuck in a dead end.
/// </remarks>
public sealed class ListControl : DialogControl
{
    private readonly List<string> _items;
    private int _selectedIndex;
    private int _topIndex;
    private string _search = string.Empty;

    /// <summary>Creates a list.</summary>
    /// <param name="items">The rows.</param>
    /// <param name="selectedIndex">The row the cursor starts on.</param>
    public ListControl(IEnumerable<string>? items = null, int selectedIndex = 0)
    {
        _items = items is null ? [] : [.. items];
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);
        Bounds = new Rect(0, 0, 1, 1);
    }

    /// <summary>The rows.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>The row under the cursor, or <c>-1</c> when the list is empty.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                return;
            }

            int next = Math.Clamp(value, 0, _items.Count - 1);
            if (next != _selectedIndex)
            {
                _selectedIndex = next;
                SelectionChanged?.Invoke(next);
            }

            ScrollIntoView();
        }
    }

    /// <summary>The text of the row under the cursor, or <see langword="null"/>.</summary>
    public string? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    /// <summary>The first visible row.</summary>
    public int TopIndex => _topIndex;

    /// <summary>The type-search prefix accumulated so far.</summary>
    public string SearchPrefix => _search;

    /// <summary>When set, a scroll bar is drawn on the right edge whenever the rows do not all fit.</summary>
    public bool ShowScrollBar { get; set; } = true;

    /// <summary>When set, letters extend the type-search prefix.</summary>
    public bool TypeSearch { get; set; } = true;

    /// <summary>Raised when the cursor moves.</summary>
    public Action<int>? SelectionChanged { get; set; }

    /// <summary>Raised by Enter or a double click, with the row index.</summary>
    public Action<int>? ItemActivated { get; set; }

    /// <inheritdoc/>
    public override bool WantsCursor => false;

    /// <summary>Replaces the rows and resets the cursor.</summary>
    /// <param name="items">The new rows.</param>
    /// <param name="selectedIndex">The row the cursor should land on.</param>
    public void SetItems(IEnumerable<string> items, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);
        _topIndex = 0;
        _search = string.Empty;
        ScrollIntoView();
    }

    /// <inheritdoc/>
    public override void Draw(ScreenBuffer buffer, Rect client, DialogPalette palette)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(palette);

        var r = ScreenBounds(client);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        ScrollIntoView();

        bool bar = ShowScrollBar && _items.Count > r.Height && r.Width > 2;
        int textWidth = bar ? r.Width - 1 : r.Width;

        for (int row = 0; row < r.Height; row++)
        {
            int index = _topIndex + row;
            bool current = index == _selectedIndex;
            var style = !Enabled ? palette.EditDisabled
                : current ? palette.ListSelected
                : palette.ListText;

            string text = index >= 0 && index < _items.Count ? " " + _items[index] : string.Empty;
            buffer.WriteFixed(r.X, r.Y + row, textWidth, text, style);
        }

        if (bar)
        {
            DrawScrollBar(buffer, new Rect(r.Right - 1, r.Y, 1, r.Height), palette);
        }
    }

    /// <inheritdoc/>
    public override bool HandleKey(KeyEvent key)
    {
        if (!Enabled)
        {
            return false;
        }

        int page = Math.Max(1, Bounds.Height);

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                ResetSearch();
                SelectedIndex = _selectedIndex - 1;
                return true;

            case ConsoleKey.DownArrow:
                ResetSearch();
                SelectedIndex = _selectedIndex + 1;
                return true;

            case ConsoleKey.PageUp:
                ResetSearch();
                SelectedIndex = _selectedIndex - page;
                return true;

            case ConsoleKey.PageDown:
                ResetSearch();
                SelectedIndex = _selectedIndex + page;
                return true;

            case ConsoleKey.Home:
                ResetSearch();
                SelectedIndex = 0;
                return true;

            case ConsoleKey.End:
                ResetSearch();
                SelectedIndex = _items.Count - 1;
                return true;

            case ConsoleKey.Enter:
                ResetSearch();
                if (_selectedIndex >= 0)
                {
                    ItemActivated?.Invoke(_selectedIndex);
                }

                return true;

            case ConsoleKey.Backspace when _search.Length > 0:
                _search = _search[..^1];
                return true;
        }

        if (TypeSearch && key.IsPlainChar)
        {
            return ExtendSearch(key.Ch);
        }

        return false;
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse, Rect client)
    {
        var r = ScreenBounds(client);

        if (mouse.Kind == MouseKind.Wheel)
        {
            ScrollBy(-mouse.Wheel);
            return true;
        }

        if (!mouse.IsPress || mouse.Button != MouseButton.Left)
        {
            return false;
        }

        int index = _topIndex + (mouse.Y - r.Y);
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        SelectedIndex = index;
        if (mouse.Kind == MouseKind.DoubleClick)
        {
            ItemActivated?.Invoke(index);
        }

        return true;
    }

    /// <summary>Scrolls the viewport without moving the cursor off the list.</summary>
    /// <param name="rows">Positive scrolls down.</param>
    public void ScrollBy(int rows)
    {
        if (_items.Count == 0)
        {
            // Nothing to scroll - and the cursor clamp below would be handed an inverted
            // range (min 0, max -1), which throws. A wheel tick over an empty list is a no-op.
            return;
        }

        int height = Math.Max(1, Bounds.Height);
        int max = Math.Max(0, _items.Count - height);
        _topIndex = Math.Clamp(_topIndex + rows, 0, max);
        _selectedIndex = Math.Clamp(_selectedIndex, _topIndex, Math.Min(_items.Count - 1, _topIndex + height - 1));
    }

    /// <summary>Clears the type-search prefix.</summary>
    public void ResetSearch() => _search = string.Empty;

    private bool ExtendSearch(char c)
    {
        string candidate = _search + c;
        int match = FindPrefix(candidate, _selectedIndex);
        if (match < 0)
        {
            return true; // consumed, but the prefix would match nothing - keep the old one
        }

        _search = candidate;
        SelectedIndex = match;
        return true;
    }

    private int FindPrefix(string prefix, int from)
    {
        if (_items.Count == 0)
        {
            return -1;
        }

        int start = Math.Max(0, from);
        for (int i = 0; i < _items.Count; i++)
        {
            int index = (start + i) % _items.Count;
            if (_items[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void ScrollIntoView()
    {
        int height = Math.Max(1, Bounds.Height);
        int max = Math.Max(0, _items.Count - height);

        if (_selectedIndex >= 0)
        {
            if (_selectedIndex < _topIndex)
            {
                _topIndex = _selectedIndex;
            }
            else if (_selectedIndex >= _topIndex + height)
            {
                _topIndex = _selectedIndex - height + 1;
            }
        }

        _topIndex = Math.Clamp(_topIndex, 0, max);
    }

    private void DrawScrollBar(ScreenBuffer buffer, Rect bar, DialogPalette palette)
    {
        var style = palette.ListText;
        buffer.Fill(bar, BoxChars.ScrollBarTrack, style);

        if (_items.Count <= 0 || bar.Height <= 0)
        {
            return;
        }

        int thumb = Math.Max(1, bar.Height * bar.Height / _items.Count);
        thumb = Math.Min(thumb, bar.Height);

        int maxTop = Math.Max(1, _items.Count - bar.Height);
        int offset = bar.Height - thumb <= 0
            ? 0
            : _topIndex * (bar.Height - thumb) / maxTop;

        for (int i = 0; i < thumb; i++)
        {
            buffer.Set(bar.X, bar.Y + Math.Min(bar.Height - 1, offset + i), BoxChars.ScrollBarThumb, style);
        }
    }
}
