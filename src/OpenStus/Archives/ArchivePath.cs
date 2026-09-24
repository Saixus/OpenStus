using OpenStus.Files;

namespace OpenStus.Archives;

/// <summary>
/// The paths a panel shows while it is inside an archive: the archive file's own path with the
/// path inside the archive appended, <c>C:\Downloads\tools.zip\bin\run.cmd</c>.
/// </summary>
/// <remarks>
/// Such a path reads naturally in the panel title and on the command line, walks up with the
/// ordinary parent rules - the parent of the archive's root is the folder holding the archive -
/// and survives the folder history and the saved tabs, because <see cref="TrySplit"/> can always
/// take it apart again.
/// </remarks>
public static class ArchivePath
{
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Builds the panel path for a place inside an archive.</summary>
    /// <param name="archiveFile">The archive file.</param>
    /// <param name="innerPath">The <c>/</c> separated path inside it; empty for its root.</param>
    /// <returns>The combined path, with platform separators.</returns>
    public static string Combine(string archiveFile, string? innerPath) =>
        string.IsNullOrEmpty(innerPath)
            ? archiveFile
            : archiveFile + Path.DirectorySeparatorChar + innerPath.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// Takes the path inside a known archive back out of a panel path.
    /// </summary>
    /// <param name="path">The panel path.</param>
    /// <param name="archiveFile">The archive file.</param>
    /// <param name="innerPath">The <c>/</c> separated path inside the archive; empty for its root.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is the archive or lies inside it.</returns>
    public static bool TryGetInner(string path, string archiveFile, out string innerPath)
    {
        innerPath = string.Empty;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(archiveFile))
        {
            return false;
        }

        if (string.Equals(path, archiveFile, PathComparison))
        {
            return true;
        }

        if (path.Length <= archiveFile.Length + 1 ||
            !path.StartsWith(archiveFile, PathComparison) ||
            !IsSeparator(path[archiveFile.Length]))
        {
            return false;
        }

        innerPath = path[(archiveFile.Length + 1)..]
            .Replace('\\', '/')
            .Trim('/');
        return true;
    }

    /// <summary>
    /// Splits a path that runs through an archive into the archive file and the path inside it.
    /// Touches the disk: the path is walked up until something on it exists.
    /// </summary>
    /// <param name="path">The path to split.</param>
    /// <param name="archiveFile">The archive file.</param>
    /// <param name="innerPath">The <c>/</c> separated path inside it; empty for its root.</param>
    /// <returns>
    /// <see langword="true"/> when the nearest existing thing on the path is a supported archive
    /// file; <see langword="false"/> for an ordinary folder, a missing path, or any other file.
    /// </returns>
    public static bool TrySplit(string? path, out string archiveFile, out string innerPath)
    {
        archiveFile = string.Empty;
        innerPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string? candidate = FileSystemProvider.NormalizeDisplayPath(path);
        var tail = new List<string>();

        try
        {
            while (!string.IsNullOrEmpty(candidate))
            {
                if (Directory.Exists(candidate))
                {
                    return false;
                }

                if (File.Exists(candidate))
                {
                    if (!ArchiveFile.CanOpen(candidate))
                    {
                        return false;
                    }

                    tail.Reverse();
                    archiveFile = candidate;
                    innerPath = string.Join('/', tail);
                    return true;
                }

                tail.Add(Path.GetFileName(candidate));
                candidate = Path.GetDirectoryName(candidate);
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Whether a panel could show a path: a folder that exists, or a place inside an archive.</summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> when the path can be navigated to.</returns>
    public static bool CanNavigate(string? path) =>
        FileSystemProvider.DirectoryExists(path) || TrySplit(path, out _, out _);

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
