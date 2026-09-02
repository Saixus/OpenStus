using System.Globalization;
using System.Reflection;

namespace OpenStus.Core;

/// <summary>
/// The parsed <c>stus</c> command line.
/// </summary>
/// <remarks>
/// <para>
/// Parsing never throws: a malformed option lands in <see cref="Error"/> and the caller prints the
/// usage text and exits non-zero. Both <c>--option value</c> and <c>--option=value</c> spellings are
/// accepted, and on Windows the <c>/?</c> and <c>-?</c> help spellings work too.
/// </para>
/// <para>
/// The single positional argument is the start directory; <c>--left</c> and <c>--right</c> override
/// it per panel.
/// </para>
/// <para>
/// <c>--ansi</c> and <c>--size</c> both only mean anything with <c>--screenshot</c>. <c>--size</c>
/// is rejected without it rather than silently doing nothing, because a forced size makes the
/// terminal headless and an interactive run would then paint into a screen no one can see.
/// </para>
/// <para>
/// <c>--colors</c> and <c>--palette</c> are the two halves of the colour question: the first decides
/// how much colour is written, the second what the 16 slots look like when it is written as RGB.
/// Both have a persisted counterpart (<see cref="Settings.Colors"/> and
/// <see cref="Settings.PalettePath"/>) that the command line overrides; see
/// <see cref="Application.ResolveColorDepth(CommandLineArgs, Settings)"/> for the full precedence.
/// </para>
/// </remarks>
public sealed class CommandLineArgs
{
    /// <summary>The virtual screen width <c>--screenshot</c> uses when <c>--size</c> is absent.</summary>
    public const int DefaultScreenshotWidth = 120;

    /// <summary>The virtual screen height <c>--screenshot</c> uses when <c>--size</c> is absent.</summary>
    public const int DefaultScreenshotHeight = 40;

    /// <summary>The smallest screen <c>--size</c> will accept.</summary>
    public const int MinSize = 10;

    /// <summary>The largest screen <c>--size</c> will accept.</summary>
    public const int MaxSize = 1000;

    /// <summary>The positional start directory, or <see langword="null"/>.</summary>
    public string? StartPath { get; set; }

    /// <summary>The initial left panel directory, or <see langword="null"/>.</summary>
    public string? LeftPath { get; set; }

    /// <summary>The initial right panel directory, or <see langword="null"/>.</summary>
    public string? RightPath { get; set; }

    /// <summary>A theme file to load instead of the built-in palette, or <see langword="null"/>.</summary>
    public string? ThemePath { get; set; }

    /// <summary>
    /// The colour depth asked for with <c>--colors</c>, or <see langword="null"/> when the option was
    /// absent - which is not the same as <see cref="ColorMode.Auto"/>: an absent option defers to the
    /// <see cref="Settings.Colors"/> setting, while an explicit <c>--colors auto</c> overrides it and
    /// asks for detection.
    /// </summary>
    public ColorMode? Colors { get; set; }

    /// <summary>
    /// A palette file giving the RGB behind the 16 colour slots, or <see langword="null"/>. Only
    /// consulted in <see cref="Rendering.ColorDepth.TrueColor"/>.
    /// </summary>
    public string? PalettePath { get; set; }

    /// <summary>Render exactly one frame to stdout and exit, without touching the real console.</summary>
    public bool Screenshot { get; set; }

    /// <summary>With <see cref="Screenshot"/>, emit SGR colour escapes instead of plain text.</summary>
    public bool Ansi { get; set; }

    /// <summary>
    /// The forced virtual screen width, or <see langword="null"/>. Only meaningful together with
    /// <see cref="Screenshot"/>; <see cref="Parse"/> rejects the combination without it.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>The forced virtual screen height, or <see langword="null"/>; see <see cref="Width"/>.</summary>
    public int? Height { get; set; }

    /// <summary>The initial panel view mode number 1..9, or <see langword="null"/>.</summary>
    public int? ViewMode { get; set; }

    /// <summary>
    /// A fixed time for the corner clock, or <see langword="null"/> to read the wall clock.
    /// </summary>
    /// <remarks>
    /// Exists so <c>--screenshot</c> can be byte-for-byte reproducible. A live clock in the top
    /// right corner means two runs a minute apart differ, which defeats comparing a rendered frame
    /// against a golden file - the one thing <c>--screenshot</c> is for.
    /// </remarks>
    public TimeOnly? ClockTime { get; set; }

    /// <summary>The user asked for <c>--clock off</c>: draw no clock at all.</summary>
    public bool HideClock { get; set; }

    /// <summary>The user asked for <c>--help</c>.</summary>
    public bool ShowHelp { get; set; }

    /// <summary>The user asked for <c>--version</c>.</summary>
    public bool ShowVersion { get; set; }

    /// <summary>The first parse failure, or <see langword="null"/> when the line was well formed.</summary>
    public string? Error { get; set; }

    /// <summary><see langword="true"/> when <see cref="Error"/> is set.</summary>
    public bool HasError => Error is not null;

    /// <summary>
    /// The screen width to build the terminal with: the forced one, or the screenshot default when
    /// <c>--screenshot</c> was given without <c>--size</c>.
    /// </summary>
    public int? EffectiveWidth => Width ?? (Screenshot ? DefaultScreenshotWidth : null);

    /// <summary>The screen height to build the terminal with; see <see cref="EffectiveWidth"/>.</summary>
    public int? EffectiveHeight => Height ?? (Screenshot ? DefaultScreenshotHeight : null);

    /// <summary>The usage text printed for <c>--help</c> and for a malformed command line.</summary>
    public static string UsageText =>
        """
        Open Stus - a dual-pane console file manager.

        Usage: stus [startPath] [options]

          startPath              the folder both panels open in

        Options:
          --left <path>          initial left panel folder
          --right <path>         initial right panel folder
          --theme <file.json>    theme file: which colour each element uses
          --colors <mode>        auto (the default), truecolor or indexed;
                                 indexed keeps the terminal's own colour scheme
          --palette <name|file>  RGB values for the 16 colour slots, used by
                                 truecolor: nt (the default, the Windows NT
                                 console table), vga, campbell, or a JSON file
          --view <1-9>           initial view mode for both panels
                                 1 Brief  2 Medium  3 Full  4 Wide  5 Detailed
          --clock <HH:mm|off>    pin the corner clock to a fixed time, or hide
                                 it, so a rendered frame is reproducible
          --screenshot           render one frame to stdout and exit, without
                                 touching the real console
          --ansi                 with --screenshot, emit SGR colour escapes
          --size <WxH>           with --screenshot, force the virtual screen
                                 size (the default is 120x40)
          --version              print the version and exit
          --help                 print this text and exit
        """;

    /// <summary>The version banner printed for <c>--version</c>.</summary>
    public static string VersionText
    {
        get
        {
            string version =
                typeof(CommandLineArgs).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(CommandLineArgs).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";

            int plus = version.IndexOf('+', StringComparison.Ordinal);
            if (plus > 0)
            {
                version = version[..plus];
            }

            return "Open Stus " + version;
        }
    }

    /// <summary>
    /// Parses a command line. Never throws; the first problem is reported through
    /// <see cref="Error"/>.
    /// </summary>
    /// <param name="args">The raw arguments, exactly as the runtime handed them over.</param>
    /// <returns>The parsed arguments.</returns>
    public static CommandLineArgs Parse(string[]? args)
    {
        var parsed = new CommandLineArgs();
        if (args is null || args.Length == 0)
        {
            return parsed;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i] ?? string.Empty;
            if (raw.Length == 0)
            {
                continue;
            }

            if (!IsOption(raw))
            {
                if (parsed.StartPath is null)
                {
                    parsed.StartPath = raw;
                    continue;
                }

                parsed.Error = "Unexpected argument: " + raw;
                return parsed;
            }

            (string name, string? inlineValue) = SplitOption(raw);

            switch (name)
            {
                case "help" or "h" or "?":
                    parsed.ShowHelp = true;
                    break;

                case "version" or "v":
                    parsed.ShowVersion = true;
                    break;

                case "screenshot":
                    parsed.Screenshot = true;
                    break;

                case "ansi":
                    parsed.Ansi = true;
                    break;

                case "left":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? left))
                    {
                        return parsed;
                    }

                    parsed.LeftPath = left;
                    break;

                case "right":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? right))
                    {
                        return parsed;
                    }

                    parsed.RightPath = right;
                    break;

                case "theme":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? theme))
                    {
                        return parsed;
                    }

                    parsed.ThemePath = theme;
                    break;

                case "colors" or "colours":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? colors))
                    {
                        return parsed;
                    }

                    if (!TryParseColorMode(colors, out ColorMode colorMode))
                    {
                        parsed.Error = "--colors expects auto, truecolor or indexed";
                        return parsed;
                    }

                    parsed.Colors = colorMode;
                    break;

                case "palette":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? palette))
                    {
                        return parsed;
                    }

                    parsed.PalettePath = palette;
                    break;

                case "size":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? size))
                    {
                        return parsed;
                    }

                    if (!TryParseSize(size!, out int w, out int h))
                    {
                        parsed.Error = "--size expects WxH, for example 160x50";
                        return parsed;
                    }

                    parsed.Width = w;
                    parsed.Height = h;
                    break;

                case "view":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? view))
                    {
                        return parsed;
                    }

                    if (!int.TryParse(view, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode) ||
                        mode is < 1 or > 9)
                    {
                        parsed.Error = "--view expects a view mode number between 1 and 9";
                        return parsed;
                    }

                    parsed.ViewMode = mode;
                    break;

                case "clock":
                    if (!TryTakeValue(args, ref i, inlineValue, name, parsed, out string? clock))
                    {
                        return parsed;
                    }

                    if (IsClockOff(clock))
                    {
                        parsed.HideClock = true;
                        parsed.ClockTime = null;
                        break;
                    }

                    if (!TryParseClock(clock, out TimeOnly time))
                    {
                        parsed.Error = "--clock expects a time like 10:06, 10:06:30 or 10:06 AM, or \"off\"";
                        return parsed;
                    }

                    parsed.ClockTime = time;
                    parsed.HideClock = false;
                    break;

                default:
                    parsed.Error = "Unknown option: " + raw;
                    return parsed;
            }
        }

        // Checked after the loop so the two options may be given in either order. An interactive
        // terminal takes its size from the console, and Terminal.Create treats a forced size as
        // headless - so "stus --size 80x25" on its own would build a shell nobody can see, print
        // nothing and exit 0. Say so instead of pretending it worked.
        if (parsed.Error is null && !parsed.ShowHelp && !parsed.ShowVersion &&
            (parsed.Width.HasValue || parsed.Height.HasValue) && !parsed.Screenshot)
        {
            parsed.Error = "--size requires --screenshot";
        }

        return parsed;
    }

    /// <summary>
    /// Parses a <c>WxH</c> screen size.
    /// </summary>
    /// <param name="text">The text, for example <c>160x50</c>. The separator may be <c>x</c> or <c>X</c>.</param>
    /// <param name="width">Receives the width.</param>
    /// <param name="height">Receives the height.</param>
    /// <returns><see langword="true"/> when both parts parsed and are in range.</returns>
    public static bool TryParseSize(string? text, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int split = text.IndexOfAny(['x', 'X', '*']);
        if (split <= 0 || split == text.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(text[..split], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
            !int.TryParse(text[(split + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
        {
            return false;
        }

        if (w is < MinSize or > MaxSize || h is < MinSize or > MaxSize)
        {
            return false;
        }

        width = w;
        height = h;
        return true;
    }

    /// <summary>
    /// Parses a <c>--colors</c> mode.
    /// </summary>
    /// <remarks>
    /// The documented spellings are <c>auto</c>, <c>truecolor</c> and <c>indexed</c>; the handful of
    /// aliases below are the words a user is likely to reach for instead (the British spelling, the
    /// <c>COLORTERM</c> spelling, the colour count). This is also what the <c>colors</c> entry of the
    /// settings file is read with, so the file and the command line cannot drift apart.
    /// </remarks>
    /// <param name="text">The value as written on the command line or in the settings file.</param>
    /// <param name="mode">Receives the mode; <see cref="ColorMode.Auto"/> when parsing failed.</param>
    /// <returns><see langword="true"/> when the text named a mode.</returns>
    public static bool TryParseColorMode(string? text, out ColorMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "auto" or "detect" or "default":
                mode = ColorMode.Auto;
                return true;

            case "truecolor" or "truecolour" or "true-color" or "24bit" or "rgb":
                mode = ColorMode.TrueColor;
                return true;

            case "indexed" or "index" or "16" or "16color" or "ansi16":
                mode = ColorMode.Indexed;
                return true;

            default:
                mode = ColorMode.Auto;
                return false;
        }
    }

    private static bool IsOption(string arg) =>
        arg.StartsWith("--", StringComparison.Ordinal) ||
        (arg.Length > 1 && arg[0] == '-' && !Path.IsPathRooted(arg)) ||
        (OperatingSystem.IsWindows() && arg is "/?" or "/help");

    private static (string Name, string? Value) SplitOption(string arg)
    {
        string body = arg switch
        {
            _ when arg.StartsWith("--", StringComparison.Ordinal) => arg[2..],
            _ when arg.StartsWith('/') => arg[1..],
            _ => arg[1..],
        };

        int eq = body.IndexOf('=', StringComparison.Ordinal);
        return eq < 0
            ? (body.ToLowerInvariant(), null)
            : (body[..eq].ToLowerInvariant(), body[(eq + 1)..]);
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string? inlineValue,
        string name,
        CommandLineArgs parsed,
        out string? value)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = null;
            parsed.Error = "--" + name + " needs a value";
            return false;
        }

        value = args[++index];
        return true;
    }

    /// <summary>The spellings of <c>--clock</c> that mean "draw no clock".</summary>
    private static bool IsClockOff(string? value) =>
        value?.Trim().ToLowerInvariant() is "off" or "none" or "no" or "hide" or "hidden";

    /// <summary>
    /// Parses a <c>--clock</c> time. Accepts 24 hour (<c>10:06</c>, <c>10:06:30</c>) and the
    /// 12 hour spelling the clock itself renders (<c>10:06 AM</c>), always under the invariant
    /// culture so a build machine's locale cannot change what a golden frame looks like.
    /// </summary>
    /// <param name="value">The raw option value.</param>
    /// <param name="time">The parsed time on success.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> was a time.</returns>
    public static bool TryParseClock(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] formats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss", "h:mm tt", "hh:mm tt", "h:mm:ss tt", "hh:mm:ss tt"];
        return TimeOnly.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }
}
