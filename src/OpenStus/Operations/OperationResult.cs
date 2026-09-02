namespace OpenStus.Operations;

/// <summary>
/// One thing that went wrong during an operation: which step, which path, and why.
/// </summary>
/// <param name="Operation">The step that failed - <c>"Copy"</c>, <c>"Delete"</c>, and so on.</param>
/// <param name="Path">The full path the step was working on.</param>
/// <param name="Message">A message fit to put in a dialog.</param>
public sealed record OperationError(string Operation, string Path, string Message)
{
    /// <summary>The exception behind the failure, when there was one.</summary>
    public Exception? Exception { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        string.IsNullOrEmpty(Path) ? $"{Operation}: {Message}" : $"{Operation} \"{Path}\": {Message}";
}

/// <summary>
/// What an operation did and what it could not do.
/// </summary>
/// <remarks>
/// No method on <see cref="FileOperations"/> throws; every failure ends up in <see cref="Errors"/>
/// instead, and a user who cancelled shows up as <see cref="Cancelled"/> rather than as an error.
/// A result with neither is a <see cref="Success"/>.
/// </remarks>
public sealed class OperationResult
{
    private readonly List<OperationError> _errors = [];

    /// <summary>
    /// Creates an empty result.
    /// </summary>
    /// <param name="operation">The operation's name, used as the default for error entries.</param>
    public OperationResult(string operation) => Operation = operation ?? string.Empty;

    /// <summary>The operation's name: Copy, Move, Delete, Create folder, Rename, Search.</summary>
    public string Operation { get; }

    /// <summary>Whether the user stopped the operation before it finished.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Files successfully copied, moved or deleted.</summary>
    public int FilesProcessed { get; set; }

    /// <summary>Directories successfully created, moved or deleted.</summary>
    public int DirectoriesProcessed { get; set; }

    /// <summary>Items the user chose to skip, or that an error prompt skipped past.</summary>
    public int SkippedCount { get; set; }

    /// <summary>Bytes actually transferred.</summary>
    public long BytesProcessed { get; set; }

    /// <summary>Everything that went wrong, in the order it happened.</summary>
    public IReadOnlyList<OperationError> Errors => _errors;

    /// <summary>Whether anything went wrong.</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Whether the operation ran to the end without a single failure.</summary>
    public bool Success => !Cancelled && _errors.Count == 0;

    /// <summary>The first failure, or <see langword="null"/> when there was none.</summary>
    public OperationError? FirstError => _errors.Count > 0 ? _errors[0] : null;

    /// <summary>
    /// Records a failure.
    /// </summary>
    /// <param name="path">The path involved.</param>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception behind it, if any.</param>
    /// <returns>This result, so calls can be chained.</returns>
    public OperationResult AddError(string path, string message, Exception? exception = null)
    {
        _errors.Add(new OperationError(Operation, path ?? string.Empty, message ?? string.Empty)
        {
            Exception = exception,
        });

        return this;
    }

    /// <summary>
    /// Records a failure taking the message from an exception.
    /// </summary>
    /// <param name="path">The path involved.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>This result, so calls can be chained.</returns>
    public OperationResult AddError(string path, Exception exception) =>
        AddError(path, Describe(exception), exception);

    /// <summary>
    /// Records a failure attributed to a named step rather than to the whole operation.
    /// </summary>
    /// <param name="step">The step name.</param>
    /// <param name="path">The path involved.</param>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception behind it, if any.</param>
    /// <returns>This result, so calls can be chained.</returns>
    public OperationResult AddError(string step, string path, string message, Exception? exception = null)
    {
        _errors.Add(new OperationError(step ?? Operation, path ?? string.Empty, message ?? string.Empty)
        {
            Exception = exception,
        });

        return this;
    }

    /// <summary>Marks the operation as cancelled by the user.</summary>
    /// <returns>This result, so calls can be chained.</returns>
    public OperationResult MarkCancelled()
    {
        Cancelled = true;
        return this;
    }

    /// <summary>
    /// A result that failed before it started.
    /// </summary>
    /// <param name="operation">The operation's name.</param>
    /// <param name="path">The path involved.</param>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception behind it, if any.</param>
    /// <returns>The result.</returns>
    public static OperationResult Failed(string operation, string path, string message, Exception? exception = null) =>
        new OperationResult(operation).AddError(path, message, exception);

    /// <summary>
    /// The message an exception should be shown with - <see cref="Exception.Message"/> for the
    /// exceptions the file system raises, prefixed with the type for anything more surprising.
    /// </summary>
    /// <param name="exception">The exception to describe.</param>
    /// <returns>The message.</returns>
    public static string Describe(Exception exception)
    {
        if (exception is null)
        {
            return string.Empty;
        }

        return exception switch
        {
            UnauthorizedAccessException => "Access denied",
            FileNotFoundException => "The file no longer exists",
            DirectoryNotFoundException => "The folder no longer exists",
            PathTooLongException => "The path is too long",
            IOException => exception.Message,
            _ => $"{exception.GetType().Name}: {exception.Message}",
        };
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        string state = Cancelled ? "cancelled" : HasErrors ? $"{_errors.Count} error(s)" : "ok";
        return $"{Operation}: {state}, {FilesProcessed} file(s), {DirectoriesProcessed} folder(s), " +
               $"{BytesProcessed} byte(s), {SkippedCount} skipped";
    }
}
