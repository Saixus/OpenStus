namespace Dvopan.Rendering;

/// <summary>
/// How many colours the renderer writes to the terminal.
/// </summary>
/// <remarks>
/// The distinction is not decorative. In <see cref="Indexed16"/> the application only names a
/// colour slot and the terminal decides what that slot looks like, so the whole UI inherits the
/// user's colour scheme - under Windows Terminal's default "Campbell" scheme blue is a bright
/// #0037DA and cyan a muted #3A96DD, two neighbouring blues, and the panel washes out to a
/// 4.7:1 contrast ratio. In <see cref="TrueColor"/> the renderer resolves every slot through a
/// <see cref="Palette"/> and writes literal RGB, which pins the classic look at 10.8:1 no matter
/// how the terminal is themed.
/// </remarks>
public enum ColorDepth
{
    /// <summary>
    /// The 16 ANSI colour slots (SGR 30-37/90-97 and 40-47/100-107). The terminal's own scheme
    /// decides the actual RGB.
    /// </summary>
    Indexed16,

    /// <summary>
    /// 24-bit colour (SGR <c>38;2;r;g;b</c> and <c>48;2;r;g;b</c>) resolved through a
    /// <see cref="Palette"/>, so the appearance is independent of the terminal's scheme.
    /// </summary>
    TrueColor,
}

/// <summary>
/// Works out whether the attached terminal can be trusted with 24-bit colour.
/// </summary>
/// <remarks>
/// There is no reliable query for this - terminals advertise their capabilities inconsistently or
/// not at all - so <see cref="Detect(Func{string, string?}, bool, ColorDepth)"/> is a documented
/// chain of heuristics ordered by how much each one can be trusted. The whole chain lives in that
/// one method, with the environment lookup injected, so it can be audited and unit tested without
/// touching the real environment.
/// </remarks>
public static class ColorDepthDetector
{
    /// <summary>
    /// The first Windows retail build whose console host understands <c>38;2;r;g;b</c>
    /// (Windows 10 1703 "Creators Update"; 24-bit landed in Insider build 14931).
    /// </summary>
    private const int TrueColorWindowsBuild = 15063;

    /// <summary>
    /// Terminals that identify themselves through <c>TERM_PROGRAM</c> and are known to render
    /// 24-bit colour correctly.
    /// </summary>
    private static readonly string[] TrueColorTermPrograms =
        ["vscode", "WezTerm", "iTerm.app", "ghostty", "Hyper", "rio"];

    /// <summary>
    /// Detects the colour depth of the real environment. Equivalent to the injectable overload
    /// with the process environment, the real redirection state and <see cref="PlatformDefault"/>.
    /// </summary>
    public static ColorDepth Detect() =>
        Detect(Environment.GetEnvironmentVariable, IsOutputRedirected(), PlatformDefault());

    /// <summary>
    /// Decides the colour depth from a set of environment probes, most trustworthy signal first.
    /// </summary>
    /// <param name="environment">
    /// Environment variable lookup; returns <see langword="null"/> for an unset variable. Injected
    /// so the whole table below is testable.
    /// </param>
    /// <param name="outputRedirected">
    /// <see langword="true"/> when stdout is a pipe or a file rather than a terminal.
    /// </param>
    /// <param name="platformDefault">
    /// The verdict for a terminal that identified itself in no way at all - see
    /// <see cref="PlatformDefault"/>.
    /// </param>
    /// <remarks>
    /// The order is the point of this method, so each step says why it sits where it does:
    /// <list type="number">
    /// <item>
    /// <b>NO_COLOR</b> (present and non-empty, whatever its value - that is the informal standard
    /// at no-color.org). A user who sets it has asked not to have their terminal's colours
    /// overridden, and pinning RGB is exactly such an override, so this wins over every heuristic.
    /// </item>
    /// <item>
    /// <b>Redirected stdout.</b> A pipe or a file has no palette to fight with, so there is
    /// nothing to pin; staying indexed keeps captured output small and diffable.
    /// </item>
    /// <item>
    /// <b>COLORTERM</b> equal to <c>truecolor</c> or <c>24bit</c>. The one deliberate,
    /// cross-platform advertisement of 24-bit support (VTE, Konsole, iTerm2, kitty, alacritty,
    /// WezTerm). It is not forwarded over ssh or sudo, so its absence proves nothing - which is
    /// why the chain continues rather than stopping here.
    /// </item>
    /// <item>
    /// <b>WT_SESSION.</b> Windows Terminal has rendered 24-bit colour since its first release but
    /// still does not set COLORTERM, so this is load bearing rather than belt-and-braces. The
    /// variable is undocumented; it is stable in practice but is not a contract.
    /// </item>
    /// <item>
    /// <b>ConEmuANSI=ON.</b> ConEmu (and cmder on top of it) with ANSI processing enabled.
    /// </item>
    /// <item>
    /// <b>TERM_PROGRAM.</b> Self-identifying terminals. <c>Apple_Terminal</c> is an explicit
    /// negative, not merely an unknown: macOS Terminal.app is 256-colour only and has never
    /// implemented 24-bit, so it must not fall through to the optimistic steps below.
    /// </item>
    /// <item>
    /// <b>TERM.</b> Only <c>truecolor</c> and <c>direct</c> imply 24-bit. <c>256color</c>
    /// deliberately does not - it asserts 256 indexed colours, and <c>xterm-256color</c> is
    /// exactly what Terminal.app and older mintty report. <c>dumb</c> means no capabilities.
    /// </item>
    /// <item>
    /// <b>The platform default.</b> A last-resort guess, made safe by the console host degrading
    /// gracefully: a down-level conhost parses <c>38;2;r;g;b</c> correctly and snaps the RGB to the
    /// nearest of its 16 colours instead of printing stray digits, so the worst case is the colour
    /// that would have been used anyway.
    /// </item>
    /// </list>
    /// </remarks>
    public static ColorDepth Detect(
        Func<string, string?> environment,
        bool outputRedirected,
        ColorDepth platformDefault)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // 1. NO_COLOR: the user's terminal theme wins, so stay with the indexed slots.
        if (!string.IsNullOrEmpty(environment("NO_COLOR")))
        {
            return ColorDepth.Indexed16;
        }

        // 2. Not a terminal at all.
        if (outputRedirected)
        {
            return ColorDepth.Indexed16;
        }

        // 3. The one intentional, cross-platform capability advertisement.
        string? colorTerm = environment("COLORTERM");
        if (Matches(colorTerm, "truecolor") || Matches(colorTerm, "24bit"))
        {
            return ColorDepth.TrueColor;
        }

        // 4. Windows Terminal: always 24-bit, never sets COLORTERM.
        if (!string.IsNullOrEmpty(environment("WT_SESSION")))
        {
            return ColorDepth.TrueColor;
        }

        // 5. ConEmu / cmder with ANSI processing on.
        if (Matches(environment("ConEmuANSI"), "ON"))
        {
            return ColorDepth.TrueColor;
        }

        // 6. Self-identifying terminals, including one known negative.
        string? termProgram = environment("TERM_PROGRAM");
        if (!string.IsNullOrEmpty(termProgram))
        {
            if (Matches(termProgram, "Apple_Terminal"))
            {
                return ColorDepth.Indexed16; // 256 colours only, never 24-bit
            }

            foreach (string known in TrueColorTermPrograms)
            {
                if (Matches(termProgram, known))
                {
                    return ColorDepth.TrueColor;
                }
            }
        }

        // 7. TERM: a weak hint, and "256color" is not one of them.
        string term = environment("TERM") ?? string.Empty;
        if (term.Contains("truecolor", StringComparison.OrdinalIgnoreCase)
            || term.Contains("direct", StringComparison.OrdinalIgnoreCase))
        {
            return ColorDepth.TrueColor;
        }

        if (Matches(term, "dumb"))
        {
            return ColorDepth.Indexed16;
        }

        // 8. Nothing identified itself.
        return platformDefault;
    }

    /// <summary>
    /// The verdict for a terminal that advertises nothing: 24-bit on a Windows console new enough
    /// to understand it, indexed everywhere else.
    /// </summary>
    /// <remarks>
    /// Being optimistic on Windows is what actually fixes the reported bug, because Windows
    /// Terminal announces nothing at all; being conservative on an unknown Unix terminal costs
    /// only the classic look, never a garbled screen.
    /// </remarks>
    public static ColorDepth PlatformDefault() =>
        OperatingSystem.IsWindows()
            ? (OperatingSystem.IsWindowsVersionAtLeast(10, 0, TrueColorWindowsBuild)
                ? ColorDepth.TrueColor
                : ColorDepth.Indexed16)
            : ColorDepth.Indexed16;

    private static bool Matches(string? value, string expected) =>
        value is not null && value.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsOutputRedirected()
    {
        try
        {
            return Console.IsOutputRedirected;
        }
        catch
        {
            return true;
        }
    }
}
