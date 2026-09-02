using Dvopan.Rendering;
using Dvopan.Theming;

namespace Dvopan.Core;

/// <summary>
/// Everything a command needs in order to do its job: the palette, the screen, the modal UI, the
/// two panels and the settings.
/// </summary>
/// <remarks>
/// This is the single seam between the shell (the application object that owns the event loop) and
/// every feature. Features take an <see cref="IAppContext"/> and never reach for a global.
/// </remarks>
public interface IAppContext
{
    /// <summary>The active colour palette.</summary>
    Theme Theme { get; }

    /// <summary>The physical terminal, for size queries and forced repaints.</summary>
    Terminal Terminal { get; }

    /// <summary>Modal dialogs, menus and message boxes.</summary>
    IUiServices Ui { get; }

    /// <summary>The panel with the keyboard focus.</summary>
    IFilePanel ActivePanel { get; }

    /// <summary>The panel without the keyboard focus - the destination of F5 and F6.</summary>
    IFilePanel PassivePanel { get; }

    /// <summary>The left panel, focused or not.</summary>
    IFilePanel LeftPanel { get; }

    /// <summary>The right panel, focused or not.</summary>
    IFilePanel RightPanel { get; }

    /// <summary>Swaps the left and right panels (Ctrl+U).</summary>
    void SwapPanels();

    /// <summary>Moves the focus to the other panel (Tab).</summary>
    void SwitchPanel();

    /// <summary>Asks the event loop to exit after the current iteration (F10).</summary>
    void RequestQuit();

    /// <summary>Marks the screen dirty so the next loop iteration repaints.</summary>
    void Redraw();

    /// <summary>Re-reads both panels, keeping the cursor position where possible (Ctrl+R).</summary>
    void RefreshBothPanels();

    /// <summary>The user settings; changes take effect on the next frame.</summary>
    Settings Settings { get; }

    /// <summary>
    /// Runs a command line through the platform shell, leaving the alternate screen buffer for the
    /// duration so the command's own output is visible.
    /// </summary>
    /// <param name="command">The command line, exactly as typed.</param>
    void RunShellCommand(string command);

    /// <summary>Inserts text at the command line caret (Ctrl+J, Ctrl+F, Ctrl+[ and friends).</summary>
    /// <param name="text">The text to insert.</param>
    void InsertIntoCommandLine(string text);
}
