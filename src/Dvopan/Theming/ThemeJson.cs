using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dvopan.Rendering;

namespace Dvopan.Theming;

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

    /// <summary>
    /// The optional RGB palette block. Kept as a raw <see cref="JsonElement"/> because several
    /// shapes are accepted; see <see cref="ThemePalette.TryParse"/>. Absent in every theme file
    /// written before palettes existed, which is exactly why it is optional.
    /// </summary>
    public JsonElement? Palette { get; set; }

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

    /// <summary>The RGB palette block.</summary>
    public ThemePaletteOut? Palette { get; set; }
}

/// <summary>
/// The written shape of a theme's palette block: deliberately the same
/// <c>{ "name": ..., "colors": { ... } }</c> shape a standalone palette file uses, so the two are
/// interchangeable by copy and paste.
/// </summary>
public sealed class ThemePaletteOut
{
    /// <summary>Human readable palette name.</summary>
    public string? Name { get; set; }

    /// <summary>The RGB table as <c>"#RRGGBB"</c> strings, keyed by <see cref="ConsoleColor"/> name.</summary>
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
/// Reads the optional <c>"palette"</c> block of a theme file into a <see cref="Palette"/>.
/// </summary>
/// <remarks>
/// Four shapes are accepted, all tolerant of anything they do not recognise:
/// <list type="bullet">
/// <item><description>
/// a string naming a built-in palette - <c>"palette": "WindowsNt"</c>;
/// </description></item>
/// <item><description>
/// an object of <c>"&lt;ConsoleColor&gt;": "#RRGGBB"</c> entries, optionally nested under
/// <c>"colors"</c>, optionally named with <c>"name"</c>, and optionally starting from a named
/// built-in with <c>"base"</c> - anything not mentioned keeps the base value;
/// </description></item>
/// <item><description>
/// an array of 16 <c>"#RRGGBB"</c> strings in <see cref="ConsoleColor"/> order.
/// </description></item>
/// </list>
/// Slot names go through <see cref="ThemeColor.TryParseColor"/>, so the Far spellings
/// (<c>LightCyan</c>, <c>Brown</c>, <c>B_BLUE</c>) and the indices 0-15 all work.
/// </remarks>
public static class ThemePalette
{
    /// <summary>
    /// Resolves a built-in palette by name, ignoring case and separators.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="name"/> named a built-in palette.</returns>
    public static bool TryGetBuiltIn(string? name, [NotNullWhen(true)] out Palette? palette)
    {
        palette = Normalize(name) switch
        {
            "classicvga" or "vga" or "classic" or "dos" or "ega" or "cga" => Palette.ClassicVga,
            "windowsnt" or "nt" or "legacy" or "conhost" or "far" or "vintage" => Palette.WindowsNt,
            "campbell" or "windowsterminal" or "terminal" => Palette.Campbell,
            _ => null,
        };

        return palette is not null;
    }

    /// <summary>
    /// Parses a palette block.
    /// </summary>
    /// <param name="element">The <c>"palette"</c> value from the theme file.</param>
    /// <param name="fallback">The palette to start from when the block only overrides some slots.</param>
    /// <param name="palette">The parsed palette.</param>
    /// <returns><see langword="false"/> when the block carried nothing usable at all.</returns>
    public static bool TryParse(JsonElement element, Palette? fallback, [NotNullWhen(true)] out Palette? palette)
    {
        palette = null;
        Palette start = fallback ?? Palette.ClassicVga;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return TryGetBuiltIn(element.GetString(), out palette);

            case JsonValueKind.Array:
            {
                var entries = Copy(start);
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (i >= Palette.Size)
                    {
                        break;
                    }

                    if (item.ValueKind == JsonValueKind.String && Rgb.TryParse(item.GetString(), out Rgb? rgb))
                    {
                        entries[i] = rgb;
                    }

                    i++;
                }

                if (i == 0)
                {
                    return false;
                }

                palette = new Palette(entries, null);
                return true;
            }

            case JsonValueKind.Object:
            {
                string? name = null;
                bool touched = false;

                // "base" first: it decides what the individual entries are layered on top of.
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string key = Normalize(prop.Name);
                    if (key is "base" or "basepalette" or "from" or "inherits" or "extends")
                    {
                        if (TryGetBuiltIn(prop.Value.GetString(), out Palette? baseline))
                        {
                            start = baseline;
                            touched = true;
                        }
                    }
                    else if (key == "name")
                    {
                        name = prop.Value.GetString();
                    }
                }

                var entries = Copy(start);

                // Root level entries first, so an explicit "colors" block wins over a stray key,
                // matching how the theme's own colour table is layered.
                touched |= ApplyEntries(entries, element);

                if (element.TryGetProperty("colors", out var colors) || element.TryGetProperty("Colors", out colors))
                {
                    touched |= ApplyEntries(entries, colors);
                }

                if (!touched && name is null)
                {
                    return false;
                }

                palette = new Palette(entries, string.IsNullOrWhiteSpace(name) ? start.Name : name);
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Lower-cases and strips the separators a hand-written key might use.</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        Span<char> buf = stackalloc char[64];
        int n = 0;
        foreach (char c in text)
        {
            if (c is ' ' or '_' or '-' or '.' or '\t')
            {
                continue;
            }

            if (n == buf.Length)
            {
                return string.Empty;
            }

            buf[n++] = char.ToLowerInvariant(c);
        }

        return new string(buf[..n]);
    }

    private static Rgb[] Copy(Palette source)
    {
        var entries = new Rgb[Palette.Size];
        for (int i = 0; i < Palette.Size; i++)
        {
            entries[i] = source[i];
        }

        return entries;
    }

    /// <summary>Applies every <c>"&lt;slot&gt;": "#RRGGBB"</c> member of an object.</summary>
    /// <returns><see langword="true"/> when at least one entry was recognised.</returns>
    private static bool ApplyEntries(Rgb[] entries, JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        bool any = false;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!ThemeColor.TryParseColor(prop.Name, out var slot)
                || !Rgb.TryParse(prop.Value.GetString(), out Rgb? rgb))
            {
                continue;
            }

            entries[(int)slot & 0x0F] = rgb;
            any = true;
        }

        return any;
    }
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
