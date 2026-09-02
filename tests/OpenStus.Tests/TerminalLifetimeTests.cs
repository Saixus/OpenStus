using System.Runtime.InteropServices;
using System.Text;
using OpenStus.Core;
using OpenStus.Rendering;
using OpenStus.Theming;

namespace OpenStus.Tests;

/// <summary>
/// Checks that the console is always handed back on the way out - to the operating system at exit,
/// and to a child command mid-session - and that the one-frame <c>--screenshot</c> output does not
/// depend on whatever codepage the host console happens to be sitting on.
/// </summary>
public class TerminalLifetimeTests
{
    /// <summary>
    /// Every non-ASCII glyph the UI draws: the two box drawing sets, the scroll bar track and
    /// thumb, and the truncation ellipsis.
    /// </summary>
    private const string NonAsciiGlyphs = "┌─┐│└┘├┤┬┴┼╔═╗║╚╝░█…";

    // ------------------------------------------------------------ interrupt hooks

    [Fact]
    public void TheInterruptHooksCoverSigintSigtermAndSighup()
    {
        // Asserted on every platform, so a Windows-only CI run still pins the intent.
        Assert.Equal(
            new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP },
            Terminal.InterruptSignals);
    }

    [Fact]
    public void InstallingTheInterruptHooksIsIdempotent()
    {
        Terminal.InstallInterruptHooks();
        Assert.True(Terminal.InterruptHooksInstalled);

        int hooked = Terminal.PosixSignalHookCount;

        Terminal.InstallInterruptHooks();
        Terminal.InstallInterruptHooks();

        // Several terminals over the life of one process must not stack registrations.
        Assert.Equal(hooked, Terminal.PosixSignalHookCount);
    }

    [Fact]
    public void EveryInterruptSignalIsRegisteredOnUnixAndNoneOnWindows()
    {
        Terminal.InstallInterruptHooks();

        // Windows routes Ctrl+C, Ctrl+Break and the close button through SetConsoleCtrlHandler
        // instead, so there is deliberately nothing to register there.
        int expected = OperatingSystem.IsWindows() ? 0 : Terminal.InterruptSignals.Length;
        Assert.Equal(expected, Terminal.PosixSignalHookCount);
    }

    [Fact]
    public void HandlingAnInterruptWithNoLiveTerminalIsHarmless()
    {
        // The handler runs from a signal context: throwing there would take the process down in a
        // worse state than the one it is trying to repair.
        Terminal.HandleInterrupt();
        Terminal.HandleInterrupt();
    }

    // ------------------------------------------------------------ restoration

    [Fact]
    public void DisposingATerminalTwiceIsSafe()
    {
        var terminal = Terminal.Create(80, 25);

        terminal.Dispose();
        terminal.Dispose();
    }

    [Fact]
    public void AForcedSizeTerminalIsHeadlessAndNeverTouchesTheConsole()
    {
        using Terminal terminal = Terminal.Create(80, 25);

        Assert.True(terminal.IsHeadless);
        Assert.Equal(80, terminal.Width);
        Assert.Equal(25, terminal.Height);

        // A headless Render() builds the frame and drops it; nothing reaches stdout, and a forced
        // size never follows the console.
        terminal.Render();
        Assert.False(terminal.SyncSize());
    }

    // ------------------------------------------------------------ child command handover

    [Fact]
    public void SuspendingAndRestoringTheInputModeIsSafeInAnyOrderWhenHeadless()
    {
        using Terminal terminal = Terminal.Create(80, 25);

        // Headless has no console input buffer to hand over, so every call is a no-op - and it
        // must be a silent one, because CommandExecutor brackets every child with this pair
        // unconditionally.
        terminal.SuspendConsoleInputMode();
        terminal.RestoreConsoleInputMode();
        terminal.RestoreConsoleInputMode();
        terminal.SuspendConsoleInputMode();
        terminal.SuspendConsoleInputMode();
        terminal.RestoreConsoleInputMode();
    }

    // ------------------------------------------------------------ cursor visibility

    [Fact]
    public void AHideCursorTransitionSurvivesAZeroCellDiff()
    {
        using Terminal terminal = Terminal.Create(8, 2);

        terminal.SetCursor(1, 1, visible: true);
        Assert.Contains("\u001b[?25h", terminal.BuildFrameText(), StringComparison.Ordinal);

        // No cell changed, but the hide must still go out: discarding the whole frame here would
        // leave the hardware cursor blinking on screen after SetCursor(..., false).
        terminal.SetCursor(1, 1, visible: false);
        string frame = terminal.BuildFrameText();
        Assert.Contains("\u001b[?25l", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[?25h", frame, StringComparison.Ordinal);

        // Once the hidden state has been emitted, an unchanged frame collapses to nothing again.
        Assert.Equal(string.Empty, terminal.BuildFrameText());
    }

    // ------------------------------------------------------------ screenshot encoding

    [Fact]
    public void TheScreenshotWriterEmitsUtf8WhateverTheConsoleEncodingIs()
    {
        string frame = "┌" + new string('─', 6) + "┐\n│ " + NonAsciiGlyphs + " │";
        string expected = frame + Environment.NewLine;

        using var stream = new MemoryStream();
        Program.WriteFrame(stream, frame);

        byte[] bytes = stream.ToArray();

        Assert.Equal(new UTF8Encoding(false).GetBytes(expected), bytes);
        Assert.Equal(expected, new UTF8Encoding(false).GetString(bytes));
    }

    [Fact]
    public void TheScreenshotWriterIgnoresTheAmbientSingleByteEncoding()
    {
        string frame = NonAsciiGlyphs;

        using var stream = new MemoryStream();
        Program.WriteFrame(stream, frame);

        byte[] bytes = stream.ToArray();

        // Console.Out encodes with Console.OutputEncoding, so on a host still on a single-byte
        // console codepage the frame goes out mangled: Latin1 stands in for any of them here, and
        // the legacy DOS/Windows pages are no better - they best-fit substitute instead, which is
        // quieter and just as lossy. Either way the bytes stop being the UTF-8 a screenshot
        // consumer decodes.
        byte[] degraded = Encoding.Latin1.GetBytes(frame + Environment.NewLine);

        Assert.Contains((byte)'?', degraded);    // the ambient path really would lose the glyphs
        Assert.DoesNotContain((byte)'?', bytes); // the explicit UTF-8 writer keeps every one
        Assert.NotEqual(degraded, bytes);
    }

    [Fact]
    public void TheScreenshotWriterAddsOneLineBreakAndNoByteOrderMark()
    {
        using var stream = new MemoryStream();
        Program.WriteFrame(stream, "abc");

        byte[] bytes = stream.ToArray();

        Assert.Equal(Encoding.UTF8.GetBytes("abc" + Environment.NewLine), bytes);
        Assert.NotEqual((byte)0xEF, bytes[0]);
    }

    [Fact]
    public void TheScreenshotWriterLeavesTheStreamOpenForTheCaller()
    {
        using var stream = new MemoryStream();

        Program.WriteFrame(stream, "one");
        Program.WriteFrame(stream, "two");

        Assert.Equal(
            "one" + Environment.NewLine + "two" + Environment.NewLine,
            Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void ARenderedFrameRoundTripsThroughUtf8BytesUnchanged()
    {
        using var tree = new TempTree("utf8");
        using Terminal terminal = Terminal.Create(100, 30);

        using var app = new Application(
            terminal,
            new Settings { ShowClock = false },
            Theme.Classic(),
            input: null);

        app.Initialize(new CommandLineArgs { LeftPath = tree.Root, RightPath = tree.Root });
        app.Layout();
        app.DrawFrame();

        string frame = terminal.Buffer.RenderPlainText();

        // If the real frame were pure ASCII this test would prove nothing.
        Assert.Contains('─', frame);

        using var stream = new MemoryStream();
        Program.WriteFrame(stream, frame);

        Assert.Equal(frame + Environment.NewLine, new UTF8Encoding(false).GetString(stream.ToArray()));
    }

    /// <summary>A throwaway folder the shell can be pointed at.</summary>
    private sealed class TempTree : IDisposable
    {
        public TempTree(string name)
        {
            Root = Path.Combine(Path.GetTempPath(), "oc-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "sub"));
            File.WriteAllText(Path.Combine(Root, "readme.md"), "# hello\n");
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
