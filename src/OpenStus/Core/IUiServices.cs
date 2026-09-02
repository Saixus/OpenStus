using OpenStus.Rendering;

namespace OpenStus.Core;

/// <summary>
/// The modal user interface services every feature uses to talk to the user, so that no feature has
/// to know how dialogs are actually built or how the event loop is driven.
/// </summary>
/// <remarks>
/// Every method here is blocking: it runs its own modal loop and returns only once the user has
/// answered. That is deliberate - the TUI event loop is synchronous throughout.
/// </remarks>
public interface IUiServices
{
    /// <summary>
    /// Shows a message box and waits for an answer.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="lines">The body, one screen line per element.</param>
    /// <param name="buttons">Which buttons to offer.</param>
    /// <param name="warning">When set, the red warning palette is used.</param>
    /// <returns>The button the user chose, or <see cref="DialogResult.Cancel"/> when they pressed Esc.</returns>
    DialogResult Message(string title, string[] lines, MessageButtons buttons, bool warning = false);

    /// <summary>
    /// Shows a Yes/No question.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="lines">The body, one screen line per element.</param>
    /// <param name="warning">When set, the red warning palette is used.</param>
    /// <returns><see langword="true"/> when the user answered Yes.</returns>
    bool Confirm(string title, string[] lines, bool warning = false);

    /// <summary>
    /// Shows an error message with a single OK button, in the warning palette.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The message; embedded newlines split it into lines.</param>
    void Error(string title, string message);

    /// <summary>
    /// Asks for a line of text.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="prompt">The label above the edit field.</param>
    /// <param name="initial">The text the field starts with.</param>
    /// <param name="historyKey">
    /// Names the history list the field offers, or <see langword="null"/> for no history.
    /// </param>
    /// <returns>The entered text, or <see langword="null"/> when the user cancelled.</returns>
    string? Input(string title, string prompt, string initial = "", string? historyKey = null);

    /// <summary>
    /// Shows a popup menu and waits for a choice.
    /// </summary>
    /// <param name="title">The menu title drawn in the frame.</param>
    /// <param name="items">The items; separators are skipped when moving the cursor.</param>
    /// <param name="selected">The index the cursor starts on.</param>
    /// <param name="position">Where to place the menu, or <see langword="null"/> to centre it.</param>
    /// <returns>The chosen index, or <c>-1</c> when the user cancelled.</returns>
    int Menu(string title, IReadOnlyList<MenuItem> items, int selected = 0, Rect? position = null);

    /// <summary>
    /// Runs <paramref name="component"/> modally until it closes.
    /// </summary>
    /// <param name="component">The component to put on top of the screen.</param>
    void RunModal(IScreenComponent component);

    /// <summary>Repaints the whole screen immediately; used by long running operations.</summary>
    void Redraw();
}
