using System.Runtime.InteropServices;
using System.Text;

namespace Dvopan.Rendering;

/// <summary>
/// Owns the physical console: virtual terminal setup, the alternate screen buffer, the console
/// modes, and the diff-based flush of a <see cref="ScreenBuffer"/> to stdout.
/// </summary>
/// <remarks>
/// <para>
/// Everything is written as ANSI/VT escape sequences through a single buffered
/// <see cref="StreamWriter"/>; the <see cref="Console"/> class's own cursor and colour state is
/// never touched, because mixing the two desynchronises the cached state from the terminal.
/// </para>
/// <para>
/// The terminal state is restored from <see cref="Dispose"/>, from <c>ProcessExit</c>, from an
/// unhandled exception, from <c>Console.CancelKeyPress</c>, and from the platform's interrupt
/// path: the console control handler on Windows, which also covers the window close button, and
/// <c>SIGINT</c>, <c>SIGTERM</c> and <c>SIGHUP</c> registrations everywhere else. None of those
/// handlers cancel the interrupt, so Ctrl+C still quits - it just quits with the primary screen
/// buffer, the cursor and autowrap put back. Restoration is idempotent and never throws, which is
/// what makes it safe to run from a signal context.
/// </para>
/// </remarks>
public sealed class Terminal : IDisposable
{
    private const int DefaultWidth = 120;
    private const int DefaultHeight = 40;

    /// <summary>Cheaper to overwrite this many unchanged cells than to emit a cursor move.</summary>
    private const int GapTolerance = 5;

    /// <summary>The escape character, for the few callers that compose raw VT for the user screen.</summary>
    internal const string Esc = "\u001b";
    private const string Prologue = Esc + "[?1049h" + Esc + "[?25l" + Esc + "[?7l" + Esc + "[2J" + Esc + "[H";

    // End any open synchronized update, turn mouse reporting off, autowrap back on, reset SGR,
    // show the cursor, and only then leave the alternate buffer.
    private const string Epilogue =
        Esc + "[?2026l" +
        Esc + "[?1006l" + Esc + "[?1015l" + Esc + "[?1003l" + Esc + "[?1002l" + Esc + "[?1000l" +
        Esc + "[?7h" + Esc + "[0m" + Esc + "[?25h" + Esc + "[?1049l";

    private readonly object _sync = new();
    private readonly bool _forcedSize;
    private readonly StreamWriter? _writer;
    private readonly Stream? _stdout;
    private readonly StringBuilder _frame = new(16 * 1024);

    private ScreenBuffer _buffer;
    private Palette _palette;
    private Cell[] _front;
    private int _frontWidth;
    private int _frontHeight;
    private bool _forceFull = true;
    private bool _altScreen;
    private int _restored;

    private int _cursorX;
    private int _cursorY;
    private bool _cursorVisible;
    private bool _lastEmittedCursorVisible;

    // The raw input mode captured by SuspendConsoleInputMode, so RestoreConsoleInputMode can put
    // back exactly what the input backend had established - mouse reporting bits included.
    private uint _suspendedRawInMode;
    private bool _inputModeSuspended;

    private Encoding? _savedOutputEncoding;

    // Windows console state captured at startup so Dispose can put it back exactly.
    private readonly nint _hOut;
    private readonly nint _hIn;
    private readonly uint _savedOutMode;
    private readonly uint _savedInMode;
    private readonly bool _haveOutMode;
    private readonly bool _haveInMode;

    private static NativeMethods.ConsoleCtrlHandler? _ctrlHandler; // rooted against the GC
    private static PosixSignalRegistration[]? _signalHooks;        // rooted against the GC
    private static readonly object HookSync = new();
    private static bool _interruptHooksInstalled;
    private static Terminal? _live;

    /// <summary>
    /// The signals whose default disposition would otherwise kill the process with the alternate
    /// screen buffer still up. Registered on non-Windows only; Windows routes the same events
    /// through <see cref="NativeMethods.SetConsoleCtrlHandler"/>.
    /// </summary>
    internal static readonly PosixSignal[] InterruptSignals =
        [PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP];

    private Terminal(
        bool headless,
        bool forcedSize,
        int width,
        int height,
        ColorDepth depth,
        Palette palette,
        nint hOut,
        nint hIn,
        uint savedOutMode,
        bool haveOutMode,
        uint savedInMode,
        bool haveInMode)
    {
        IsHeadless = headless;
        _forcedSize = forcedSize;
        ColorDepth = depth;
        _palette = palette;
        _hOut = hOut;
        _hIn = hIn;
        _savedOutMode = savedOutMode;
        _haveOutMode = haveOutMode;
        _savedInMode = savedInMode;
        _haveInMode = haveInMode;

        _buffer = new ScreenBuffer(width, height);
        _frontWidth = _buffer.Width;
        _frontHeight = _buffer.Height;
        _front = new Cell[_frontWidth * _frontHeight];

        if (headless)
        {
            return;
        }

        _stdout = Console.OpenStandardOutput();
        _writer = new StreamWriter(_stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 64 * 1024)
        {
            AutoFlush = false,
        };
    }

    /// <summary>
    /// Creates and initialises the terminal. When <paramref name="forcedWidth"/> and
    /// <paramref name="forcedHeight"/> are given - or stdout is redirected - the terminal runs
    /// headless: nothing is written to the console, the buffer is only kept in memory. That is
    /// the mode <c>--screenshot</c> uses.
    /// </summary>
    /// <param name="forcedWidth">Width in cells, or <see langword="null"/> to use the console's.</param>
    /// <param name="forcedHeight">Height in cells, or <see langword="null"/> to use the console's.</param>
    /// <param name="forcedDepth">
    /// Overrides the colour depth. <see langword="null"/> auto-detects with
    /// <see cref="ColorDepthDetector.Detect(Func{string, string?}, bool, ColorDepth)"/> - except
    /// for a headless terminal, which stays <see cref="ColorDepth.Indexed16"/> so that screenshots
    /// are reproducible on any machine rather than varying with the environment that took them.
    /// </param>
    /// <param name="palette">
    /// The RGB table used in <see cref="ColorDepth.TrueColor"/>; defaults to
    /// <see cref="Palette.ClassicVga"/>.
    /// </param>
    public static Terminal Create(
        int? forcedWidth = null,
        int? forcedHeight = null,
        ColorDepth? forcedDepth = null,
        Palette? palette = null)
    {
        bool forcedSize = forcedWidth.HasValue || forcedHeight.HasValue;
        bool redirected = SafeIsOutputRedirected();
        bool headless = forcedSize || redirected;

        (int consoleW, int consoleH) = ReadConsoleSize();
        int width = Math.Max(1, forcedWidth ?? (forcedSize ? DefaultWidth : consoleW));
        int height = Math.Max(1, forcedHeight ?? (forcedSize ? DefaultHeight : consoleH));

        ColorDepth depth = forcedDepth ?? (headless
            ? ColorDepth.Indexed16
            : ColorDepthDetector.Detect(Environment.GetEnvironmentVariable, redirected, ColorDepthDetector.PlatformDefault()));

        nint hOut = 0;
        nint hIn = 0;
        uint savedOut = 0;
        uint savedIn = 0;
        bool haveOut = false;
        bool haveIn = false;

        if (!headless && OperatingSystem.IsWindows())
        {
            (hOut, hIn, savedOut, haveOut, savedIn, haveIn) = EnableWindowsVirtualTerminal();
        }

        var terminal = new Terminal(
            headless,
            forcedSize,
            width,
            height,
            depth,
            palette ?? Palette.ClassicVga,
            hOut,
            hIn,
            savedOut,
            haveOut,
            savedIn,
            haveIn);

        if (!headless)
        {
            terminal.TrySetUtf8OutputEncoding();
            terminal.EnterAlternateScreen();
            terminal.InstallExitHooks();
        }

        return terminal;
    }

    /// <summary>The back buffer every component draws into.</summary>
    public ScreenBuffer Buffer => _buffer;

    /// <summary>Current width in cells.</summary>
    public int Width => _buffer.Width;

    /// <summary>Current height in cells.</summary>
    public int Height => _buffer.Height;

    /// <summary>
    /// <see langword="true"/> when nothing is written to the console and the buffer is only kept
    /// in memory (forced size, or stdout redirected).
    /// </summary>
    public bool IsHeadless { get; }

    /// <summary>
    /// How colours are written: the terminal's own 16 slots, or literal RGB resolved through
    /// <see cref="Palette"/>. Fixed for the life of the terminal - it is decided once by
    /// <see cref="Create"/>, from the caller's override or from
    /// <see cref="ColorDepthDetector"/>.
    /// </summary>
    public ColorDepth ColorDepth { get; }

    /// <summary>
    /// The RGB table used in <see cref="Rendering.ColorDepth.TrueColor"/>. Assigning a new palette
    /// repaints the whole screen, so a palette can be swapped at run time. Ignored entirely in
    /// <see cref="Rendering.ColorDepth.Indexed16"/>, where the terminal's own scheme is in charge.
    /// </summary>
    public Palette Palette
    {
        get => _palette;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_sync)
            {
                if (ReferenceEquals(_palette, value))
                {
                    return;
                }

                _palette = value;
                _forceFull = true;
            }
        }
    }

    /// <summary>
    /// Re-reads the console size and resizes <see cref="Buffer"/> when it changed. Cheap enough
    /// to call on every event loop tick; a headless or force-sized terminal never resizes.
    /// </summary>
    /// <returns><see langword="true"/> when the size actually changed.</returns>
    public bool SyncSize()
    {
        if (IsHeadless || _forcedSize)
        {
            return false;
        }

        (int w, int h) = ReadConsoleSize();
        if (w == _buffer.Width && h == _buffer.Height)
        {
            return false;
        }

        lock (_sync)
        {
            _buffer.Resize(w, h);
            _frontWidth = _buffer.Width;
            _frontHeight = _buffer.Height;
            _front = new Cell[_frontWidth * _frontHeight];
            _forceFull = true;
        }

        return true;
    }

    /// <summary>Forces the next <see cref="Render"/> to repaint every cell.</summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            _forceFull = true;
        }
    }

    /// <summary>
    /// <see langword="true"/> while the alternate screen buffer is up - the normal state. It goes
    /// down around a child command and while Ctrl+O shows the user screen.
    /// </summary>
    public bool OnAlternateScreen => _altScreen;

    /// <summary>
    /// Leaves the alternate screen buffer so the primary one - the user screen, with the output of
    /// every command run so far - becomes visible. This is what Ctrl+O shows: the point of the key
    /// is that command output survives underneath the panels. While the user screen is up
    /// the caller must not <see cref="Render"/>; a frame written now would scribble over the very
    /// output being shown. A no-op when headless or already on the primary buffer.
    /// </summary>
    public void ShowUserScreen()
    {
        lock (_sync)
        {
            if (_writer is null || !_altScreen)
            {
                return;
            }

            // End any open synchronized update, put autowrap and the colours back, show the
            // cursor, and only then switch buffers - the same order the epilogue uses.
            _writer.Write(Esc + "[?2026l" + Esc + "[?7h" + Esc + "[0m" + Esc + "[?25h" + Esc + "[?1049l");
            _writer.Flush();
            _altScreen = false;
        }
    }

    /// <summary>
    /// Returns from the user screen to the alternate buffer and forces the next
    /// <see cref="Render"/> to repaint everything. A no-op when headless or already up.
    /// </summary>
    public void ShowPanelsScreen()
    {
        lock (_sync)
        {
            if (_writer is null || _altScreen)
            {
                return;
            }

            _writer.Write(Prologue);
            _writer.Flush();
            _altScreen = true;
            _forceFull = true;
        }
    }

    /// <summary>
    /// Writes raw text straight to the user screen, bypassing the cell buffer - the Ctrl+O prompt
    /// echo uses this. Ignored while the alternate buffer is up, where every write must go through
    /// the diff.
    /// </summary>
    /// <param name="text">The text, VT escapes allowed.</param>
    public void WriteUserScreen(string text)
    {
        lock (_sync)
        {
            if (_writer is null || _altScreen || string.IsNullOrEmpty(text))
            {
                return;
            }

            _writer.Write(text);
            _writer.Flush();
        }
    }

    /// <summary>
    /// Logs one line on the user screen the way a shell prints a prompt: on the bottom row - the
    /// attributes reset first, so a child that quit with a colour still open cannot paint the
    /// cleared remainder - then a newline, which scrolls the line up and leaves the cursor on a
    /// fresh bottom row for whatever prints next. Ignored while the alternate buffer is up.
    /// </summary>
    /// <param name="text">The line, VT colour escapes allowed; it is reset at the end.</param>
    public void WriteUserScreenLine(string text)
    {
        WriteUserScreen(
            Esc + "[" + Height.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";1H" +
            Esc + "[0m" + Esc + "[K" + text + Esc + "[0m\r\n");
    }

    /// <summary>Positions the hardware cursor, applied at the end of the next <see cref="Render"/>.</summary>
    public void SetCursor(int x, int y, bool visible)
    {
        _cursorX = x;
        _cursorY = y;
        _cursorVisible = visible;
    }

    /// <summary>
    /// Puts the console input buffer back into the cooked mode captured at startup, for the
    /// benefit of a child process about to run on this console. The input mode is a property of
    /// the shared input buffer that every child inherits; left raw, the child gets no echo, no
    /// line editing, and a Ctrl+C that queues as an ordinary key instead of interrupting it.
    /// Pair with <see cref="RestoreConsoleInputMode"/> once the child has exited. A no-op when
    /// headless, off Windows, when the startup mode was never captured, or when already suspended.
    /// </summary>
    public void SuspendConsoleInputMode()
    {
        if (IsHeadless || !OperatingSystem.IsWindows() || !_haveInMode || _inputModeSuspended)
        {
            return;
        }

        try
        {
            // The mode in force now is not _savedInMode: the input backend layered its own raw
            // mode on top after startup. Capture whatever is current so the restore re-applies
            // exactly that.
            if (NativeMethods.GetConsoleMode(_hIn, out uint current))
            {
                _suspendedRawInMode = current;
                _inputModeSuspended = NativeMethods.SetConsoleMode(_hIn, _savedInMode);
            }
        }
        catch
        {
            // Not a real console; the child takes whatever mode there is.
        }
    }

    /// <summary>
    /// Re-applies the raw input mode captured by <see cref="SuspendConsoleInputMode"/>. A no-op
    /// when nothing is suspended, so the pair is safe to call unconditionally around a child.
    /// </summary>
    public void RestoreConsoleInputMode()
    {
        if (!_inputModeSuspended)
        {
            return;
        }

        _inputModeSuspended = false;

        try
        {
            NativeMethods.SetConsoleMode(_hIn, _suspendedRawInMode);
        }
        catch
        {
            // The console went away while the child ran; there is nothing to put back.
        }
    }

    /// <summary>
    /// Flushes <see cref="Buffer"/> to the console, emitting only the cells that changed since
    /// the previous frame, an SGR sequence only when the colour pair changes, and a cursor move
    /// only when there is a gap worth skipping. The whole frame goes out in one write, wrapped in
    /// a synchronized update so conforming terminals present it atomically.
    /// </summary>
    public void Render()
    {
        lock (_sync)
        {
            // The frame is built even when headless: it keeps the shadow buffer honest and means
            // the diff is exercised by exactly the same code path in tests and in production.
            BuildFrame();

            if (IsHeadless || _writer is null || _frame.Length == 0)
            {
                _frame.Clear();
                return;
            }

            _writer.Write(_frame);
            _writer.Flush();
            _frame.Clear();
        }
    }

    /// <summary>
    /// Builds the next frame and returns the escape sequence that would be written, instead of
    /// writing it. Exists so the diff renderer can be tested without a real console.
    /// </summary>
    internal string BuildFrameText()
    {
        lock (_sync)
        {
            BuildFrame();
            string text = _frame.ToString();
            _frame.Clear();
            return text;
        }
    }

    private void BuildFrame()
    {
        var cells = _buffer.Cells;
        int w = _buffer.Width;
        int h = _buffer.Height;

        if (_frontWidth != w || _frontHeight != h)
        {
            _frontWidth = w;
            _frontHeight = h;
            _front = new Cell[w * h];
            _forceFull = true;
        }

        var sb = _frame;
        sb.Clear();
        sb.Append(Esc).Append("[?2026h").Append(Esc).Append("[?25l");
        int prefix = sb.Length;
        bool full = _forceFull;

        CellStyle lastStyle = default;
        bool haveStyle = false;
        int vRow = -1;
        int vCol = -1;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int x = 0;
            while (x < w)
            {
                if (!full && cells[row + x].SameAs(in _front[row + x]))
                {
                    x++;
                    continue;
                }

                if (vRow != y || vCol != x)
                {
                    if (vRow == y && x > vCol && x - vCol <= 3)
                    {
                        sb.Append(Esc).Append('[').Append(x - vCol).Append('C');
                    }
                    else
                    {
                        sb.Append(Esc).Append('[').Append(y + 1).Append(';').Append(x + 1).Append('H');
                    }

                    vRow = y;
                    vCol = x;
                }

                int clean = 0;
                while (x < w && clean <= GapTolerance)
                {
                    ref var back = ref cells[row + x];
                    clean = !full && back.SameAs(in _front[row + x]) ? clean + 1 : 0;

                    if (!haveStyle || back.Style != lastStyle)
                    {
                        // The only per-frame cost of true colour: this runs on style changes
                        // only, which the diff has already decided are worth writing bytes for.
                        ScreenBuffer.AppendSgr(sb, back.Style, ColorDepth, _palette);
                        lastStyle = back.Style;
                        haveStyle = true;
                    }

                    sb.Append(back.Glyph);
                    _front[row + x] = back;
                    x++;
                    vCol++;
                }
            }
        }

        bool painted = sb.Length > prefix;
        _forceFull = false;

        // An empty frame is normally discarded whole - but the prefix's "hide the cursor" is
        // load-bearing when the previous frame ended with the cursor shown: dropping it would
        // leave the hardware cursor blinking after SetCursor(..., false) that happened to
        // coincide with a zero-cell diff.
        if (!painted && !_cursorVisible && !_lastEmittedCursorVisible)
        {
            sb.Clear();
            return;
        }

        if (_cursorVisible)
        {
            int cx = Math.Clamp(_cursorX, 0, w - 1);
            int cy = Math.Clamp(_cursorY, 0, h - 1);
            sb.Append(Esc).Append('[').Append(cy + 1).Append(';').Append(cx + 1).Append('H');
            sb.Append(Esc).Append("[?25h");
        }

        // Every emitted frame ends with the cursor in a known state: shown by the tail above, or
        // hidden by the prefix. That is what the discard test above relies on.
        _lastEmittedCursorVisible = _cursorVisible;

        sb.Append(Esc).Append("[?2026l");
    }

    /// <summary>
    /// Restores the primary screen buffer, the cursor, colours and console modes. Safe to call
    /// any number of times and from any thread; never throws.
    /// </summary>
    public void Dispose()
    {
        Restore();
        GC.SuppressFinalize(this);
    }

    private void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        // Raw byte write, not the buffered writer: this also runs from the console control
        // handler, where the console may already be partially torn down.
        try
        {
            if (_altScreen && _stdout is not null)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(Epilogue);
                _stdout.Write(bytes, 0, bytes.Length);
                _stdout.Flush();
            }
        }
        catch
        {
            // Nothing useful to do while the process is going away.
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (_haveOutMode)
                {
                    NativeMethods.SetConsoleMode(_hOut, _savedOutMode);
                }

                if (_haveInMode)
                {
                    NativeMethods.SetConsoleMode(_hIn, _savedInMode);
                }
            }
        }
        catch
        {
            // Ignored.
        }

        try
        {
            if (_savedOutputEncoding is not null)
            {
                Console.OutputEncoding = _savedOutputEncoding;
            }
        }
        catch
        {
            // Ignored.
        }

        _altScreen = false;
        if (ReferenceEquals(_live, this))
        {
            _live = null;
        }
    }

    private void EnterAlternateScreen()
    {
        if (_writer is null)
        {
            return;
        }

        _writer.Write(Prologue);
        _writer.Flush();
        _altScreen = true;
        _forceFull = true;
    }

    private void TrySetUtf8OutputEncoding()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            _savedOutputEncoding = Console.OutputEncoding;
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch
        {
            _savedOutputEncoding = null;
        }
    }

    private void InstallExitHooks()
    {
        _live = this;

        AppDomain.CurrentDomain.ProcessExit += static (_, _) => _live?.Restore();
        AppDomain.CurrentDomain.UnhandledException += static (_, _) => _live?.Restore();

        // Before the Windows-only branch: on Unix nothing else covers an interrupt. The portable
        // input backend leaves ISIG on, so Ctrl+C raises SIGINT; without a handler the runtime
        // restores the default disposition and re-raises, the process is signal-terminated,
        // managed shutdown never runs, ProcessExit never fires - and the user is left staring at
        // the alternate buffer with a hidden cursor, needing a blind "reset".
        InstallInterruptHooks();

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // Must live in a static field or the GC collects it and the callback faults.
            _ctrlHandler = static type =>
            {
                _live?.Restore();
                return 0; // let the default handler terminate the process
            };
            NativeMethods.SetConsoleCtrlHandler(_ctrlHandler, true);
        }
        catch
        {
            _ctrlHandler = null;
        }
    }

    /// <summary>
    /// Subscribes the cross-platform interrupt paths that put the terminal back:
    /// <c>Console.CancelKeyPress</c> everywhere, plus a <see cref="PosixSignalRegistration"/> per
    /// <see cref="InterruptSignals"/> entry on non-Windows. No handler sets <c>Cancel</c>, so the
    /// default disposition still terminates the process - the semantics stay "Ctrl+C quits", the
    /// console is simply handed back on the way out. Idempotent: several terminals over the life
    /// of one process share one set of registrations.
    /// </summary>
    internal static void InstallInterruptHooks()
    {
        lock (HookSync)
        {
            if (_interruptHooksInstalled)
            {
                return;
            }

            _interruptHooksInstalled = true;

            try
            {
                Console.CancelKeyPress += static (_, _) => HandleInterrupt();
            }
            catch
            {
                // No console to hook; the signal registrations below may still work.
            }

            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                // Rooted in a static field, exactly like _ctrlHandler: collecting the
                // registration unhooks the signal.
                var hooks = new PosixSignalRegistration[InterruptSignals.Length];
                for (int i = 0; i < InterruptSignals.Length; i++)
                {
                    hooks[i] = PosixSignalRegistration.Create(InterruptSignals[i], static _ => HandleInterrupt());
                }

                _signalHooks = hooks;
            }
            catch
            {
                _signalHooks = null;
            }
        }
    }

    /// <summary>
    /// Restores the live terminal, if there is one. Runs from a signal context, so it must never
    /// throw and must not allocate anything interesting - <see cref="Restore"/> satisfies both.
    /// </summary>
    internal static void HandleInterrupt() => _live?.Restore();

    /// <summary><see langword="true"/> once <see cref="InstallInterruptHooks"/> has run.</summary>
    internal static bool InterruptHooksInstalled
    {
        get
        {
            lock (HookSync)
            {
                return _interruptHooksInstalled;
            }
        }
    }

    /// <summary>How many POSIX signals are currently hooked; always <c>0</c> on Windows.</summary>
    internal static int PosixSignalHookCount
    {
        get
        {
            lock (HookSync)
            {
                return _signalHooks?.Length ?? 0;
            }
        }
    }

    private static bool SafeIsOutputRedirected()
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

    private static (int Width, int Height) ReadConsoleSize()
    {
        try
        {
            int w = Console.WindowWidth;
            int h = Console.WindowHeight;
            if (w > 0 && h > 0)
            {
                return (w, h);
            }
        }
        catch
        {
            // No console attached, or the tty went away mid-resize.
        }

        return (DefaultWidth, DefaultHeight);
    }

    private static (nint HOut, nint HIn, uint SavedOut, bool HaveOut, uint SavedIn, bool HaveIn)
        EnableWindowsVirtualTerminal()
    {
        nint hOut = 0;
        nint hIn = 0;
        uint savedOut = 0;
        uint savedIn = 0;
        bool haveOut = false;
        bool haveIn = false;

        try
        {
            hOut = NativeMethods.GetStdHandle(NativeMethods.StdOutputHandle);
            hIn = NativeMethods.GetStdHandle(NativeMethods.StdInputHandle);

            if (hOut != 0 && hOut != -1 && NativeMethods.GetConsoleMode(hOut, out savedOut))
            {
                haveOut = true;
                uint want = savedOut
                            | NativeMethods.EnableProcessedOutput
                            | NativeMethods.EnableVirtualTerminalProcessing
                            | NativeMethods.DisableNewlineAutoReturn;

                if (!NativeMethods.SetConsoleMode(hOut, want)
                    && Marshal.GetLastWin32Error() == NativeMethods.ErrorInvalidParameter)
                {
                    // Down-level console: retry without DISABLE_NEWLINE_AUTO_RETURN.
                    want = savedOut | NativeMethods.EnableProcessedOutput | NativeMethods.EnableVirtualTerminalProcessing;
                    NativeMethods.SetConsoleMode(hOut, want);
                }
            }

            if (hIn != 0 && hIn != -1 && NativeMethods.GetConsoleMode(hIn, out savedIn))
            {
                haveIn = true;

                // ENABLE_EXTENDED_FLAGS must be set for the quick-edit / insert bits to be
                // honoured at all; without it quick edit stays on and eats mouse input.
                uint want = savedIn | NativeMethods.EnableExtendedFlags | NativeMethods.EnableWindowInput;
                want &= ~(NativeMethods.EnableQuickEditMode
                          | NativeMethods.EnableLineInput
                          | NativeMethods.EnableEchoInput
                          | NativeMethods.EnableInsertMode
                          | NativeMethods.EnableProcessedInput
                          | NativeMethods.EnableVirtualTerminalInput);
                NativeMethods.SetConsoleMode(hIn, want);
            }
        }
        catch
        {
            // Not a real console; the VT path degrades to plain writes.
        }

        return (hOut, hIn, savedOut, haveOut, savedIn, haveIn);
    }

    private static class NativeMethods
    {
        internal const int StdInputHandle = -10;
        internal const int StdOutputHandle = -11;

        internal const uint EnableProcessedOutput = 0x0001;
        internal const uint EnableVirtualTerminalProcessing = 0x0004;
        internal const uint DisableNewlineAutoReturn = 0x0008;

        internal const uint EnableProcessedInput = 0x0001;
        internal const uint EnableLineInput = 0x0002;
        internal const uint EnableEchoInput = 0x0004;
        internal const uint EnableWindowInput = 0x0008;
        internal const uint EnableInsertMode = 0x0020;
        internal const uint EnableQuickEditMode = 0x0040;
        internal const uint EnableExtendedFlags = 0x0080;
        internal const uint EnableVirtualTerminalInput = 0x0200;

        internal const int ErrorInvalidParameter = 87;

        internal delegate int ConsoleCtrlHandler(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetConsoleMode(nint handle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetConsoleMode(nint handle, uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    }
}
