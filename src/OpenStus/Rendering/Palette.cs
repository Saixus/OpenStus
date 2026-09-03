using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenStus.Rendering;

/// <summary>
/// A 24-bit colour.
/// </summary>
/// <param name="R">Red channel, 0-255.</param>
/// <param name="G">Green channel, 0-255.</param>
/// <param name="B">Blue channel, 0-255.</param>
public sealed record Rgb(byte R, byte G, byte B)
{
    /// <summary>Parses <c>"#RRGGBB"</c>. The <c>#</c> and surrounding whitespace are optional.</summary>
    /// <returns><see langword="true"/> when <paramref name="text"/> was a valid colour.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out Rgb? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> s = text.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
        {
            s = s[1..];
        }

        if (s.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(s[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        value = new Rgb(r, g, b);
        return true;
    }

    /// <summary>Parses <c>"#RRGGBB"</c>, throwing <see cref="FormatException"/> on anything else.</summary>
    public static Rgb Parse(string text) =>
        TryParse(text, out Rgb? value) ? value : throw new FormatException($"'{text}' is not a #RRGGBB colour.");

    /// <summary>Formats the colour as <c>"#RRGGBB"</c>, upper case.</summary>
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>Same as <see cref="ToHex"/>.</summary>
    public override string ToString() => ToHex();
}

/// <summary>
/// The RGB values behind the 16 console colour slots.
/// </summary>
/// <remarks>
/// <para>
/// In <see cref="ColorDepth.Indexed16"/> this table is unused: the renderer names a slot and the
/// terminal's own scheme decides how it looks. In <see cref="ColorDepth.TrueColor"/> every
/// <see cref="CellStyle"/> is resolved through a palette and written as literal RGB, which is what
/// makes the classic blue-panel look survive whatever theme the terminal is configured with.
/// </para>
/// <para>
/// A palette is immutable; <see cref="With"/> returns a modified copy.
/// </para>
/// </remarks>
public sealed class Palette
{
    /// <summary>Number of slots in a palette - one per <see cref="ConsoleColor"/>.</summary>
    public const int Size = 16;

    private readonly Rgb[] _entries;

    /// <summary>Creates a palette from exactly <see cref="Size"/> entries, in <see cref="ConsoleColor"/> order.</summary>
    /// <exception cref="ArgumentException">The wrong number of entries was supplied.</exception>
    public Palette(IReadOnlyList<Rgb> entries, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != Size)
        {
            throw new ArgumentException($"A palette needs exactly {Size} entries, got {entries.Count}.", nameof(entries));
        }

        _entries = new Rgb[Size];
        for (int i = 0; i < Size; i++)
        {
            _entries[i] = entries[i] ?? throw new ArgumentException("A palette entry cannot be null.", nameof(entries));
        }

        Name = string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim();
    }

    /// <summary>Human readable palette name.</summary>
    public string Name { get; }

    /// <summary>The RGB behind a console colour.</summary>
    public Rgb this[ConsoleColor color] => _entries[(int)color & 0x0F];

    /// <summary>The RGB behind a slot index; the index is masked to 0-15.</summary>
    public Rgb this[int index] => _entries[index & 0x0F];

    /// <summary>Returns a copy of this palette with one slot changed.</summary>
    public Palette With(ConsoleColor color, Rgb value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var copy = (Rgb[])_entries.Clone();
        copy[(int)color & 0x0F] = value;
        return new Palette(copy, Name);
    }

    // =============================================================================================

    /// <summary>
    /// The classic VGA/EGA text palette - the deep navy and hard bright cyan of a DOS-era file
    /// manager. The application's own default is <see cref="Default"/>, the NT table.
    /// </summary>
    /// <remarks>
    /// The RGBI hardware rule is 0x00/0xAA per channel with a +0x55 intensity offset, plus the
    /// famous "brown fixup" at slot 6 (<see cref="ConsoleColor.DarkYellow"/>), which would
    /// otherwise be a muddy dark yellow. Cyan-on-DarkBlue - the colour of 78% of the screen -
    /// scores 10.84:1 here against 4.73:1 under Windows Terminal's default scheme.
    /// </remarks>
    public static Palette ClassicVga { get; } = new(
        [
            new Rgb(0x00, 0x00, 0x00), // Black
            new Rgb(0x00, 0x00, 0xAA), // DarkBlue
            new Rgb(0x00, 0xAA, 0x00), // DarkGreen
            new Rgb(0x00, 0xAA, 0xAA), // DarkCyan
            new Rgb(0xAA, 0x00, 0x00), // DarkRed
            new Rgb(0xAA, 0x00, 0xAA), // DarkMagenta
            new Rgb(0xAA, 0x55, 0x00), // DarkYellow (brown)
            new Rgb(0xAA, 0xAA, 0xAA), // Gray
            new Rgb(0x55, 0x55, 0x55), // DarkGray
            new Rgb(0x55, 0x55, 0xFF), // Blue
            new Rgb(0x55, 0xFF, 0x55), // Green
            new Rgb(0x55, 0xFF, 0xFF), // Cyan
            new Rgb(0xFF, 0x55, 0x55), // Red
            new Rgb(0xFF, 0x55, 0xFF), // Magenta
            new Rgb(0xFF, 0xFF, 0x55), // Yellow
            new Rgb(0xFF, 0xFF, 0xFF), // White
        ],
        "Classic VGA");

    /// <summary>
    /// The legacy Windows NT console palette, built from the channel values 0, 128, 192 and 255.
    /// </summary>
    /// <remarks>
    /// This is the colour table of the Windows console itself before Windows Terminal, and
    /// therefore what the classic look was actually showing on Windows. Its blue is an even
    /// deeper #000080 and its bright cyan a pure #00FFFF, so it is slightly harder still than
    /// <see cref="ClassicVga"/> (12.77:1 on the dominant pair). Offered as an alternative for
    /// anyone chasing byte-exact Windows-console fidelity rather than the DOS look.
    /// </remarks>
    public static Palette WindowsNt { get; } = new(
        [
            new Rgb(0x00, 0x00, 0x00), // Black
            new Rgb(0x00, 0x00, 0x80), // DarkBlue
            new Rgb(0x00, 0x80, 0x00), // DarkGreen
            new Rgb(0x00, 0x80, 0x80), // DarkCyan
            new Rgb(0x80, 0x00, 0x00), // DarkRed
            new Rgb(0x80, 0x00, 0x80), // DarkMagenta
            new Rgb(0x80, 0x80, 0x00), // DarkYellow
            new Rgb(0xC0, 0xC0, 0xC0), // Gray
            new Rgb(0x80, 0x80, 0x80), // DarkGray
            new Rgb(0x00, 0x00, 0xFF), // Blue
            new Rgb(0x00, 0xFF, 0x00), // Green
            new Rgb(0x00, 0xFF, 0xFF), // Cyan
            new Rgb(0xFF, 0x00, 0x00), // Red
            new Rgb(0xFF, 0x00, 0xFF), // Magenta
            new Rgb(0xFF, 0xFF, 0x00), // Yellow
            new Rgb(0xFF, 0xFF, 0xFF), // White
        ],
        "Windows NT");

    /// <summary>
    /// Windows Terminal's default "Campbell" scheme - what an indexed-colour run actually looks
    /// like on a stock Windows 11 box.
    /// </summary>
    /// <remarks>
    /// Kept as a named palette because it is the reference case for the washout this whole layer
    /// exists to fix: Campbell packs blue, bright blue, cyan and bright cyan into a narrow
    /// luminance band, so the panel's Cyan-on-DarkBlue collapses from 10.84:1 to 4.73:1.
    /// </remarks>
    public static Palette Campbell { get; } = new(
        [
            new Rgb(0x0C, 0x0C, 0x0C), // Black
            new Rgb(0x00, 0x37, 0xDA), // DarkBlue
            new Rgb(0x13, 0xA1, 0x0E), // DarkGreen
            new Rgb(0x3A, 0x96, 0xDD), // DarkCyan
            new Rgb(0xC5, 0x0F, 0x1F), // DarkRed
            new Rgb(0x88, 0x17, 0x98), // DarkMagenta
            new Rgb(0xC1, 0x9C, 0x00), // DarkYellow
            new Rgb(0xCC, 0xCC, 0xCC), // Gray
            new Rgb(0x76, 0x76, 0x76), // DarkGray
            new Rgb(0x3B, 0x78, 0xFF), // Blue
            new Rgb(0x16, 0xC6, 0x0C), // Green
            new Rgb(0x61, 0xD6, 0xD6), // Cyan
            new Rgb(0xE7, 0x48, 0x56), // Red
            new Rgb(0xB4, 0x00, 0x9E), // Magenta
            new Rgb(0xF9, 0xF1, 0xA5), // Yellow
            new Rgb(0xF2, 0xF2, 0xF2), // White
        ],
        "Campbell");

    // =============================================================================================

    /// <summary>
    /// WCAG 2.x relative luminance of a colour, 0 (black) to 1 (white).
    /// </summary>
    public static double RelativeLuminance(Rgb color)
    {
        ArgumentNullException.ThrowIfNull(color);
        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

        static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>
    /// WCAG 2.x contrast ratio between two colours, from 1:1 (identical) to 21:1 (black on white).
    /// Symmetric in its arguments. 4.5 is the threshold for body text, 7 for the strictest level.
    /// </summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        (double lighter, double darker) = la >= lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Contrast ratio between the two colours of a style, resolved through this palette.</summary>
    public double ContrastOf(CellStyle style) => ContrastRatio(this[style.Fg], this[style.Bg]);

    // =============================================================================================

    /// <summary>
    /// The palette a run uses when nothing else is asked for: <see cref="WindowsNt"/>.
    /// </summary>
    /// <remarks>
    /// Console file managers of the classic era installed this table into the console themselves
    /// at startup, over whatever scheme the terminal came with, so it is what the classic look was
    /// actually rendered with. Matching that look means matching the table it was drawn on, so the
    /// NT values are the default here and <see cref="ClassicVga"/> is the alternative, not the
    /// other way round.
    /// </remarks>
    public static Palette Default => WindowsNt;

    /// <summary>
    /// Resolves a built-in palette by name: <c>nt</c> / <c>windows-nt</c>, <c>vga</c> /
    /// <c>classic-vga</c>, <c>campbell</c>. Case and hyphens are ignored.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="name"/> named a preset.</returns>
    public static bool TryGetPreset(string? name, [NotNullWhen(true)] out Palette? palette)
    {
        palette = Normalise(name) switch
        {
            "nt" or "windowsnt" or "windows" => WindowsNt,
            "vga" or "classicvga" or "classic" or "cga" => ClassicVga,
            "campbell" or "terminal" => Campbell,
            _ => null,
        };

        return palette is not null;

        static string Normalise(string? s) =>
            s is null ? string.Empty : s.Replace("-", string.Empty, StringComparison.Ordinal)
                                        .Replace("_", string.Empty, StringComparison.Ordinal)
                                        .Trim()
                                        .ToLowerInvariant();
    }

    /// <summary>
    /// Loads a palette from a JSON file of <c>"#RRGGBB"</c> strings keyed by
    /// <see cref="ConsoleColor"/> name. Entries that are missing, unknown or malformed keep their
    /// <see cref="Default"/> value, so a partial file is perfectly valid. Both the nested
    /// <c>{ "colors": { ... } }</c> form and colour names straight at the root are accepted.
    /// </summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static Palette Load(string path)
    {
        string json = File.ReadAllText(path);
        var entries = new Rgb[Size];
        for (int i = 0; i < Size; i++)
        {
            entries[i] = Default[i];
        }

        var file = JsonSerializer.Deserialize(json, PaletteJsonContext.Default.PaletteFile);
        string name = file?.Name is { Length: > 0 } n ? n.Trim() : Path.GetFileNameWithoutExtension(path);

        // The flat form first, so an explicit "colors" block wins over a stray root-level key.
        if (file?.Extra is not null)
        {
            foreach (var (key, value) in file.Extra)
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    ApplyEntry(entries, key, value.GetString());
                }
            }
        }

        if (file?.Colors is not null)
        {
            foreach (var (key, value) in file.Colors)
            {
                ApplyEntry(entries, key, value);
            }
        }

        return new Palette(entries, name);
    }

    /// <summary>
    /// Resolves <paramref name="path"/> to a palette: a built-in preset name (see
    /// <see cref="TryGetPreset"/>), or a readable palette file, falling back to
    /// <see cref="Default"/> for anything else. Never throws.
    /// </summary>
    public static Palette LoadOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Default;
        }

        if (TryGetPreset(path, out Palette? preset))
        {
            return preset;
        }

        try
        {
            return File.Exists(path) ? Load(path) : Default;
        }
        catch
        {
            return Default;
        }
    }

    /// <summary>Writes the whole palette to a JSON file, creating the directory if needed.</summary>
    public void SaveToJson(string path)
    {
        var colors = new Dictionary<string, string>(Size, StringComparer.Ordinal);
        for (int i = 0; i < Size; i++)
        {
            colors[((ConsoleColor)i).ToString()] = _entries[i].ToHex();
        }

        var dto = new PaletteFile { Name = Name, Colors = colors };
        string json = JsonSerializer.Serialize(dto, PaletteJsonContext.Default.PaletteFile);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, json);
    }

    private static void ApplyEntry(Rgb[] entries, string key, string? value)
    {
        if (TryParseSlot(key, out ConsoleColor slot) && Rgb.TryParse(value, out Rgb? rgb))
        {
            entries[(int)slot] = rgb;
        }
    }

    /// <summary>
    /// Resolves a slot name: any <see cref="ConsoleColor"/> name, the indices 0-15, or the common
    /// aliases a hand-written file is likely to use (<c>LightCyan</c>, <c>Brown</c>, ...).
    /// </summary>
    private static bool TryParseSlot(string? key, out ConsoleColor color)
    {
        color = ConsoleColor.Black;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string k = key.Trim();

        if (int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            if (index is < 0 or >= Size)
            {
                return false;
            }

            color = (ConsoleColor)index;
            return true;
        }

        if (Enum.TryParse(k, ignoreCase: true, out color) && Enum.IsDefined(color))
        {
            return true;
        }

        // "Bright" is the other common spelling of the high-intensity half.
        if (k.StartsWith("bright", StringComparison.OrdinalIgnoreCase))
        {
            k = "Light" + k[6..];
        }

        ConsoleColor? alias = k.ToLowerInvariant() switch
        {
            "brown" => ConsoleColor.DarkYellow,
            "lightgray" or "lightgrey" or "silver" => ConsoleColor.Gray,
            "darkgrey" => ConsoleColor.DarkGray,
            "lightblue" => ConsoleColor.Blue,
            "lightgreen" => ConsoleColor.Green,
            "lightcyan" => ConsoleColor.Cyan,
            "lightred" => ConsoleColor.Red,
            "lightmagenta" or "lightpurple" => ConsoleColor.Magenta,
            "lightyellow" => ConsoleColor.Yellow,
            "lightwhite" => ConsoleColor.White,
            "purple" => ConsoleColor.DarkMagenta,
            _ => null,
        };

        if (alias is null)
        {
            return false;
        }

        color = alias.Value;
        return true;
    }
}

/// <summary>The on-disk shape of a palette file. Colour names found at the root land in <see cref="Extra"/>.</summary>
public sealed class PaletteFile
{
    /// <summary>Human readable palette name.</summary>
    public string? Name { get; set; }

    /// <summary>The colour table, keyed by <see cref="ConsoleColor"/> name.</summary>
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>Any other top-level key; colour entries found here are honoured too.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Source-generated serialisation metadata; keeps palette I/O trimming and AOT friendly.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PaletteFile))]
public sealed partial class PaletteJsonContext : JsonSerializerContext
{
}
