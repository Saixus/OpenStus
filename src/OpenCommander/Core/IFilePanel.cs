using OpenCommander.Files;
using OpenCommander.Input;
using OpenCommander.Rendering;

namespace OpenCommander.Core;

/// <summary>
/// One of the two file panels.
/// </summary>
/// <remarks>
/// Implemented by <c>OpenCommander.Panels.FilePanel</c>; the interface is declared here so that
/// <see cref="IAppContext"/> - and therefore every command - can talk about a panel without the
/// core layer depending on the panel implementation.
/// </remarks>
public interface IFilePanel
{
    /// <summary>The screen rectangle the panel occupies, frame included.</summary>
    Rect Bounds { get; set; }

    /// <summary>Whether this panel has the keyboard focus. Only the active panel draws a cursor bar.</summary>
    bool IsActive { get; set; }

    /// <summary>Whether the panel is drawn at all (Ctrl+F1 / Ctrl+F2 / Ctrl+O hide it).</summary>
    bool IsVisible { get; set; }

    /// <summary>The directory the panel is showing.</summary>
    string CurrentPath { get; }

    /// <summary>The entry under the cursor, or <see langword="null"/> when the listing is empty.</summary>
    FileEntry? Current { get; }

    /// <summary>Every entry in display order, <c>".."</c> included.</summary>
    IReadOnlyList<FileEntry> Entries { get; }

    /// <summary>
    /// The tagged entries, or - when nothing is tagged - just the entry under the cursor. Empty when
    /// the cursor sits on <c>".."</c> with nothing tagged, which is what makes F5/F6/F8 do nothing there.
    /// </summary>
    IReadOnlyList<FileEntry> SelectedOrCurrent { get; }

    /// <summary><see langword="true"/> when at least one entry is tagged.</summary>
    bool HasSelection { get; }

    /// <summary>
    /// Changes directory.
    /// </summary>
    /// <param name="path">The directory to show.</param>
    /// <param name="focusName">The entry to put the cursor on, or <see langword="null"/> for the first one.</param>
    void Navigate(string path, string? focusName = null);

    /// <summary>
    /// Re-reads the current directory.
    /// </summary>
    /// <param name="keepPosition">When set, the cursor stays on the same entry name if it still exists.</param>
    void Reload(bool keepPosition = true);

    /// <summary>Un-tags every entry.</summary>
    void ClearSelection();

    /// <summary>Paints the panel into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The back buffer for this frame.</param>
    void Draw(ScreenBuffer buffer);

    /// <summary>
    /// Handles a key press aimed at this panel.
    /// </summary>
    /// <param name="key">The key press.</param>
    /// <param name="ctx">The application context.</param>
    /// <returns><see langword="true"/> when the panel consumed the key.</returns>
    bool HandleKey(KeyEvent key, IAppContext ctx);

    /// <summary>
    /// Handles a mouse event inside <see cref="Bounds"/>.
    /// </summary>
    /// <param name="m">The mouse event.</param>
    /// <param name="ctx">The application context.</param>
    /// <returns><see langword="true"/> when the panel consumed the event.</returns>
    bool HandleMouse(MouseEvent m, IAppContext ctx);

    /// <summary>
    /// The key bar captions for the current panel state.
    /// </summary>
    /// <param name="mods">The modifier keys currently held down.</param>
    /// <returns>The captions, or <see langword="null"/> to use the default panel key bar.</returns>
    KeyBarLabels? KeyBarFor(KeyMods mods);
}
