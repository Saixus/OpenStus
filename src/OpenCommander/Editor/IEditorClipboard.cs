namespace OpenCommander.Editor;

/// <summary>
/// The clipboard as the editor needs it: get text, set text, nothing else.
/// </summary>
/// <remarks>
/// Deliberately minimal so that the editor never depends on how the platform clipboard is reached.
/// The shell supplies a real implementation; when it does not, the editor falls back to
/// <see cref="InMemoryClipboard"/> so that copy and paste still work within the session.
/// </remarks>
public interface IEditorClipboard
{
    /// <summary>Reads the clipboard.</summary>
    /// <returns>The text, or <see langword="null"/> when the clipboard is empty or unavailable.</returns>
    string? GetText();

    /// <summary>Writes the clipboard.</summary>
    /// <param name="text">The text to place on the clipboard.</param>
    /// <returns><see langword="false"/> when the clipboard could not be written.</returns>
    bool SetText(string text);
}

/// <summary>
/// A clipboard that lives only in this process. It is the editor's fallback, and it keeps copy and
/// paste usable on a platform with no clipboard integration at all.
/// </summary>
public sealed class InMemoryClipboard : IEditorClipboard
{
    private string _text = string.Empty;

    /// <summary>A process-wide instance, so a copy in one editor pastes into another.</summary>
    public static InMemoryClipboard Shared { get; } = new();

    /// <inheritdoc/>
    public string? GetText() => _text.Length == 0 ? null : _text;

    /// <inheritdoc/>
    public bool SetText(string text)
    {
        _text = text ?? string.Empty;
        return true;
    }
}
