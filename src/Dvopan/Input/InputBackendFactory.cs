namespace Dvopan.Input;

/// <summary>
/// Chooses the best available <see cref="IInputBackend"/> for the current process.
/// </summary>
public static class InputBackendFactory
{
    /// <summary>
    /// Creates an input backend.
    /// </summary>
    /// <param name="enableMouse">
    /// Whether to request mouse reporting. Ignored by backends that cannot report mouse events.
    /// </param>
    /// <returns>
    /// A <see cref="WindowsConsoleInput"/> when running on Windows with a real console attached -
    /// that backend is the only one that can report modifier-only key presses and the mouse.
    /// Otherwise a <see cref="PortableConsoleInput"/>, which is also the fallback when the process
    /// has no console at all (piped output, a test host, <c>--screenshot</c> mode).
    /// </returns>
    public static IInputBackend Create(bool enableMouse = true)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (WindowsConsoleInput.TryCreate(enableMouse, out var windows))
                {
                    return windows;
                }
            }
            catch
            {
                // Any failure attaching to the raw console is a fallback, never a crash.
            }
        }

        return new PortableConsoleInput();
    }
}
