using System.Runtime.InteropServices;

namespace OpenStus.Input;

/// <summary>
/// The portable input backend: polls <see cref="Console.KeyAvailable"/> and
/// <see cref="Console.ReadKey(bool)"/>, and detects a resize by watching
/// <see cref="Console.WindowWidth"/>/<see cref="Console.WindowHeight"/> (plus SIGWINCH where the
/// platform has it).
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately degraded tier used on Linux and macOS, and on Windows when the process has
/// no real console. It cannot report mouse events and it cannot see a modifier key on its own,
/// because <see cref="Console.ReadKey(bool)"/> discards key-up records and filters out the modifier
/// virtual keys. <see cref="CurrentModifiers"/> therefore only moves when a complete chord arrives,
/// and the key bar has to render its unmodified row until then - which is honest, rather than
/// pretending.
/// </para>
/// <para>
/// Known platform gaps that are not worked around here because they are unfixable at this layer:
/// Ctrl with several digits is indistinguishable from the bare digit, Ctrl+H/I/J/M collapse onto
/// Backspace/Tab/Enter, and a lone Escape only surfaces after the runtime's lookahead resolves.
/// </para>
/// </remarks>
public sealed class PortableConsoleInput : IInputBackend
{
    /// <summary>How often the window size is re-read, in milliseconds.</summary>
    private const int SizePollIntervalMs = 50;

    /// <summary>Cap on keys drained per <see cref="TryRead"/> burst so rendering is not starved.</summary>
    private const int MaxQueuedKeys = 256;

    private readonly Queue<InputEvent> _queue = new();
    private readonly IDisposable? _sigwinch;

    private KeyMods _mods;
    private int _width;
    private int _height;
    private long _nextSizePoll;
    private int _resizePending = 1;
    private bool _disposed;

    /// <summary>Creates the backend and captures the current console size as the baseline.</summary>
    public PortableConsoleInput()
    {
        TryGetSize(out _width, out _height);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // Set a flag only; the callback runs on a thread-pool thread and must not render.
                _sigwinch = PosixSignalRegistration.Create(
                    PosixSignal.SIGWINCH,
                    _ => Interlocked.Exchange(ref _resizePending, 1));
            }
            catch (PlatformNotSupportedException)
            {
                _sigwinch = null;
            }
        }

        // The initial baseline is already applied; do not report it as a resize.
        Interlocked.Exchange(ref _resizePending, 0);
    }

    /// <inheritdoc/>
    public KeyMods CurrentModifiers => _mods;

    /// <inheritdoc/>
    public bool SupportsMouse => false;

    /// <inheritdoc/>
    public bool SupportsModifierTracking => false;

    /// <inheritdoc/>
    public bool TryRead(out InputEvent ev)
    {
        ev = InputEvent.None;
        if (_disposed)
        {
            return false;
        }

        if (_queue.Count == 0)
        {
            DrainKeys();
            if (_queue.Count == 0)
            {
                CheckResize();
            }
        }

        if (_queue.Count == 0)
        {
            return false;
        }

        ev = _queue.Dequeue();
        return true;
    }

    private void DrainKeys()
    {
        try
        {
            if (Console.IsInputRedirected)
            {
                return;
            }

            var guard = MaxQueuedKeys;
            while (guard-- > 0 && Console.KeyAvailable)
            {
                var info = Console.ReadKey(intercept: true);
                var key = FromConsoleKeyInfo(info);

                if (key.Mods != _mods)
                {
                    _mods = key.Mods;
                    _queue.Enqueue(InputEvent.ModifiersChangedTo(_mods));
                }

                _queue.Enqueue(InputEvent.FromKey(key));
            }
        }
        catch (InvalidOperationException)
        {
            // KeyAvailable throws when stdin is not a console after all.
        }
        catch (IOException)
        {
            // The tty went away.
        }
    }

    private void CheckResize()
    {
        var now = Environment.TickCount64;
        var signalled = Interlocked.Exchange(ref _resizePending, 0) != 0;
        if (!signalled && now < _nextSizePoll)
        {
            return;
        }

        _nextSizePoll = now + SizePollIntervalMs;

        if (!TryGetSize(out var w, out var h))
        {
            return;
        }

        if (w == _width && h == _height)
        {
            return;
        }

        _width = w;
        _height = h;
        _queue.Enqueue(InputEvent.Resized());
    }

    private static bool TryGetSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            var w = Console.WindowWidth;
            var h = Console.WindowHeight;

            // Both can transiently read zero in the middle of a resize.
            if (w < 1 || h < 1)
            {
                return false;
            }

            width = w;
            height = h;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a <see cref="ConsoleKeyInfo"/> into a <see cref="KeyEvent"/>.
    /// </summary>
    /// <param name="info">The key info returned by <see cref="Console.ReadKey(bool)"/>.</param>
    /// <returns>The equivalent key event.</returns>
    public static KeyEvent FromConsoleKeyInfo(ConsoleKeyInfo info) =>
        new(info.Key, info.KeyChar, MapModifiers(info.Modifiers));

    /// <summary>
    /// Converts <see cref="ConsoleModifiers"/> into <see cref="KeyMods"/>.
    /// </summary>
    /// <param name="modifiers">The BCL modifier flags.</param>
    /// <returns>The equivalent <see cref="KeyMods"/> set.</returns>
    public static KeyMods MapModifiers(ConsoleModifiers modifiers)
    {
        var mods = KeyMods.None;
        if ((modifiers & ConsoleModifiers.Shift) != 0)
        {
            mods |= KeyMods.Shift;
        }

        if ((modifiers & ConsoleModifiers.Control) != 0)
        {
            mods |= KeyMods.Ctrl;
        }

        if ((modifiers & ConsoleModifiers.Alt) != 0)
        {
            mods |= KeyMods.Alt;
        }

        return mods;
    }

    /// <summary>Releases the SIGWINCH registration. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Clear();
        try
        {
            _sigwinch?.Dispose();
        }
        catch
        {
            // Shutdown must never throw.
        }
    }
}
