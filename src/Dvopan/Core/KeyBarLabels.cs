namespace Dvopan.Core;

/// <summary>
/// The twelve captions shown on the function key bar, index 0 being F1.
/// </summary>
/// <remarks>
/// An empty caption means the key is unbound in the current context and the cell is drawn blank.
/// The array is normalised on construction: a shorter one is padded with empty strings and a longer
/// one is truncated, so <see cref="Labels"/> always has exactly <see cref="KeyCount"/> non-null
/// entries and drawing code never has to bounds-check.
/// </remarks>
/// <param name="Labels">The captions, index 0 == F1.</param>
public sealed record KeyBarLabels(string[] Labels)
{
    /// <summary>The number of function keys on the bar.</summary>
    public const int KeyCount = 12;

    /// <summary>The captions, index 0 == F1. Always exactly <see cref="KeyCount"/> non-null entries.</summary>
    public string[] Labels { get; init; } = Normalize(Labels);

    /// <summary>A bar with every caption blank.</summary>
    public static KeyBarLabels Empty => new(new string[KeyCount]);

    /// <summary>The caption for one function key; out-of-range indices read as empty.</summary>
    /// <param name="index">Zero-based key index, 0 == F1.</param>
    public string this[int index] =>
        (uint)index < (uint)Labels.Length ? Labels[index] : string.Empty;

    /// <summary>Builds a bar from a caption list.</summary>
    /// <param name="labels">The captions, index 0 == F1; padded or truncated to twelve.</param>
    /// <returns>The normalised bar.</returns>
    public static KeyBarLabels Of(params string[] labels) => new(labels);

    /// <summary>Returns a copy of this bar with one caption replaced.</summary>
    /// <param name="index">Zero-based key index, 0 == F1. Out-of-range indices are ignored.</param>
    /// <param name="label">The new caption.</param>
    /// <returns>A new bar; this instance is left untouched.</returns>
    public KeyBarLabels WithLabel(int index, string label)
    {
        if ((uint)index >= KeyCount)
        {
            return this;
        }

        var copy = (string[])Labels.Clone();
        copy[index] = label ?? string.Empty;
        return new KeyBarLabels(copy);
    }

    private static string[] Normalize(string[]? labels)
    {
        var result = new string[KeyCount];
        for (int i = 0; i < KeyCount; i++)
        {
            result[i] = labels is not null && i < labels.Length ? labels[i] ?? string.Empty : string.Empty;
        }

        return result;
    }
}
