namespace OpenStus.Archives;

/// <summary>
/// Scratch folders for files taken out of an archive to be viewed, run or copied. Everything lives
/// under one folder per process in the system temp directory, removed when the shell exits.
/// </summary>
public static class TempArea
{
    private static readonly object Sync = new();
    private static string? _root;

    /// <summary>The per-process scratch root; created on first use.</summary>
    public static string Root
    {
        get
        {
            lock (Sync)
            {
                _root ??= Path.Combine(
                    Path.GetTempPath(),
                    "OpenStus",
                    Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(_root);
                return _root;
            }
        }
    }

    /// <summary>Creates a fresh, empty folder under <see cref="Root"/>.</summary>
    /// <returns>The folder's full path.</returns>
    public static string CreateDirectory()
    {
        string path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Deletes a folder and everything in it. Never throws.</summary>
    /// <param name="path">The folder.</param>
    public static void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                // Extracted files may carry the read-only bit the archive recorded.
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A file still held open by a program the user started from an archive stays behind;
            // the system's temp cleanup gets it later.
        }
    }

    /// <summary>Removes the whole per-process scratch root. Never throws.</summary>
    public static void Cleanup()
    {
        string? root;
        lock (Sync)
        {
            root = _root;
            _root = null;
        }

        Delete(root);
    }
}
