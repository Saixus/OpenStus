using System.Runtime.CompilerServices;
using System.Text;
using OpenStus.Core;
using OpenStus.Rendering;

namespace OpenStus;

/// <summary>Process entry point: argument parsing, the one-frame screenshot mode and the event loop.</summary>
internal static class Program
{
    /// <summary>Exit code for a malformed command line.</summary>
    public const int ExitUsage = 2;

    /// <summary>Exit code for an unexpected failure.</summary>
    public const int ExitFailure = 1;

    /// <summary>
    /// Makes the legacy Windows code pages (windows-125x and friends) available to
    /// <see cref="Encoding.GetEncoding(int)"/>. A stock .NET runtime ships only the Unicode
    /// encodings, so without this <see cref="Text.EncodingDetector.AnsiFallback"/> could never
    /// resolve the operating system's real ANSI code page and every legacy text file would decode
    /// as Latin-1 mojibake. A module initializer rather than a line in <see cref="Main"/> so that
    /// the tests, which never run <see cref="Main"/>, decode exactly like the shipped binary.
    /// Registering is idempotent for the shared <see cref="CodePagesEncodingProvider.Instance"/>.
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterLegacyCodePages() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Runs OpenStus.
    /// </summary>
    /// <param name="args">The command line; see <see cref="CommandLineArgs"/>.</param>
    /// <returns>
    /// <c>0</c> on success, <see cref="ExitUsage"/> for a bad command line and
    /// <see cref="ExitFailure"/> for an unexpected failure.
    /// </returns>
    public static int Main(string[] args)
    {
        CommandLineArgs parsed = CommandLineArgs.Parse(args);

        if (parsed.HasError)
        {
            Console.Error.WriteLine(parsed.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLineArgs.UsageText);
            return ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            Console.WriteLine(CommandLineArgs.UsageText);
            return 0;
        }

        if (parsed.ShowVersion)
        {
            Console.WriteLine(CommandLineArgs.VersionText);
            return 0;
        }

        try
        {
            return parsed.Screenshot ? Screenshot(parsed) : Interactive(parsed);
        }
        catch (Exception e)
        {
            // The terminal has already been restored by Application.Dispose or by the exit hooks,
            // so this lands on the primary screen where the user can actually read it.
            Console.Error.WriteLine("Open Stus failed: " + e.Message);
            return ExitFailure;
        }
    }

    /// <summary>
    /// Renders exactly one frame to stdout and returns, without entering the alternate screen buffer
    /// and without reading a single key. This is the project's automated verification hook.
    /// </summary>
    /// <param name="args">The parsed command line.</param>
    /// <returns>Always <c>0</c>.</returns>
    private static int Screenshot(CommandLineArgs args)
    {
        using var app = Application.Create(args);

        app.Layout();
        app.DrawFrame();

        using Stream stdout = Console.OpenStandardOutput();
        WriteFrame(stdout, RenderFrame(app, args));

        return 0;
    }

    /// <summary>
    /// Renders the one screenshot frame: plain text, or SGR escapes at the colour depth and through
    /// the palette this run resolved.
    /// </summary>
    /// <remarks>
    /// Passing the terminal's own depth and palette rather than the indexed default is what makes
    /// <c>--screenshot --ansi</c> show what the live terminal is actually sent - so <c>--colors</c>
    /// and <c>--palette</c> can be inspected without starting the interactive shell.
    /// </remarks>
    /// <param name="app">The laid out and drawn application.</param>
    /// <param name="args">The parsed command line; only <see cref="CommandLineArgs.Ansi"/> is read.</param>
    /// <returns>The frame text.</returns>
    internal static string RenderFrame(Application app, CommandLineArgs args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        ScreenBuffer buffer = app.Terminal.Buffer;

        return args.Ansi
            ? buffer.RenderAnsi(app.Terminal.ColorDepth, app.Terminal.Palette)
            : buffer.RenderPlainText();
    }

    /// <summary>
    /// Writes one rendered frame as UTF-8, followed by a line break.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Console.Out</c>: that encodes with the ambient
    /// <c>Console.OutputEncoding</c>, and <c>--screenshot</c> is always headless, so
    /// <c>Terminal</c> never gets to switch the console over to UTF-8. On a host still at codepage
    /// 437 or 1252 every box drawing character, the U+2026 ellipsis and the U+2591/U+2588 scroll
    /// bar glyphs would land as <c>'?'</c> - silently breaking the project's own verification hook
    /// and anyone piping a screenshot to a file.
    /// </remarks>
    /// <param name="stdout">The stream to write to; left open for the caller to dispose.</param>
    /// <param name="frame">The rendered frame.</param>
    internal static void WriteFrame(Stream stdout, string frame)
    {
        ArgumentNullException.ThrowIfNull(stdout);

        using var writer = new StreamWriter(
            stdout,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };

        writer.Write(frame);
        writer.Write(Environment.NewLine);
        writer.Flush();
    }

    /// <summary>Runs the interactive shell.</summary>
    /// <param name="args">The parsed command line.</param>
    /// <returns>The application's exit code.</returns>
    private static int Interactive(CommandLineArgs args)
    {
        using var app = Application.Create(args);
        return app.Run();
    }
}
