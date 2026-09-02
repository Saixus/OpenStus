using Dvopan.Core;
using Dvopan.Rendering;
using Dvopan.Theming;
using Dvopan.Ui.Controls;

namespace Dvopan.Ui;

/// <summary>
/// A framed picker for the long lists that would overflow a <see cref="PopupMenu"/>: drive lists,
/// command and folder history, find results.
/// </summary>
/// <remarks>
/// Enter (or a double click) accepts the row under the cursor and closes with
/// <see cref="DialogResult.Ok"/>; Esc closes with <see cref="DialogResult.Cancel"/> and leaves
/// <see cref="AcceptedIndex"/> at <c>-1</c>.
/// </remarks>
public sealed class ListDialog : Dialog
{
    /// <summary>The largest list height the dialog asks for before the screen clamps it.</summary>
    public const int MaxListHeight = 20;

    /// <summary>Creates a list dialog sized to its content.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="title">The title centred on the top frame line.</param>
    /// <param name="items">The rows.</param>
    /// <param name="selected">The row the cursor starts on.</param>
    /// <param name="showButtons">When set, an OK/Cancel row is added below the list.</param>
    public ListDialog(
        Theme theme,
        string title,
        IReadOnlyList<string> items,
        int selected = 0,
        bool showButtons = true)
        : base(theme, title, 30, 10)
    {
        ArgumentNullException.ThrowIfNull(items);

        List = Add(new ListControl(items, selected));
        List.ItemActivated = _ => Accept();

        ShowButtons = showButtons;
        if (showButtons)
        {
            OkButton = Add(new ButtonControl("&Ok", DialogResult.None, Accept) { IsDefault = true });
            CancelButtonControl = Add(new ButtonControl("&Cancel", DialogResult.Cancel));
            DefaultButton = OkButton;
            CancelButton = CancelButtonControl;
        }

        BareHotkeys = false; // the list owns printable characters for its type-search
        SetFocus(List);

        int longest = 0;
        foreach (string item in items)
        {
            longest = Math.Max(longest, (item ?? string.Empty).Length);
        }

        Width = Math.Max(Math.Max(30, title.Length + 6), longest + 6);
        int rows = Math.Clamp(items.Count == 0 ? 1 : items.Count, 1, MaxListHeight);
        Height = rows + 2 + (showButtons ? 2 : 0);
    }

    /// <summary>The list control.</summary>
    public ListControl List { get; }

    /// <summary>The OK button, or <see langword="null"/> when the dialog has no button row.</summary>
    public ButtonControl? OkButton { get; }

    /// <summary>The Cancel button, or <see langword="null"/> when the dialog has no button row.</summary>
    public ButtonControl? CancelButtonControl { get; }

    /// <summary><see langword="true"/> when a button row is drawn below the list.</summary>
    public bool ShowButtons { get; }

    /// <summary>The row under the cursor.</summary>
    public int SelectedIndex => List.SelectedIndex;

    /// <summary>The accepted row, or <c>-1</c> when the dialog was cancelled.</summary>
    public int AcceptedIndex => Result == DialogResult.Ok ? List.SelectedIndex : -1;

    /// <summary>The accepted row text, or <see langword="null"/> when the dialog was cancelled.</summary>
    public string? AcceptedItem => Result == DialogResult.Ok ? List.SelectedItem : null;

    /// <summary>Accepts the row under the cursor and closes.</summary>
    public void Accept()
    {
        if (List.SelectedIndex < 0)
        {
            Close(DialogResult.Cancel);
            return;
        }

        Close(DialogResult.Ok);
    }

    /// <inheritdoc/>
    protected override void OnLayout()
    {
        int client = ClientWidth;
        int listHeight = Math.Max(1, ClientHeight - (ShowButtons ? 2 : 0));

        List.Bounds = new Rect(0, 0, client, listHeight);

        if (!ShowButtons || OkButton is null || CancelButtonControl is null)
        {
            return;
        }

        int row = ClientHeight - 1;
        int total = OkButton.PreferredWidth + 1 + CancelButtonControl.PreferredWidth;
        int x = Math.Max(0, (client - total) / 2);

        OkButton.Bounds = new Rect(x, row, OkButton.PreferredWidth, 1);
        CancelButtonControl.Bounds =
            new Rect(x + OkButton.PreferredWidth + 1, row, CancelButtonControl.PreferredWidth, 1);
    }
}
