using Dvopan.Core;
using Dvopan.Input;
using Dvopan.Rendering;
using Dvopan.Theming;
using Dvopan.Ui.Controls;

namespace Dvopan.Ui;

/// <summary>
/// The progress box a long file operation keeps on screen: two text lines, one or two bars, and a
/// Cancel affordance.
/// </summary>
/// <remarks>
/// <para>
/// Esc and the Cancel button only raise <see cref="CancelRequested"/>; they never close the dialog
/// by themselves. The operation polls the flag, unwinds cleanly and then calls
/// <see cref="Complete"/> - otherwise a half-copied file would be left behind the moment the user
/// hit Esc. Enter is swallowed outright: the dialog often appears right after an input dialog was
/// confirmed with Enter, and a type-ahead or double-tapped Enter must not cancel the operation the
/// user just started - a progress box only reacts to Esc.
/// </para>
/// <para>
/// The bars are painted with <c>ProgressBar</c> over <c>ProgressBarEmpty</c>, using the full block
/// and light shade glyphs so the split is visible even in a monochrome terminal.
/// </para>
/// </remarks>
public sealed class ProgressDialog : Dialog
{
    /// <summary>The width the dialog asks for before the screen clamps it.</summary>
    public const int DefaultWidth = 60;

    private readonly LabelControl _line1;
    private readonly LabelControl _line2;
    private readonly ProgressBarControl _primary;
    private readonly ProgressBarControl? _secondary;
    private readonly ButtonControl _cancel;

    /// <summary>Creates a progress dialog.</summary>
    /// <param name="theme">The palette to draw with.</param>
    /// <param name="title">The title centred on the top frame line.</param>
    /// <param name="showSecondary">When set, a second bar is drawn under the first.</param>
    /// <param name="width">The desired outer width.</param>
    public ProgressDialog(Theme theme, string title, bool showSecondary = false, int width = DefaultWidth)
        : base(theme, title, Math.Max(24, width), showSecondary ? 10 : 9)
    {
        _line1 = Add(new LabelControl(string.Empty) { ParseHotkey = false, Bounds = new Rect(1, 1, 1, 1) });
        _line2 = Add(new LabelControl(string.Empty) { ParseHotkey = false, Bounds = new Rect(1, 2, 1, 1) });
        _primary = Add(new ProgressBarControl { Bounds = new Rect(1, 3, 1, 1) });

        if (showSecondary)
        {
            _secondary = Add(new ProgressBarControl { Bounds = new Rect(1, 4, 1, 1) });
        }

        // Deliberately no DefaultButton: Enter must never cancel a running operation (OnKey
        // swallows it as well), so cancelling takes Esc, Space on the button, or a click.
        _cancel = Add(new ButtonControl("&Cancel", DialogResult.None, RequestCancel));
        CancelButton = _cancel;
        BareHotkeys = false;
        SetFocus(_cancel);
    }

    /// <summary><see langword="true"/> once the user has asked to stop (Esc or the Cancel button).</summary>
    public bool CancelRequested { get; private set; }

    /// <summary>The first text line, normally the source name.</summary>
    public string Line1 => _line1.Text;

    /// <summary>The second text line, normally the destination name.</summary>
    public string Line2 => _line2.Text;

    /// <summary>The primary bar's fill, 0..1.</summary>
    public double Primary => _primary.Value;

    /// <summary>The secondary bar's fill, 0..1, or <see langword="null"/> when there is no second bar.</summary>
    public double? Secondary => _secondary?.Value;

    /// <summary><see langword="true"/> when a second bar is drawn.</summary>
    public bool HasSecondaryBar => _secondary is not null;

    /// <summary>When set, the fill percentage is written across the middle of each bar.</summary>
    public bool ShowPercent
    {
        get => _primary.ShowPercent;
        set
        {
            _primary.ShowPercent = value;
            if (_secondary is not null)
            {
                _secondary.ShowPercent = value;
            }
        }
    }

    /// <summary>Refreshes the two text lines and the bars.</summary>
    /// <param name="line1">The first text line.</param>
    /// <param name="line2">The second text line.</param>
    /// <param name="primary">The primary fill, 0..1; values outside the range are clamped.</param>
    /// <param name="secondary">
    /// The secondary fill, 0..1, or <see langword="null"/> to leave the second bar untouched.
    /// </param>
    public void Update(string line1, string line2, double primary, double? secondary)
    {
        _line1.Text = line1 ?? string.Empty;
        _line2.Text = line2 ?? string.Empty;
        _primary.Value = primary;

        if (secondary is double s && _secondary is not null)
        {
            _secondary.Value = s;
        }
    }

    /// <summary>Raises <see cref="CancelRequested"/> without closing the dialog.</summary>
    public void RequestCancel() => CancelRequested = true;

    /// <summary>Closes the dialog once the operation has finished or unwound.</summary>
    /// <param name="result">
    /// The result to report; defaults to <see cref="DialogResult.Cancel"/> when the user asked to
    /// stop and <see cref="DialogResult.Ok"/> otherwise.
    /// </param>
    public void Complete(DialogResult? result = null) =>
        Close(result ?? (CancelRequested ? DialogResult.Cancel : DialogResult.Ok));

    /// <inheritdoc/>
    protected override bool OnKey(KeyEvent key)
    {
        if (key.Is(ConsoleKey.Escape))
        {
            RequestCancel();
            return true; // consumed: the operation decides when the dialog goes away
        }

        if (key.Is(ConsoleKey.Enter))
        {
            // Swallowed before the focused Cancel button can see it: a type-ahead Enter from
            // the dialog that launched the operation must not abort it. Only Esc, Space on the
            // button, or a click cancel.
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnLayout()
    {
        int client = ClientWidth;
        int inner = Math.Max(1, client - 2);

        _line1.Bounds = new Rect(1, 1, inner, 1);
        _line2.Bounds = new Rect(1, 2, inner, 1);
        _primary.Bounds = new Rect(1, 3, inner, 1);
        _secondary?.SetBounds(new Rect(1, 4, inner, 1));

        int row = ClientHeight - 1;
        int w = _cancel.PreferredWidth;
        _cancel.Bounds = new Rect(Math.Max(0, (client - w) / 2), row, w, 1);
    }
}

/// <summary>
/// A horizontal fill gauge: the filled part in <c>ProgressBar</c>, the rest in
/// <c>ProgressBarEmpty</c>, with an optional percentage written across the middle.
/// </summary>
public sealed class ProgressBarControl : DialogControl
{
    private double _value;

    /// <summary>The fill, 0..1. Assigning a value outside the range clamps it.</summary>
    public double Value
    {
        get => _value;
        set => _value = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
    }

    /// <summary>When set, the fill percentage is written across the middle of the bar.</summary>
    public bool ShowPercent { get; set; } = true;

    /// <summary>Progress bars are never focusable.</summary>
    public override bool CanFocus => false;

    /// <summary>Assigns the bar rectangle. A convenience for the null-conditional call in a layout pass.</summary>
    /// <param name="bounds">The new bounds, relative to the dialog client area.</param>
    public void SetBounds(Rect bounds) => Bounds = bounds;

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

        int filled = (int)Math.Round(_value * r.Width, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, r.Width);

        for (int i = 0; i < r.Width; i++)
        {
            bool on = i < filled;
            buffer.Set(
                r.X + i,
                r.Y,
                on ? BoxChars.ScrollBarThumb : BoxChars.ScrollBarTrack,
                on ? palette.ProgressBar : palette.ProgressBarEmpty);
        }

        if (!ShowPercent || r.Width < 5)
        {
            return;
        }

        string text = $"{(int)Math.Round(_value * 100, MidpointRounding.AwayFromZero)}%";
        int x = r.X + ((r.Width - text.Length) / 2);
        for (int i = 0; i < text.Length; i++)
        {
            int cx = x + i;
            var style = cx - r.X < filled ? palette.ProgressBar : palette.ProgressBarEmpty;
            buffer.Set(cx, r.Y, text[i], style);
        }
    }
}
