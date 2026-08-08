using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenCommander.Ui;

/// <summary>
/// The text clipboard an <see cref="Controls.EditControl"/> talks to.
/// </summary>
/// <remarks>
/// Injectable so that dialogs stay testable: a test can hand an edit control an in-memory
/// implementation and never touch the machine clipboard.
/// </remarks>
public interface IClipboard
{
    /// <summary>Reads the clipboard.</summary>
    /// <returns>The text, or <see langword="null"/> when the clipboard holds no text.</returns>
    string? GetText();

    /// <summary>Replaces the clipboard contents.</summary>
    /// <param name="text">The text to store; <see langword="null"/> is treated as empty.</param>
    /// <returns><see langword="true"/> when the text was stored.</returns>
    bool SetText(string? text);
}

/// <summary>
/// An in-process clipboard. Used as the fallback on platforms with no native support and as a
/// test double.
/// </summary>
public sealed class MemoryClipboard : IClipboard
{
    private string _text = string.Empty;

    /// <inheritdoc/>
    public string? GetText() => _text.Length == 0 ? null : _text;

    /// <inheritdoc/>
    public bool SetText(string? text)
    {
        _text = text ?? string.Empty;
        return true;
    }
}

/// <summary>
/// The platform clipboard: the Windows clipboard through <c>user32</c> when available, and a
/// process-wide in-memory buffer everywhere else (and whenever a native call fails).
/// </summary>
/// <remarks>
/// <para>
/// <c>user32.dll</c> is bound lazily by the P/Invoke stubs, so a session that never copies or
/// pastes never loads it. That matters: a console process that has loaded <c>user32</c> is
/// classified as a GUI application by Windows and stops receiving
/// <c>CTRL_LOGOFF_EVENT</c>/<c>CTRL_SHUTDOWN_EVENT</c> in its console control handler.
/// </para>
/// <para>
/// Every native call is wrapped: a locked or unavailable clipboard degrades to the in-memory
/// buffer rather than throwing into the render loop.
/// </para>
/// </remarks>
public sealed class Clipboard : IClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int OpenRetries = 5;

    private static readonly MemoryClipboard Fallback = new();

    /// <summary>The shared instance used by every edit control that is not given its own.</summary>
    public static Clipboard Shared { get; } = new();

    /// <summary>
    /// The clipboard new edit controls default to. Assignable so a host - or a test - can swap in
    /// a different implementation globally.
    /// </summary>
    public static IClipboard Default { get; set; } = Shared;

    /// <summary>
    /// <see langword="true"/> when the native Windows clipboard is used. Clearing it forces the
    /// in-memory buffer, which is what the tests do.
    /// </summary>
    public bool UseNative { get; set; } = OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public string? GetText()
    {
        if (UseNative && OperatingSystem.IsWindows() && TryGetNative(out string? text))
        {
            return text;
        }

        return Fallback.GetText();
    }

    /// <inheritdoc/>
    public bool SetText(string? text)
    {
        string s = text ?? string.Empty;

        // The fallback is always kept in sync so a later failed native read still returns
        // something sensible.
        Fallback.SetText(s);

        if (UseNative && OperatingSystem.IsWindows())
        {
            TrySetNative(s);
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetNative(out string? text)
    {
        text = null;
        if (!TryOpen())
        {
            return false;
        }

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText))
            {
                return false;
            }

            nint handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
            {
                return false;
            }

            nint ptr = GlobalLock(handle);
            if (ptr == 0)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(ptr);
                return text is not null;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            TryClose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TrySetNative(string text)
    {
        if (!TryOpen())
        {
            return false;
        }

        nint mem = 0;
        try
        {
            // The clipboard owns a NUL terminated UTF-16 buffer in movable global memory.
            int bytes = (text.Length + 1) * 2;
            mem = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (mem == 0)
            {
                return false;
            }

            nint ptr = GlobalLock(mem);
            if (ptr == 0)
            {
                return false;
            }

            try
            {
                for (int i = 0; i < text.Length; i++)
                {
                    Marshal.WriteInt16(ptr, i * 2, (short)text[i]);
                }

                Marshal.WriteInt16(ptr, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(mem);
            }

            if (!EmptyClipboard())
            {
                return false;
            }

            if (SetClipboardData(CfUnicodeText, mem) == 0)
            {
                return false;
            }

            mem = 0; // ownership transferred to the clipboard
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (mem != 0)
            {
                GlobalFree(mem);
            }

            TryClose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryOpen()
    {
        for (int i = 0; i < OpenRetries; i++)
        {
            try
            {
                if (OpenClipboard(0))
                {
                    return true;
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }

            Thread.Sleep(10); // another process holds it; it is normally released immediately
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static void TryClose()
    {
        try
        {
            CloseClipboard();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // nothing sensible to do while unwinding
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint hMem);
}
