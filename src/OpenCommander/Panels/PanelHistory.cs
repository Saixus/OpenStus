namespace OpenCommander.Panels;

/// <summary>
/// The per-panel folder history behind Alt+F12: the directories this panel has shown, most recently
/// visited first, capped at <see cref="Capacity"/> entries.
/// </summary>
/// <remarks>
/// Re-visiting a directory moves it back to the front rather than adding a duplicate, so the list
/// stays a true most-recently-used order. Comparison follows the platform: case insensitive on
/// Windows, exact elsewhere.
/// </remarks>
public sealed class PanelHistory
{
    /// <summary>How many directories are remembered before the oldest is dropped.</summary>
    public const int Capacity = 64;

    private readonly List<string> _items = [];

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The remembered directories, most recently visited first.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>How many directories are remembered.</summary>
    public int Count => _items.Count;

    /// <summary>The most recently visited directory, or <see langword="null"/> when the list is empty.</summary>
    public string? Latest => _items.Count > 0 ? _items[0] : null;

    /// <summary>
    /// Records a visit, moving the directory to the front and evicting the oldest entry when the
    /// list is full. Empty and whitespace-only paths are ignored.
    /// </summary>
    /// <param name="path">The directory that was visited.</param>
    public void Push(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        int existing = IndexOf(path);
        if (existing >= 0)
        {
            if (existing == 0)
            {
                return;
            }

            _items.RemoveAt(existing);
        }

        _items.Insert(0, path);

        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }
    }

    /// <summary>The position of a directory in the list.</summary>
    /// <param name="path">The directory to look for.</param>
    /// <returns>The zero-based index, or <c>-1</c> when it is not remembered.</returns>
    public int IndexOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return -1;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i], path, Comparison))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether a directory is remembered.</summary>
    /// <param name="path">The directory to look for.</param>
    /// <returns><see langword="true"/> when it is in the list.</returns>
    public bool Contains(string? path) => IndexOf(path) >= 0;

    /// <summary>Forgets everything.</summary>
    public void Clear() => _items.Clear();
}
