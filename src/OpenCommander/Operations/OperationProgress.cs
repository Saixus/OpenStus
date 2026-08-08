namespace OpenCommander.Operations;

/// <summary>
/// The live state of a running file operation: what it is working on, how far it has got, and
/// whether the user has asked it to stop.
/// </summary>
/// <remarks>
/// <para>
/// One instance is created by the caller, handed to <see cref="FileOperations"/>, and read by the
/// progress dialog from the <c>onProgress</c> callback. The operation only ever writes to it and the
/// dialog only ever reads from it, with one exception: <see cref="Cancelled"/> is written by the user
/// interface and polled by the operation. That single field is the whole cancellation protocol.
/// </para>
/// <para>
/// The counters are plain fields behind properties rather than interlocked, because a file operation
/// runs on one thread. <see cref="Cancelled"/> is the only member touched from another one and it is
/// volatile.
/// </para>
/// </remarks>
public sealed class OperationProgress
{
    private volatile bool _cancelled;

    /// <summary>The name of the operation, used as the progress dialog's title: Copy, Move, Delete.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The full path of the item being read right now.</summary>
    public string CurrentSource { get; set; } = string.Empty;

    /// <summary>The full path being written right now; empty for a delete.</summary>
    public string CurrentTarget { get; set; } = string.Empty;

    /// <summary>Total bytes the operation expects to move, or zero when the totals are unknown.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Bytes moved so far.</summary>
    public long DoneBytes { get; set; }

    /// <summary>Total files the operation expects to touch, or zero when the totals are unknown.</summary>
    public long TotalFiles { get; set; }

    /// <summary>Files finished so far.</summary>
    public long DoneFiles { get; set; }

    /// <summary>The size of the file currently being transferred.</summary>
    public long CurrentFileBytes { get; set; }

    /// <summary>How much of the current file has been transferred.</summary>
    public long CurrentFileDone { get; set; }

    /// <summary>
    /// Whether the pre-pass ran and <see cref="TotalBytes"/>/<see cref="TotalFiles"/> can be trusted
    /// as a denominator.
    /// </summary>
    public bool TotalsKnown { get; set; }

    /// <summary>
    /// Set by the user interface to stop the operation at the next block boundary. The operation
    /// polls it; it is never cleared by the operation itself.
    /// </summary>
    public bool Cancelled
    {
        get => _cancelled;
        set => _cancelled = value;
    }

    /// <summary>How much of the whole operation is done, from 0 to 1.</summary>
    /// <remarks>
    /// Measured in bytes when the byte total is known, in files otherwise, and zero when neither is -
    /// a progress bar with no denominator should draw itself empty (or as a marquee), not full.
    /// </remarks>
    public double TotalFraction
    {
        get
        {
            if (TotalBytes > 0)
            {
                return Clamp((double)DoneBytes / TotalBytes);
            }

            return TotalFiles > 0 ? Clamp((double)DoneFiles / TotalFiles) : 0d;
        }
    }

    /// <summary>How much of the current file is done, from 0 to 1.</summary>
    public double FileFraction =>
        CurrentFileBytes > 0 ? Clamp((double)CurrentFileDone / CurrentFileBytes) : 0d;

    /// <summary>Asks the operation to stop. Equivalent to setting <see cref="Cancelled"/>.</summary>
    public void Cancel() => Cancelled = true;

    /// <summary>
    /// Clears every counter and the cancellation flag, so one instance can be reused for the next
    /// operation.
    /// </summary>
    public void Reset()
    {
        Title = string.Empty;
        CurrentSource = string.Empty;
        CurrentTarget = string.Empty;
        TotalBytes = 0;
        DoneBytes = 0;
        TotalFiles = 0;
        DoneFiles = 0;
        CurrentFileBytes = 0;
        CurrentFileDone = 0;
        TotalsKnown = false;
        _cancelled = false;
    }

    /// <summary>
    /// Records that work has started on one file.
    /// </summary>
    /// <param name="source">The full path being read.</param>
    /// <param name="target">The full path being written, or an empty string.</param>
    /// <param name="bytes">The size of the file.</param>
    public void BeginFile(string source, string target, long bytes)
    {
        CurrentSource = source;
        CurrentTarget = target;
        CurrentFileBytes = bytes < 0 ? 0 : bytes;
        CurrentFileDone = 0;
    }

    /// <summary>
    /// Records that another block of the current file has been transferred.
    /// </summary>
    /// <param name="bytes">The block size.</param>
    public void Advance(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        CurrentFileDone += bytes;
        DoneBytes += bytes;
    }

    /// <summary>
    /// Records that the current file is finished, charging any bytes the transfer never actually
    /// moved - a skipped file, or one handed to the fast move path - to the total.
    /// </summary>
    /// <param name="chargeRemainingBytes">
    /// Whether to advance <see cref="DoneBytes"/> by the part of the file that was never streamed, so
    /// that a skipped file does not leave the bar permanently short.
    /// </param>
    public void CompleteFile(bool chargeRemainingBytes = true)
    {
        if (chargeRemainingBytes && CurrentFileBytes > CurrentFileDone)
        {
            DoneBytes += CurrentFileBytes - CurrentFileDone;
        }

        CurrentFileDone = CurrentFileBytes;
        DoneFiles++;
    }

    /// <summary>
    /// Charges a whole subtree to the counters at once, used when the fast move path relocates a
    /// directory the operation never walked into.
    /// </summary>
    /// <param name="files">The number of files inside it.</param>
    /// <param name="bytes">Their total size.</param>
    public void AddCompleted(long files, long bytes)
    {
        if (files > 0)
        {
            DoneFiles += files;
        }

        if (bytes > 0)
        {
            DoneBytes += bytes;
        }
    }

    private static double Clamp(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
}
