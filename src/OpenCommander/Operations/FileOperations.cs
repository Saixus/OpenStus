using System.Buffers;
using OpenCommander.Core;
using OpenCommander.Files;

namespace OpenCommander.Operations;

/// <summary>
/// Asks the user what to do about a destination file that already exists.
/// </summary>
/// <param name="source">The file being copied or moved.</param>
/// <param name="target">The file already sitting at the destination.</param>
/// <param name="newName">
/// On entry the destination's current name; the prompt may replace it and answer
/// <see cref="DialogResult.Rename"/> to write under that name instead. A bare name is taken relative
/// to the destination folder; a rooted path is used as it stands.
/// </param>
/// <returns>
/// <see cref="DialogResult.Overwrite"/>, <see cref="DialogResult.All"/> (overwrite this and every
/// later one), <see cref="DialogResult.Skip"/>, <see cref="DialogResult.SkipAll"/>,
/// <see cref="DialogResult.Append"/>, <see cref="DialogResult.Rename"/>, or
/// <see cref="DialogResult.Cancel"/> to stop the whole operation.
/// </returns>
public delegate DialogResult OverwritePrompt(FileEntry source, FileInfo target, ref string newName);

/// <summary>
/// Tells the user something went wrong and asks how to carry on.
/// </summary>
/// <param name="operation">The step that failed: <c>"Copy"</c>, <c>"Move"</c>, <c>"Delete"</c>...</param>
/// <param name="path">The full path being worked on.</param>
/// <param name="error">The failure.</param>
/// <returns>
/// <see cref="DialogResult.Retry"/> to try the same step again, <see cref="DialogResult.Skip"/> to
/// give up on this item, <see cref="DialogResult.SkipAll"/> to give up on this and every later
/// failure without asking again, or <see cref="DialogResult.Cancel"/> to stop.
/// </returns>
public delegate DialogResult ErrorPrompt(string operation, string path, Exception error);

/// <summary>
/// Copy, move, delete, create folder and rename - synchronous, cancellable, progress reporting, and
/// completely unaware of dialogs.
/// </summary>
/// <remarks>
/// <para>
/// Every question goes out through <see cref="OverwritePrompt"/> or <see cref="ErrorPrompt"/>, and
/// every answer of the "...all" kind is remembered for the rest of the operation. Cancellation is a
/// single flag on <see cref="OperationProgress"/>, polled at each 64 KB block and between items.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> Bad arguments, a vanished source, a full disk and a prompt delegate
/// that itself throws all come back as an <see cref="OperationResult"/> carrying
/// <see cref="OperationResult.Errors"/>, because a file manager that unwinds out of the render loop
/// is worse than one that reports "access denied".
/// </para>
/// </remarks>
public static class FileOperations
{
    private const int RenameLoopGuard = 64;

    private static readonly EnumerationOptions ChildOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.PlatformDefault,
    };

    /// <summary>The attributes a copy carries over; the structural ones are never copied.</summary>
    private const FileAttributes CopyableAttributes =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
        FileAttributes.Archive | FileAttributes.NotContentIndexed | FileAttributes.Temporary;

    private enum ErrorAction
    {
        Retry,
        Skip,
        Cancel,
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Copies files and folders to <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// When there is one source and <paramref name="destination"/> is neither an existing folder nor
    /// spelled with a trailing separator, the destination is the new <em>name</em>; otherwise every
    /// source lands inside it under its own name. Copying a folder into itself or into one of its own
    /// descendants is detected and refused.
    /// </remarks>
    /// <param name="sources">What to copy. <c>".."</c> entries are ignored.</param>
    /// <param name="destination">The destination folder, or the new name for a single source.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="progress">The progress to fill in, or <see langword="null"/> for a throwaway one.</param>
    /// <param name="onProgress">Called at most ~30 times a second while the copy runs.</param>
    /// <param name="onOverwrite">Asked about an existing destination file. Null answers Skip.</param>
    /// <param name="onError">Asked about a failure. Null answers Skip and records the error.</param>
    /// <returns>What happened. Never <see langword="null"/>.</returns>
    public static OperationResult Copy(
        IReadOnlyList<FileEntry> sources,
        string destination,
        OperationOptions? options = null,
        OperationProgress? progress = null,
        Action? onProgress = null,
        OverwritePrompt? onOverwrite = null,
        ErrorPrompt? onError = null) =>
        Transfer(sources, destination, options, progress, onProgress, onOverwrite, onError, move: false);

    /// <summary>
    /// Moves files and folders to <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// Within one volume this is a rename - <see cref="Directory.Move"/> relocates a whole tree
    /// without reading a byte. Across volumes, and whenever the destination already exists and has to
    /// be merged, it falls back to copying and then deleting the source.
    /// <see cref="OperationOptions.ForceCopyThenDelete"/> disables the fast path entirely.
    /// </remarks>
    /// <param name="sources">What to move. <c>".."</c> entries are ignored.</param>
    /// <param name="destination">The destination folder, or the new name for a single source.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="progress">The progress to fill in, or <see langword="null"/> for a throwaway one.</param>
    /// <param name="onProgress">Called at most ~30 times a second while the move runs.</param>
    /// <param name="onOverwrite">Asked about an existing destination file. Null answers Skip.</param>
    /// <param name="onError">Asked about a failure. Null answers Skip and records the error.</param>
    /// <returns>What happened. Never <see langword="null"/>.</returns>
    public static OperationResult Move(
        IReadOnlyList<FileEntry> sources,
        string destination,
        OperationOptions? options = null,
        OperationProgress? progress = null,
        Action? onProgress = null,
        OverwritePrompt? onOverwrite = null,
        ErrorPrompt? onError = null) =>
        Transfer(sources, destination, options, progress, onProgress, onOverwrite, onError, move: true);

    /// <summary>
    /// Deletes files and folders, to the recycle bin when
    /// <see cref="OperationOptions.UseRecycleBin"/> is set and the platform has one.
    /// </summary>
    /// <param name="sources">What to delete. <c>".."</c> entries are ignored.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="progress">The progress to fill in, or <see langword="null"/> for a throwaway one.</param>
    /// <param name="onProgress">Called at most ~30 times a second while the delete runs.</param>
    /// <param name="onError">
    /// Asked about a failure, and - when <see cref="OperationOptions.ConfirmReadOnlyDelete"/> is set -
    /// about each read-only file. Answering Retry, Yes, Ok or All deletes it anyway.
    /// </param>
    /// <returns>What happened. Never <see langword="null"/>.</returns>
    public static OperationResult Delete(
        IReadOnlyList<FileEntry> sources,
        OperationOptions? options = null,
        OperationProgress? progress = null,
        Action? onProgress = null,
        ErrorPrompt? onError = null)
    {
        var result = new OperationResult("Delete");
        var context = new Context("Delete", options ?? new OperationOptions(), progress ?? new OperationProgress(), result)
        {
            OnProgress = onProgress,
            OnError = onError,
        };

        try
        {
            DeleteCore(sources, context);
        }
        catch (Exception e)
        {
            result.AddError(context.Progress.CurrentSource, e);
        }

        context.Report(force: true);
        return result;
    }

    /// <summary>
    /// Creates a folder, including any missing parents.
    /// </summary>
    /// <param name="path">The folder to create; relative paths resolve against the process directory.</param>
    /// <returns>What happened. Never <see langword="null"/>, never throwing.</returns>
    public static OperationResult CreateDirectory(string path)
    {
        var result = new OperationResult("Create folder");

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return result.AddError(string.Empty, "No folder name given");
            }

            string full = Path.GetFullPath(path.Trim().Trim('"'));

            if (File.Exists(full))
            {
                return result.AddError(full, "A file with that name already exists");
            }

            if (Directory.Exists(full))
            {
                return result.AddError(full, "The folder already exists");
            }

            Directory.CreateDirectory(full);
            result.DirectoriesProcessed++;
            return result;
        }
        catch (Exception e)
        {
            return result.AddError(path ?? string.Empty, e);
        }
    }

    /// <summary>
    /// Renames an entry, or moves it when the new name carries a path.
    /// </summary>
    /// <remarks>
    /// A name that differs from the current one only in case is allowed and performed in place; any
    /// other existing target is refused rather than silently overwritten, because F6-with-a-typo must
    /// not destroy a file.
    /// </remarks>
    /// <param name="source">The entry to rename. The <c>".."</c> entry is refused.</param>
    /// <param name="newName">The new name, or a relative or absolute path.</param>
    /// <returns>What happened. Never <see langword="null"/>, never throwing.</returns>
    public static OperationResult Rename(FileEntry source, string newName)
    {
        var result = new OperationResult("Rename");

        try
        {
            if (source is null || string.IsNullOrWhiteSpace(source.FullPath))
            {
                return result.AddError(string.Empty, "Nothing to rename");
            }

            if (source.IsParent)
            {
                return result.AddError(source.FullPath, "The parent folder entry cannot be renamed");
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                return result.AddError(source.FullPath, "No new name given");
            }

            string wanted = newName.Trim().Trim('"');
            if (wanted.Length == 0)
            {
                return result.AddError(source.FullPath, "No new name given");
            }

            string sourceFull = Path.GetFullPath(source.FullPath);
            string parent = Path.GetDirectoryName(sourceFull) ?? sourceFull;

            string target = Path.IsPathRooted(wanted)
                ? Path.GetFullPath(wanted)
                : Path.GetFullPath(Path.Combine(parent, wanted));

            if (string.Equals(target, sourceFull, StringComparison.Ordinal))
            {
                // Nothing to do, and certainly nothing to fail over.
                return result;
            }

            bool caseOnlyChange = string.Equals(target, sourceFull, PathComparison);
            if (!caseOnlyChange && (File.Exists(target) || Directory.Exists(target)))
            {
                return result.AddError(target, "An item with that name already exists");
            }

            string? targetParent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            if (source.IsDirectory)
            {
                Directory.Move(sourceFull, target);
                result.DirectoriesProcessed++;
            }
            else
            {
                File.Move(sourceFull, target, overwrite: false);
                result.FilesProcessed++;
            }

            return result;
        }
        catch (Exception e)
        {
            return result.AddError(source?.FullPath ?? string.Empty, e);
        }
    }

    // ----------------------------------------------------------------- copy / move

    private static OperationResult Transfer(
        IReadOnlyList<FileEntry> sources,
        string destination,
        OperationOptions? options,
        OperationProgress? progress,
        Action? onProgress,
        OverwritePrompt? onOverwrite,
        ErrorPrompt? onError,
        bool move)
    {
        string name = move ? "Move" : "Copy";
        var result = new OperationResult(name);
        var context = new Context(name, options ?? new OperationOptions(), progress ?? new OperationProgress(), result)
        {
            IsMove = move,
            OnProgress = onProgress,
            OnOverwrite = onOverwrite,
            OnError = onError,
        };

        try
        {
            TransferCore(sources, destination, context);
        }
        catch (Exception e)
        {
            result.AddError(destination ?? string.Empty, e);
        }

        context.Report(force: true);
        return result;
    }

    private static void TransferCore(IReadOnlyList<FileEntry> sources, string destination, Context ctx)
    {
        OperationResult result = ctx.Result;
        ctx.Progress.Title = ctx.Name;

        List<FileEntry> items = Usable(sources);
        if (items.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            result.AddError(string.Empty, "No destination given");
            return;
        }

        string typed = destination.Trim().Trim('"');
        bool wantsFolder = items.Count > 1 ||
                           (typed.Length > 0 && Path.EndsInDirectorySeparator(typed));

        string destinationFull;
        try
        {
            destinationFull = Path.GetFullPath(typed);
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            result.AddError(typed, "The destination path is not valid", e);
            return;
        }

        bool intoFolder = wantsFolder || Directory.Exists(destinationFull);

        if (intoFolder && File.Exists(destinationFull))
        {
            result.AddError(destinationFull, "A file with that name is in the way");
            return;
        }

        if (intoFolder && !Directory.Exists(destinationFull) && !EnsureDirectory(destinationFull, ctx))
        {
            return;
        }

        var plan = new List<(FileEntry Entry, string Target)>(items.Count);
        foreach (FileEntry entry in items)
        {
            string sourceFull;
            try
            {
                sourceFull = Path.GetFullPath(entry.FullPath);
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                result.AddError(entry.FullPath, "The source path is not valid", e);
                continue;
            }

            if (!File.Exists(sourceFull) && !Directory.Exists(sourceFull))
            {
                result.AddError(sourceFull, "The item no longer exists");
                continue;
            }

            string target;
            try
            {
                target = Path.GetFullPath(intoFolder ? Path.Combine(destinationFull, entry.Name) : destinationFull);
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                result.AddError(entry.Name, "The destination path is not valid", e);
                continue;
            }

            if (string.Equals(target, sourceFull, PathComparison))
            {
                result.AddError(sourceFull, "The source and the destination are the same");
                continue;
            }

            if (entry.IsDirectory && IsSameOrBelow(target, sourceFull))
            {
                result.AddError(
                    sourceFull,
                    $"\"{entry.Name}\" cannot be {(ctx.IsMove ? "moved" : "copied")} into itself");
                continue;
            }

            plan.Add((entry, target));
        }

        if (plan.Count == 0)
        {
            return;
        }

        if (ctx.Options.CountTotalsFirst)
        {
            long files = 0;
            long bytes = 0;
            foreach ((FileEntry entry, _) in plan)
            {
                if (ctx.IsCancelled)
                {
                    break;
                }

                (long Files, long Bytes) counted = Count(entry.FullPath, entry.IsDirectory, ctx);
                files += counted.Files;
                bytes += counted.Bytes;
            }

            ctx.Progress.TotalFiles = files;
            ctx.Progress.TotalBytes = bytes;
            ctx.Progress.TotalsKnown = !ctx.IsCancelled;
            ctx.Report(force: true);
        }

        foreach ((FileEntry entry, string target) in plan)
        {
            if (ctx.IsCancelled)
            {
                break;
            }

            if (entry.IsDirectory)
            {
                TransferDirectory(entry.FullPath, target, ctx);
            }
            else
            {
                TransferFile(entry.FullPath, target, ctx);
            }
        }

        if (ctx.IsCancelled)
        {
            result.MarkCancelled();
        }
    }

    private static void TransferDirectory(string source, string target, Context ctx)
    {
        if (ctx.IsCancelled)
        {
            return;
        }

        ctx.Progress.CurrentSource = source;
        ctx.Progress.CurrentTarget = target;
        ctx.Report();

        bool isLink = IsLink(source);
        bool targetExists = Directory.Exists(target);

        if (ctx.IsMove && !ctx.Options.ForceCopyThenDelete && !targetExists && !File.Exists(target) &&
            SameVolume(source, target) && TryFastMoveDirectory(source, target, ctx))
        {
            return;
        }

        if (!EnsureDirectory(target, ctx))
        {
            return;
        }

        if (!targetExists)
        {
            ctx.Result.DirectoriesProcessed++;
        }

        if (!isLink || ctx.Options.FollowLinks)
        {
            List<FileSystemInfo>? children = ReadChildren(source, ctx);
            if (children is null)
            {
                return;
            }

            foreach (FileSystemInfo child in children)
            {
                if (ctx.IsCancelled)
                {
                    return;
                }

                bool childIsDirectory;
                try
                {
                    childIsDirectory = (child.Attributes & FileAttributes.Directory) != 0;
                }
                catch (Exception e) when (IsFileSystemException(e))
                {
                    continue;
                }

                string childTarget = Path.Combine(target, child.Name);
                if (childIsDirectory)
                {
                    TransferDirectory(child.FullName, childTarget, ctx);
                }
                else
                {
                    TransferFile(child.FullName, childTarget, ctx);
                }
            }
        }

        // Attributes go on last: a target folder marked ReadOnly or Hidden up front makes writing
        // into it awkward on Windows.
        ApplyDirectoryMetadata(source, target, ctx);

        if (ctx.IsMove && !ctx.IsCancelled)
        {
            RemoveEmptySourceDirectory(source, ctx, isLink && !ctx.Options.FollowLinks);
        }
    }

    private static bool TryFastMoveDirectory(string source, string target, Context ctx)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // Cross-volume, a lock, or a race with someone creating the target: fall back to the
            // copy-then-delete path, which knows how to ask the user about all of that.
            return false;
        }

        (long Files, long Bytes) totals = ctx.LookupTotals(source);
        ctx.Progress.AddCompleted(totals.Files, totals.Bytes);
        ctx.Result.FilesProcessed += (int)Math.Min(totals.Files, int.MaxValue);
        ctx.Result.BytesProcessed += totals.Bytes;
        ctx.Result.DirectoriesProcessed++;
        ctx.Report(force: true);
        return true;
    }

    private static void TransferFile(string source, string target, Context ctx)
    {
        if (ctx.IsCancelled)
        {
            return;
        }

        OperationResult result = ctx.Result;
        OperationProgress progress = ctx.Progress;

        long size = SafeLength(source);
        progress.BeginFile(source, target, size);
        ctx.Report();

        string finalTarget = target;
        bool append = false;
        bool overwrite = false;
        int renameGuard = 0;

        while (true)
        {
            if (ctx.IsCancelled)
            {
                return;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(finalTarget);
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                result.AddError(finalTarget, "The destination path is not valid", e);
                progress.CompleteFile();
                return;
            }

            if (!info.Exists)
            {
                if (Directory.Exists(finalTarget))
                {
                    result.AddError(finalTarget, "A folder with that name is in the way");
                    result.SkippedCount++;
                    progress.CompleteFile();
                    return;
                }

                break;
            }

            if (string.Equals(info.FullName, Path.GetFullPath(source), PathComparison))
            {
                result.AddError(source, "The source and the destination are the same");
                result.SkippedCount++;
                progress.CompleteFile();
                return;
            }

            if (!ctx.Options.ConfirmOverwrite)
            {
                overwrite = true;
                break;
            }

            string newName = info.Name;
            DialogResult answer = ctx.ResolveOverwrite(EntryFor(source), info, ref newName);

            if (answer == DialogResult.Rename)
            {
                string renamed = ++renameGuard >= RenameLoopGuard
                    ? UniqueName(finalTarget)
                    : ResolveRenameTarget(finalTarget, newName);

                if (string.Equals(renamed, finalTarget, PathComparison))
                {
                    renamed = UniqueName(finalTarget);
                }

                finalTarget = renamed;
                progress.CurrentTarget = finalTarget;
                continue;
            }

            switch (answer)
            {
                case DialogResult.Overwrite:
                    overwrite = true;
                    break;

                case DialogResult.Append:
                    append = true;
                    break;

                case DialogResult.Skip:
                    result.SkippedCount++;
                    progress.CompleteFile();
                    ctx.Report();
                    return;

                default:
                    ctx.Cancel();
                    return;
            }

            break;
        }

        string? targetFolder = Path.GetDirectoryName(finalTarget);
        if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder) &&
            !EnsureDirectory(targetFolder, ctx))
        {
            return;
        }

        if (ctx.IsMove && !append && !ctx.Options.ForceCopyThenDelete && SameVolume(source, finalTarget) &&
            TryFastMoveFile(source, finalTarget, overwrite, ctx))
        {
            progress.CompleteFile();
            result.FilesProcessed++;
            result.BytesProcessed += size;
            ctx.Report();
            return;
        }

        if (!CopyFileData(source, finalTarget, append, ctx))
        {
            return;
        }

        if (ctx.IsMove)
        {
            DeleteMovedSource(source, ctx);
        }
    }

    private static bool TryFastMoveFile(string source, string target, bool overwrite, Context ctx)
    {
        try
        {
            if (overwrite && File.Exists(target))
            {
                ClearBlockingAttributes(target);
            }

            File.Move(source, target, overwrite);
            return true;
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // Every reason File.Move can fail here - a different volume, a lock, a permission - is
            // also a reason to try the slow path, which will ask the user if it fails too.
            return false;
        }
    }

    private static bool CopyFileData(string source, string target, bool append, Context ctx)
    {
        OperationResult result = ctx.Result;
        OperationProgress progress = ctx.Progress;

        while (true)
        {
            if (ctx.IsCancelled)
            {
                return false;
            }

            bool targetExisted = File.Exists(target);

            try
            {
                if (targetExisted)
                {
                    // Windows refuses FileMode.Create over a hidden or read-only file; the source's
                    // own attributes are put back afterwards.
                    ClearBlockingAttributes(target);
                }

                CopyStream(source, target, append, ctx);
                ApplyFileMetadata(source, target, ctx);

                result.FilesProcessed++;
                progress.CompleteFile();
                ctx.Report();
                return true;
            }
            catch (OperationCanceledException)
            {
                RollBack(target, targetExisted, append, ctx);
                ctx.Cancel();
                return false;
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                RollBack(target, targetExisted, append, ctx);

                switch (ctx.HandleError(ctx.Name, source, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        result.SkippedCount++;
                        progress.CompleteFile();
                        ctx.Report();
                        return false;

                    default:
                        ctx.Cancel();
                        return false;
                }
            }
        }
    }

    private static void CopyStream(string source, string target, bool append, Context ctx)
    {
        int blockSize = ctx.Options.BufferSize;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(blockSize);

        try
        {
            using var input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);

            using var output = new FileStream(
                target, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan);

            int read;
            while ((read = input.Read(buffer, 0, blockSize)) > 0)
            {
                if (ctx.IsCancelled)
                {
                    throw new OperationCanceledException();
                }

                output.Write(buffer, 0, read);
                ctx.Progress.Advance(read);
                ctx.Result.BytesProcessed += read;
                ctx.Report();
            }

            output.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void RollBack(string target, bool targetExisted, bool append, Context ctx)
    {
        // Un-charge whatever this attempt wrote, so a retry does not count the same bytes twice.
        long written = ctx.Progress.CurrentFileDone;
        if (written > 0)
        {
            ctx.Progress.DoneBytes -= written;
            ctx.Result.BytesProcessed -= written;
            ctx.Progress.CurrentFileDone = 0;
        }

        if (targetExisted || append)
        {
            // The original is already gone, or we were adding to a file the user asked us to keep;
            // deleting it now would destroy more than we created.
            return;
        }

        try
        {
            if (File.Exists(target))
            {
                ClearBlockingAttributes(target);
                File.Delete(target);
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // A partial file we cannot remove is not worth failing the whole operation over.
        }
    }

    private static void DeleteMovedSource(string source, Context ctx)
    {
        while (true)
        {
            if (ctx.IsCancelled)
            {
                return;
            }

            try
            {
                ClearBlockingAttributes(source);
                File.Delete(source);
                return;
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                switch (ctx.HandleError("Move", source, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        return;

                    default:
                        ctx.Cancel();
                        return;
                }
            }
        }
    }

    private static void RemoveEmptySourceDirectory(string source, Context ctx, bool isLink)
    {
        try
        {
            // A link enumerates whatever it points at, so its emptiness says nothing about whether
            // the link itself can go.
            if (!isLink && Directory.EnumerateFileSystemEntries(source).Any())
            {
                // Something inside was skipped or failed; the folder legitimately stays behind.
                ctx.Result.SkippedCount++;
                return;
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            ctx.Result.AddError("Move", source, OperationResult.Describe(e), e);
            return;
        }

        while (true)
        {
            try
            {
                ClearBlockingAttributes(source);
                Directory.Delete(source, recursive: false);
                return;
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                switch (ctx.HandleError("Move", source, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        return;

                    default:
                        ctx.Cancel();
                        return;
                }
            }
        }
    }

    // ----------------------------------------------------------------- delete

    private static void DeleteCore(IReadOnlyList<FileEntry> sources, Context ctx)
    {
        ctx.Progress.Title = "Delete";

        List<FileEntry> items = Usable(sources);
        if (items.Count == 0)
        {
            return;
        }

        if (ctx.Options.CountTotalsFirst)
        {
            long files = 0;
            long bytes = 0;
            foreach (FileEntry entry in items)
            {
                if (ctx.IsCancelled)
                {
                    break;
                }

                (long Files, long Bytes) counted = Count(entry.FullPath, entry.IsDirectory, ctx);
                files += counted.Files;
                bytes += counted.Bytes;
            }

            ctx.Progress.TotalFiles = files;
            ctx.Progress.TotalBytes = bytes;
            ctx.Progress.TotalsKnown = !ctx.IsCancelled;
            ctx.Report(force: true);
        }

        bool recycle = ctx.Options.UseRecycleBin && RecycleBin.IsAvailable;

        foreach (FileEntry entry in items)
        {
            if (ctx.IsCancelled)
            {
                break;
            }

            string path;
            try
            {
                path = Path.GetFullPath(entry.FullPath);
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                ctx.Result.AddError(entry.FullPath, "The path is not valid", e);
                continue;
            }

            bool isDirectory = Directory.Exists(path);
            if (!isDirectory && !File.Exists(path))
            {
                ctx.Result.AddError(path, "The item no longer exists");
                continue;
            }

            ctx.Progress.CurrentSource = path;
            ctx.Progress.CurrentTarget = string.Empty;
            ctx.Report();

            if (recycle)
            {
                RecycleOne(path, isDirectory, ctx);
            }
            else
            {
                DeleteRecursive(path, isDirectory, ctx);
            }
        }

        if (ctx.IsCancelled)
        {
            ctx.Result.MarkCancelled();
        }
    }

    private static void RecycleOne(string path, bool isDirectory, Context ctx)
    {
        while (true)
        {
            if (ctx.IsCancelled)
            {
                return;
            }

            if (RecycleBin.TryDelete(path, out string error))
            {
                (long Files, long Bytes) totals = isDirectory
                    ? ctx.LookupTotals(path)
                    : (1, SafeLength(path));

                ctx.Progress.AddCompleted(totals.Files, totals.Bytes);
                ctx.Result.FilesProcessed += (int)Math.Min(totals.Files, int.MaxValue);
                ctx.Result.BytesProcessed += totals.Bytes;
                if (isDirectory)
                {
                    ctx.Result.DirectoriesProcessed++;
                }

                ctx.Report(force: true);
                return;
            }

            switch (ctx.HandleError("Delete", path, new IOException(error)))
            {
                case ErrorAction.Retry:
                    continue;

                case ErrorAction.Skip:
                    ctx.Result.SkippedCount++;
                    return;

                default:
                    ctx.Cancel();
                    return;
            }
        }
    }

    private static void DeleteRecursive(string path, bool isDirectory, Context ctx)
    {
        if (ctx.IsCancelled)
        {
            return;
        }

        if (!isDirectory)
        {
            DeleteOneFile(path, ctx);
            return;
        }

        // A link is removed as a link; walking into it would delete the target's contents.
        bool isLink = IsLink(path);
        if (!isLink || ctx.Options.FollowLinks)
        {
            List<FileSystemInfo>? children = ReadChildren(path, ctx);
            if (children is null)
            {
                return;
            }

            foreach (FileSystemInfo child in children)
            {
                if (ctx.IsCancelled)
                {
                    return;
                }

                bool childIsDirectory;
                try
                {
                    childIsDirectory = (child.Attributes & FileAttributes.Directory) != 0;
                }
                catch (Exception e) when (IsFileSystemException(e))
                {
                    continue;
                }

                DeleteRecursive(child.FullName, childIsDirectory, ctx);
            }
        }

        if (ctx.IsCancelled)
        {
            return;
        }

        RemoveDirectory(path, ctx, isLink && !ctx.Options.FollowLinks);
    }

    private static void DeleteOneFile(string path, Context ctx)
    {
        while (true)
        {
            if (ctx.IsCancelled)
            {
                return;
            }

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return;
                }

                long size = SafeLength(info);
                ctx.Progress.BeginFile(path, string.Empty, size);

                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    switch (ctx.ConfirmReadOnly(path))
                    {
                        case ErrorAction.Skip:
                            ctx.Result.SkippedCount++;
                            ctx.Progress.CompleteFile();
                            return;

                        case ErrorAction.Cancel:
                            ctx.Cancel();
                            return;
                    }
                }

                ClearBlockingAttributes(path);
                File.Delete(path);

                ctx.Result.FilesProcessed++;
                ctx.Result.BytesProcessed += size;
                ctx.Progress.CompleteFile();
                ctx.Report();
                return;
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                switch (ctx.HandleError("Delete", path, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        ctx.Result.SkippedCount++;
                        return;

                    default:
                        ctx.Cancel();
                        return;
                }
            }
        }
    }

    private static void RemoveDirectory(string path, Context ctx, bool isLink)
    {
        while (true)
        {
            if (ctx.IsCancelled)
            {
                return;
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                if (!isLink && Directory.EnumerateFileSystemEntries(path).Any())
                {
                    // Left behind because something inside it was skipped.
                    ctx.Result.SkippedCount++;
                    return;
                }

                ClearBlockingAttributes(path);
                Directory.Delete(path, recursive: false);
                ctx.Result.DirectoriesProcessed++;
                ctx.Report();
                return;
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                switch (ctx.HandleError("Delete", path, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        ctx.Result.SkippedCount++;
                        return;

                    default:
                        ctx.Cancel();
                        return;
                }
            }
        }
    }

    // ----------------------------------------------------------------- shared helpers

    private static List<FileEntry> Usable(IReadOnlyList<FileEntry>? sources)
    {
        var items = new List<FileEntry>();
        if (sources is null)
        {
            return items;
        }

        foreach (FileEntry entry in sources)
        {
            if (entry is null || entry.IsParent || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                continue;
            }

            items.Add(entry);
        }

        return items;
    }

    private static (long Files, long Bytes) Count(string path, bool isDirectory, Context ctx)
    {
        if (ctx.IsCancelled)
        {
            return (0, 0);
        }

        if (!isDirectory && !Directory.Exists(path))
        {
            return (1, SafeLength(path));
        }

        return CountDirectory(path, ctx);
    }

    private static (long Files, long Bytes) CountDirectory(string path, Context ctx)
    {
        long files = 0;
        long bytes = 0;

        if (IsLink(path) && !ctx.Options.FollowLinks)
        {
            ctx.Totals[Normalize(path)] = (0, 0);
            return (0, 0);
        }

        try
        {
            var directory = new DirectoryInfo(path);
            if (directory.Exists)
            {
                foreach (FileSystemInfo info in directory.EnumerateFileSystemInfos("*", ChildOptions))
                {
                    if (ctx.IsCancelled)
                    {
                        break;
                    }

                    if ((info.Attributes & FileAttributes.Directory) != 0)
                    {
                        (long Files, long Bytes) sub = CountDirectory(info.FullName, ctx);
                        files += sub.Files;
                        bytes += sub.Bytes;
                    }
                    else
                    {
                        files++;
                        bytes += info is FileInfo file ? SafeLength(file) : 0;
                    }
                }
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // An unreadable folder simply contributes nothing to the total.
        }

        ctx.Totals[Normalize(path)] = (files, bytes);
        ctx.Report();
        return (files, bytes);
    }

    private static List<FileSystemInfo>? ReadChildren(string path, Context ctx)
    {
        while (true)
        {
            if (ctx.IsCancelled)
            {
                return null;
            }

            try
            {
                return [.. new DirectoryInfo(path).EnumerateFileSystemInfos("*", ChildOptions)];
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                switch (ctx.HandleError(ctx.Name, path, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        ctx.Result.SkippedCount++;
                        return null;

                    default:
                        ctx.Cancel();
                        return null;
                }
            }
        }
    }

    private static bool EnsureDirectory(string path, Context ctx)
    {
        while (true)
        {
            if (ctx.IsCancelled)
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception e) when (IsFileSystemException(e))
            {
                switch (ctx.HandleError("Create folder", path, e))
                {
                    case ErrorAction.Retry:
                        continue;

                    case ErrorAction.Skip:
                        ctx.Result.SkippedCount++;
                        return false;

                    default:
                        ctx.Cancel();
                        return false;
                }
            }
        }
    }

    private static void ApplyFileMetadata(string source, string target, Context ctx)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists)
            {
                return;
            }

            if (ctx.Options.PreserveTimestamps)
            {
                File.SetCreationTimeUtc(target, info.CreationTimeUtc);
                File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(target, info.LastAccessTimeUtc);
            }

            // Attributes last: ReadOnly would otherwise block the timestamp writes above.
            if (ctx.Options.PreserveAttributes)
            {
                FileAttributes attributes = info.Attributes & CopyableAttributes;
                if (attributes != 0)
                {
                    File.SetAttributes(target, attributes);
                }
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // Metadata is a nicety; a copy that got the bytes across has done its job.
        }
    }

    private static void ApplyDirectoryMetadata(string source, string target, Context ctx)
    {
        try
        {
            var info = new DirectoryInfo(source);
            if (!info.Exists)
            {
                return;
            }

            if (ctx.Options.PreserveTimestamps)
            {
                Directory.SetCreationTimeUtc(target, info.CreationTimeUtc);
                Directory.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
                Directory.SetLastAccessTimeUtc(target, info.LastAccessTimeUtc);
            }

            if (ctx.Options.PreserveAttributes)
            {
                FileAttributes attributes = info.Attributes & CopyableAttributes;
                if (attributes != 0)
                {
                    var targetInfo = new DirectoryInfo(target);
                    targetInfo.Attributes |= attributes;
                }
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // As above: best effort.
        }
    }

    private static void ClearBlockingAttributes(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            FileAttributes blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
            if ((attributes & blocking) != 0)
            {
                File.SetAttributes(path, attributes & ~blocking);
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // The caller is about to try the real operation and will report a proper failure.
        }
    }

    private static FileEntry EntryFor(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                return new FileEntry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    Size = SafeLength(info),
                    Modified = info.LastWriteTime,
                    Created = info.CreationTime,
                    Accessed = info.LastAccessTime,
                    Attributes = info.Attributes,
                };
            }
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            // Fall through to the bare entry below.
        }

        return new FileEntry
        {
            Name = Path.GetFileName(path),
            FullPath = path,
        };
    }

    private static string ResolveRenameTarget(string currentTarget, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return UniqueName(currentTarget);
        }

        string folder = Path.GetDirectoryName(currentTarget) ?? string.Empty;
        string wanted = newName.Trim().Trim('"');

        try
        {
            return Path.IsPathRooted(wanted)
                ? Path.GetFullPath(wanted)
                : Path.GetFullPath(Path.Combine(folder, wanted));
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return UniqueName(currentTarget);
        }
    }

    private static string UniqueName(string path)
    {
        string folder = Path.GetDirectoryName(path) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int index = 2; index < 10_000; index++)
        {
            string candidate = Path.Combine(folder, $"{baseName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{baseName} ({Guid.NewGuid():N}){extension}");
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return false;
        }
    }

    private static bool SameVolume(string a, string b)
    {
        try
        {
            string rootA = Path.GetPathRoot(Path.GetFullPath(a)) ?? string.Empty;
            string rootB = Path.GetPathRoot(Path.GetFullPath(b)) ?? string.Empty;

            if (rootA.Length == 0 || rootB.Length == 0)
            {
                return false;
            }

            return string.Equals(TrimSeparators(rootA), TrimSeparators(rootB), PathComparison);
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="root"/> itself or lives underneath it -
    /// the test that stops a folder being copied into its own subtree.
    /// </summary>
    private static bool IsSameOrBelow(string candidate, string root)
    {
        try
        {
            string a = TrimSeparators(Path.GetFullPath(candidate));
            string b = TrimSeparators(Path.GetFullPath(root));

            if (string.Equals(a, b, PathComparison))
            {
                return true;
            }

            if (a.Length <= b.Length || !a.StartsWith(b, PathComparison))
            {
                return false;
            }

            // A root ("/" or "C:\") keeps its separator, so everything under it already matches;
            // otherwise the next character has to be the separator or "C:\foo2" would look like it
            // lives inside "C:\foo".
            bool rootLike = b.Length > 0 &&
                            (b[^1] == Path.DirectorySeparatorChar || b[^1] == Path.AltDirectorySeparatorChar);

            return rootLike ||
                   a[b.Length] == Path.DirectorySeparatorChar ||
                   a[b.Length] == Path.AltDirectorySeparatorChar;
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return false;
        }
    }

    private static string TrimSeparators(string path)
    {
        int end = path.Length;
        while (end > 1 && (path[end - 1] == Path.DirectorySeparatorChar || path[end - 1] == Path.AltDirectorySeparatorChar))
        {
            end--;
        }

        return end == path.Length ? path : path[..end];
    }

    private static string Normalize(string path)
    {
        try
        {
            return TrimSeparators(Path.GetFullPath(path));
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return path;
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? SafeLength(info) : 0;
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return 0;
        }
    }

    private static long SafeLength(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (Exception e) when (IsFileSystemException(e))
        {
            return 0;
        }
    }

    private static bool IsFileSystemException(Exception e) =>
        e is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or System.Security.SecurityException;

    /// <summary>
    /// The mutable state one operation carries around: the options, the progress, the callbacks, the
    /// remembered "...all" answers, and the pre-pass totals per folder.
    /// </summary>
    private sealed class Context(string name, OperationOptions options, OperationProgress progress, OperationResult result)
    {
        private DialogResult? _overwriteAll;
        private bool _skipAllErrors;
        private bool _deleteReadOnlyAll;
        private bool _skipReadOnlyAll;
        private long _lastReport = -1;
        private bool _callbackFaulted;

        public string Name { get; } = name;

        public OperationOptions Options { get; } = options;

        public OperationProgress Progress { get; } = progress;

        public OperationResult Result { get; } = result;

        public bool IsMove { get; init; }

        public Action? OnProgress { get; init; }

        public OverwritePrompt? OnOverwrite { get; init; }

        public ErrorPrompt? OnError { get; init; }

        /// <summary>File and byte counts per folder, filled in by the pre-pass.</summary>
        public Dictionary<string, (long Files, long Bytes)> Totals { get; } = new(PathComparer);

        public bool IsCancelled => Progress.Cancelled || Result.Cancelled;

        public void Cancel()
        {
            Progress.Cancel();
            Result.MarkCancelled();
        }

        public (long Files, long Bytes) LookupTotals(string path) =>
            Totals.TryGetValue(Normalize(path), out (long Files, long Bytes) totals) ? totals : (0, 0);

        public void Report(bool force = false)
        {
            if (OnProgress is null)
            {
                return;
            }

            long now = Environment.TickCount64;
            if (!force && _lastReport >= 0 && now - _lastReport < Options.ProgressIntervalMs)
            {
                return;
            }

            _lastReport = now;

            try
            {
                OnProgress();
            }
            catch (OperationCanceledException)
            {
                Cancel();
            }
            catch (Exception e)
            {
                // A broken progress callback must not take the operation down with it, but the user
                // should hear about it once.
                if (!_callbackFaulted)
                {
                    _callbackFaulted = true;
                    Result.AddError(Name, Progress.CurrentSource, "The progress callback failed: " + e.Message, e);
                }
            }
        }

        public DialogResult ResolveOverwrite(FileEntry source, FileInfo target, ref string newName)
        {
            if (_overwriteAll == DialogResult.All)
            {
                return DialogResult.Overwrite;
            }

            if (_overwriteAll == DialogResult.SkipAll)
            {
                return DialogResult.Skip;
            }

            if (OnOverwrite is null)
            {
                // Nobody to ask, and silently destroying a file is the one answer that cannot be
                // undone.
                return DialogResult.Skip;
            }

            DialogResult answer;
            try
            {
                answer = OnOverwrite(source, target, ref newName);
            }
            catch (Exception e)
            {
                Result.AddError(Name, target.FullName, "The overwrite prompt failed: " + e.Message, e);
                return DialogResult.Cancel;
            }

            switch (answer)
            {
                case DialogResult.All:
                    _overwriteAll = DialogResult.All;
                    return DialogResult.Overwrite;

                case DialogResult.SkipAll:
                    _overwriteAll = DialogResult.SkipAll;
                    return DialogResult.Skip;

                case DialogResult.Overwrite:
                case DialogResult.Yes:
                case DialogResult.Ok:
                    return DialogResult.Overwrite;

                case DialogResult.Skip:
                case DialogResult.No:
                    return DialogResult.Skip;

                case DialogResult.Append:
                    return DialogResult.Append;

                case DialogResult.Rename:
                    return DialogResult.Rename;

                default:
                    return DialogResult.Cancel;
            }
        }

        public ErrorAction HandleError(string step, string path, Exception error)
        {
            if (IsCancelled)
            {
                return ErrorAction.Cancel;
            }

            if (_skipAllErrors || OnError is null)
            {
                Result.AddError(step, path, OperationResult.Describe(error), error);
                return ErrorAction.Skip;
            }

            DialogResult answer;
            try
            {
                answer = OnError(step, path, error);
            }
            catch (Exception e)
            {
                Result.AddError(step, path, "The error prompt failed: " + e.Message, e);
                return ErrorAction.Cancel;
            }

            switch (answer)
            {
                case DialogResult.Retry:
                    return ErrorAction.Retry;

                case DialogResult.SkipAll:
                    _skipAllErrors = true;
                    Result.AddError(step, path, OperationResult.Describe(error), error);
                    return ErrorAction.Skip;

                case DialogResult.Skip:
                case DialogResult.No:
                case DialogResult.Ok:
                case DialogResult.Yes:
                case DialogResult.All:
                    Result.AddError(step, path, OperationResult.Describe(error), error);
                    return ErrorAction.Skip;

                default:
                    return ErrorAction.Cancel;
            }
        }

        /// <summary>
        /// Asks whether a read-only file may be deleted, through the error prompt because that is the
        /// only channel <see cref="FileOperations.Delete"/> has.
        /// </summary>
        /// <returns>
        /// <see cref="ErrorAction.Retry"/> to go ahead, <see cref="ErrorAction.Skip"/> to leave it,
        /// <see cref="ErrorAction.Cancel"/> to stop.
        /// </returns>
        public ErrorAction ConfirmReadOnly(string path)
        {
            if (!Options.ConfirmReadOnlyDelete || _deleteReadOnlyAll || OnError is null)
            {
                return ErrorAction.Retry;
            }

            if (_skipReadOnlyAll)
            {
                return ErrorAction.Skip;
            }

            DialogResult answer;
            try
            {
                answer = OnError(
                    "Delete",
                    path,
                    new UnauthorizedAccessException($"\"{Path.GetFileName(path)}\" is read-only. Delete it anyway?"));
            }
            catch (Exception e)
            {
                Result.AddError("Delete", path, "The error prompt failed: " + e.Message, e);
                return ErrorAction.Cancel;
            }

            switch (answer)
            {
                case DialogResult.All:
                    _deleteReadOnlyAll = true;
                    return ErrorAction.Retry;

                case DialogResult.Retry:
                case DialogResult.Yes:
                case DialogResult.Ok:
                case DialogResult.Overwrite:
                    return ErrorAction.Retry;

                case DialogResult.SkipAll:
                    _skipReadOnlyAll = true;
                    return ErrorAction.Skip;

                case DialogResult.Skip:
                case DialogResult.No:
                    return ErrorAction.Skip;

                default:
                    return ErrorAction.Cancel;
            }
        }
    }
}
