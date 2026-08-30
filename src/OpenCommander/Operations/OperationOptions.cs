using OpenCommander.Core;

namespace OpenCommander.Operations;

/// <summary>
/// The knobs a copy, move or delete runs with.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are Far Manager's: delete to the recycle bin, ask before overwriting, and carry the
/// timestamps and attributes over to the copy. <see cref="FromSettings"/> derives an instance from
/// the persisted user preferences, so the panels never have to translate the two by hand.
/// </para>
/// <para>
/// Nothing in here describes the user interface. Every question an operation needs answered is asked
/// through the prompt delegates on <see cref="FileOperations"/>; these options only decide what is
/// worth asking about in the first place.
/// </para>
/// </remarks>
public sealed class OperationOptions
{
    /// <summary>The block size a copy reads and writes with, in bytes.</summary>
    public const int DefaultBufferSize = 64 * 1024;

    /// <summary>The shortest gap between two progress callbacks, in milliseconds (~30 Hz).</summary>
    public const int DefaultProgressIntervalMs = 33;

    private int _bufferSize = DefaultBufferSize;
    private int _progressIntervalMs = DefaultProgressIntervalMs;

    /// <summary>
    /// Delete to the platform's recycle bin rather than permanently. Ignored where the platform has
    /// no recycle bin - see <see cref="RecycleBin.IsAvailable"/>.
    /// </summary>
    public bool UseRecycleBin { get; set; } = true;

    /// <summary>
    /// Ask through the overwrite prompt before writing over an existing file. When clear, an existing
    /// target is overwritten without a question.
    /// </summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>
    /// Ask through the error prompt before deleting a file carrying the ReadOnly attribute, whether
    /// the delete is permanent or goes to the recycle bin. Answering Retry, Yes, Ok or All deletes
    /// it anyway.
    /// </summary>
    public bool ConfirmReadOnlyDelete { get; set; } = true;

    /// <summary>Copy the creation, write and access times over to the target.</summary>
    public bool PreserveTimestamps { get; set; } = true;

    /// <summary>Copy the file system attributes over to the target.</summary>
    public bool PreserveAttributes { get; set; } = true;

    /// <summary>
    /// Never use the <see cref="File.Move(string, string)"/> / <see cref="Directory.Move"/> fast
    /// path, always copy and then delete. Set by the tests to exercise the cross-volume path on a
    /// single-volume machine; also useful when the fast path is known to be unavailable.
    /// </summary>
    public bool ForceCopyThenDelete { get; set; }

    /// <summary>
    /// Walk the sources once up front to learn the total byte and file counts, so the progress bar
    /// has a denominator. Clearing it starts the operation instantly at the cost of an unknown total.
    /// </summary>
    public bool CountTotalsFirst { get; set; } = true;

    /// <summary>
    /// Recurse into directories that are reparse points (symlinks, junctions, volume mounts). Off by
    /// default: the link itself is recreated at the target, pointing at the same place, rather than
    /// risking a cycle - and a link that cannot be recreated is reported, never silently replaced by
    /// an empty plain folder.
    /// </summary>
    public bool FollowLinks { get; set; }

    /// <summary>
    /// The block size a copy transfers with, clamped to 4 KB - 16 MB.
    /// </summary>
    public int BufferSize
    {
        get => _bufferSize;
        set => _bufferSize = Math.Clamp(value, 4 * 1024, 16 * 1024 * 1024);
    }

    /// <summary>
    /// The shortest gap between two progress callbacks in milliseconds, clamped to 0 - 5000. Zero
    /// reports every block, which is what the tests want and no interactive caller does.
    /// </summary>
    public int ProgressIntervalMs
    {
        get => _progressIntervalMs;
        set => _progressIntervalMs = Math.Clamp(value, 0, 5000);
    }

    /// <summary>A fresh instance carrying the shipping defaults.</summary>
    public static OperationOptions Default => new();

    /// <summary>
    /// Builds the options implied by the persisted preferences.
    /// </summary>
    /// <param name="settings">The user settings, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options.</returns>
    public static OperationOptions FromSettings(Settings? settings)
    {
        var options = new OperationOptions();
        if (settings is null)
        {
            return options;
        }

        options.UseRecycleBin = settings.UseRecycleBin;
        options.ConfirmOverwrite = settings.ConfirmOverwrite;
        options.ConfirmReadOnlyDelete = settings.ConfirmDelete;
        return options;
    }

    /// <summary>Returns an independent copy of these options.</summary>
    /// <returns>The copy.</returns>
    public OperationOptions Clone() => (OperationOptions)MemberwiseClone();
}
