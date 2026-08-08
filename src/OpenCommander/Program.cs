using System.Text;
using OpenCommander.Core;
using OpenCommander.Rendering;

namespace OpenCommander;

/// <summary>Process entry point: argument parsing, the one-frame screenshot mode and the event loop.</summary>
internal static class Program
{
    /// <summary>Exit code for a malformed command line.</summary>
    public const int ExitUsage = 2;

    /// <summary>Exit code for an unexpected failure.</summary>
    public const int ExitFailure = 1;

    /// <summary>
    /// Runs Open Commander.
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
            Console.Error.WriteLine("Open Commander failed: " + e.Message);
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

        ScreenBuffer buffer = app.Terminal.Buffer;
        string frame = args.Ansi ? buffer.RenderAnsi() : buffer.RenderPlainText();

        using Stream stdout = Console.OpenStandardOutput();
        WriteFrame(stdout, frame);

        return 0;
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
