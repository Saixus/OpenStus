namespace OpenStus.Input;

/// <summary>
/// A non-blocking source of console input events.
/// </summary>
/// <remarks>
/// The TUI event loop is synchronous and frame paced: it drains the backend completely, then renders
/// once. <see cref="TryRead"/> must therefore never block and never wait for a key.
/// </remarks>
public interface IInputBackend : IDisposable
{
    /// <summary>
    /// Dequeues the next pending event, if any. Never blocks.
    /// </summary>
    /// <param name="ev">Receives the event, or <see cref="InputEvent.None"/> when none was pending.</param>
    /// <returns><see langword="true"/> when an event was produced.</returns>
    bool TryRead(out InputEvent ev);

    /// <summary>
    /// The modifier keys believed to be held down right now. Drives the live key bar. Best effort:
    /// backends where <see cref="SupportsModifierTracking"/> is <see langword="false"/> only update
    /// this when a complete chord arrives.
    /// </summary>
    KeyMods CurrentModifiers { get; }

    /// <summary><see langword="true"/> when the backend reports mouse events.</summary>
    bool SupportsMouse { get; }

    /// <summary>
    /// <see langword="true"/> when the backend can observe a modifier key being pressed or released
    /// on its own, without an accompanying character key.
    /// </summary>
    bool SupportsModifierTracking { get; }
}
