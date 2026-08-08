using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Theming;
using OpenCommander.Ui.Controls;

namespace OpenCommander.Ui;

/// <summary>
/// A modal, centred, shadowed dialog box: a double-line frame in the dialog (or warning) palette,
/// a centred title on the top frame line, an optional centred footer on the bottom one, and a list
/// of <see cref="DialogControl"/>s laid out inside.
/// </summary>
/// <remarks>
/// <para>
/// A dialog never runs its own input loop. The application's <see cref="IUiServices.RunModal"/>
/// pumps it: drain input into <see cref="HandleInput"/>, call <see cref="Draw"/>, flush. The dialog
/// signals completion through <see cref="IsClosed"/>, and the answer through <see cref="Result"/>.
/// </para>
/// <para>
/// Key handling order is: the <see cref="OnKey"/> hook (so a subclass can veto anything), then the
/// focused control, then Tab/Shift+Tab, Enter, Esc, and finally the Alt+hotkey and bare-hotkey
/// lookups. Because the focused control gets the key first, an edit field naturally swallows plain
/// characters and hotkeys stop firing while the caret is in it.
/// </para>
/// </remarks>
public class Dialog : IScreenComponent
{
    private readonly List<DialogControl> _controls = [];
    private DialogControl? _focused;
    private Rect _area;

    /// <summary>Creates a dialog of a fixed size.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="title">The title centred on the top frame line; may be empty.</param>
    /// <param name="width">The desired outer width, including the frame.</param>
    /// <param name="height">The desired outer height, including the frame.</param>
    /// <param name="warning">When set, the red warning palette is used.</param>
    public Dialog(Theme theme, string title, int width, int height, bool warning = false)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Theme = theme;
        Title = title ?? string.Empty;
        Width = Math.Max(3, width);
        Height = Math.Max(3, height);
        IsWarning = warning;
        Bounds = new Rect(0, 0, Width, Height);
    }

    /// <summary>The palette this dialog draws with.</summary>
    public Theme Theme { get; }

    /// <summary>The title centred on the top frame line.</summary>
    public string Title { get; set; }

    /// <summary>An optional caption centred on the bottom frame line.</summary>
    public string? Footer { get; set; }

    /// <summary>When set, the red warning palette is used.</summary>
    public bool IsWarning { get; set; }

    /// <summary>The frame line style; Far uses double lines for dialogs.</summary>
    public BoxStyle FrameStyle { get; set; } = BoxStyle.Double;

    /// <summary>When set, the classic two-column drop shadow is painted.</summary>
    public bool HasShadow { get; set; } = true;

    /// <summary>The desired outer width, including the frame.</summary>
    public int Width { get; set; }

    /// <summary>The desired outer height, including the frame.</summary>
    public int Height { get; set; }

    /// <summary>
    /// The dialog's outer rectangle in screen cells, as computed by the last <see cref="Layout"/>.
    /// </summary>
    public Rect Bounds { get; private set; }

    /// <summary>The rectangle inside the frame; every control's <see cref="DialogControl.Bounds"/> is relative to it.</summary>
    public Rect ClientBounds => Bounds.Inflate(-1, -1);

    /// <summary>The width available to controls.</summary>
    public int ClientWidth => Math.Max(0, Width - 2);

    /// <summary>The height available to controls.</summary>
    public int ClientHeight => Math.Max(0, Height - 2);

    /// <summary>How the dialog ended; <see cref="DialogResult.None"/> while it is still open.</summary>
    public DialogResult Result { get; protected set; } = DialogResult.None;

    /// <inheritdoc/>
    public bool IsClosed { get; private set; }

    /// <summary>The controls, in the order they were added - which is also the Tab order.</summary>
    public IReadOnlyList<DialogControl> Controls => _controls;

    /// <summary>The control with the keyboard focus, or <see langword="null"/> when nothing can take it.</summary>
    public DialogControl? Focused => _focused;

    /// <summary>
    /// The button Enter presses when the focus is not on a button. When left unset the first button
    /// marked <see cref="ButtonControl.IsDefault"/> is used, and failing that the first button added.
    /// </summary>
    public ButtonControl? DefaultButton { get; set; }

    /// <summary>
    /// The button Esc presses. When left unset, Esc simply closes the dialog with
    /// <see cref="DialogResult.Cancel"/>.
    /// </summary>
    public ButtonControl? CancelButton { get; set; }

    /// <summary>
    /// When set, a plain letter that no control consumed activates the control declaring it as a
    /// hotkey - the behaviour message boxes want, where <c>y</c> answers Yes.
    /// </summary>
    public bool BareHotkeys { get; set; } = true;

    /// <summary>Raised just before the dialog closes, with the result it is closing with.</summary>
    public Action<DialogResult>? Closing { get; set; }

    /// <summary><see langword="true"/> when the focused control wants the hardware cursor shown.</summary>
    public bool WantsCursor => _focused is { Visible: true } f && f.WantsCursor;

    /// <summary>The absolute screen column of the hardware cursor.</summary>
    public int CursorX
    {
        get
        {
            if (_focused is null)
            {
                return Bounds.X;
            }

            return ClientBounds.X + _focused.Bounds.X + _focused.CursorOffset.X;
        }
    }

    /// <summary>The absolute screen row of the hardware cursor.</summary>
    public int CursorY
    {
        get
        {
            if (_focused is null)
            {
                return Bounds.Y;
            }

            return ClientBounds.Y + _focused.Bounds.Y + _focused.CursorOffset.Y;
        }
    }

    /// <summary>Adds a control and returns it, so calls can be chained into a field.</summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="control">The control to add.</param>
    /// <returns><paramref name="control"/>.</returns>
    public T Add<T>(T control)
        where T : DialogControl
    {
        ArgumentNullException.ThrowIfNull(control);

        control.Owner = this;
        _controls.Add(control);
        _focused ??= control.CanFocus ? control : null;
        return control;
    }

    /// <summary>Moves the focus to a control. Controls that cannot take the focus are ignored.</summary>
    /// <param name="control">The control to focus.</param>
    /// <returns><see langword="true"/> when the focus moved.</returns>
    public bool SetFocus(DialogControl? control)
    {
        if (control is null || !control.CanFocus || !_controls.Contains(control))
        {
            return false;
        }

        _focused = control;
        return true;
    }

    /// <summary>Moves the focus to the next focusable control, wrapping at the end.</summary>
    public void FocusNext() => MoveFocus(1);

    /// <summary>Moves the focus to the previous focusable control, wrapping at the start.</summary>
    public void FocusPrevious() => MoveFocus(-1);

    /// <summary>Closes the dialog with a result.</summary>
    /// <param name="result">The answer to report through <see cref="Result"/>.</param>
    public void Close(DialogResult result)
    {
        if (IsClosed)
        {
            return;
        }

        Result = result;
        IsClosed = true;
        Closing?.Invoke(result);
    }

    /// <inheritdoc/>
    public void Layout(Rect area)
    {
        _area = area;

        int w = Math.Max(3, Math.Min(Width, Math.Max(3, area.Width)));
        int h = Math.Max(3, Math.Min(Height, Math.Max(3, area.Height)));
        int x = area.X + Math.Max(0, (area.Width - w) / 2);
        int y = area.Y + Math.Max(0, (area.Height - h) / 2);

        Bounds = new Rect(x, y, w, h);
        OnLayout();
    }

    /// <summary>
    /// Called at the end of every <see cref="Layout"/>, once <see cref="Bounds"/> is known, so a
    /// self-sizing dialog can reposition its controls.
    /// </summary>
    protected virtual void OnLayout()
    {
    }

    /// <inheritdoc/>
    public virtual void Draw(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var palette = DialogPalette.For(Theme, IsWarning);
        var bounds = Bounds;

        buffer.Fill(bounds, ' ', palette.Box);
        buffer.DrawBox(bounds, FrameStyle, palette.Box);

        DrawFrameCaption(buffer, bounds.Y, Title, palette.Title);
        DrawFrameCaption(buffer, bounds.Bottom - 1, Footer, palette.Title);

        var client = ClientBounds;
        foreach (var control in _controls)
        {
            if (control.Visible)
            {
                control.Draw(buffer, client, palette);
            }
        }

        if (HasShadow)
        {
            buffer.DrawShadow(bounds);
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
    public virtual KeyBarLabels? KeyBarFor(KeyMods mods) => KeyBarLabels.Empty;

    /// <summary>
    /// A subclass hook that sees every key before the focused control does.
    /// </summary>
    /// <param name="key">The key press.</param>
    /// <returns><see langword="true"/> to consume the key and stop all further processing.</returns>
    protected virtual bool OnKey(KeyEvent key) => false;

    /// <summary>Presses the Cancel button, or closes with <see cref="DialogResult.Cancel"/>.</summary>
    protected virtual void Cancel()
    {
        if (CancelButton is { Visible: true, Enabled: true } b)
        {
            b.Activate();
            return;
        }

        Close(DialogResult.Cancel);
    }

    /// <summary>Presses the default button, if there is one.</summary>
    protected virtual void ActivateDefault()
    {
        var button = ResolveDefaultButton();
        button?.Activate();
    }

    /// <summary>Feeds one key press through the whole dialog key pipeline.</summary>
    /// <param name="key">The key press.</param>
    /// <returns><see langword="true"/> when something handled it.</returns>
    public bool HandleKey(KeyEvent key)
    {
        if (IsClosed)
        {
            return false;
        }

        if (OnKey(key))
        {
            return true;
        }

        if (_focused is { Visible: true, Enabled: true } focused && focused.HandleKey(key))
        {
            return true;
        }

        if (key.Is(ConsoleKey.Tab))
        {
            FocusNext();
            return true;
        }

        if (key.Is(ConsoleKey.Tab, KeyMods.Shift))
        {
            FocusPrevious();
            return true;
        }

        if (key.Is(ConsoleKey.Escape))
        {
            Cancel();
            return true;
        }

        if (key.Is(ConsoleKey.Enter))
        {
            ActivateDefault();
            return true;
        }

        char? hot = HotkeyOf(key);
        if (hot is char c)
        {
            bool alt = (key.Mods & KeyMods.Alt) != 0;
            if ((alt || BareHotkeys) && TryHotkey(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Feeds one mouse event through the dialog.</summary>
    /// <param name="mouse">The event, in absolute screen coordinates.</param>
    /// <returns><see langword="true"/> when something handled it.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        if (IsClosed)
        {
            return false;
        }

        var client = ClientBounds;
        var hit = ControlAt(mouse.X, mouse.Y, client);

        if (mouse.Kind is MouseKind.Down or MouseKind.DoubleClick)
        {
            if (hit is null)
            {
                return false;
            }

            SetFocus(hit);
            return hit.HandleMouse(mouse, client);
        }

        var target = hit ?? _focused;
        return target is not null && target.HandleMouse(mouse, client);
    }

    /// <summary>The control under a screen cell, or <see langword="null"/>.</summary>
    /// <param name="x">Screen column.</param>
    /// <param name="y">Screen row.</param>
    /// <param name="client">The dialog client rectangle.</param>
    /// <returns>The topmost matching control.</returns>
    protected DialogControl? ControlAt(int x, int y, Rect client)
    {
        for (int i = _controls.Count - 1; i >= 0; i--)
        {
            var c = _controls[i];
            if (c.Visible && c.Enabled && c.ScreenBounds(client).Contains(x, y))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Activates the control declaring <paramref name="hotkey"/>.</summary>
    /// <param name="hotkey">The lowercased hotkey character.</param>
    /// <returns><see langword="true"/> when a control claimed it.</returns>
    protected bool TryHotkey(char hotkey)
    {
        foreach (var c in _controls)
        {
            if (!c.Visible || !c.Enabled || c.Hotkey != hotkey)
            {
                continue;
            }

            if (c.CanFocus)
            {
                SetFocus(c);
            }

            c.Activate();
            return true;
        }

        return false;
    }

    /// <summary>The button Enter presses, resolving the explicit, the marked and the first button in turn.</summary>
    /// <returns>The button, or <see langword="null"/> when the dialog has none.</returns>
    protected ButtonControl? ResolveDefaultButton()
    {
        if (DefaultButton is { Visible: true, Enabled: true })
        {
            return DefaultButton;
        }

        ButtonControl? first = null;
        foreach (var c in _controls)
        {
            if (c is not ButtonControl { Visible: true, Enabled: true } b)
            {
                continue;
            }

            if (b.IsDefault)
            {
                return b;
            }

            first ??= b;
        }

        return first;
    }

    private static char? HotkeyOf(KeyEvent key)
    {
        if (key.Ch != '\0' && !char.IsControl(key.Ch))
        {
            return char.ToLowerInvariant(key.Ch);
        }

        // Windows reports Alt+letter with no character attached; recover it from the virtual key.
        if (key.Key >= ConsoleKey.A && key.Key <= ConsoleKey.Z)
        {
            return (char)('a' + (key.Key - ConsoleKey.A));
        }

        if (key.Key >= ConsoleKey.D0 && key.Key <= ConsoleKey.D9)
        {
            return (char)('0' + (key.Key - ConsoleKey.D0));
        }

        return null;
    }

    private void DrawFrameCaption(ScreenBuffer buffer, int row, string? caption, CellStyle style)
    {
        if (string.IsNullOrEmpty(caption) || Bounds.Width < 5)
        {
            return;
        }

        string text = " " + caption + " ";
        int max = Bounds.Width - 2;
        int x = Bounds.X + 1 + Math.Max(0, (max - text.Length) / 2);
        buffer.WriteFixed(x, row, Math.Min(text.Length, max), text, style);
    }

    private void MoveFocus(int step)
    {
        if (_controls.Count == 0)
        {
            return;
        }

        int start = _focused is null ? -1 : _controls.IndexOf(_focused);
        for (int i = 1; i <= _controls.Count; i++)
        {
            int index = ((start + (step * i)) % _controls.Count + _controls.Count) % _controls.Count;
            if (_controls[index].CanFocus)
            {
                _focused = _controls[index];
                return;
            }
        }
    }
}
