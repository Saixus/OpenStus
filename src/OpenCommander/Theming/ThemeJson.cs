using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCommander.Rendering;

namespace OpenCommander.Theming;

/// <summary>
/// The on-disk shape of a theme file, as read. Unknown top-level keys land in
/// <see cref="Extra"/>, which is how the flat form (colour names straight at the root) is
/// supported alongside the nested <c>"colors"</c> form.
/// </summary>
public sealed class ThemeFile
{
    /// <summary>Human readable theme name.</summary>
    public string? Name { get; set; }

    /// <summary>The colour table, keyed by <see cref="Theme"/> property name.</summary>
    public Dictionary<string, JsonElement>? Colors { get; set; }

    /// <summary>Any other top-level key; colour entries found here are honoured too.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The on-disk shape of a theme file, as written: always the nested, string-valued form.</summary>
public sealed class ThemeFileOut
{
    /// <summary>Human readable theme name.</summary>
    public string? Name { get; set; }

    /// <summary>The colour table, keyed by <see cref="Theme"/> property name.</summary>
    public Dictionary<string, string>? Colors { get; set; }
}

/// <summary>Source-generated serialisation metadata; keeps theme I/O trimming and AOT friendly.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ThemeFile))]
[JsonSerializable(typeof(ThemeFileOut))]
public sealed partial class ThemeJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Parsing and formatting of the colour values found in theme files. Both
/// <c>"Cyan on DarkBlue"</c> strings and <c>{ "fg": "Cyan", "bg": "DarkBlue" }</c> objects are
/// accepted, along with two-element arrays and packed numeric attributes.
/// </summary>
public static class ThemeColor
{
    /// <summary>Formats a style the way <see cref="Theme.SaveToJson"/> writes it.</summary>
    public static string Format(CellStyle style) => $"{style.Fg} on {style.Bg}";

    /// <summary>
    /// Parses a colour name. Accepts every <see cref="ConsoleColor"/> name (case-insensitively),
    /// the decimal indices 0-15, and the common aliases used by Far Manager's palette
    /// (<c>Brown</c>, <c>LightGray</c>, <c>LightCyan</c>, ...).
    /// </summary>
    public static bool TryParseColor(string? text, out ConsoleColor color)
    {
        color = ConsoleColor.Black;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string lowered = text.Trim().ToLowerInvariant();

        // Far's own palette spells colours C_BLUE / F_LIGHTCYAN / B_BLUE; accept those too.
        if (lowered.Length > 2 && lowered[1] == '_' && lowered[0] is 'c' or 'f' or 'b')
        {
            lowered = lowered[2..];
        }

        Span<char> buf = stackalloc char[32];
        int n = 0;
        foreach (char c in lowered)
        {
            if (c is ' ' or '_' or '-' or '\t')
            {
                continue;
            }

            if (n == buf.Length)
            {
                return false;
            }

            buf[n++] = c;
        }

        if (n == 0)
        {
            return false;
        }

        var key = new string(buf[..n]);

        if (int.TryParse(key, out int index))
        {
            if (index is >= 0 and <= 15)
            {
                color = (ConsoleColor)index;
                return true;
            }

            return false;
        }

        if (Aliases.TryGetValue(key, out var alias))
        {
            color = alias;
            return true;
        }

        if (Enum.TryParse(key, ignoreCase: true, out ConsoleColor parsed) && (int)parsed is >= 0 and <= 15)
        {
            color = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a style string. Recognised separators are <c>" on "</c>, <c>/</c>, <c>,</c>,
    /// <c>|</c> and plain whitespace; a single colour sets only the foreground.
    /// </summary>
    public static bool TryParseStyle(string? text, out CellStyle style)
    {
        style = CellStyle.Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string t = text.Trim();
        string fgText;
        string bgText;

        int on = t.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
        if (on >= 0)
        {
            fgText = t[..on];
            bgText = t[(on + 4)..];
        }
        else
        {
            int sep = t.IndexOfAny([':', '/', ',', '|']);
            if (sep >= 0)
            {
                fgText = t[..sep];
                bgText = t[(sep + 1)..];
            }
            else
            {
                string[] words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 1)
                {
                    if (!TryParseColor(words[0], out var only))
                    {
                        return false;
                    }

                    style = new CellStyle(only, CellStyle.Default.Bg);
                    return true;
                }

                if (words.Length != 2)
                {
                    return false;
                }

                fgText = words[0];
                bgText = words[1];
            }
        }

        if (!TryParseColor(fgText, out var fg) || !TryParseColor(bgText, out var bg))
        {
            return false;
        }

        style = new CellStyle(fg, bg);
        return true;
    }

    /// <summary>
    /// Parses any of the accepted JSON shapes into a style: a string, an object with
    /// <c>fg</c>/<c>bg</c> (or <c>foreground</c>/<c>background</c>) members, a two element array,
    /// or a packed <c>fg | bg &lt;&lt; 4</c> attribute number.
    /// </summary>
    public static bool TryParseStyle(JsonElement element, out CellStyle style)
    {
        style = CellStyle.Default;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return TryParseStyle(element.GetString(), out style);

            case JsonValueKind.Number:
                if (element.TryGetInt32(out int attr) && attr is >= 0 and <= 255)
                {
                    style = new CellStyle((ConsoleColor)(attr & 0x0F), (ConsoleColor)((attr >> 4) & 0x0F));
                    return true;
                }

                return false;

            case JsonValueKind.Array:
            {
                ConsoleColor? fg = null;
                ConsoleColor? bg = null;
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (!TryParseElementColor(item, out var c))
                    {
                        return false;
                    }

                    if (i == 0)
                    {
                        fg = c;
                    }
                    else if (i == 1)
                    {
                        bg = c;
                    }

                    i++;
                }

                if (fg is null)
                {
                    return false;
                }

                style = new CellStyle(fg.Value, bg ?? CellStyle.Default.Bg);
                return true;
            }

            case JsonValueKind.Object:
            {
                ConsoleColor? fg = null;
                ConsoleColor? bg = null;
                foreach (var prop in element.EnumerateObject())
                {
                    string name = prop.Name.ToLowerInvariant();
                    bool isFg = name is "fg" or "f" or "foreground" or "fore" or "text";
                    bool isBg = name is "bg" or "b" or "background" or "back";
                    if (!isFg && !isBg)
                    {
                        continue;
                    }

                    if (!TryParseElementColor(prop.Value, out var c))
                    {
                        continue;
                    }

                    if (isFg)
                    {
                        fg = c;
                    }
                    else
                    {
                        bg = c;
                    }
                }

                if (fg is null && bg is null)
                {
                    return false;
                }

                style = new CellStyle(fg ?? CellStyle.Default.Fg, bg ?? CellStyle.Default.Bg);
                return true;
            }

            default:
                return false;
        }
    }

    private static bool TryParseElementColor(JsonElement element, out ConsoleColor color)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return TryParseColor(element.GetString(), out color);
            case JsonValueKind.Number when element.TryGetInt32(out int n) && n is >= 0 and <= 15:
                color = (ConsoleColor)n;
                return true;
            default:
                color = ConsoleColor.Black;
                return false;
        }
    }

    private static readonly Dictionary<string, ConsoleColor> Aliases = new(StringComparer.Ordinal)
    {
        ["brown"] = ConsoleColor.DarkYellow,
        ["orange"] = ConsoleColor.DarkYellow,
        ["darkbrown"] = ConsoleColor.DarkYellow,
        ["purple"] = ConsoleColor.DarkMagenta,
        ["grey"] = ConsoleColor.Gray,
        ["silver"] = ConsoleColor.Gray,
        ["lightgray"] = ConsoleColor.Gray,
        ["lightgrey"] = ConsoleColor.Gray,
        ["darkgrey"] = ConsoleColor.DarkGray,
        ["lightblue"] = ConsoleColor.Blue,
        ["lightgreen"] = ConsoleColor.Green,
        ["lightcyan"] = ConsoleColor.Cyan,
        ["lightred"] = ConsoleColor.Red,
        ["lightmagenta"] = ConsoleColor.Magenta,
        ["lightyellow"] = ConsoleColor.Yellow,
        ["lightwhite"] = ConsoleColor.White,
        ["brightwhite"] = ConsoleColor.White,
    };
}
