using OpenCommander.Core;
using OpenCommander.Input;
using OpenCommander.Rendering;
using OpenCommander.Theming;
using OpenCommander.Ui;
using OpenCommander.Ui.Controls;

namespace OpenCommander.Tests;

/// <summary>Shared scaffolding: a fixed-size screen, the Far palette and synthetic input.</summary>
internal static class Fx
{
    public const int Width = 80;
    public const int Height = 25;

    public static Theme Palette() => Theme.FarDefault();

    /// <summary>Lays a component out on a fixed screen, draws it, and returns the rows as text.</summary>
    public static string[] Render(IScreenComponent component, int width = Width, int height = Height)
    {
        var buffer = new ScreenBuffer(width, height);
        component.Layout(new Rect(0, 0, width, height));
        component.Draw(buffer);
        return buffer.RenderPlainText().Split('\n');
    }

    /// <summary>Lays a component out and draws it, handing back the buffer for colour assertions.</summary>
    public static ScreenBuffer Paint(IScreenComponent component, int width = Width, int height = Height)
    {
        var buffer = new ScreenBuffer(width, height);
        component.Layout(new Rect(0, 0, width, height));
        component.Draw(buffer);
        return buffer;
    }

    public static KeyEvent Key(ConsoleKey key, KeyMods mods = KeyMods.None) => new(key, '\0', mods);

    public static KeyEvent Char(char c, KeyMods mods = KeyMods.None) => new(CharToKey(c), c, mods);

    public static InputEvent Input(ConsoleKey key, KeyMods mods = KeyMods.None) =>
        InputEvent.FromKey(Key(key, mods));

    public static MouseEvent Click(int x, int y) =>
        new(MouseKind.Down, x, y, MouseButton.Left, 0, KeyMods.None);

    public static void Type(Dialog dialog, string text)
    {
        foreach (char c in text)
        {
            dialog.HandleKey(Char(c));
        }
    }

    /// <summary>The index of the first row containing <paramref name="needle"/>, or -1.</summary>
    public static int RowWith(string[] rows, string needle)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Contains(needle, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static ConsoleKey CharToKey(char c)
    {
        char u = char.ToUpperInvariant(c);
        if (u is >= 'A' and <= 'Z')
        {
            return ConsoleKey.A + (u - 'A');
        }

        if (u is >= '0' and <= '9')
        {
            return ConsoleKey.D0 + (u - '0');
        }

        return c == ' ' ? ConsoleKey.Spacebar : ConsoleKey.None;
    }
}

public class MessageDialogTests
{
    private static MessageDialog Confirm() =>
        new(Fx.Palette(), "Warning", ["Delete file?"], MessageButtons.Yes | MessageButtons.No);

    [Fact]
    public void DrawsADoubleFrameWithACentredTitle()
    {
        var dialog = Confirm();
        var rows = Fx.Render(dialog);

        var b = dialog.Bounds;
        string top = rows[b.Y];

        Assert.Equal('╔', top[b.X]);
        Assert.Equal('╗', top[b.Right - 1]);
        Assert.Contains(" Warning ", top);

        // The title block is centred inside the frame to within the odd-cell rounding.
        int start = top.IndexOf(" Warning ", StringComparison.Ordinal);
        int titleCentre = start + (" Warning ".Length / 2);
        int frameCentre = b.X + (b.Width / 2);
        Assert.InRange(titleCentre, frameCentre - 1, frameCentre + 1);

        string bottom = rows[b.Bottom - 1];
        Assert.Equal('╚', bottom[b.X]);
        Assert.Equal('╝', bottom[b.Right - 1]);
    }

    [Fact]
    public void DrawsTheBodyAndABracketedButtonRow()
    {
        var rows = Fx.Render(Confirm());

        Assert.True(Fx.RowWith(rows, "Delete file?") >= 0);

        int buttonRow = Fx.RowWith(rows, "[ Yes ]");
        Assert.True(buttonRow >= 0);
        Assert.Contains("[ No ]", rows[buttonRow]);
        Assert.True(
            rows[buttonRow].IndexOf("[ Yes ]", StringComparison.Ordinal)
            < rows[buttonRow].IndexOf("[ No ]", StringComparison.Ordinal));
    }

    [Fact]
    public void SizesItselfToTheContent()
    {
        var wide = new MessageDialog(
            Fx.Palette(),
            "T",
            ["a really quite long message line that forces the box open"],
            MessageButtons.Ok);

        Assert.Equal(57 + 8, wide.Width);
        Assert.Equal(6, wide.Height);

        var tall = new MessageDialog(Fx.Palette(), "T", ["one", "two", "three"], MessageButtons.Ok);
        Assert.Equal(8, tall.Height);
    }

    [Fact]
    public void ClampsToASmallScreen()
    {
        var dialog = new MessageDialog(
            Fx.Palette(),
            "Title",
            ["a line that is far wider than the console it has to fit into"],
            MessageButtons.Ok);

        Fx.Render(dialog, 30, 8);

        Assert.True(dialog.Bounds.Width <= 30);
        Assert.True(dialog.Bounds.Height <= 8);
        Assert.True(dialog.Bounds.X >= 0);
        Assert.True(dialog.Bounds.Y >= 0);
    }

    [Fact]
    public void TheFirstButtonStartsFocusedAndEnterPressesIt()
    {
        var dialog = Confirm();
        Assert.Same(dialog.Buttons[0], dialog.Focused);

        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.True(dialog.IsClosed);
        Assert.Equal(DialogResult.Yes, dialog.Result);
    }

    [Fact]
    public void TabMovesTheFocusAndWrapsAround()
    {
        var dialog = Confirm();

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));
        Assert.Same(dialog.Buttons[1], dialog.Focused);

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));
        Assert.Same(dialog.Buttons[0], dialog.Focused);

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab, KeyMods.Shift));
        Assert.Same(dialog.Buttons[1], dialog.Focused);
    }

    [Fact]
    public void AltHotkeyPressesTheButtonWithoutFocusingItFirst()
    {
        var dialog = Confirm();

        dialog.HandleKey(Fx.Char('n', KeyMods.Alt));

        Assert.True(dialog.IsClosed);
        Assert.Equal(DialogResult.No, dialog.Result);
    }

    [Fact]
    public void ABareLetterAlsoPressesTheMatchingButton()
    {
        var dialog = Confirm();

        dialog.HandleKey(Fx.Char('n'));

        Assert.Equal(DialogResult.No, dialog.Result);
    }

    [Fact]
    public void AltLetterIsRecoveredFromTheVirtualKeyWhenNoCharacterArrives()
    {
        var dialog = Confirm();

        // Windows reports Alt+N with UnicodeChar == 0.
        dialog.HandleKey(new KeyEvent(ConsoleKey.N, '\0', KeyMods.Alt));

        Assert.Equal(DialogResult.No, dialog.Result);
    }

    [Fact]
    public void EscapeAlwaysAnswersCancel()
    {
        var dialog = Confirm();

        dialog.HandleKey(Fx.Key(ConsoleKey.Escape));

        Assert.True(dialog.IsClosed);
        Assert.Equal(DialogResult.Cancel, dialog.Result);
    }

    [Fact]
    public void HandleInputReportsClosureToTheModalLoop()
    {
        var dialog = Confirm();

        Assert.True(dialog.HandleInput(Fx.Input(ConsoleKey.Tab)));
        Assert.False(dialog.HandleInput(Fx.Input(ConsoleKey.Escape)));
    }

    [Fact]
    public void AnEmptyButtonSetStillOffersOk()
    {
        var dialog = new MessageDialog(Fx.Palette(), "T", ["x"], 0);

        Assert.Single(dialog.Buttons);
        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));
        Assert.Equal(DialogResult.Ok, dialog.Result);
    }

    [Fact]
    public void AmpersandsInTheBodyAreDrawnLiterally()
    {
        var dialog = new MessageDialog(Fx.Palette(), "T", ["a & b"], MessageButtons.Ok);
        var rows = Fx.Render(dialog);

        Assert.True(Fx.RowWith(rows, "a & b") >= 0);
    }

    [Fact]
    public void WarningDialogsUseTheRedPalette()
    {
        var theme = Fx.Palette();
        var dialog = new MessageDialog(theme, "Error", ["boom"], MessageButtons.Ok, warning: true);
        var buffer = Fx.Paint(dialog);

        Assert.Equal(theme.WarnDialogBox, buffer.Get(dialog.Bounds.X, dialog.Bounds.Y).Style);
    }

    [Fact]
    public void TheFocusedButtonUsesTheSelectedButtonColours()
    {
        var theme = Fx.Palette();
        var dialog = Confirm();
        var buffer = Fx.Paint(dialog);

        var yes = dialog.Buttons[0].ScreenBounds(dialog.ClientBounds);
        var no = dialog.Buttons[1].ScreenBounds(dialog.ClientBounds);

        Assert.Equal(theme.DialogButtonSelected, buffer.Get(yes.X, yes.Y).Style);
        Assert.Equal(theme.DialogButton, buffer.Get(no.X, no.Y).Style);
    }
}

public class DialogFocusTests
{
    private static Dialog Build(out ButtonControl ok, out EditControl edit, out CheckBoxControl check)
    {
        var dialog = new Dialog(Fx.Palette(), "Test", 40, 10);
        dialog.Add(new LabelControl("&Name") { Bounds = new Rect(1, 1, 10, 1) });
        edit = dialog.Add(new EditControl("abc") { Bounds = new Rect(1, 2, 20, 1) });
        check = dialog.Add(new CheckBoxControl("&Hidden") { Bounds = new Rect(1, 3, 20, 1) });
        ok = dialog.Add(new ButtonControl("&Ok", DialogResult.Ok) { Bounds = new Rect(1, 5, 6, 1) });
        return dialog;
    }

    [Fact]
    public void TheFirstFocusableControlTakesTheFocus()
    {
        var dialog = Build(out _, out var edit, out _);
        Assert.Same(edit, dialog.Focused);
    }

    [Fact]
    public void LabelsAreSkippedByTabAndTheOrderWraps()
    {
        var dialog = Build(out var ok, out var edit, out var check);

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));
        Assert.Same(check, dialog.Focused);

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));
        Assert.Same(ok, dialog.Focused);

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));
        Assert.Same(edit, dialog.Focused);
    }

    [Fact]
    public void ALabelHotkeyFocusesItsLinkedControl()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 40, 8);
        var edit = dialog.Add(new EditControl { Bounds = new Rect(1, 2, 20, 1) });
        var label = dialog.Add(new LabelControl("&Mask") { Bounds = new Rect(1, 1, 10, 1) });
        label.LinkedControl = edit;
        var ok = dialog.Add(new ButtonControl("&Ok", DialogResult.Ok) { Bounds = new Rect(1, 4, 6, 1) });

        dialog.SetFocus(ok);
        dialog.HandleKey(Fx.Char('m', KeyMods.Alt));

        Assert.Same(edit, dialog.Focused);
    }

    [Fact]
    public void AnEditFieldSwallowsPlainLettersSoBareHotkeysDoNotFire()
    {
        var dialog = Build(out _, out var edit, out _);

        dialog.HandleKey(Fx.Char('o')); // would otherwise press [ Ok ]

        Assert.False(dialog.IsClosed);
        Assert.Equal("abco", edit.Text);
    }

    [Fact]
    public void DisabledControlsCannotTakeTheFocus()
    {
        var dialog = Build(out var ok, out var edit, out var check);
        check.Enabled = false;

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));

        Assert.Same(ok, dialog.Focused);
        Assert.Same(edit, dialog.Controls[1]);
    }

    [Fact]
    public void AClickFocusesTheControlUnderThePointerAndPressesIt()
    {
        var dialog = Build(out var ok, out _, out _);
        Fx.Render(dialog);

        var r = ok.ScreenBounds(dialog.ClientBounds);
        dialog.HandleMouse(Fx.Click(r.X, r.Y));

        Assert.Equal(DialogResult.Ok, dialog.Result);
    }

    [Fact]
    public void TheCursorFollowsTheFocusedEditField()
    {
        var dialog = Build(out _, out var edit, out _);
        Fx.Render(dialog);

        Assert.True(dialog.WantsCursor);
        Assert.Equal(dialog.ClientBounds.X + edit.Bounds.X + 3, dialog.CursorX);
        Assert.Equal(dialog.ClientBounds.Y + edit.Bounds.Y, dialog.CursorY);
    }

    [Fact]
    public void ASeparatorDrawsIntoTheFrameWithTeeCharacters()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 8);
        dialog.Add(new SeparatorControl("More") { Bounds = new Rect(0, 3, 1, 1) });

        var rows = Fx.Render(dialog);
        string row = rows[dialog.Bounds.Y + 4];

        Assert.Equal('╠', row[dialog.Bounds.X]);
        Assert.Equal('╣', row[dialog.Bounds.Right - 1]);
        Assert.Contains(" More ", row);
    }

    [Fact]
    public void TheShadowRecoloursWithoutTouchingGlyphs()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 20, 6);
        var buffer = Fx.Paint(dialog);
        var b = dialog.Bounds;

        var shadow = buffer.Get(b.Right, b.Y + 1);
        Assert.Equal(' ', shadow.Ch);
        Assert.Equal(ConsoleColor.Black, shadow.Style.Bg);
    }

    [Fact]
    public void AKeyBarIsSuppressedWhileADialogIsOnTop()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 20, 6);
        var bar = dialog.KeyBarFor(KeyMods.None);

        Assert.NotNull(bar);
        Assert.All(bar!.Labels, label => Assert.Equal(string.Empty, label));
    }
}

public class EditControlTests
{
    private static EditControl Field(out Dialog dialog, string text = "", int width = 20)
    {
        dialog = new Dialog(Fx.Palette(), "T", width + 4, 8) { BareHotkeys = false };
        return dialog.Add(new EditControl(text)
        {
            Bounds = new Rect(1, 1, width, 1),
            Clipboard = new MemoryClipboard(),
        });
    }

    [Fact]
    public void TypingInsertsAtTheCaret()
    {
        var edit = Field(out var dialog);

        Fx.Type(dialog, "hello");
        Assert.Equal("hello", edit.Text);
        Assert.Equal(5, edit.Caret);

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        Fx.Type(dialog, "say ");
        Assert.Equal("say hello", edit.Text);
    }

    [Fact]
    public void HomeAndEndMoveToTheEnds()
    {
        var edit = Field(out var dialog, "abcdef");

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        Assert.Equal(0, edit.Caret);

        dialog.HandleKey(Fx.Key(ConsoleKey.End));
        Assert.Equal(6, edit.Caret);
    }

    [Fact]
    public void CtrlArrowsMoveByWords()
    {
        var edit = Field(out var dialog, "one two three");

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Ctrl));
        Assert.Equal(4, edit.Caret);

        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Ctrl));
        Assert.Equal(8, edit.Caret);

        dialog.HandleKey(Fx.Key(ConsoleKey.LeftArrow, KeyMods.Ctrl));
        Assert.Equal(4, edit.Caret);
    }

    [Fact]
    public void BackspaceAndDeleteRemoveOneCharacter()
    {
        var edit = Field(out var dialog, "abcd");

        dialog.HandleKey(Fx.Key(ConsoleKey.Backspace));
        Assert.Equal("abc", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        dialog.HandleKey(Fx.Key(ConsoleKey.Delete));
        Assert.Equal("bc", edit.Text);
    }

    [Fact]
    public void CtrlBackspaceDeletesTheWholeWord()
    {
        var edit = Field(out var dialog, "one two");

        dialog.HandleKey(Fx.Key(ConsoleKey.Backspace, KeyMods.Ctrl));

        Assert.Equal("one ", edit.Text);
    }

    [Fact]
    public void InsertTogglesOverwriteMode()
    {
        var edit = Field(out var dialog, "abcd");
        dialog.HandleKey(Fx.Key(ConsoleKey.Home));

        Assert.True(edit.InsertMode);
        dialog.HandleKey(Fx.Key(ConsoleKey.Insert));
        Assert.False(edit.InsertMode);

        Fx.Type(dialog, "XY");
        Assert.Equal("XYcd", edit.Text);
    }

    [Fact]
    public void CtrlYClearsTheLine()
    {
        var edit = Field(out var dialog, "throw me away");

        dialog.HandleKey(Fx.Key(ConsoleKey.Y, KeyMods.Ctrl));

        Assert.Equal(string.Empty, edit.Text);
        Assert.Equal(0, edit.Caret);
    }

    [Fact]
    public void MaxLengthIsHonoured()
    {
        var edit = Field(out var dialog);
        edit.MaxLength = 3;

        Fx.Type(dialog, "abcdef");

        Assert.Equal("abc", edit.Text);
    }

    [Fact]
    public void ShiftArrowsSelectAndTypingReplacesTheSelection()
    {
        var edit = Field(out var dialog, "abcdef");
        dialog.HandleKey(Fx.Key(ConsoleKey.Home));

        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Shift));
        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Shift));
        Assert.Equal(2, edit.SelectionLength);
        Assert.Equal("ab", edit.SelectedText);

        Fx.Type(dialog, "Z");
        Assert.Equal("Zcdef", edit.Text);
    }

    [Fact]
    public void ShiftEndSelectsToTheEndAndAnUnshiftedMoveDropsIt()
    {
        var edit = Field(out var dialog, "abcdef");
        dialog.HandleKey(Fx.Key(ConsoleKey.Home));

        dialog.HandleKey(Fx.Key(ConsoleKey.End, KeyMods.Shift));
        Assert.Equal(6, edit.SelectionLength);

        dialog.HandleKey(Fx.Key(ConsoleKey.LeftArrow));
        Assert.Equal(0, edit.SelectionLength);
    }

    [Fact]
    public void CopyCutAndPasteGoThroughTheInjectedClipboard()
    {
        var clipboard = new MemoryClipboard();
        var edit = Field(out var dialog, "abcdef");
        edit.Clipboard = clipboard;

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Shift));
        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Shift));
        dialog.HandleKey(Fx.Key(ConsoleKey.C, KeyMods.Ctrl));
        Assert.Equal("ab", clipboard.GetText());
        Assert.Equal("abcdef", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.X, KeyMods.Ctrl));
        Assert.Equal("cdef", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.End));
        dialog.HandleKey(Fx.Key(ConsoleKey.V, KeyMods.Ctrl));
        Assert.Equal("cdefab", edit.Text);
    }

    [Fact]
    public void TheFarClipboardBindingsWorkToo()
    {
        var clipboard = new MemoryClipboard();
        clipboard.SetText("xyz");
        var edit = Field(out var dialog, "ab");
        edit.Clipboard = clipboard;

        dialog.HandleKey(Fx.Key(ConsoleKey.Insert, KeyMods.Shift));
        Assert.Equal("abxyz", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        dialog.HandleKey(Fx.Key(ConsoleKey.RightArrow, KeyMods.Shift));
        dialog.HandleKey(Fx.Key(ConsoleKey.Delete, KeyMods.Shift));
        Assert.Equal("bxyz", edit.Text);
        Assert.Equal("a", clipboard.GetText());
    }

    [Fact]
    public void PastedNewlinesAreCutAtTheFirstLineBreak()
    {
        var clipboard = new MemoryClipboard();
        clipboard.SetText("first\r\nsecond");
        var edit = Field(out var dialog);
        edit.Clipboard = clipboard;

        dialog.HandleKey(Fx.Key(ConsoleKey.V, KeyMods.Ctrl));

        Assert.Equal("first", edit.Text);
    }

    [Fact]
    public void PasswordModeMasksTheTextAndRefusesToCopyIt()
    {
        var clipboard = new MemoryClipboard();
        var edit = Field(out var dialog, "secret");
        edit.PasswordChar = '*';
        edit.Clipboard = clipboard;

        var rows = Fx.Render(dialog);
        string row = rows[dialog.ClientBounds.Y + 1];
        Assert.Contains("******", row);
        Assert.DoesNotContain("secret", row);

        edit.SelectAll();
        Assert.False(edit.Copy());
        Assert.Null(clipboard.GetText());
    }

    [Fact]
    public void TheViewportScrollsToKeepTheCaretVisible()
    {
        var edit = Field(out var dialog, string.Empty, width: 10);

        Fx.Type(dialog, "0123456789ABCDEF");
        Assert.Equal(16, edit.Caret);

        edit.EnsureCaretVisible(10);
        Assert.Equal(7, edit.ScrollOffset);

        var rows = Fx.Render(dialog);
        Assert.Contains("789ABCDEF", rows[dialog.ClientBounds.Y + 1]);
    }

    [Fact]
    public void UpAndDownWalkTheHistoryAndRestoreTheDraft()
    {
        var edit = Field(out var dialog);
        edit.History = ["oldest", "newest"];
        Fx.Type(dialog, "draft");

        dialog.HandleKey(Fx.Key(ConsoleKey.UpArrow));
        Assert.Equal("newest", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.UpArrow));
        Assert.Equal("oldest", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal("newest", edit.Text);

        dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal("draft", edit.Text);
    }

    [Fact]
    public void CtrlDownAsksTheHostToPickFromHistory()
    {
        var edit = Field(out var dialog);
        edit.History = ["a", "b"];
        edit.HistoryChooser = items => items[1];

        dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow, KeyMods.Ctrl));

        Assert.Equal("b", edit.Text);
    }

    [Fact]
    public void AReadOnlyFieldRefusesEdits()
    {
        var edit = Field(out var dialog, "fixed");
        edit.ReadOnly = true;

        Fx.Type(dialog, "x");
        dialog.HandleKey(Fx.Key(ConsoleKey.Backspace));

        Assert.Equal("fixed", edit.Text);
    }

    [Fact]
    public void EnterIsLeftForTheDialogToHandle()
    {
        var edit = Field(out var dialog, "text");
        var ok = dialog.Add(new ButtonControl("&Ok", DialogResult.Ok) { Bounds = new Rect(1, 3, 6, 1) });
        dialog.DefaultButton = ok;
        dialog.SetFocus(edit);

        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.Equal(DialogResult.Ok, dialog.Result);
    }
}

public class CheckBoxAndRadioTests
{
    [Fact]
    public void ACheckBoxDrawsItsStateAndTogglesOnSpace()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 8);
        var check = dialog.Add(new CheckBoxControl("&Recurse") { Bounds = new Rect(1, 1, 20, 1) });

        var rows = Fx.Render(dialog);
        Assert.Contains("[ ] Recurse", rows[dialog.ClientBounds.Y + 1]);

        dialog.HandleKey(Fx.Key(ConsoleKey.Spacebar));
        Assert.True(check.Checked);

        rows = Fx.Render(dialog);
        Assert.Contains("[x] Recurse", rows[dialog.ClientBounds.Y + 1]);
    }

    [Fact]
    public void ACheckBoxRespondsToItsHotkey()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 8);
        var check = dialog.Add(new CheckBoxControl("&Recurse") { Bounds = new Rect(1, 1, 20, 1) });
        var ok = dialog.Add(new ButtonControl("&Ok", DialogResult.Ok) { Bounds = new Rect(1, 3, 6, 1) });
        dialog.SetFocus(ok);

        dialog.HandleKey(Fx.Char('r', KeyMods.Alt));

        Assert.True(check.Checked);
        Assert.Same(check, dialog.Focused);
    }

    [Fact]
    public void ARadioGroupIsOneFocusStopAndArrowsMoveInsideIt()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 10);
        var group = dialog.Add(new RadioGroupControl(["&Ascii", "&Binary", "&Auto"])
        {
            Bounds = new Rect(1, 1, 20, 3),
        });
        var ok = dialog.Add(new ButtonControl("&Ok", DialogResult.Ok) { Bounds = new Rect(1, 5, 6, 1) });

        Assert.Same(group, dialog.Focused);
        Assert.Equal(0, group.SelectedIndex);

        dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal(1, group.SelectedIndex);

        dialog.HandleKey(Fx.Key(ConsoleKey.UpArrow));
        dialog.HandleKey(Fx.Key(ConsoleKey.UpArrow));
        Assert.Equal(2, group.SelectedIndex); // wraps past the top

        dialog.HandleKey(Fx.Key(ConsoleKey.Tab));
        Assert.Same(ok, dialog.Focused);
    }

    [Fact]
    public void ARadioGroupDrawsAMarkerOnTheSelectedRow()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 10);
        dialog.Add(new RadioGroupControl(["&Ascii", "&Binary"], 1) { Bounds = new Rect(1, 1, 20, 2) });

        var rows = Fx.Render(dialog);

        Assert.Contains("( ) Ascii", rows[dialog.ClientBounds.Y + 1]);
        Assert.Contains("(•) Binary", rows[dialog.ClientBounds.Y + 2]);
    }
}

public class ListControlTests
{
    private static ListControl Build(out Dialog dialog, int count, int height = 5)
    {
        var items = new List<string>();
        for (int i = 0; i < count; i++)
        {
            items.Add($"item{i:00}");
        }

        dialog = new Dialog(Fx.Palette(), "T", 30, height + 4) { BareHotkeys = false };
        return dialog.Add(new ListControl(items) { Bounds = new Rect(1, 1, 20, height) });
    }

    [Fact]
    public void ArrowsMoveTheCursorAndTheViewportFollows()
    {
        var list = Build(out var dialog, 30);
        Fx.Render(dialog);

        for (int i = 0; i < 7; i++)
        {
            dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        }

        Assert.Equal(7, list.SelectedIndex);
        Assert.Equal(3, list.TopIndex);
    }

    [Fact]
    public void EndAndHomeJumpToTheEnds()
    {
        var list = Build(out var dialog, 30);
        Fx.Render(dialog);

        dialog.HandleKey(Fx.Key(ConsoleKey.End));
        Assert.Equal(29, list.SelectedIndex);
        Assert.Equal(25, list.TopIndex);

        dialog.HandleKey(Fx.Key(ConsoleKey.Home));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, list.TopIndex);
    }

    [Fact]
    public void AScrollBarAppearsOnlyWhenTheRowsDoNotFit()
    {
        var big = Build(out var dialogBig, 30);
        var rowsBig = Fx.Render(dialogBig);
        var r = big.ScreenBounds(dialogBig.ClientBounds);
        Assert.Equal('█', rowsBig[r.Y][r.Right - 1]);
        Assert.Equal('░', rowsBig[r.Bottom - 1][r.Right - 1]);

        var small = Build(out var dialogSmall, 3);
        var rowsSmall = Fx.Render(dialogSmall);
        var rs = small.ScreenBounds(dialogSmall.ClientBounds);
        Assert.DoesNotContain("░", rowsSmall[rs.Y]);
    }

    [Fact]
    public void TypeSearchJumpsToTheFirstMatchingRow()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 10) { BareHotkeys = false };
        var list = dialog.Add(new ListControl(["alpha", "beta", "gamma", "delta"])
        {
            Bounds = new Rect(1, 1, 20, 4),
        });

        dialog.HandleKey(Fx.Char('g'));
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("g", list.SearchPrefix);

        dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal(string.Empty, list.SearchPrefix);

        dialog.HandleKey(Fx.Char('d'));
        dialog.HandleKey(Fx.Char('e'));
        Assert.Equal(3, list.SelectedIndex);
        Assert.Equal("de", list.SearchPrefix);
    }

    [Fact]
    public void ASearchCharacterThatWouldMatchNothingIsIgnored()
    {
        var dialog = new Dialog(Fx.Palette(), "T", 30, 10) { BareHotkeys = false };
        var list = dialog.Add(new ListControl(["alpha", "beta"]) { Bounds = new Rect(1, 1, 20, 2) });

        dialog.HandleKey(Fx.Char('b'));
        dialog.HandleKey(Fx.Char('z'));

        Assert.Equal("b", list.SearchPrefix);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void EnterRaisesItemActivated()
    {
        int activated = -1;
        var dialog = new Dialog(Fx.Palette(), "T", 30, 10) { BareHotkeys = false };
        var list = dialog.Add(new ListControl(["a", "b", "c"], 1) { Bounds = new Rect(1, 1, 20, 3) });
        list.ItemActivated = i => activated = i;

        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.Equal(1, activated);
    }

    [Fact]
    public void TheWheelScrollsWithoutLosingTheCursor()
    {
        var list = Build(out var dialog, 30);
        Fx.Render(dialog);

        dialog.HandleMouse(new MouseEvent(MouseKind.Wheel, 5, 5, MouseButton.None, -3, KeyMods.None));

        Assert.Equal(3, list.TopIndex);
        Assert.InRange(list.SelectedIndex, list.TopIndex, list.TopIndex + 4);
    }
}

public class InputDialogTests
{
    [Fact]
    public void ShowsThePromptAndTheInitialText()
    {
        var dialog = new InputDialog(Fx.Palette(), "Make folder", "Create the folder", "newdir");
        var rows = Fx.Render(dialog);

        Assert.True(Fx.RowWith(rows, "Create the folder") >= 0);
        Assert.True(Fx.RowWith(rows, "newdir") >= 0);

        int buttonRow = Fx.RowWith(rows, "[ Ok ]");
        Assert.True(buttonRow >= 0);
        Assert.Contains("[ Cancel ]", rows[buttonRow]);
        Assert.Contains(" Make folder ", rows[dialog.Bounds.Y]);
    }

    [Fact]
    public void TheEditFieldStartsFocusedAndEnterAccepts()
    {
        var dialog = new InputDialog(Fx.Palette(), "T", "Name", "abc");
        Assert.Same(dialog.Edit, dialog.Focused);

        Fx.Type(dialog, "d");
        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.Equal(DialogResult.Ok, dialog.Result);
        Assert.Equal("abcd", dialog.AcceptedText);
    }

    [Fact]
    public void EscapeCancelsAndYieldsNoText()
    {
        var dialog = new InputDialog(Fx.Palette(), "T", "Name", "abc");

        dialog.HandleKey(Fx.Key(ConsoleKey.Escape));

        Assert.Equal(DialogResult.Cancel, dialog.Result);
        Assert.Null(dialog.AcceptedText);
        Assert.Equal("abc", dialog.Text);
    }
}

public class ListDialogTests
{
    [Fact]
    public void EnterAcceptsTheRowUnderTheCursor()
    {
        var dialog = new ListDialog(Fx.Palette(), "Drives", ["C:", "D:", "E:"], 1);
        Fx.Render(dialog);

        dialog.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.True(dialog.IsClosed);
        Assert.Equal(DialogResult.Ok, dialog.Result);
        Assert.Equal(2, dialog.AcceptedIndex);
        Assert.Equal("E:", dialog.AcceptedItem);
    }

    [Fact]
    public void EscapeCancelsAndReportsMinusOne()
    {
        var dialog = new ListDialog(Fx.Palette(), "Drives", ["C:", "D:"]);

        dialog.HandleKey(Fx.Key(ConsoleKey.Escape));

        Assert.Equal(-1, dialog.AcceptedIndex);
        Assert.Null(dialog.AcceptedItem);
    }

    [Fact]
    public void DrawsTheRowsAndTheButtonRow()
    {
        var dialog = new ListDialog(Fx.Palette(), "History", ["dir c:", "copy a b"]);
        var rows = Fx.Render(dialog);

        Assert.True(Fx.RowWith(rows, "dir c:") >= 0);
        Assert.True(Fx.RowWith(rows, "copy a b") >= 0);
        Assert.True(Fx.RowWith(rows, "[ Ok ]") >= 0);
    }
}

public class ProgressDialogTests
{
    [Fact]
    public void UpdateDrawsTheLinesAndFillsTheBar()
    {
        var dialog = new ProgressDialog(Fx.Palette(), "Copying") { ShowPercent = false };
        dialog.Update("from.txt", "to.txt", 0.5, null);

        var rows = Fx.Render(dialog);
        Assert.True(Fx.RowWith(rows, "from.txt") >= 0);
        Assert.True(Fx.RowWith(rows, "to.txt") >= 0);

        int barRow = Fx.RowWith(rows, "█");
        Assert.True(barRow >= 0);

        int filled = rows[barRow].Count(c => c == '█');
        int empty = rows[barRow].Count(c => c == '░');
        Assert.Equal(filled, empty);
    }

    [Fact]
    public void ThePercentageIsWrittenAcrossTheBar()
    {
        var dialog = new ProgressDialog(Fx.Palette(), "Copying");
        dialog.Update("a", "b", 0.42, null);

        var rows = Fx.Render(dialog);

        Assert.True(Fx.RowWith(rows, "42%") >= 0);
    }

    [Fact]
    public void ASecondBarIsOptional()
    {
        var one = new ProgressDialog(Fx.Palette(), "T");
        Assert.False(one.HasSecondaryBar);
        Assert.Null(one.Secondary);

        var two = new ProgressDialog(Fx.Palette(), "T", showSecondary: true);
        two.Update("a", "b", 0.25, 0.75);

        Assert.True(two.HasSecondaryBar);
        Assert.Equal(0.25, two.Primary);
        Assert.Equal(0.75, two.Secondary);
    }

    [Fact]
    public void ValuesAreClamped()
    {
        var dialog = new ProgressDialog(Fx.Palette(), "T");

        dialog.Update("a", "b", 5, null);
        Assert.Equal(1.0, dialog.Primary);

        dialog.Update("a", "b", -2, null);
        Assert.Equal(0.0, dialog.Primary);
    }

    [Fact]
    public void EscapeRequestsCancellationButLeavesTheDialogUp()
    {
        var dialog = new ProgressDialog(Fx.Palette(), "Copying");

        Assert.True(dialog.HandleInput(Fx.Input(ConsoleKey.Escape)));

        Assert.True(dialog.CancelRequested);
        Assert.False(dialog.IsClosed);

        dialog.Complete();
        Assert.True(dialog.IsClosed);
        Assert.Equal(DialogResult.Cancel, dialog.Result);
    }

    [Fact]
    public void TheCancelButtonRequestsCancellationToo()
    {
        var dialog = new ProgressDialog(Fx.Palette(), "Copying");

        dialog.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.True(dialog.CancelRequested);
        Assert.False(dialog.IsClosed);
    }
}

public class PopupMenuTests
{
    private static IReadOnlyList<MenuItem> SortMenu() =>
    [
        new MenuItem("&Name", "Ctrl+F3"),
        new MenuItem("&Extension", "Ctrl+F4"),
        MenuItem.Separator(),
        new MenuItem("&Size", "Ctrl+F6") { Checked = true },
        new MenuItem("&Unsorted", "Ctrl+F7") { Enabled = false },
        new MenuItem("&Owner", "Ctrl+F11"),
    ];

    [Fact]
    public void DrawsASingleLineFrameWithACentredTitle()
    {
        var menu = new PopupMenu(Fx.Palette(), "Sort by", SortMenu());
        var rows = Fx.Render(menu);
        var b = menu.Bounds;

        Assert.Equal('┌', rows[b.Y][b.X]);
        Assert.Equal('┐', rows[b.Y][b.Right - 1]);
        Assert.Equal('└', rows[b.Bottom - 1][b.X]);
        Assert.Equal('┘', rows[b.Bottom - 1][b.Right - 1]);
        Assert.Contains(" Sort by ", rows[b.Y]);
    }

    [Fact]
    public void DrawsItemsWithRightAlignedAcceleratorsAndCheckMarks()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        var rows = Fx.Render(menu);

        int row = Fx.RowWith(rows, "Name");
        Assert.True(row >= 0);
        Assert.Contains("Ctrl+F3", rows[row]);
        Assert.True(
            rows[row].IndexOf("Name", StringComparison.Ordinal)
            < rows[row].IndexOf("Ctrl+F3", StringComparison.Ordinal));

        Assert.Contains("√", rows[Fx.RowWith(rows, "Size")]);
    }

    [Fact]
    public void DrawsSeparatorsAsARuleWithTees()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        var rows = Fx.Render(menu);
        var b = menu.Bounds;

        string separator = rows[b.Y + 3];
        Assert.Equal('├', separator[b.X]);
        Assert.Equal('┤', separator[b.Right - 1]);
        Assert.Contains("──", separator);
    }

    [Fact]
    public void ArrowsSkipSeparatorsAndDisabledItemsAndWrapAround()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        Fx.Render(menu);

        menu.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal(1, menu.SelectedIndex);

        menu.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal(3, menu.SelectedIndex); // index 2 is a separator

        menu.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal(5, menu.SelectedIndex); // index 4 is disabled

        menu.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        Assert.Equal(0, menu.SelectedIndex); // wraps

        menu.HandleKey(Fx.Key(ConsoleKey.UpArrow));
        Assert.Equal(5, menu.SelectedIndex);
    }

    [Fact]
    public void EnterChooses()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu(), 1);
        Fx.Render(menu);

        Assert.True(menu.HandleInput(InputEvent.FromKey(Fx.Key(ConsoleKey.DownArrow))));
        Assert.False(menu.HandleInput(InputEvent.FromKey(Fx.Key(ConsoleKey.Enter))));

        Assert.True(menu.IsClosed);
        Assert.Equal(3, menu.Result);
        Assert.Equal("&Size", menu.ChosenItem?.Text);
    }

    [Fact]
    public void EscapeCancels()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        Fx.Render(menu);

        menu.HandleKey(Fx.Key(ConsoleKey.Escape));

        Assert.True(menu.Cancelled);
        Assert.Equal(-1, menu.Result);
        Assert.Null(menu.ChosenItem);
    }

    [Fact]
    public void AHotkeyChoosesTheItemOutright()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        Fx.Render(menu);

        menu.HandleKey(Fx.Char('o'));

        Assert.True(menu.IsClosed);
        Assert.Equal(5, menu.Result);
    }

    [Fact]
    public void ADisabledItemsHotkeyDoesNothing()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        Fx.Render(menu);

        menu.HandleKey(Fx.Char('u')); // "&Unsorted" is disabled

        Assert.False(menu.IsClosed);
    }

    [Fact]
    public void TypeSearchMovesTheCursorWithoutChoosing()
    {
        var items = new List<MenuItem> { new("Alpha"), new("Beta"), new("Gamma") };
        var menu = new PopupMenu(Fx.Palette(), null, items);
        Fx.Render(menu);

        menu.HandleKey(Fx.Char('g'));

        Assert.False(menu.IsClosed);
        Assert.Equal(2, menu.SelectedIndex);
        Assert.Equal("g", menu.SearchPrefix);
    }

    [Fact]
    public void ActionsOnlyRunWhenTheMenuIsAskedToRunThem()
    {
        int ran = 0;
        var items = new List<MenuItem> { new("&Go", null, () => ran++) };

        var quiet = new PopupMenu(Fx.Palette(), null, items);
        Fx.Render(quiet);
        quiet.HandleKey(Fx.Key(ConsoleKey.Enter));
        Assert.Equal(0, ran);

        var loud = new PopupMenu(Fx.Palette(), null, items) { InvokeActions = true };
        Fx.Render(loud);
        loud.HandleKey(Fx.Key(ConsoleKey.Enter));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void AnExplicitAnchorIsHonouredAndClampedToTheScreen()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu(), 0, new Rect(4, 2, 0, 0));
        Fx.Render(menu);
        Assert.Equal(4, menu.Bounds.X);
        Assert.Equal(2, menu.Bounds.Y);

        var edge = new PopupMenu(Fx.Palette(), null, SortMenu(), 0, new Rect(78, 24, 0, 0));
        Fx.Render(edge);
        Assert.True(edge.Bounds.Right <= Fx.Width);
        Assert.True(edge.Bounds.Bottom <= Fx.Height);
    }

    [Fact]
    public void ALongMenuScrollsAndShowsAScrollBar()
    {
        var items = new List<MenuItem>();
        for (int i = 0; i < 40; i++)
        {
            items.Add(new MenuItem($"entry {i:00}"));
        }

        var menu = new PopupMenu(Fx.Palette(), null, items);
        var rows = Fx.Render(menu, 40, 12);

        Assert.Equal(12, menu.Bounds.Height);
        Assert.Equal('█', rows[menu.Bounds.Y + 1][menu.Bounds.Right - 1]);
        Assert.Equal('░', rows[menu.Bounds.Bottom - 2][menu.Bounds.Right - 1]);

        menu.HandleKey(Fx.Key(ConsoleKey.End));
        Assert.Equal(39, menu.SelectedIndex);
        Assert.Equal(30, menu.TopIndex);
    }

    [Fact]
    public void AClickOutsideDismissesTheMenu()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        Fx.Render(menu);

        menu.HandleMouse(Fx.Click(0, 0));

        Assert.True(menu.Cancelled);
    }

    [Fact]
    public void AClickOnARowChoosesIt()
    {
        var menu = new PopupMenu(Fx.Palette(), null, SortMenu());
        Fx.Render(menu);
        var b = menu.Bounds;

        menu.HandleMouse(Fx.Click(b.X + 3, b.Y + 2));

        Assert.Equal(1, menu.Result);
    }
}

public class MenuBarTests
{
    private static IReadOnlyList<MenuItem> Bar() =>
    [
        new MenuItem("&Left")
        {
            SubItems = [new MenuItem("&Brief", "Ctrl+1"), new MenuItem("&Medium", "Ctrl+2")],
        },
        new MenuItem("&Files")
        {
            SubItems = [new MenuItem("&View", "F3"), new MenuItem("&Edit", "F4")],
        },
        new MenuItem("&Right")
        {
            SubItems = [new MenuItem("&Re-read", "Ctrl+R")],
        },
    ];

    [Fact]
    public void DrawsEveryTitleOnTheTopRow()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        var rows = Fx.Render(bar);

        Assert.Contains("Left", rows[0]);
        Assert.Contains("Files", rows[0]);
        Assert.Contains("Right", rows[0]);
        Assert.True(
            rows[0].IndexOf("Left", StringComparison.Ordinal)
            < rows[0].IndexOf("Files", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSelectedTitleUsesTheSelectedBarColours()
    {
        var theme = Fx.Palette();
        var bar = new MenuBar(theme, Bar());
        var buffer = Fx.Paint(bar);

        Assert.Equal(theme.MenuBarSelected, buffer.Get(1, 0).Style);
        Assert.Equal(theme.MenuBarText, buffer.Get(0, 0).Style);
    }

    [Fact]
    public void LeftAndRightWalkTheTitlesAndWrap()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleKey(Fx.Key(ConsoleKey.RightArrow));
        Assert.Equal(1, bar.SelectedIndex);

        bar.HandleKey(Fx.Key(ConsoleKey.RightArrow));
        bar.HandleKey(Fx.Key(ConsoleKey.RightArrow));
        Assert.Equal(0, bar.SelectedIndex);

        bar.HandleKey(Fx.Key(ConsoleKey.LeftArrow));
        Assert.Equal(2, bar.SelectedIndex);
    }

    [Fact]
    public void DownOpensThePullDownUnderTheTitle()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleKey(Fx.Key(ConsoleKey.DownArrow));

        Assert.True(bar.IsMenuOpen);
        Assert.Equal(1, bar.OpenMenu!.Bounds.Y);

        var rows = Fx.Render(bar);
        Assert.True(Fx.RowWith(rows, "Brief") >= 0);
        Assert.True(Fx.RowWith(rows, "Ctrl+1") >= 0);
    }

    [Fact]
    public void LeftAndRightSwitchPullDownsWhileOneIsOpen()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        bar.HandleKey(Fx.Key(ConsoleKey.RightArrow));

        Assert.True(bar.IsMenuOpen);
        Assert.Equal(1, bar.SelectedIndex);
        Assert.Equal("&View", bar.OpenMenu!.Items[0].Text);
    }

    [Fact]
    public void EscapeClosesThePullDownThenTheBar()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        bar.HandleKey(Fx.Key(ConsoleKey.Escape));
        Assert.False(bar.IsMenuOpen);
        Assert.False(bar.IsClosed);

        bar.HandleKey(Fx.Key(ConsoleKey.Escape));
        Assert.True(bar.IsClosed);
        Assert.Null(bar.ChosenItem);
    }

    [Fact]
    public void ChoosingALeafClosesTheBarAndReportsTheItem()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleKey(Fx.Char('f')); // opens Files
        Assert.True(bar.IsMenuOpen);

        bar.HandleKey(Fx.Key(ConsoleKey.DownArrow));
        bar.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.True(bar.IsClosed);
        Assert.Equal("&Edit", bar.ChosenItem?.Text);
        Assert.Equal(1, bar.ChosenMenuIndex);
        Assert.Equal(1, bar.ChosenItemIndex);
    }

    [Fact]
    public void AHotkeyOpensTheMatchingPullDown()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleKey(Fx.Char('r'));

        Assert.True(bar.IsMenuOpen);
        Assert.Equal(2, bar.SelectedIndex);
    }

    [Fact]
    public void ClickingATitleOpensIt()
    {
        var bar = new MenuBar(Fx.Palette(), Bar());
        Fx.Render(bar);

        bar.HandleMouse(Fx.Click(2, 0));

        Assert.True(bar.IsMenuOpen);
        Assert.Equal(0, bar.SelectedIndex);
    }

    [Fact]
    public void ATopLevelEntryWithNoChildrenIsACommandInItsOwnRight()
    {
        var bar = new MenuBar(Fx.Palette(), [new MenuItem("&Quit")]);
        Fx.Render(bar);

        bar.HandleKey(Fx.Key(ConsoleKey.Enter));

        Assert.True(bar.IsClosed);
        Assert.Equal("&Quit", bar.ChosenItem?.Text);
    }
}

public class ClipboardTests
{
    [Fact]
    public void TheMemoryClipboardRoundTrips()
    {
        var clipboard = new MemoryClipboard();

        Assert.Null(clipboard.GetText());
        Assert.True(clipboard.SetText("hello"));
        Assert.Equal("hello", clipboard.GetText());

        clipboard.SetText(null);
        Assert.Null(clipboard.GetText());
    }

    [Fact]
    public void ThePlatformClipboardFallsBackToTheInProcessBuffer()
    {
        var clipboard = new Clipboard { UseNative = false };

        Assert.True(clipboard.SetText("open commander"));
        Assert.Equal("open commander", clipboard.GetText());
    }

    [Fact]
    public void EditControlsGetAClipboardByDefault()
    {
        var edit = new EditControl();

        Assert.NotNull(edit.Clipboard);
    }
}
