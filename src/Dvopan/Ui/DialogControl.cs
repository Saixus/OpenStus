using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Theming;

namespace Dvopan.Ui;

/// <summary>
/// The eleven colours a dialog and its controls draw with, already resolved for the normal or the
/// warning palette so no drawing code has to branch on <c>warning</c>.
/// </summary>
/// <remarks>
/// The palette has no warning variants for the edit and list colours, so those fall back to the
/// normal dialog entries, keeping the classic look: an edit field inside a red warning box is
/// still black on cyan.
/// </remarks>
public sealed class DialogPalette
{
    private DialogPalette()
    {
    }

    /// <summary>Frame and background.</summary>
    public CellStyle Box { get; private init; }

    /// <summary>The title drawn on the top frame line.</summary>
    public CellStyle Title { get; private init; }

    /// <summary>Ordinary label text.</summary>
    public CellStyle Text { get; private init; }

    /// <summary>The hotkey character inside label text.</summary>
    public CellStyle Highlight { get; private init; }

    /// <summary>Edit field background.</summary>
    public CellStyle Edit { get; private init; }

    /// <summary>Selected text inside an edit field.</summary>
    public CellStyle EditSelected { get; private init; }

    /// <summary>A disabled (read-only) edit field.</summary>
    public CellStyle EditDisabled { get; private init; }

    /// <summary>An unfocused button.</summary>
    public CellStyle Button { get; private init; }

    /// <summary>The hotkey character of an unfocused button.</summary>
    public CellStyle ButtonHighlight { get; private init; }

    /// <summary>The focused button.</summary>
    public CellStyle ButtonSelected { get; private init; }

    /// <summary>The hotkey character of the focused button.</summary>
    public CellStyle ButtonSelectedHighlight { get; private init; }

    /// <summary>An ordinary list row.</summary>
    public CellStyle ListText { get; private init; }

    /// <summary>The hotkey character inside a list row.</summary>
    public CellStyle ListHighlight { get; private init; }

    /// <summary>The list row under the cursor.</summary>
    public CellStyle ListSelected { get; private init; }

    /// <summary>The hotkey character of the list row under the cursor.</summary>
    public CellStyle ListSelectedHighlight { get; private init; }

    /// <summary>The filled part of a progress bar.</summary>
    public CellStyle ProgressBar { get; private init; }

    /// <summary>The empty part of a progress bar.</summary>
    public CellStyle ProgressBarEmpty { get; private init; }

    /// <summary>Resolves the palette a dialog draws with.</summary>
    /// <param name="theme">The active theme.</param>
    /// <param name="warning">When set, the red warning entries are used for the frame and text.</param>
    /// <returns>A resolved palette; never <see langword="null"/>.</returns>
    public static DialogPalette For(Theme theme, bool warning)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return warning
            ? new DialogPalette
            {
                Box = theme.WarnDialogBox,
                Title = theme.WarnDialogBoxTitle,
                Text = theme.WarnDialogText,
                Highlight = theme.WarnDialogHighlight,
                Edit = theme.DialogEdit,
                EditSelected = theme.DialogEditSelected,
                EditDisabled = theme.DialogEditDisabled,
                Button = theme.WarnDialogButton,
                ButtonHighlight = theme.WarnDialogButtonHighlight,
                ButtonSelected = theme.WarnDialogButtonSelected,
                ButtonSelectedHighlight = theme.WarnDialogButtonSelectedHighlight,
                ListText = theme.DialogListText,
                ListHighlight = theme.DialogListHighlight,
                ListSelected = theme.DialogListSelected,
                ListSelectedHighlight = theme.DialogListSelectedHighlight,
                ProgressBar = theme.ProgressBar,
                ProgressBarEmpty = theme.ProgressBarEmpty,
            }
            : new DialogPalette
            {
                Box = theme.DialogBox,
                Title = theme.DialogBoxTitle,
                Text = theme.DialogText,
                Highlight = theme.DialogHighlight,
                Edit = theme.DialogEdit,
                EditSelected = theme.DialogEditSelected,
                EditDisabled = theme.DialogEditDisabled,
                Button = theme.DialogButton,
                ButtonHighlight = theme.DialogButtonHighlight,
                ButtonSelected = theme.DialogButtonSelected,
                ButtonSelectedHighlight = theme.DialogButtonSelectedHighlight,
                ListText = theme.DialogListText,
                ListHighlight = theme.DialogListHighlight,
                ListSelected = theme.DialogListSelected,
                ListSelectedHighlight = theme.DialogListSelectedHighlight,
                ProgressBar = theme.ProgressBar,
                ProgressBarEmpty = theme.ProgressBarEmpty,
            };
    }
}

/// <summary>
/// Base class of everything that lives inside a <see cref="Dialog"/>.
/// </summary>
/// <remarks>
/// <see cref="Bounds"/> is relative to the dialog's client area (the rectangle inside the frame), so
/// a control never has to know where the dialog ended up on screen. All drawing goes through the
/// clipping primitives of <see cref="ScreenBuffer"/>, so a control that does not fit simply gets cut
/// off instead of throwing.
/// </remarks>
public abstract class DialogControl
{
    /// <summary>The dialog this control was added to, or <see langword="null"/> while detached.</summary>
    public Dialog? Owner { get; internal set; }

    /// <summary>Position and size relative to the owner's client area.</summary>
    public Rect Bounds { get; set; }

    /// <summary>When clear, the control is neither drawn nor reachable by Tab.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>When clear, the control is drawn greyed out and cannot take the focus.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Arbitrary caller data; the toolkit never looks at it.</summary>
    public object? Tag { get; set; }

    /// <summary><see langword="true"/> when Tab can land on this control.</summary>
    public virtual bool CanFocus => Visible && Enabled;

    /// <summary><see langword="true"/> when this control currently owns the dialog's focus.</summary>
    public bool HasFocus => Owner is not null && ReferenceEquals(Owner.Focused, this);

    /// <summary>
    /// The Alt+letter shortcut declared by the control's caption with a <c>'&amp;'</c> marker,
    /// lowercased, or <see langword="null"/> when it declares none.
    /// </summary>
    public virtual char? Hotkey => null;

    /// <summary><see langword="true"/> when the hardware cursor should be shown inside this control.</summary>
    public virtual bool WantsCursor => false;

    /// <summary>Where the hardware cursor goes, relative to the control's own top-left corner.</summary>
    public virtual (int X, int Y) CursorOffset => (0, 0);

    /// <summary>The control's absolute screen rectangle, given the dialog's client area.</summary>
    /// <param name="client">The dialog client rectangle in screen cells.</param>
    /// <returns>The control rectangle in screen cells.</returns>
    public Rect ScreenBounds(Rect client) =>
        new(client.X + Bounds.X, client.Y + Bounds.Y, Bounds.Width, Bounds.Height);

    /// <summary>Paints the control.</summary>
    /// <param name="buffer">The back buffer.</param>
    /// <param name="client">The dialog's client rectangle in screen cells.</param>
    /// <param name="palette">The resolved dialog palette.</param>
    public abstract void Draw(ScreenBuffer buffer, Rect client, DialogPalette palette);

    /// <summary>Handles a key press aimed at the focused control.</summary>
    /// <param name="key">The key press.</param>
    /// <returns><see langword="true"/> when the control consumed the key.</returns>
    public virtual bool HandleKey(KeyEvent key) => false;

    /// <summary>Handles a mouse event that landed on this control.</summary>
    /// <param name="mouse">The event, in absolute screen coordinates.</param>
    /// <param name="client">The dialog's client rectangle in screen cells.</param>
    /// <returns><see langword="true"/> when the control consumed the event.</returns>
    public virtual bool HandleMouse(MouseEvent mouse, Rect client) => false;

    /// <summary>
    /// Runs the control's default action - what Alt+hotkey, Enter on a button or a click does.
    /// </summary>
    /// <returns><see langword="true"/> when something happened.</returns>
    public virtual bool Activate() => false;

    /// <summary>
    /// Called by the owning dialog whenever the focus lands on this control after having been
    /// elsewhere. The base implementation does nothing; an edit field selects its whole text
    /// here.
    /// </summary>
    protected internal virtual void OnFocusEntered()
    {
    }

    /// <summary>The width the control would like to be given, used by the self-sizing dialogs.</summary>
    public virtual int PreferredWidth => Bounds.Width;
}
