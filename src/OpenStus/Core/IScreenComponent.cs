using OpenStus.Input;
using OpenStus.Rendering;

namespace OpenStus.Core;

/// <summary>
/// Anything that can own the screen: a dialog, a menu, the viewer, the editor, the help screen.
/// </summary>
/// <remarks>
/// The event loop is synchronous. A modal component is pushed with
/// <see cref="IUiServices.RunModal"/>, which then loops "drain input, feed
/// <see cref="HandleInput"/>, <see cref="Draw"/>, render" until the component closes.
/// </remarks>
public interface IScreenComponent
{
    /// <summary>Paints the component into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The back buffer for this frame.</param>
    void Draw(ScreenBuffer buffer);

    /// <summary>
    /// Handles one input event.
    /// </summary>
    /// <param name="ev">The event.</param>
    /// <returns>
    /// <see langword="false"/> to close this component (the modal loop stops), <see langword="true"/>
    /// to keep it open - whether or not the event was actually consumed.
    /// </returns>
    bool HandleInput(InputEvent ev);

    /// <summary><see langword="true"/> once the component has finished and should be popped.</summary>
    bool IsClosed { get; }

    /// <summary>
    /// The key bar this component wants shown while it is on top.
    /// </summary>
    /// <param name="mods">The modifier keys currently held down.</param>
    /// <returns>The captions, or <see langword="null"/> to keep the panel key bar.</returns>
    KeyBarLabels? KeyBarFor(KeyMods mods);

    /// <summary>Assigns the screen rectangle the component may draw in.</summary>
    /// <param name="area">The area, in screen cells.</param>
    void Layout(Rect area);
}
