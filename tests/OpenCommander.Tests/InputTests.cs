using OpenCommander.Input;
using Xunit;

namespace OpenCommander.Tests;

/// <summary>
/// Unit tests for the input subsystem. Everything here is pure: no test touches a real console, so
/// the suite runs identically on a build agent with redirected stdio.
/// </summary>
public class InputTests
{
    // ---------------------------------------------------------------------------------------
    // KeyEvent.Is - exact modifier matching
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Is_MatchesKeyAndModifiersExactly()
    {
        var ev = new KeyEvent(ConsoleKey.F5, '\0', KeyMods.Ctrl | KeyMods.Alt);

        Assert.True(ev.Is(ConsoleKey.F5, KeyMods.Ctrl | KeyMods.Alt));
        Assert.False(ev.Is(ConsoleKey.F5));
        Assert.False(ev.Is(ConsoleKey.F5, KeyMods.Ctrl));
        Assert.False(ev.Is(ConsoleKey.F5, KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift));
        Assert.False(ev.Is(ConsoleKey.F6, KeyMods.Ctrl | KeyMods.Alt));
    }

    [Fact]
    public void Is_UnmodifiedKeyDoesNotMatchWhenAModifierIsHeld()
    {
        var shiftEnter = new KeyEvent(ConsoleKey.Enter, '\r', KeyMods.Shift);

        Assert.False(shiftEnter.Is(ConsoleKey.Enter));
        Assert.True(shiftEnter.Is(ConsoleKey.Enter, KeyMods.Shift));
    }

    [Fact]
    public void IsIgnoringShift_IgnoresOnlyTheShiftFlag()
    {
        var ev = new KeyEvent(ConsoleKey.Add, '\0', KeyMods.Ctrl | KeyMods.Shift);

        Assert.True(ev.IsIgnoringShift(ConsoleKey.Add, KeyMods.Ctrl));
        Assert.False(ev.IsIgnoringShift(ConsoleKey.Add, KeyMods.Alt));
    }

    [Theory]
    [InlineData('a', KeyMods.None, true)]
    [InlineData('A', KeyMods.Shift, true)]
    [InlineData(' ', KeyMods.None, true)]
    [InlineData('\0', KeyMods.None, false)]
    [InlineData('\r', KeyMods.None, false)]
    [InlineData('a', KeyMods.Ctrl, false)]
    [InlineData('a', KeyMods.Alt, false)]
    public void IsPlainChar_OnlyTrueForPrintableCharsWithoutCtrlOrAlt(char ch, KeyMods mods, bool expected)
    {
        Assert.Equal(expected, new KeyEvent(ConsoleKey.None, ch, mods).IsPlainChar);
    }

    [Fact]
    public void None_IsTheDefaultKeyEvent()
    {
        Assert.Equal(default(KeyEvent), KeyEvent.None);
        Assert.False(KeyEvent.None.HasMods);
    }

    // ---------------------------------------------------------------------------------------
    // KeyEvent.ToDisplayString
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ConsoleKey.F5, KeyMods.Ctrl | KeyMods.Alt, "Ctrl+Alt+F5")]
    [InlineData(ConsoleKey.Insert, KeyMods.Shift, "Shift+Ins")]
    [InlineData(ConsoleKey.F1, KeyMods.Alt, "Alt+F1")]
    [InlineData(ConsoleKey.Enter, KeyMods.None, "Enter")]
    [InlineData(ConsoleKey.A, KeyMods.None, "A")]
    [InlineData(ConsoleKey.Add, KeyMods.None, "Gray+")]
    [InlineData(ConsoleKey.Subtract, KeyMods.Ctrl, "Ctrl+Gray-")]
    [InlineData(ConsoleKey.Multiply, KeyMods.Alt, "Alt+Gray*")]
    [InlineData(ConsoleKey.Escape, KeyMods.None, "Esc")]
    [InlineData(ConsoleKey.PageDown, KeyMods.Ctrl, "Ctrl+PgDn")]
    [InlineData(ConsoleKey.PageUp, KeyMods.Ctrl | KeyMods.Shift, "Ctrl+Shift+PgUp")]
    [InlineData(ConsoleKey.Delete, KeyMods.Shift, "Shift+Del")]
    [InlineData(ConsoleKey.UpArrow, KeyMods.None, "Up")]
    [InlineData(ConsoleKey.Spacebar, KeyMods.None, "Space")]
    [InlineData(ConsoleKey.D3, KeyMods.Ctrl, "Ctrl+3")]
    [InlineData(ConsoleKey.NumPad5, KeyMods.None, "Num5")]
    [InlineData(ConsoleKey.Oem5, KeyMods.Ctrl, "Ctrl+\\")]
    [InlineData(ConsoleKey.F12, KeyMods.Ctrl, "Ctrl+F12")]
    public void ToDisplayString_UsesFarNotation(ConsoleKey key, KeyMods mods, string expected)
    {
        Assert.Equal(expected, new KeyEvent(key, '\0', mods).ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_EmitsModifiersInCtrlAltShiftOrder()
    {
        var ev = new KeyEvent(ConsoleKey.Enter, '\0', KeyMods.Shift | KeyMods.Alt | KeyMods.Ctrl);
        Assert.Equal("Ctrl+Alt+Shift+Enter", ev.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_FallsBackToTheCharacterWhenTheKeyIsUnknown()
    {
        Assert.Equal("Q", new KeyEvent(ConsoleKey.None, 'q', KeyMods.None).ToDisplayString());
        Assert.Equal("None", new KeyEvent(ConsoleKey.None, '\0', KeyMods.None).ToDisplayString());
    }

    // ---------------------------------------------------------------------------------------
    // KeyChord parsing
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Ctrl+Alt+F5", ConsoleKey.F5, KeyMods.Ctrl | KeyMods.Alt)]
    [InlineData("ctrl+alt+f5", ConsoleKey.F5, KeyMods.Ctrl | KeyMods.Alt)]
    [InlineData("Shift+Ins", ConsoleKey.Insert, KeyMods.Shift)]
    [InlineData("Shift+Insert", ConsoleKey.Insert, KeyMods.Shift)]
    [InlineData("Alt+F1", ConsoleKey.F1, KeyMods.Alt)]
    [InlineData("Enter", ConsoleKey.Enter, KeyMods.None)]
    [InlineData("Return", ConsoleKey.Enter, KeyMods.None)]
    [InlineData("Esc", ConsoleKey.Escape, KeyMods.None)]
    [InlineData("Tab", ConsoleKey.Tab, KeyMods.None)]
    [InlineData("Ctrl+R", ConsoleKey.R, KeyMods.Ctrl)]
    [InlineData("Ctrl+1", ConsoleKey.D1, KeyMods.Ctrl)]
    [InlineData("Ctrl+PgDn", ConsoleKey.PageDown, KeyMods.Ctrl)]
    [InlineData("Ctrl+Shift+PgDn", ConsoleKey.PageDown, KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData("Alt+Shift+Ins", ConsoleKey.Insert, KeyMods.Alt | KeyMods.Shift)]
    [InlineData("Ctrl+\\", ConsoleKey.Oem5, KeyMods.Ctrl)]
    [InlineData("Ctrl+;", ConsoleKey.Oem1, KeyMods.Ctrl)]
    [InlineData("Ctrl+[", ConsoleKey.Oem4, KeyMods.Ctrl)]
    [InlineData("Ctrl+]", ConsoleKey.Oem6, KeyMods.Ctrl)]
    [InlineData("Numpad5", ConsoleKey.NumPad5, KeyMods.None)]
    [InlineData("Num5", ConsoleKey.NumPad5, KeyMods.None)]
    [InlineData("F24", ConsoleKey.F24, KeyMods.None)]
    [InlineData("  Ctrl+U  ", ConsoleKey.U, KeyMods.Ctrl)]
    public void TryParse_ParsesChords(string text, ConsoleKey expectedKey, KeyMods expectedMods)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(expectedKey, chord.Key);
        Assert.Equal(expectedMods, chord.Mods);
    }

    [Theory]
    [InlineData("Gray+", ConsoleKey.Add, KeyMods.None)]
    [InlineData("Gray-", ConsoleKey.Subtract, KeyMods.None)]
    [InlineData("Gray*", ConsoleKey.Multiply, KeyMods.None)]
    [InlineData("Gray/", ConsoleKey.Divide, KeyMods.None)]
    [InlineData("Gray.", ConsoleKey.Decimal, KeyMods.None)]
    [InlineData("Ctrl+Gray+", ConsoleKey.Add, KeyMods.Ctrl)]
    [InlineData("Ctrl+Gray-", ConsoleKey.Subtract, KeyMods.Ctrl)]
    [InlineData("Alt+Gray*", ConsoleKey.Multiply, KeyMods.Alt)]
    [InlineData("Shift+Gray+", ConsoleKey.Add, KeyMods.Shift)]
    [InlineData("Ctrl+Shift+Gray+", ConsoleKey.Add, KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData("+", ConsoleKey.Add, KeyMods.None)]
    [InlineData("Ctrl++", ConsoleKey.Add, KeyMods.Ctrl)]
    [InlineData("gray+", ConsoleKey.Add, KeyMods.None)]
    public void TryParse_HandlesGrayKeysAndTheSeparatorAsAKey(string text, ConsoleKey expectedKey, KeyMods expectedMods)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(expectedKey, chord.Key);
        Assert.Equal(expectedMods, chord.Mods);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hyper+F1")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+")]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("13")]
    public void TryParse_RejectsGarbage(string? text)
    {
        Assert.False(KeyChord.TryParse(text, out var chord));
        Assert.Equal(KeyChord.None, chord);
    }

    [Fact]
    public void Parse_ThrowsOnGarbage()
    {
        Assert.Throws<FormatException>(() => KeyChord.Parse("Hyper+F1"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+F5")]
    [InlineData("Shift+Ins")]
    [InlineData("Alt+F1")]
    [InlineData("Enter")]
    [InlineData("Gray+")]
    [InlineData("Ctrl+Gray*")]
    [InlineData("Ctrl+Shift+PgUp")]
    [InlineData("Ctrl+\\")]
    [InlineData("Num5")]
    [InlineData("A")]
    [InlineData("Ctrl+3")]
    public void ToString_RoundTripsThroughParse(string canonical)
    {
        var chord = KeyChord.Parse(canonical);
        Assert.Equal(canonical, chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }

    [Fact]
    public void Matches_RequiresAnExactModifierMatch()
    {
        var chord = KeyChord.Parse("Ctrl+PgDn");

        Assert.True(chord.Matches(new KeyEvent(ConsoleKey.PageDown, '\0', KeyMods.Ctrl)));
        Assert.False(chord.Matches(new KeyEvent(ConsoleKey.PageDown, '\0', KeyMods.None)));
        Assert.False(chord.Matches(new KeyEvent(ConsoleKey.PageDown, '\0', KeyMods.Ctrl | KeyMods.Shift)));
        Assert.False(chord.Matches(new KeyEvent(ConsoleKey.PageUp, '\0', KeyMods.Ctrl)));
    }

    [Fact]
    public void Matches_StaticOverloadParsesAndTests()
    {
        Assert.True(KeyChord.Matches("Shift+Ins", new KeyEvent(ConsoleKey.Insert, '\0', KeyMods.Shift)));
        Assert.False(KeyChord.Matches("nonsense", new KeyEvent(ConsoleKey.Insert, '\0', KeyMods.Shift)));
    }

    [Fact]
    public void None_MatchesNothing()
    {
        Assert.True(KeyChord.None.IsNone);
        Assert.False(KeyChord.None.Matches(new KeyEvent(ConsoleKey.None, '\0', KeyMods.None)));
    }

    [Fact]
    public void ToPredicate_BehavesLikeMatches()
    {
        var predicate = KeyChord.Parse("Ctrl+U").ToPredicate();

        Assert.True(predicate(new KeyEvent(ConsoleKey.U, '\0', KeyMods.Ctrl)));
        Assert.False(predicate(new KeyEvent(ConsoleKey.U, 'u', KeyMods.None)));
    }

    [Fact]
    public void ToKeyEvent_ProducesAMatchingEvent()
    {
        var chord = KeyChord.Parse("Ctrl+Alt+F5");
        var ev = chord.ToKeyEvent();

        Assert.True(chord.Matches(ev));
        Assert.Equal("Ctrl+Alt+F5", ev.ToDisplayString());
    }

    [Fact]
    public void ParseList_SkipsUnparsableEntries()
    {
        var chords = KeyChord.ParseList("F3, Num5 ; garbage+, Ctrl+F3");

        Assert.Equal(3, chords.Count);
        Assert.Equal(new KeyChord(ConsoleKey.F3, KeyMods.None), chords[0]);
        Assert.Equal(new KeyChord(ConsoleKey.NumPad5, KeyMods.None), chords[1]);
        Assert.Equal(new KeyChord(ConsoleKey.F3, KeyMods.Ctrl), chords[2]);
    }

    [Fact]
    public void ParseList_OfNullOrEmptyIsEmpty()
    {
        Assert.Empty(KeyChord.ParseList(null));
        Assert.Empty(KeyChord.ParseList("  "));
    }

    // ---------------------------------------------------------------------------------------
    // Virtual key -> ConsoleKey mapping
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x08, ConsoleKey.Backspace)]
    [InlineData(0x09, ConsoleKey.Tab)]
    [InlineData(0x0D, ConsoleKey.Enter)]
    [InlineData(0x1B, ConsoleKey.Escape)]
    [InlineData(0x20, ConsoleKey.Spacebar)]
    [InlineData(0x21, ConsoleKey.PageUp)]
    [InlineData(0x22, ConsoleKey.PageDown)]
    [InlineData(0x23, ConsoleKey.End)]
    [InlineData(0x24, ConsoleKey.Home)]
    [InlineData(0x25, ConsoleKey.LeftArrow)]
    [InlineData(0x26, ConsoleKey.UpArrow)]
    [InlineData(0x27, ConsoleKey.RightArrow)]
    [InlineData(0x28, ConsoleKey.DownArrow)]
    [InlineData(0x2D, ConsoleKey.Insert)]
    [InlineData(0x2E, ConsoleKey.Delete)]
    [InlineData(0x30, ConsoleKey.D0)]
    [InlineData(0x39, ConsoleKey.D9)]
    [InlineData(0x41, ConsoleKey.A)]
    [InlineData(0x5A, ConsoleKey.Z)]
    [InlineData(0x60, ConsoleKey.NumPad0)]
    [InlineData(0x65, ConsoleKey.NumPad5)]
    [InlineData(0x69, ConsoleKey.NumPad9)]
    [InlineData(0x6A, ConsoleKey.Multiply)]
    [InlineData(0x6B, ConsoleKey.Add)]
    [InlineData(0x6D, ConsoleKey.Subtract)]
    [InlineData(0x6E, ConsoleKey.Decimal)]
    [InlineData(0x6F, ConsoleKey.Divide)]
    [InlineData(0x70, ConsoleKey.F1)]
    [InlineData(0x7B, ConsoleKey.F12)]
    [InlineData(0x87, ConsoleKey.F24)]
    [InlineData(0xBB, ConsoleKey.OemPlus)]
    [InlineData(0xBD, ConsoleKey.OemMinus)]
    [InlineData(0xDC, ConsoleKey.Oem5)]
    public void MapVirtualKey_MapsKnownCodes(int vk, ConsoleKey expected)
    {
        Assert.Equal(expected, WindowsConsoleInput.MapVirtualKey(vk));
    }

    [Theory]
    [InlineData(0x10)] // VK_SHIFT
    [InlineData(0x11)] // VK_CONTROL
    [InlineData(0x12)] // VK_MENU
    [InlineData(0x14)] // VK_CAPITAL
    [InlineData(0x90)] // VK_NUMLOCK
    [InlineData(0x91)] // VK_SCROLL
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0x07)]
    public void MapVirtualKey_ReturnsNoneForCodesWithNoConsoleKey(int vk)
    {
        Assert.Equal(ConsoleKey.None, WindowsConsoleInput.MapVirtualKey(vk));
    }

    [Fact]
    public void MapVirtualKey_NeverProducesAnUndefinedEnumValue()
    {
        for (var vk = 0; vk <= 0xFF; vk++)
        {
            var key = WindowsConsoleInput.MapVirtualKey(vk);
            Assert.True(Enum.IsDefined(key), $"VK 0x{vk:X2} mapped to undefined ConsoleKey {(int)key}.");
        }
    }

    [Fact]
    public void MapVirtualKey_IsAnIdentityMapOverTheOverlappingRanges()
    {
        // ConsoleKey values are deliberately the same numbers as the Win32 VK codes here.
        for (var vk = 0x30; vk <= 0x39; vk++)
        {
            Assert.Equal(vk, (int)WindowsConsoleInput.MapVirtualKey(vk));
        }

        for (var vk = 0x41; vk <= 0x5A; vk++)
        {
            Assert.Equal(vk, (int)WindowsConsoleInput.MapVirtualKey(vk));
        }

        for (var vk = 0x60; vk <= 0x69; vk++)
        {
            Assert.Equal(vk, (int)WindowsConsoleInput.MapVirtualKey(vk));
        }

        for (var vk = 0x70; vk <= 0x87; vk++)
        {
            Assert.Equal(vk, (int)WindowsConsoleInput.MapVirtualKey(vk));
        }
    }

    // ---------------------------------------------------------------------------------------
    // dwControlKeyState -> KeyMods
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x0000u, KeyMods.None)]
    [InlineData(0x0010u, KeyMods.Shift)]                        // SHIFT_PRESSED
    [InlineData(0x0008u, KeyMods.Ctrl)]                         // LEFT_CTRL_PRESSED
    [InlineData(0x0004u, KeyMods.Ctrl)]                         // RIGHT_CTRL_PRESSED
    [InlineData(0x0002u, KeyMods.Alt)]                          // LEFT_ALT_PRESSED
    [InlineData(0x0001u, KeyMods.Alt)]                          // RIGHT_ALT_PRESSED
    [InlineData(0x0009u, KeyMods.Ctrl | KeyMods.Alt)]           // LeftCtrl + RightAlt (AltGr)
    [InlineData(0x0018u, KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData(0x001Au, KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift)]
    [InlineData(0x00E0u, KeyMods.None)]                         // lock keys only: NumLock|ScrollLock|CapsLock
    [InlineData(0x0110u, KeyMods.Shift)]                        // ENHANCED_KEY must not leak in
    public void MapControlKeyState_FoldsLeftAndRightVariants(uint state, KeyMods expected)
    {
        Assert.Equal(expected, WindowsConsoleInput.MapControlKeyState(state));
    }

    [Theory]
    [InlineData(0x0000u, MouseButton.None)]
    [InlineData(0x0001u, MouseButton.Left)]
    [InlineData(0x0002u, MouseButton.Right)]
    [InlineData(0x0004u, MouseButton.Middle)]
    [InlineData(0x0003u, MouseButton.Left)] // left wins when several are down
    public void ToButton_PicksTheReportedButton(uint buttons, MouseButton expected)
    {
        Assert.Equal(expected, WindowsConsoleInput.ToButton(buttons));
    }

    // ---------------------------------------------------------------------------------------
    // ConsoleKeyInfo -> KeyEvent (portable backend)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FromConsoleKeyInfo_CopiesKeyCharAndModifiers()
    {
        var info = new ConsoleKeyInfo('\0', ConsoleKey.F5, shift: false, alt: true, control: true);
        var ev = PortableConsoleInput.FromConsoleKeyInfo(info);

        Assert.Equal(ConsoleKey.F5, ev.Key);
        Assert.Equal('\0', ev.Ch);
        Assert.Equal(KeyMods.Ctrl | KeyMods.Alt, ev.Mods);
        Assert.Equal("Ctrl+Alt+F5", ev.ToDisplayString());
    }

    [Fact]
    public void FromConsoleKeyInfo_KeepsPlainCharacters()
    {
        var info = new ConsoleKeyInfo('q', ConsoleKey.Q, shift: false, alt: false, control: false);
        var ev = PortableConsoleInput.FromConsoleKeyInfo(info);

        Assert.True(ev.IsPlainChar);
        Assert.Equal('q', ev.Ch);
    }

    [Theory]
    [InlineData(ConsoleModifiers.Shift, KeyMods.Shift)]
    [InlineData(ConsoleModifiers.Control, KeyMods.Ctrl)]
    [InlineData(ConsoleModifiers.Alt, KeyMods.Alt)]
    [InlineData(ConsoleModifiers.Shift | ConsoleModifiers.Control, KeyMods.Shift | KeyMods.Ctrl)]
    [InlineData((ConsoleModifiers)0, KeyMods.None)]
    public void MapModifiers_TranslatesConsoleModifiers(ConsoleModifiers modifiers, KeyMods expected)
    {
        Assert.Equal(expected, PortableConsoleInput.MapModifiers(modifiers));
    }

    // ---------------------------------------------------------------------------------------
    // InputEvent factories
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void InputEvent_DefaultIsNone()
    {
        var ev = default(InputEvent);

        Assert.Equal(InputKind.None, ev.Kind);
        Assert.True(ev.IsNone);
        Assert.Equal(InputKind.None, InputEvent.None.Kind);
        Assert.True(InputEvent.None.IsNone);
    }

    [Fact]
    public void FromKey_CarriesTheKeyAndMirrorsItsModifiers()
    {
        var key = new KeyEvent(ConsoleKey.F5, '\0', KeyMods.Ctrl);
        var ev = InputEvent.FromKey(key);

        Assert.Equal(InputKind.Key, ev.Kind);
        Assert.Equal(key, ev.Key);
        Assert.Equal(KeyMods.Ctrl, ev.Modifiers);
        Assert.False(ev.IsNone);
    }

    [Fact]
    public void FromMouse_CarriesTheMouseEventAndMirrorsItsModifiers()
    {
        var mouse = new MouseEvent(MouseKind.Wheel, 10, 4, MouseButton.None, -1, KeyMods.Shift);
        var ev = InputEvent.FromMouse(mouse);

        Assert.Equal(InputKind.Mouse, ev.Kind);
        Assert.Equal(mouse, ev.Mouse);
        Assert.Equal(KeyMods.Shift, ev.Modifiers);
        Assert.Equal(-1, ev.Mouse.Wheel);
    }

    [Fact]
    public void Resized_AndModifiersChangedTo_SetTheRightKind()
    {
        Assert.Equal(InputKind.Resize, InputEvent.Resized().Kind);

        var mods = InputEvent.ModifiersChangedTo(KeyMods.Ctrl | KeyMods.Shift);
        Assert.Equal(InputKind.ModifiersChanged, mods.Kind);
        Assert.Equal(KeyMods.Ctrl | KeyMods.Shift, mods.Modifiers);
    }

    [Fact]
    public void MouseEvent_IsPressCoversDownAndDoubleClick()
    {
        Assert.True(new MouseEvent(MouseKind.Down, 0, 0, MouseButton.Left, 0, KeyMods.None).IsPress);
        Assert.True(new MouseEvent(MouseKind.DoubleClick, 0, 0, MouseButton.Left, 0, KeyMods.None).IsPress);
        Assert.False(new MouseEvent(MouseKind.Up, 0, 0, MouseButton.Left, 0, KeyMods.None).IsPress);
        Assert.False(new MouseEvent(MouseKind.Move, 0, 0, MouseButton.None, 0, KeyMods.None).IsPress);
    }
}
