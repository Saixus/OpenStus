namespace Dvopan.Editor;

/// <summary>
/// The editor caret, together with the selection anchor and every motion the key map needs.
/// </summary>
/// <remarks>
/// <para>
/// The caret is stored as a line index plus a <em>character</em> column, not a screen column. A
/// separate preferred display column is remembered so that moving up and down a ragged block of
/// lines - or one containing tabs - returns to the same visual column instead of drifting left.
/// </para>
/// <para>
/// Every motion takes an <c>extend</c> flag. Passing it drops the anchor on the first extended
/// motion and grows the selection thereafter; not passing it drops the selection. That single rule
/// gives all of Shift+Arrow, Shift+Home, Shift+PageDown and friends.
/// </para>
/// </remarks>
public sealed class EditorCursor
{
    private bool _selecting;

    /// <summary>The line the caret is on, zero based.</summary>
    public int Line { get; private set; }

    /// <summary>The character index within the line the caret sits before, zero based.</summary>
    public int Column { get; private set; }

    /// <summary>The screen column vertical motion tries to return to.</summary>
    public int PreferredDisplayColumn { get; private set; }

    /// <summary>The line the selection was anchored on.</summary>
    public int AnchorLine { get; private set; }

    /// <summary>The column the selection was anchored at.</summary>
    public int AnchorColumn { get; private set; }

    /// <summary>Whether a non-empty block is selected.</summary>
    public bool HasSelection => _selecting && (AnchorLine != Line || AnchorColumn != Column);

    /// <summary>The upper end of the selection, whichever way round it was made.</summary>
    public (int Line, int Column) SelectionStart =>
        Before(AnchorLine, AnchorColumn, Line, Column) ? (AnchorLine, AnchorColumn) : (Line, Column);

    /// <summary>The lower end of the selection, whichever way round it was made.</summary>
    public (int Line, int Column) SelectionEnd =>
        Before(AnchorLine, AnchorColumn, Line, Column) ? (Line, Column) : (AnchorLine, AnchorColumn);

    /// <summary>Drops the selection, leaving the caret where it is.</summary>
    public void ClearSelection() => _selecting = false;

    /// <summary>Selects the whole document.</summary>
    /// <param name="buffer">The document.</param>
    public void SelectAll(TextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        AnchorLine = 0;
        AnchorColumn = 0;
        _selecting = true;
        (Line, Column) = buffer.EndPosition;
        UpdatePreferred(buffer);
    }

    /// <summary>Places the caret, clamped into the document.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="line">The target line.</param>
    /// <param name="column">The target column.</param>
    /// <param name="extend">Extend the selection to the new position.</param>
    public void MoveTo(TextBuffer buffer, int line, int column, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        (Line, Column) = buffer.Clamp(line, column);
        UpdatePreferred(buffer);
    }

    /// <summary>
    /// Places the caret without touching the selection state at all, which is what applying an edit
    /// or an undo step needs.
    /// </summary>
    /// <param name="buffer">The document.</param>
    /// <param name="line">The target line.</param>
    /// <param name="column">The target column.</param>
    public void SetPosition(TextBuffer buffer, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        (Line, Column) = buffer.Clamp(line, column);
        UpdatePreferred(buffer);
    }

    /// <summary>Pulls the caret and the anchor back inside the document after an edit.</summary>
    /// <param name="buffer">The document.</param>
    public void Clamp(TextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        (Line, Column) = buffer.Clamp(Line, Column);
        (AnchorLine, AnchorColumn) = buffer.Clamp(AnchorLine, AnchorColumn);
    }

    /// <summary>Moves one character left, wrapping to the end of the previous line.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveLeft(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        if (Column > 0)
        {
            Column--;
        }
        else if (Line > 0)
        {
            Line--;
            Column = buffer.LineLength(Line);
        }

        UpdatePreferred(buffer);
    }

    /// <summary>Moves one character right, wrapping to the start of the next line.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveRight(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        if (Column < buffer.LineLength(Line))
        {
            Column++;
        }
        else if (Line < buffer.LineCount - 1)
        {
            Line++;
            Column = 0;
        }

        UpdatePreferred(buffer);
    }

    /// <summary>Moves <paramref name="delta"/> lines, keeping the preferred display column.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="delta">How many lines; negative moves up.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveVertical(TextBuffer buffer, int delta, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        int line = buffer.ClampLine(Line + delta);
        int column = TextBuffer.FromDisplayColumn(buffer.GetLine(line), PreferredDisplayColumn, buffer.TabSize);

        Line = line;
        Column = buffer.ClampColumn(line, column);
    }

    /// <summary>Moves to the start of the line.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveHome(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        Column = 0;
        UpdatePreferred(buffer);
    }

    /// <summary>Moves to the end of the line.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveEnd(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        Column = buffer.LineLength(Line);
        UpdatePreferred(buffer);
    }

    /// <summary>Moves to the very start of the document.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveDocumentStart(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        Line = 0;
        Column = 0;
        UpdatePreferred(buffer);
    }

    /// <summary>Moves past the last character of the document.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveDocumentEnd(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        (Line, Column) = buffer.EndPosition;
        UpdatePreferred(buffer);
    }

    /// <summary>Moves to the start of the word to the left, crossing to the previous line if needed.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveWordLeft(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        if (Column == 0)
        {
            if (Line > 0)
            {
                Line--;
                Column = buffer.LineLength(Line);
            }
        }
        else
        {
            Column = TextBuffer.WordLeft(buffer.GetLine(Line), Column);
        }

        UpdatePreferred(buffer);
    }

    /// <summary>Moves to the start of the word to the right, crossing to the next line if needed.</summary>
    /// <param name="buffer">The document.</param>
    /// <param name="extend">Extend the selection.</param>
    public void MoveWordRight(TextBuffer buffer, bool extend = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        BeginMove(extend);
        if (Column >= buffer.LineLength(Line))
        {
            if (Line < buffer.LineCount - 1)
            {
                Line++;
                Column = 0;
            }
        }
        else
        {
            Column = TextBuffer.WordRight(buffer.GetLine(Line), Column);
        }

        UpdatePreferred(buffer);
    }

    /// <summary>
    /// The selected character range on one line, for painting.
    /// </summary>
    /// <param name="line">The line to ask about.</param>
    /// <param name="lineLength">That line's length in characters.</param>
    /// <param name="from">Receives the first selected character index.</param>
    /// <param name="to">Receives one past the last selected character index.</param>
    /// <returns><see langword="false"/> when nothing on that line is selected.</returns>
    /// <remarks>
    /// A line fully inside a multi-line selection reports one extra column past its end, which is
    /// how the selected newline is shown.
    /// </remarks>
    public bool SelectionOnLine(int line, int lineLength, out int from, out int to)
    {
        from = 0;
        to = 0;
        if (!HasSelection)
        {
            return false;
        }

        (int startLine, int startColumn) = SelectionStart;
        (int endLine, int endColumn) = SelectionEnd;

        if (line < startLine || line > endLine)
        {
            return false;
        }

        from = line == startLine ? Math.Clamp(startColumn, 0, lineLength) : 0;
        to = line == endLine ? Math.Clamp(endColumn, 0, lineLength) : lineLength + 1;
        return to > from;
    }

    /// <summary>Recomputes the preferred display column from the caret's current position.</summary>
    /// <param name="buffer">The document.</param>
    public void UpdatePreferred(TextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        PreferredDisplayColumn = TextBuffer.ToDisplayColumn(buffer.GetLine(Line), Column, buffer.TabSize);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        HasSelection
            ? $"({Line},{Column}) sel ({AnchorLine},{AnchorColumn})"
            : $"({Line},{Column})";

    private void BeginMove(bool extend)
    {
        if (!extend)
        {
            _selecting = false;
            return;
        }

        if (!_selecting)
        {
            AnchorLine = Line;
            AnchorColumn = Column;
            _selecting = true;
        }
    }

    private static bool Before(int aLine, int aColumn, int bLine, int bColumn) =>
        aLine < bLine || (aLine == bLine && aColumn <= bColumn);
}
