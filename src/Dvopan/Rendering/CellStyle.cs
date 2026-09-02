namespace Dvopan.Rendering;

/// <summary>
/// A foreground/background colour pair. Dvopan uses the classic 16 colour console
/// palette throughout, exactly like Far Manager, so a style is just two
/// <see cref="ConsoleColor"/> values.
/// </summary>
/// <param name="Fg">Foreground (text) colour.</param>
/// <param name="Bg">Background colour.</param>
public readonly record struct CellStyle(ConsoleColor Fg, ConsoleColor Bg)
{
    /// <summary>Returns this style with a different foreground colour.</summary>
    public CellStyle WithFg(ConsoleColor fg) => new(fg, Bg);

    /// <summary>Returns this style with a different background colour.</summary>
    public CellStyle WithBg(ConsoleColor bg) => new(Fg, bg);

    /// <summary>Gray on Black - the colour of an untouched console.</summary>
    public static CellStyle Default => new(ConsoleColor.Gray, ConsoleColor.Black);

    /// <summary>Renders the style the way theme files spell it, e.g. <c>"Cyan on DarkBlue"</c>.</summary>
    public override string ToString() => $"{Fg} on {Bg}";
}
