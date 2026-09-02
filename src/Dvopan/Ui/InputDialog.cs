using Dvopan.Core;
using Dvopan.Rendering;
using Dvopan.Theming;
using Dvopan.Ui.Controls;

namespace Dvopan.Ui;

/// <summary>
/// The one-line prompt behind <see cref="IUiServices.Input"/>: a caption, an
/// <see cref="EditControl"/> and an OK/Cancel row.
/// </summary>
/// <remarks>
/// Enter accepts, Esc cancels. The initial text opens selected with the caret at its end, so
/// the first typed character replaces the whole suggestion, and a motion key drops the
/// selection to edit it instead. When a history list is supplied the edit field walks it with Up
/// and Down, and Ctrl+Down calls <see cref="EditControl.HistoryChooser"/> - which the host wires
/// to a real <see cref="ListDialog"/>, because a control may not own a modal loop.
/// </remarks>
public sealed class InputDialog : Dialog
{
    /// <summary>The width the dialog asks for before the screen clamps it.</summary>
    public const int DefaultWidth = 60;

    private readonly LabelControl _prompt;

    /// <summary>Creates an input dialog.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="title">The title centred on the top frame line.</param>
    /// <param name="prompt">The caption drawn above the edit field.</param>
    /// <param name="initial">The text the field starts with.</param>
    /// <param name="history">The history Up and Down walk, oldest first.</param>
    /// <param name="width">The desired outer width.</param>
    public InputDialog(
        Theme theme,
        string title,
        string prompt,
        string initial = "",
        IReadOnlyList<string>? history = null,
        int width = DefaultWidth)
        : base(theme, title, Math.Max(20, width), 7)
    {
        // The prompt is a plain caption: a path or a mask must not be read as a hotkey marker.
        _prompt = Add(new LabelControl(prompt ?? string.Empty)
        {
            ParseHotkey = false,
            Bounds = new Rect(1, 1, 1, 1),
        });

        Edit = Add(new EditControl(initial) { History = history, Bounds = new Rect(1, 2, 1, 1) });

        OkButton = Add(new ButtonControl("&Ok", DialogResult.Ok) { IsDefault = true });
        CancelButtonControl = Add(new ButtonControl("&Cancel", DialogResult.Cancel));

        DefaultButton = OkButton;
        CancelButton = CancelButtonControl;
        BareHotkeys = false; // the edit field owns every printable character
        SetFocus(Edit);

        // The edit was already focused when it was added, so the focus-entry selection never
        // fired; select the suggestion explicitly so the first keypress replaces it.
        if (Edit.Text.Length > 0)
        {
            Edit.SelectAll();
        }
    }

    /// <summary>The edit field, exposed so the caller can set a mask, a history chooser or a length limit.</summary>
    public EditControl Edit { get; }

    /// <summary>The OK button.</summary>
    public ButtonControl OkButton { get; }

    /// <summary>The Cancel button.</summary>
    public ButtonControl CancelButtonControl { get; }

    /// <summary>The caption above the edit field.</summary>
    public string Prompt
    {
        get => _prompt.Text;
        set => _prompt.Text = value ?? string.Empty;
    }

    /// <summary>The entered text.</summary>
    public string Text => Edit.Text;

    /// <summary>The entered text, or <see langword="null"/> when the dialog was cancelled.</summary>
    public string? AcceptedText => Result == DialogResult.Ok ? Edit.Text : null;

    /// <inheritdoc/>
    protected override void OnLayout()
    {
        int client = ClientWidth;
        int inner = Math.Max(1, client - 2);

        _prompt.Bounds = new Rect(1, 1, inner, 1);
        Edit.Bounds = new Rect(1, 2, inner, 1);

        int row = ClientHeight - 1;
        int total = OkButton.PreferredWidth + 1 + CancelButtonControl.PreferredWidth;
        int x = Math.Max(0, (client - total) / 2);

        OkButton.Bounds = new Rect(x, row, OkButton.PreferredWidth, 1);
        CancelButtonControl.Bounds =
            new Rect(x + OkButton.PreferredWidth + 1, row, CancelButtonControl.PreferredWidth, 1);
    }
}
