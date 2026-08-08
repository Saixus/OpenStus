using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenCommander.Operations;

/// <summary>
/// Deletion through the platform's recycle bin.
/// </summary>
/// <remarks>
/// <para>
/// On Windows this is <c>SHFileOperationW</c> with <c>FOF_ALLOWUNDO</c>, which is the only documented
/// way to put something in the Recycle Bin - moving the files by hand into <c>$Recycle.Bin</c> does
/// not create the metadata that makes "Restore" work. The shell UI is suppressed
/// (<c>FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI</c>), because Open Commander asks its own
/// questions before it gets here.
/// </para>
/// <para>
/// Everywhere else there is no recycle bin this code can drive portably, so
/// <see cref="IsAvailable"/> is <see langword="false"/> and callers fall back to a permanent delete.
/// Note that even on Windows the shell silently deletes permanently when the volume has no bin - a
/// network share, most removable media, or a bin the user has switched off.
/// </para>
/// <para>
/// Nothing here throws: every entry point reports failure through a <c>bool</c> and an out message.
/// </para>
/// </remarks>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;

    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_NOERRORUI = 0x0400;

    /// <summary>Whether this platform has a recycle bin that <see cref="TryDelete(string, out string)"/> can use.</summary>
    public static bool IsAvailable => OperatingSystem.IsWindows();

    /// <summary>
    /// Moves one file or directory to the recycle bin.
    /// </summary>
    /// <param name="path">The full path to delete. A directory goes with everything inside it.</param>
    /// <param name="error">The failure message, or an empty string on success.</param>
    /// <returns><see langword="true"/> when the item reached the recycle bin.</returns>
    public static bool TryDelete(string path, out string error) => TryDelete([path], out error);

    /// <summary>
    /// Moves several files and directories to the recycle bin in one shell call, which is both
    /// faster and what makes them restore together.
    /// </summary>
    /// <param name="paths">The full paths to delete.</param>
    /// <param name="error">The failure message, or an empty string on success.</param>
    /// <returns>
    /// <see langword="true"/> when every item reached the recycle bin. A partial failure reports
    /// <see langword="false"/>; the caller should re-check what actually survived.
    /// </returns>
    public static bool TryDelete(IReadOnlyList<string> paths, out string error)
    {
        error = string.Empty;

        if (paths is null || paths.Count == 0)
        {
            return true;
        }

        if (!IsAvailable)
        {
            error = "This platform has no recycle bin";
            return false;
        }

        return TryShellDelete(paths, out error);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryShellDelete(IReadOnlyList<string> paths, out string error)
    {
        error = string.Empty;

        string list;
        try
        {
            var buffer = new System.Text.StringBuilder();
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                // The shell wants absolute paths, and it wants them NUL separated; the marshaller
                // supplies the final terminator that makes the block double-NUL terminated.
                buffer.Append(Path.GetFullPath(path)).Append('\0');
            }

            if (buffer.Length == 0)
            {
                return true;
            }

            list = buffer.ToString();
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            error = e.Message;
            return false;
        }

        var operation = new SHFILEOPSTRUCTW
        {
            Hwnd = IntPtr.Zero,
            Func = FO_DELETE,
            From = list,
            To = null,
            Flags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR,
            AnyOperationsAborted = 0,
            NameMappings = IntPtr.Zero,
            ProgressTitle = null,
        };

        int code;
        try
        {
            code = SHFileOperationW(ref operation);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            error = "The shell could not be called to recycle the item";
            return false;
        }

        if (code != 0)
        {
            error = DescribeShellError(code);
            return false;
        }

        if (operation.AnyOperationsAborted != 0)
        {
            error = "The operation was aborted";
            return false;
        }

        return true;
    }

    private static string DescribeShellError(int code) => code switch
    {
        0x71 => "The source and destination are the same file",
        0x74 => "A root folder cannot be deleted",
        0x75 => "The operation was cancelled",
        0x76 => "The destination is a subfolder of the source",
        0x7C => "The path is invalid",
        0x10000 => "An unknown error occurred at the destination",
        0x402 => "The item could not be found",
        0x10074 => "The folders are too deep to be recycled",
        _ => $"The shell reported error 0x{code:X}",
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr Hwnd;
        public uint Func;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;

        public ushort Flags;

        // A Win32 BOOL: four bytes, so never a C# bool in a marshalled struct.
        public int AnyOperationsAborted;

        public IntPtr NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW", SetLastError = false)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW fileOp);
}
