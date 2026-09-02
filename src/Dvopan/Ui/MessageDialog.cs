using Dvopan.Core;
using Dvopan.Rendering;
using Dvopan.Theming;
using Dvopan.Ui.Controls;

namespace Dvopan.Ui;

/// <summary>
/// The message box behind <see cref="IUiServices.Message"/>: a title, a block of centred body lines
/// and a centred row of buttons derived from a <see cref="MessageButtons"/> set.
/// </summary>
/// <remarks>
/// The dialog sizes itself to its content and is then clamped to the screen by
/// <see cref="Dialog.Layout"/>; over-long body lines are truncated with an ellipsis rather than
/// wrapped. The layout is Far's: a blank line, the body, a blank line, the buttons.
/// </remarks>
public sealed class MessageDialog : Dialog
{
    /// <summary>The buttons a message box can show, in the order Far lays them out.</summary>
    /// <remarks>
    /// Affirmative answers come first and Cancel last, so any set combining them - the overwrite
    /// prompt's Yes|No|All|SkipAll|Cancel, the error prompt's Retry|Skip|SkipAll|Cancel - starts
    /// with the affirmative button focused and default. Pressing Enter must overwrite or retry,
    /// never silently abort the whole operation.
    /// </remarks>
    private static readonly (MessageButtons Flag, string Caption, DialogResult Result)[] ButtonTable =
    [
        (MessageButtons.Ok, "&Ok", DialogResult.Ok),
        (MessageButtons.Yes, "&Yes", DialogResult.Yes),
        (MessageButtons.No, "&No", DialogResult.No),
        (MessageButtons.Retry, "&Retry", DialogResult.Retry),
        (MessageButtons.Skip, "&Skip", DialogResult.Skip),
        (MessageButtons.SkipAll, "S&kip all", DialogResult.SkipAll),
        (MessageButtons.All, "&All", DialogResult.All),
        (MessageButtons.Cancel, "&Cancel", DialogResult.Cancel),
    ];

    private readonly string[] _lines;
    private readonly List<ButtonControl> _buttons = [];

    /// <summary>Creates a message box sized to its content.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="title">The title centred on the top frame line.</param>
    /// <param name="lines">The body, one screen line per element.</param>
    /// <param name="buttons">Which buttons to offer; an empty set yields a lone OK.</param>
    /// <param name="warning">When set, the red warning palette is used.</param>
    public MessageDialog(
        Theme theme,
        string title,
        string[]? lines,
        MessageButtons buttons,
        bool warning = false)
        : base(theme, title, 20, 7, warning)
    {
        _lines = lines is null || lines.Length == 0 ? [string.Empty] : lines;

        foreach (var (flag, caption, result) in ButtonTable)
        {
            if ((buttons & flag) != 0)
            {
                _buttons.Add(Add(new ButtonControl(caption, result)));
            }
        }

        if (_buttons.Count == 0)
        {
            _buttons.Add(Add(new ButtonControl("&Ok", DialogResult.Ok)));
        }

        DefaultButton = _buttons[0];
        SetFocus(_buttons[0]);

        // Esc always answers Cancel, whether or not a Cancel button is on screen.
        CancelButton = null;

        for (int i = 0; i < _lines.Length; i++)
        {
            Add(new LabelControl(_lines[i], HAlign.Center)
            {
                ParseHotkey = false,
                Bounds = new Rect(0, 1 + i, 1, 1),
            });
        }

        Width = MeasureWidth();
        Height = _lines.Length + 5;
    }

    /// <summary>The body lines, exactly as passed in.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The buttons, left to right.</summary>
    public IReadOnlyList<ButtonControl> Buttons => _buttons;

    /// <summary>The total width of the button row, including the single-space gaps.</summary>
    public int ButtonRowWidth
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _buttons.Count; i++)
            {
                total += _buttons[i].PreferredWidth + (i > 0 ? 1 : 0);
            }

            return total;
        }
    }

    /// <inheritdoc/>
    protected override void OnLayout()
    {
        int client = ClientWidth;
        int buttonRow = ClientHeight - 1;

        foreach (var control in Controls)
        {
            if (control is LabelControl label)
            {
                label.Bounds = new Rect(0, label.Bounds.Y, client, 1);
            }
        }

        int x = Math.Max(0, (client - ButtonRowWidth) / 2);
        foreach (var b in _buttons)
        {
            int w = b.PreferredWidth;
            b.Bounds = new Rect(x, buttonRow, w, 1);
            x += w + 1;
        }
    }

    private int MeasureWidth()
    {
        int longest = 0;
        foreach (string line in _lines)
        {
            longest = Math.Max(longest, (line ?? string.Empty).Length);
        }

        int width = Math.Max(longest + 8, ButtonRowWidth + 4);
        width = Math.Max(width, Title.Length + 6);
        return Math.Max(20, width);
    }
}
