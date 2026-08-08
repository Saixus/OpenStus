using OpenCommander.Input;
using OpenCommander.Rendering;

namespace OpenCommander.Ui.Controls;

/// <summary>
/// A vertical group of mutually exclusive options, drawn one per row as <c>(•) Caption</c>.
/// Up/Down move within the group, Space and Enter select, and each option may declare its own
/// <c>'&amp;'</c> hotkey.
/// </summary>
/// <remarks>
/// The group is a single focus stop, exactly like Far's radio button clusters: Tab leaves the group
/// rather than stepping through the options.
/// </remarks>
public sealed class RadioGroupControl : DialogControl
{
    /// <summary>The three marker characters plus the separating space.</summary>
    public const int Decoration = 4;

    private readonly List<string> _items;
    private int _selectedIndex;

    /// <summary>Creates a radio group.</summary>
    /// <param name="items">The option captions; each may contain one <c>'&amp;'</c> hotkey marker.</param>
    /// <param name="selectedIndex">The option that starts selected.</param>
    public RadioGroupControl(IEnumerable<string> items, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = [.. items];
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);

        int width = 0;
        foreach (string item in _items)
        {
            width = Math.Max(width, ScreenBuffer.HotkeyTextLength(item) + Decoration);
        }

        Bounds = new Rect(0, 0, Math.Max(1, width), Math.Max(1, _items.Count));
    }

    /// <summary>The option captions.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>The selected option, or <c>-1</c> when the group is empty.</summary>
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
            if (next == _selectedIndex)
            {
                return;
            }

            _selectedIndex = next;
            SelectionChanged?.Invoke(next);
        }
    }

    /// <summary>The glyph drawn inside the parentheses of the selected option.</summary>
    public char SelectedGlyph { get; set; } = '•';

    /// <summary>Raised whenever the selection changes.</summary>
    public Action<int>? SelectionChanged { get; set; }

    /// <summary>The row the keyboard cursor sits on, which is always the selected option.</summary>
    public override bool WantsCursor => HasFocus && Enabled && _selectedIndex >= 0;

    /// <inheritdoc/>
    public override (int X, int Y) CursorOffset => (1, Math.Max(0, _selectedIndex));

    /// <inheritdoc/>
    public override char? Hotkey => null;

    /// <inheritdoc/>
    public override int PreferredWidth
    {
        get
        {
            int width = 0;
            foreach (string item in _items)
            {
                width = Math.Max(width, ScreenBuffer.HotkeyTextLength(item) + Decoration);
            }

            return width;
        }
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

        for (int i = 0; i < _items.Count && i < r.Height; i++)
        {
            bool current = i == _selectedIndex;
            bool focused = HasFocus && current;
            var style = !Enabled ? palette.EditDisabled
                : focused ? palette.ButtonSelected
                : palette.Text;
            var hot = !Enabled ? palette.EditDisabled
                : focused ? palette.ButtonSelectedHighlight
                : palette.Highlight;

            int y = r.Y + i;
            buffer.Fill(new Rect(r.X, y, r.Width, 1), ' ', style);
            buffer.Set(r.X, y, '(', style);
            buffer.Set(r.X + 1, y, current ? SelectedGlyph : ' ', style);
            buffer.Set(r.X + 2, y, ')', style);

            if (r.Width > Decoration)
            {
                buffer.WriteHotkey(r.X + 4, y, _items[i], style, hot);
            }
        }
    }

    /// <inheritdoc/>
    public override bool HandleKey(KeyEvent key)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        if (key.Is(ConsoleKey.UpArrow))
        {
            SelectedIndex = _selectedIndex <= 0 ? _items.Count - 1 : _selectedIndex - 1;
            return true;
        }

        if (key.Is(ConsoleKey.DownArrow))
        {
            SelectedIndex = _selectedIndex >= _items.Count - 1 ? 0 : _selectedIndex + 1;
            return true;
        }

        if (key.Is(ConsoleKey.Home))
        {
            SelectedIndex = 0;
            return true;
        }

        if (key.Is(ConsoleKey.End))
        {
            SelectedIndex = _items.Count - 1;
            return true;
        }

        if (key.IsPlainChar)
        {
            char c = char.ToLowerInvariant(key.Ch);
            for (int i = 0; i < _items.Count; i++)
            {
                if (ScreenBuffer.HotkeyOf(_items[i]) == c)
                {
                    SelectedIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse, Rect client)
    {
        if (!mouse.IsPress || mouse.Button != MouseButton.Left)
        {
            return false;
        }

        var r = ScreenBounds(client);
        SelectedIndex = mouse.Y - r.Y;
        return true;
    }
}
