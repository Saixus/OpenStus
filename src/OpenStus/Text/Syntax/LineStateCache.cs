using OpenStus.Editor;

namespace OpenStus.Text.Syntax;

/// <summary>
/// The per-line entry states of a <see cref="TextBuffer"/> being highlighted: what the editor
/// needs so a block comment opened above the viewport still colours the lines inside it.
/// </summary>
/// <remarks>
/// <para>
/// Entry <c>i</c> holds the state line <c>i</c> begins in; line zero always begins in
/// <see cref="SyntaxState.None"/>. The list is extended lazily up to whatever line the editor is
/// about to draw, one cheap state-only scan per line, so opening a file costs one pass up to the
/// viewport and scrolling costs only the newly uncovered lines.
/// </para>
/// <para>
/// Invalidation rides on <see cref="TextBuffer.Version"/> and
/// <see cref="TextBuffer.FirstChangeSince"/>: everything above the lowest line touched since the
/// cache's last look is still right and only the tail is dropped - however many splices one frame
/// drained, since a keystroke over a selection, a block indent or a redo of either is several.
/// The recompute is a state scan, not a tokenization, and it stops at the viewport.
/// </para>
/// </remarks>
public sealed class LineStateCache
{
    private readonly List<SyntaxState> _entry = [SyntaxState.None];
    private int _version = -1;

    /// <summary>
    /// The state <paramref name="line"/> begins in, extending or repairing the cache as needed.
    /// </summary>
    /// <param name="buffer">The buffer being highlighted.</param>
    /// <param name="rules">The language.</param>
    /// <param name="line">The line about to be drawn.</param>
    /// <returns>The entry state.</returns>
    public SyntaxState EntryState(TextBuffer buffer, SyntaxRules rules, int line)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(rules);

        if (_version != buffer.Version)
        {
            // A change starting at line s leaves the entry states of lines 0..s intact.
            int first = buffer.FirstChangeSince(_version);
            int keep = Math.Clamp(first == int.MaxValue ? _entry.Count : first + 1, 1, _entry.Count);

            _entry.RemoveRange(keep, _entry.Count - keep);
            _version = buffer.Version;
        }

        line = Math.Clamp(line, 0, Math.Max(0, buffer.LineCount - 1));
        while (_entry.Count <= line)
        {
            int i = _entry.Count - 1;
            _entry.Add(SyntaxTokenizer.ScanLine(buffer.GetLine(i), rules, _entry[i]));
        }

        return _entry[line];
    }
}
