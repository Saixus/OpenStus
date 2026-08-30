using OpenCommander.Files;
using OpenCommander.Rendering;

namespace OpenCommander.Panels;

/// <summary>
/// Far's fast find: the incremental prefix search a panel starts when the user holds Alt and types.
/// </summary>
/// <remarks>
/// <para>
/// The search is shown as a small box drawn over the panel's bottom frame and stays open - exactly
/// like Far - until the user presses Escape or Enter, moves the cursor with an arrow key, or
/// deletes the last character. There is no inactivity timeout.
/// </para>
/// <para>
/// Matching runs in two passes over the listing: first every name that <em>starts</em> with the typed
/// text, then - only if nothing matched - every name that merely <em>contains</em> it. That is what
/// makes typing "res" jump to <c>results.txt</c> rather than to <c>a-restore.log</c> while still
/// finding the latter when it is the only candidate. <c>'*'</c> and <c>'?'</c> in the typed text are
/// honoured through <see cref="FileMask"/>, and matching is always case insensitive.
/// </para>
/// </remarks>
public sealed class QuickSearch
{
    private string _text = string.Empty;
    private DateTime _lastInput;

    /// <summary><see langword="true"/> while the search box is open.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The text typed so far; empty when the search is not running.</summary>
    public string Text => _text;

    /// <summary>When the last keystroke arrived, as handed to <see cref="Append"/> or <see cref="Backspace"/>.</summary>
    public DateTime LastInput => _lastInput;

    /// <summary>The text as it is drawn, brackets included.</summary>
    public string DisplayText => "[" + _text + "]";

    /// <summary>
    /// Adds a character, starting the search when it was not running.
    /// </summary>
    /// <param name="c">The character typed.</param>
    /// <param name="now">The current time, used to reset the inactivity timeout.</param>
    public void Append(char c, DateTime now)
    {
        IsActive = true;
        _text += c;
        _lastInput = now;
    }

    /// <summary>
    /// Removes the last character. Deleting the last one closes the box, exactly like Far.
    /// </summary>
    /// <param name="now">The current time, used to reset the inactivity timeout.</param>
    /// <returns><see langword="true"/> when the search is still running afterwards.</returns>
    public bool Backspace(DateTime now)
    {
        if (!IsActive)
        {
            return false;
        }

        if (_text.Length <= 1)
        {
            Cancel();
            return false;
        }

        _text = _text[..^1];
        _lastInput = now;
        return true;
    }

    /// <summary>Closes the box and forgets the text.</summary>
    public void Cancel()
    {
        IsActive = false;
        _text = string.Empty;
    }

    /// <summary>
    /// Whether a name starts with the typed text.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <param name="pattern">The typed text; may contain <c>'*'</c> and <c>'?'</c>.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    public static bool IsPrefixMatch(string? name, string? pattern)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        return HasWildcards(pattern)
            ? FileMask.IsMatch(name, pattern.EndsWith('*') ? pattern : pattern + "*", ignoreCase: true)
            : name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a name contains the typed text anywhere.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <param name="pattern">The typed text; may contain <c>'*'</c> and <c>'?'</c>.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    public static bool IsContainsMatch(string? name, string? pattern)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        return HasWildcards(pattern)
            ? FileMask.IsMatch(name, "*" + pattern + "*", ignoreCase: true)
            : name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the entry the search should jump to.
    /// </summary>
    /// <param name="entries">The listing, in display order.</param>
    /// <param name="pattern">The typed text.</param>
    /// <param name="start">Where to start looking; the scan wraps around the end of the listing.</param>
    /// <param name="forward">Scan towards the end of the listing rather than towards its start.</param>
    /// <returns>The index of the match, or <c>-1</c> when nothing matched.</returns>
    public static int Find(IReadOnlyList<FileEntry>? entries, string? pattern, int start, bool forward = true)
    {
        if (entries is null || entries.Count == 0 || string.IsNullOrEmpty(pattern))
        {
            return -1;
        }

        int n = entries.Count;
        int from = ((start % n) + n) % n;

        for (int phase = 0; phase < 2; phase++)
        {
            for (int k = 0; k < n; k++)
            {
                int i = forward ? (from + k) % n : (((from - k) % n) + n) % n;
                string name = entries[i].Name;
                bool hit = phase == 0 ? IsPrefixMatch(name, pattern) : IsContainsMatch(name, pattern);
                if (hit)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Draws the search box.
    /// </summary>
    /// <param name="buffer">The back buffer.</param>
    /// <param name="x">The leftmost cell of the box.</param>
    /// <param name="y">The row the box sits on - the panel's bottom frame.</param>
    /// <param name="maxWidth">How many cells are available; the box is truncated to fit.</param>
    /// <param name="style">The <c>QuickSearch</c> theme style.</param>
    public void Draw(ScreenBuffer buffer, int x, int y, int maxWidth, CellStyle style)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (!IsActive || maxWidth <= 0)
        {
            return;
        }

        string text = DisplayText;
        if (text.Length > maxWidth && maxWidth >= 3)
        {
            // Keep the tail: the characters just typed are the ones the user is looking at.
            text = "[" + ScreenBuffer.Ellipsis + _text[^(maxWidth - 3)..] + "]";
        }

        for (int i = 0; i < text.Length && i < maxWidth; i++)
        {
            buffer.Set(x + i, y, text[i], style);
        }
    }

    private static bool HasWildcards(string pattern) =>
        pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal);
}
