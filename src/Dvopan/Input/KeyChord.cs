using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Dvopan.Input;

/// <summary>
/// A parsed key binding such as <c>"Ctrl+Alt+F5"</c>, <c>"Shift+Ins"</c>, <c>"Alt+F1"</c>,
/// <c>"Enter"</c> or <c>"Gray+"</c>, usable as a predicate over <see cref="KeyEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// The notation follows Far Manager: modifiers are written in Ctrl, Alt, Shift order and joined with
/// <c>'+'</c>, and the numeric keypad keys are spelled <c>Gray+</c>, <c>Gray-</c>, <c>Gray*</c>,
/// <c>Gray/</c> and <c>Gray.</c>. Parsing is case-insensitive and accepts a number of aliases
/// (<c>Escape</c>/<c>Esc</c>, <c>Insert</c>/<c>Ins</c>, <c>PageDown</c>/<c>PgDn</c>, …), but
/// <see cref="ToString"/> always produces the canonical spelling, so
/// <c>KeyChord.Parse(s).ToString()</c> is idempotent.
/// </para>
/// <para>
/// Matching is an exact modifier comparison: <c>"Enter"</c> does not match Shift+Enter.
/// </para>
/// </remarks>
/// <param name="Key">The virtual key the chord requires.</param>
/// <param name="Mods">The exact modifier set the chord requires.</param>
public readonly record struct KeyChord(ConsoleKey Key, KeyMods Mods)
{
    /// <summary>An unbound chord that matches nothing.</summary>
    public static KeyChord None => default;

    /// <summary><see langword="true"/> when this chord is the unbound <see cref="None"/> value.</summary>
    public bool IsNone => Key == ConsoleKey.None && Mods == KeyMods.None;

    /// <summary>
    /// Parses a chord, throwing when the text is not a valid binding.
    /// </summary>
    /// <param name="text">The chord text, for example <c>"Ctrl+Gray*"</c>.</param>
    /// <returns>The parsed chord.</returns>
    /// <exception cref="FormatException">The text is not a valid chord.</exception>
    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"Not a valid key chord: '{text}'.");

    /// <summary>
    /// Attempts to parse a chord.
    /// </summary>
    /// <param name="text">The chord text; may be <see langword="null"/> or empty.</param>
    /// <param name="chord">Receives the parsed chord, or <see cref="None"/> on failure.</param>
    /// <returns><see langword="true"/> when the text was a valid chord.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out KeyChord chord)
    {
        chord = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!KeyNames.SplitChord(text.Trim(), out var modTokens, out var keyToken))
        {
            return false;
        }

        var mods = KeyMods.None;
        foreach (var m in modTokens)
        {
            switch (m.ToLowerInvariant())
            {
                case "ctrl":
                case "ctl":
                case "control":
                    mods |= KeyMods.Ctrl;
                    break;
                case "alt":
                    mods |= KeyMods.Alt;
                    break;
                case "shift":
                case "shft":
                    mods |= KeyMods.Shift;
                    break;
                default:
                    return false;
            }
        }

        if (!KeyNames.TryParseKey(keyToken, out var key))
        {
            return false;
        }

        chord = new KeyChord(key, mods);
        return true;
    }

    /// <summary>
    /// Parses a list of chords separated by commas or semicolons, skipping entries that do not parse.
    /// </summary>
    /// <param name="list">For example <c>"F3, Numpad5"</c>.</param>
    /// <returns>The chords that parsed successfully, in order.</returns>
    public static IReadOnlyList<KeyChord> ParseList(string? list)
    {
        var result = new List<KeyChord>();
        if (string.IsNullOrWhiteSpace(list))
        {
            return result;
        }

        foreach (var part in list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParse(part, out var chord))
            {
                result.Add(chord);
            }
        }

        return result;
    }

    /// <summary>
    /// Tests whether a key press satisfies this chord (exact modifier match).
    /// </summary>
    /// <param name="ev">The key press to test.</param>
    /// <returns><see langword="true"/> when the chord fires.</returns>
    public bool Matches(KeyEvent ev) => !IsNone && ev.Key == Key && ev.Mods == Mods;

    /// <summary>
    /// Convenience overload: parses <paramref name="chord"/> and tests <paramref name="ev"/> against it.
    /// </summary>
    /// <param name="chord">The chord text.</param>
    /// <param name="ev">The key press to test.</param>
    /// <returns><see langword="true"/> when the text parses and the chord fires.</returns>
    public static bool Matches(string chord, KeyEvent ev) => TryParse(chord, out var c) && c.Matches(ev);

    /// <summary>Returns this chord as a reusable predicate for a key binding table.</summary>
    /// <returns>A predicate equivalent to <see cref="Matches(KeyEvent)"/>.</returns>
    public Func<KeyEvent, bool> ToPredicate()
    {
        var self = this;
        return ev => self.Matches(ev);
    }

    /// <summary>Materialises this chord as a <see cref="KeyEvent"/> with no character.</summary>
    /// <returns>The equivalent key event.</returns>
    public KeyEvent ToKeyEvent() => new(Key, '\0', Mods);

    /// <summary>Renders the chord in canonical Far notation, for example <c>"Ctrl+Alt+F5"</c>.</summary>
    /// <returns>The chord text.</returns>
    public override string ToString() => KeyNames.Format(Key, '\0', Mods);
}

/// <summary>
/// Shared naming: converts between <see cref="ConsoleKey"/> plus <see cref="KeyMods"/> and the
/// textual key notation used by the key bar, the help screen and the key binding table.
/// </summary>
internal static class KeyNames
{
    /// <summary>
    /// Formats a key and modifier set. Modifiers are emitted in Ctrl, Alt, Shift order.
    /// </summary>
    /// <param name="key">The virtual key.</param>
    /// <param name="ch">The character, used only when <paramref name="key"/> is <see cref="ConsoleKey.None"/>.</param>
    /// <param name="mods">The modifier set.</param>
    /// <returns>The display string.</returns>
    internal static string Format(ConsoleKey key, char ch, KeyMods mods)
    {
        var name = KeyName(key, ch);
        if (mods == KeyMods.None)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 16);
        if ((mods & KeyMods.Ctrl) != 0)
        {
            sb.Append("Ctrl+");
        }

        if ((mods & KeyMods.Alt) != 0)
        {
            sb.Append("Alt+");
        }

        if ((mods & KeyMods.Shift) != 0)
        {
            sb.Append("Shift+");
        }

        return sb.Append(name).ToString();
    }

    /// <summary>
    /// The canonical name of a single key, with no modifiers.
    /// </summary>
    /// <param name="key">The virtual key.</param>
    /// <param name="ch">Fallback character used when <paramref name="key"/> is <see cref="ConsoleKey.None"/>.</param>
    /// <returns>The key name.</returns>
    internal static string KeyName(ConsoleKey key, char ch = '\0')
    {
        if (key >= ConsoleKey.F1 && key <= ConsoleKey.F24)
        {
            return "F" + (key - ConsoleKey.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (key >= ConsoleKey.D0 && key <= ConsoleKey.D9)
        {
            return ((char)('0' + (key - ConsoleKey.D0))).ToString();
        }

        if (key >= ConsoleKey.A && key <= ConsoleKey.Z)
        {
            return ((char)('A' + (key - ConsoleKey.A))).ToString();
        }

        if (key >= ConsoleKey.NumPad0 && key <= ConsoleKey.NumPad9)
        {
            return "Num" + ((char)('0' + (key - ConsoleKey.NumPad0))).ToString();
        }

        return key switch
        {
            ConsoleKey.None => ch != '\0' ? char.ToUpperInvariant(ch).ToString() : "None",
            ConsoleKey.Enter => "Enter",
            ConsoleKey.Escape => "Esc",
            ConsoleKey.Spacebar => "Space",
            ConsoleKey.Backspace => "Backspace",
            ConsoleKey.Tab => "Tab",
            ConsoleKey.Insert => "Ins",
            ConsoleKey.Delete => "Del",
            ConsoleKey.PageUp => "PgUp",
            ConsoleKey.PageDown => "PgDn",
            ConsoleKey.UpArrow => "Up",
            ConsoleKey.DownArrow => "Down",
            ConsoleKey.LeftArrow => "Left",
            ConsoleKey.RightArrow => "Right",
            ConsoleKey.Home => "Home",
            ConsoleKey.End => "End",
            ConsoleKey.Add => "Gray+",
            ConsoleKey.Subtract => "Gray-",
            ConsoleKey.Multiply => "Gray*",
            ConsoleKey.Divide => "Gray/",
            ConsoleKey.Decimal => "Gray.",
            ConsoleKey.OemPlus => "=",
            ConsoleKey.OemMinus => "-",
            ConsoleKey.OemComma => ",",
            ConsoleKey.OemPeriod => ".",
            ConsoleKey.Oem1 => ";",
            ConsoleKey.Oem3 => "`",
            ConsoleKey.Oem4 => "[",
            ConsoleKey.Oem5 => "\\",
            ConsoleKey.Oem6 => "]",
            ConsoleKey.Oem7 => "'",
            ConsoleKey.PrintScreen => "PrtScr",
            _ => key.ToString(),
        };
    }

    /// <summary>
    /// Parses a single key token (no modifiers).
    /// </summary>
    /// <param name="token">The token, for example <c>"F5"</c>, <c>"Ins"</c> or <c>"Gray+"</c>.</param>
    /// <param name="key">Receives the key.</param>
    /// <returns><see langword="true"/> when the token was recognised.</returns>
    internal static bool TryParseKey(string token, out ConsoleKey key)
    {
        key = ConsoleKey.None;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var t = token.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        var lower = t.ToLowerInvariant();

        switch (lower)
        {
            case "gray+":
            case "num+":
            case "numpadadd":
            case "add":
            case "+":
                key = ConsoleKey.Add;
                return true;
            case "gray-":
            case "num-":
            case "numpadsubtract":
            case "subtract":
                key = ConsoleKey.Subtract;
                return true;
            case "gray*":
            case "num*":
            case "numpadmultiply":
            case "multiply":
            case "*":
                key = ConsoleKey.Multiply;
                return true;
            case "gray/":
            case "num/":
            case "numpaddivide":
            case "divide":
            case "/":
                key = ConsoleKey.Divide;
                return true;
            case "gray.":
            case "num.":
            case "decimal":
                key = ConsoleKey.Decimal;
                return true;
            case "enter":
            case "return":
                key = ConsoleKey.Enter;
                return true;
            case "esc":
            case "escape":
                key = ConsoleKey.Escape;
                return true;
            case "space":
            case "spacebar":
                key = ConsoleKey.Spacebar;
                return true;
            case "tab":
                key = ConsoleKey.Tab;
                return true;
            case "backspace":
            case "back":
            case "bs":
                key = ConsoleKey.Backspace;
                return true;
            case "ins":
            case "insert":
                key = ConsoleKey.Insert;
                return true;
            case "del":
            case "delete":
                key = ConsoleKey.Delete;
                return true;
            case "pgup":
            case "pageup":
                key = ConsoleKey.PageUp;
                return true;
            case "pgdn":
            case "pgdown":
            case "pagedown":
                key = ConsoleKey.PageDown;
                return true;
            case "up":
            case "uparrow":
                key = ConsoleKey.UpArrow;
                return true;
            case "down":
            case "downarrow":
                key = ConsoleKey.DownArrow;
                return true;
            case "left":
            case "leftarrow":
                key = ConsoleKey.LeftArrow;
                return true;
            case "right":
            case "rightarrow":
                key = ConsoleKey.RightArrow;
                return true;
            case "home":
                key = ConsoleKey.Home;
                return true;
            case "end":
                key = ConsoleKey.End;
                return true;
            case "prtscr":
            case "printscreen":
                key = ConsoleKey.PrintScreen;
                return true;
            case "=":
                key = ConsoleKey.OemPlus;
                return true;
            case "-":
                key = ConsoleKey.OemMinus;
                return true;
            case ",":
                key = ConsoleKey.OemComma;
                return true;
            case ".":
                key = ConsoleKey.OemPeriod;
                return true;
            case ";":
                key = ConsoleKey.Oem1;
                return true;
            case "`":
                key = ConsoleKey.Oem3;
                return true;
            case "[":
                key = ConsoleKey.Oem4;
                return true;
            case "\\":
                key = ConsoleKey.Oem5;
                return true;
            case "]":
                key = ConsoleKey.Oem6;
                return true;
            case "'":
                key = ConsoleKey.Oem7;
                return true;
        }

        // Fn
        if (lower.Length >= 2 && lower[0] == 'f' &&
            int.TryParse(lower.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var fn) &&
            fn is >= 1 and <= 24)
        {
            key = ConsoleKey.F1 + (fn - 1);
            return true;
        }

        // Num0..Num9 / Numpad0..Numpad9
        if (lower.StartsWith("numpad", StringComparison.Ordinal) && lower.Length == 7 && char.IsAsciiDigit(lower[6]))
        {
            key = ConsoleKey.NumPad0 + (lower[6] - '0');
            return true;
        }

        if (lower.StartsWith("num", StringComparison.Ordinal) && lower.Length == 4 && char.IsAsciiDigit(lower[3]))
        {
            key = ConsoleKey.NumPad0 + (lower[3] - '0');
            return true;
        }

        // Single letter or digit.
        if (t.Length == 1)
        {
            var c = t[0];
            if (char.IsAsciiLetter(c))
            {
                key = ConsoleKey.A + (char.ToUpperInvariant(c) - 'A');
                return true;
            }

            if (char.IsAsciiDigit(c))
            {
                key = ConsoleKey.D0 + (c - '0');
                return true;
            }
        }

        // Anything else the enum itself knows about ("Oem2", "Pause", "Clear", ...). Enum.TryParse
        // also happily accepts raw numbers, which would silently turn "13" into Enter - reject those.
        foreach (var c in t)
        {
            if (!char.IsAsciiDigit(c))
            {
                return Enum.TryParse(t, ignoreCase: true, out key) && key != ConsoleKey.None;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits a chord into its modifier tokens and its key token, correctly handling key tokens that
    /// are or end with <c>'+'</c> (<c>"Gray+"</c>, <c>"Ctrl++"</c>, <c>"+"</c>).
    /// </summary>
    /// <param name="s">The trimmed chord text.</param>
    /// <param name="mods">Receives the modifier tokens, in source order.</param>
    /// <param name="keyToken">Receives the key token.</param>
    /// <returns><see langword="true"/> when the text could be split into a non-empty key token.</returns>
    internal static bool SplitChord(string s, out List<string> mods, out string keyToken)
    {
        mods = [];
        keyToken = string.Empty;
        if (s.Length == 0)
        {
            return false;
        }

        string rest;

        // Key tokens that themselves contain the separator have to be peeled off from the right.
        if (s.Length >= 5 && s.EndsWith("gray+", StringComparison.OrdinalIgnoreCase))
        {
            keyToken = s[^5..];
            rest = s[..^5];
        }
        else if (s == "+" || (s.Length >= 2 && s[^1] == '+' && s[^2] == '+'))
        {
            // A bare "+", or the doubled form "Ctrl++". A single trailing '+' after a modifier
            // ("Ctrl+") is a dangling separator, not the gray plus key, and falls through to fail.
            keyToken = "+";
            rest = s.Length == 1 ? string.Empty : s[..^1];
        }
        else
        {
            var idx = s.LastIndexOf('+');
            if (idx < 0)
            {
                keyToken = s;
                rest = string.Empty;
            }
            else
            {
                keyToken = s[(idx + 1)..];
                rest = s[..idx];
            }
        }

        // "Ctrl+Gray+" and "Ctrl++" leave a dangling separator on the modifier part.
        rest = rest.TrimEnd('+');

        if (keyToken.Length == 0)
        {
            return false;
        }

        if (rest.Length > 0)
        {
            foreach (var m in rest.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                mods.Add(m);
            }
        }

        return true;
    }
}
