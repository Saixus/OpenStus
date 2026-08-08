using System.Text;
using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Theming;

namespace OpenCommander.Ui;

/// <summary>
/// The scrollable F1 help page: every key binding the application answers to, grouped by area.
/// </summary>
/// <remarks>
/// <see cref="Bindings"/> is the single source of truth for the key list. The screen renders it, and
/// <see cref="ToMarkdown"/> emits the same table for the README, so documentation cannot drift away
/// from the program.
/// </remarks>
public sealed class HelpScreen : IScreenComponent
{
    /// <summary>Columns reserved for the key names before the description starts.</summary>
    private const int KeyColumnWidth = 22;

    private readonly Theme _theme;
    private readonly List<Line> _lines;

    private Rect _area;
    private int _scroll;

    /// <summary>Creates the help screen.</summary>
    /// <param name="theme">The palette; the page uses the dialog colours.</param>
    public HelpScreen(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        _lines = BuildLines();
    }

    /// <summary>The title drawn in the frame.</summary>
    public string Title { get; set; } = "Open Commander Help";

    /// <summary>The index of the first content line shown.</summary>
    public int Scroll => _scroll;

    /// <summary>How many content lines the page has, section headings and blank spacers included.</summary>
    public int LineCount => _lines.Count;

    /// <inheritdoc/>
    public bool IsClosed { get; private set; }

    /// <inheritdoc/>
    public void Layout(Rect area) => _area = area;

    /// <inheritdoc/>
    public void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Rect area = _area.IsEmpty ? new Rect(0, 0, buffer.Width, buffer.Height) : _area;
        if (area.Width < 4 || area.Height < 3)
        {
            return;
        }

        buffer.Fill(area, ' ', _theme.DialogText);
        buffer.DrawBox(area, BoxStyle.Double, _theme.DialogBox);

        string title = $" {Title} ";
        if (title.Length <= area.Width - 2)
        {
            buffer.Write(area.X + ((area.Width - title.Length) / 2), area.Y, title, _theme.DialogBoxTitle);
        }

        int innerX = area.X + 2;
        int innerWidth = area.Width - 4;
        int firstRow = area.Y + 1;
        int rows = area.Height - 2;
        if (rows <= 0 || innerWidth <= 0)
        {
            return;
        }

        ClampScroll(rows);

        for (int i = 0; i < rows; i++)
        {
            int index = _scroll + i;
            if (index >= _lines.Count)
            {
                break;
            }

            DrawLine(buffer, _lines[index], innerX, firstRow + i, innerWidth);
        }

        DrawScrollBar(buffer, area, firstRow, rows);
    }

    /// <inheritdoc/>
    public bool HandleInput(InputEvent ev)
    {
        int page = Math.Max(1, _area.Height - 3);

        if (ev.Kind == InputKind.Mouse)
        {
            if (ev.Mouse.Kind == MouseKind.Wheel)
            {
                ScrollBy(-ev.Mouse.Wheel * 3);
            }

            return true;
        }

        if (ev.Kind != InputKind.Key)
        {
            return true;
        }

        KeyEvent key = ev.Key;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter:
            case ConsoleKey.F1:
            case ConsoleKey.F10:
                IsClosed = true;
                return false;

            case ConsoleKey.UpArrow:
                ScrollBy(-1);
                return true;

            case ConsoleKey.DownArrow:
                ScrollBy(1);
                return true;

            case ConsoleKey.PageUp:
                ScrollBy(-page);
                return true;

            case ConsoleKey.PageDown:
                ScrollBy(page);
                return true;

            case ConsoleKey.Home:
                _scroll = 0;
                return true;

            case ConsoleKey.End:
                _scroll = int.MaxValue;
                ClampScroll(Math.Max(1, _area.Height - 2));
                return true;

            default:
                return true;
        }
    }

    /// <inheritdoc/>
    public KeyBarLabels? KeyBarFor(KeyMods mods) =>
        mods == KeyMods.None ? HelpKeyBar : KeyBarLabels.Empty;

    /// <summary>Scrolls by a number of lines, clamped to the content.</summary>
    /// <param name="delta">Lines to move; negative scrolls towards the top.</param>
    public void ScrollBy(int delta)
    {
        _scroll += delta;
        ClampScroll(Math.Max(1, _area.Height - 2));
    }

    private void ClampScroll(int rows)
    {
        int max = Math.Max(0, _lines.Count - rows);
        _scroll = Math.Clamp(_scroll, 0, max);
    }

    private void DrawLine(ScreenBuffer buffer, Line line, int x, int y, int width)
    {
        if (line.IsSection)
        {
            buffer.WriteFixed(x, y, width, line.Text, _theme.DialogHighlight);
            return;
        }

        if (line.Keys.Length == 0 && line.Text.Length == 0)
        {
            return; // spacer
        }

        int keyWidth = Math.Min(KeyColumnWidth, width);
        buffer.WriteFixed(x, y, keyWidth, line.Keys, _theme.DialogHighlight);

        int textX = x + keyWidth;
        int textWidth = width - keyWidth;
        if (textWidth > 0)
        {
            buffer.WriteFixed(textX, y, textWidth, line.Text, _theme.DialogText);
        }
    }

    private void DrawScrollBar(ScreenBuffer buffer, Rect area, int firstRow, int rows)
    {
        if (_lines.Count <= rows || rows <= 0)
        {
            return;
        }

        int x = area.Right - 1;
        buffer.VLine(x, firstRow, rows, BoxChars.ScrollBarTrack, _theme.DialogBox);

        int thumbSize = Math.Max(1, rows * rows / _lines.Count);
        int span = Math.Max(1, _lines.Count - rows);
        int thumbTop = (_scroll * (rows - thumbSize)) / span;
        buffer.VLine(x, firstRow + thumbTop, thumbSize, BoxChars.ScrollBarThumb, _theme.DialogBox);
    }

    /// <summary>
    /// Every key binding, grouped by area. This is the list the help screen renders and the README
    /// is generated from.
    /// </summary>
    public static IReadOnlyList<(string Section, string Keys, string Description)> Bindings { get; } =
    [
        ("Panels", "Up / Down", "Move the cursor one item"),
        ("Panels", "Left / Right", "Move one column, or edit the command line"),
        ("Panels", "PgUp / PgDn", "Scroll one page"),
        ("Panels", "Home / End", "First / last item"),
        ("Panels", "Enter", "Enter a folder, or run the file under the cursor"),
        ("Panels", "Ctrl+PgDn", "Enter the folder under the cursor"),
        ("Panels", "Ctrl+PgUp", "Go to the parent folder"),
        ("Panels", "Ctrl+\\", "Go to the root of the current drive"),
        ("Panels", "Tab", "Switch the active panel"),
        ("Panels", "Ctrl+U", "Swap the two panels"),
        ("Panels", "Ctrl+R", "Re-read the active panel"),
        ("Panels", "Ctrl+O", "Hide or show both panels"),
        ("Panels", "Ctrl+P", "Hide or show the passive panel"),
        ("Panels", "Ctrl+F1 / Ctrl+F2", "Hide or show the left / right panel"),
        ("Panels", "Ctrl+H", "Show or hide hidden and system files"),
        ("Panels", "Ctrl+B", "Show or hide the function key bar"),
        ("Panels", "Alt+F1 / Alt+F2", "Change the drive of the left / right panel"),
        ("Panels", "Alt+<letter>", "Quick search by name"),

        ("Selection", "Ins", "Tag the item and move down"),
        ("Selection", "Shift+arrows", "Tag while moving the cursor"),
        ("Selection", "Gray +", "Tag a group of files by mask"),
        ("Selection", "Gray -", "Untag a group of files by mask"),
        ("Selection", "Gray *", "Invert the selection"),
        ("Selection", "Ctrl+Gray +", "Tag every file with the same extension"),
        ("Selection", "Ctrl+Gray -", "Untag every file with the same extension"),
        ("Selection", "Shift+Gray +", "Tag everything"),
        ("Selection", "Shift+Gray -", "Untag everything"),
        ("Selection", "Ctrl+A", "Tag every file in the panel"),

        ("View modes", "Ctrl+1", "Brief - three name columns"),
        ("View modes", "Ctrl+2", "Medium - two name columns"),
        ("View modes", "Ctrl+3", "Full - name, size, date and time"),
        ("View modes", "Ctrl+4", "Wide - name and size"),
        ("View modes", "Ctrl+5", "Detailed - with the attributes"),

        ("Sorting", "Ctrl+F3", "Sort by name"),
        ("Sorting", "Ctrl+F4", "Sort by extension"),
        ("Sorting", "Ctrl+F5", "Sort by last write time"),
        ("Sorting", "Ctrl+F6", "Sort by size"),
        ("Sorting", "Ctrl+F7", "Leave the panel unsorted"),
        ("Sorting", "Ctrl+F8", "Sort by creation time"),
        ("Sorting", "Ctrl+F9", "Sort by access time"),
        ("Sorting", "Ctrl+F12", "Show the sort modes menu"),

        ("Commands", "F1", "This help"),
        ("Commands", "F2", "User menu"),
        ("Commands", "F3", "View the file under the cursor"),
        ("Commands", "F4", "Edit the file under the cursor"),
        ("Commands", "F5", "Copy"),
        ("Commands", "F6", "Rename or move"),
        ("Commands", "F7", "Create a folder"),
        ("Commands", "F8", "Delete"),
        ("Commands", "F9", "Open the horizontal menu"),
        ("Commands", "F10", "Quit"),
        ("Commands", "F11", "Extras: file search, folder size, compare, swap"),
        ("Commands", "F12", "Screens list"),
        ("Commands", "Shift+F4", "Create and edit a new file"),
        ("Commands", "Shift+F5", "Copy the item under the cursor into this folder"),
        ("Commands", "Shift+F6", "Rename the item under the cursor"),
        ("Commands", "Shift+F8", "Delete permanently, bypassing the recycle bin"),
        ("Commands", "Shift+Del", "Delete permanently, bypassing the recycle bin"),
        ("Commands", "Shift+F9", "Save the settings"),
        ("Commands", "Ctrl+L", "Folder size of the tagged items"),
        ("Commands", "Ctrl+Ins", "Copy the tagged names to the clipboard"),
        ("Commands", "Alt+Shift+Ins", "Copy the tagged full paths to the clipboard"),
        ("Commands", "Alt+F7", "Find file"),
        ("Commands", "Alt+F8", "Command history"),
        ("Commands", "Alt+F12", "Folders history"),
        ("Commands", "Alt+F10", "Find folder - not implemented in this version"),

        ("Viewer", "Up / Down", "Scroll one line"),
        ("Viewer", "PgUp / PgDn", "Scroll one page"),
        ("Viewer", "Home / End", "Start / end of the file"),
        ("Viewer", "Left / Right", "Scroll sideways in unwrapped mode"),
        ("Viewer", "F2", "Toggle line wrapping"),
        ("Viewer", "F4", "Switch between text and hex"),
        ("Viewer", "F7", "Search"),
        ("Viewer", "Shift+F7", "Search again"),
        ("Viewer", "F10 / Esc", "Close the viewer"),

        ("Editor", "Arrows / PgUp / PgDn", "Move the caret"),
        ("Editor", "Home / End", "Start / end of the line"),
        ("Editor", "Ctrl+Home / Ctrl+End", "Start / end of the file"),
        ("Editor", "Shift+arrows", "Select text"),
        ("Editor", "Ctrl+Y", "Delete the current line"),
        ("Editor", "F2", "Save"),
        ("Editor", "F7", "Search"),
        ("Editor", "Shift+F7", "Search again"),
        ("Editor", "F10 / Esc", "Close the editor"),

        ("Command line", "Any character", "Type a command"),
        ("Command line", "Enter", "Run the command"),
        ("Command line", "Esc", "Clear the line"),
        ("Command line", "Ctrl+Y", "Clear the line"),
        ("Command line", "Up / Down", "Walk the command history"),
        ("Command line", "Ctrl+E / Ctrl+X", "Walk the command history"),
        ("Command line", "Tab", "Complete the path under the caret"),
        ("Command line", "Ctrl+Left / Ctrl+Right", "Move one word"),
        ("Command line", "Ctrl+Enter / Ctrl+J", "Insert the name under the cursor"),
        ("Command line", "Ctrl+F", "Insert the full name under the cursor"),
        ("Command line", "cd <path>", "Change the active panel's folder"),
    ];

    /// <summary>The section names, in the order the page shows them.</summary>
    public static IReadOnlyList<string> Sections { get; } =
        [.. Bindings.Select(static b => b.Section).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Renders <see cref="Bindings"/> as Markdown tables, one per section, for the README.
    /// </summary>
    /// <returns>The Markdown text.</returns>
    public static string ToMarkdown()
    {
        var sb = new StringBuilder();
        string? section = null;

        foreach ((string s, string keys, string description) in Bindings)
        {
            if (!string.Equals(section, s, StringComparison.Ordinal))
            {
                if (section is not null)
                {
                    sb.Append('\n');
                }

                section = s;
                sb.Append("### ").Append(s).Append("\n\n");
                sb.Append("| Key | Action |\n| --- | --- |\n");
            }

            sb.Append("| `").Append(keys).Append("` | ").Append(description).Append(" |\n");
        }

        return sb.ToString();
    }

    private static List<Line> BuildLines()
    {
        var lines = new List<Line>(Bindings.Count + (Sections.Count * 2));
        string? section = null;

        foreach ((string s, string keys, string description) in Bindings)
        {
            if (!string.Equals(section, s, StringComparison.Ordinal))
            {
                if (section is not null)
                {
                    lines.Add(Line.Spacer);
                }

                section = s;
                lines.Add(new Line(true, string.Empty, s));
            }

            lines.Add(new Line(false, keys, description));
        }

        return lines;
    }

    private static readonly KeyBarLabels HelpKeyBar = KeyBarLabels.Of(
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, "Quit");

    private readonly record struct Line(bool IsSection, string Keys, string Text)
    {
        public static Line Spacer => new(false, string.Empty, string.Empty);
    }
}
