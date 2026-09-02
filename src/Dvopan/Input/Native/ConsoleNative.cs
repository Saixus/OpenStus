using System.Runtime.InteropServices;

namespace Dvopan.Input.Native;

// The LibraryImport analyser would like every one of these converted; DllImport is deliberate here
// because the array-marshalling overload of ReadConsoleInputW keeps the whole project compiling
// without <AllowUnsafeBlocks>.
#pragma warning disable SYSLIB1054

/// <summary>A Win32 <c>COORD</c>: a cell position or a buffer size, in console cells.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct COORD
{
    /// <summary>Column.</summary>
    public short X;

    /// <summary>Row.</summary>
    public short Y;
}

/// <summary>
/// A Win32 <c>KEY_EVENT_RECORD</c>.
/// </summary>
/// <remarks>
/// <c>bKeyDown</c> is a Win32 <c>BOOL</c> and therefore four bytes: declaring it as a C# <c>bool</c>
/// would shift every field after it. <c>UnicodeChar</c> is kept as a <see cref="ushort"/> so the
/// struct stays blittable regardless of the P/Invoke char set.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct KEY_EVENT_RECORD
{
    /// <summary>Non-zero when the key was pressed, zero when released.</summary>
    public int bKeyDown;

    /// <summary>How many times this key press repeated; a held key may batch into one record.</summary>
    public ushort wRepeatCount;

    /// <summary>The Win32 virtual key code.</summary>
    public ushort wVirtualKeyCode;

    /// <summary>The hardware scan code.</summary>
    public ushort wVirtualScanCode;

    /// <summary>The UTF-16 code unit the key produced, or zero.</summary>
    public ushort UnicodeChar;

    /// <summary>Bit set of held modifier and lock keys.</summary>
    public uint dwControlKeyState;
}

/// <summary>A Win32 <c>MOUSE_EVENT_RECORD</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MOUSE_EVENT_RECORD
{
    /// <summary>Pointer position in screen-buffer cells.</summary>
    public COORD dwMousePosition;

    /// <summary>Low word: currently pressed buttons. High word: signed wheel delta.</summary>
    public uint dwButtonState;

    /// <summary>Bit set of held modifier and lock keys.</summary>
    public uint dwControlKeyState;

    /// <summary>Move / double click / wheel flags; zero means a button press or release.</summary>
    public uint dwEventFlags;
}

/// <summary>A Win32 <c>WINDOW_BUFFER_SIZE_RECORD</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOW_BUFFER_SIZE_RECORD
{
    /// <summary>The new screen buffer size in cells.</summary>
    public COORD dwSize;
}

/// <summary>
/// A Win32 <c>INPUT_RECORD</c>.
/// </summary>
/// <remarks>
/// The union starts at offset 4, not 2: the <c>WORD EventType</c> is followed by two bytes of
/// padding because the union members require four-byte alignment. The whole record is 20 bytes on
/// both x86 and x64. Getting this offset wrong yields plausible-looking but wrong virtual key codes.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct INPUT_RECORD
{
    /// <summary>One of the <c>*_EVENT</c> constants on <see cref="ConsoleNative"/>.</summary>
    [FieldOffset(0)]
    public ushort EventType;

    /// <summary>Valid when <see cref="EventType"/> is <see cref="ConsoleNative.KEY_EVENT"/>.</summary>
    [FieldOffset(4)]
    public KEY_EVENT_RECORD KeyEvent;

    /// <summary>Valid when <see cref="EventType"/> is <see cref="ConsoleNative.MOUSE_EVENT"/>.</summary>
    [FieldOffset(4)]
    public MOUSE_EVENT_RECORD MouseEvent;

    /// <summary>Valid when <see cref="EventType"/> is <see cref="ConsoleNative.WINDOW_BUFFER_SIZE_EVENT"/>.</summary>
    [FieldOffset(4)]
    public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
}

/// <summary>
/// The kernel32 console input entry points and constants used by <see cref="WindowsConsoleInput"/>.
/// </summary>
internal static class ConsoleNative
{
    /// <summary>Standard input handle selector for <see cref="GetStdHandle"/>.</summary>
    internal const int STD_INPUT_HANDLE = -10;

    /// <summary>Standard output handle selector for <see cref="GetStdHandle"/>.</summary>
    internal const int STD_OUTPUT_HANDLE = -11;

    // ---- input modes -----------------------------------------------------------------------
    internal const uint ENABLE_PROCESSED_INPUT = 0x0001;
    internal const uint ENABLE_LINE_INPUT = 0x0002;
    internal const uint ENABLE_ECHO_INPUT = 0x0004;
    internal const uint ENABLE_WINDOW_INPUT = 0x0008;
    internal const uint ENABLE_MOUSE_INPUT = 0x0010;
    internal const uint ENABLE_INSERT_MODE = 0x0020;
    internal const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    internal const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    internal const uint ENABLE_AUTO_POSITION = 0x0100;
    internal const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

    // ---- record types ----------------------------------------------------------------------
    internal const ushort KEY_EVENT = 0x0001;
    internal const ushort MOUSE_EVENT = 0x0002;
    internal const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;
    internal const ushort MENU_EVENT = 0x0008;
    internal const ushort FOCUS_EVENT = 0x0010;

    // ---- dwControlKeyState ------------------------------------------------------------------
    internal const uint RIGHT_ALT_PRESSED = 0x0001;
    internal const uint LEFT_ALT_PRESSED = 0x0002;
    internal const uint RIGHT_CTRL_PRESSED = 0x0004;
    internal const uint LEFT_CTRL_PRESSED = 0x0008;
    internal const uint SHIFT_PRESSED = 0x0010;
    internal const uint NUMLOCK_ON = 0x0020;
    internal const uint SCROLLLOCK_ON = 0x0040;
    internal const uint CAPSLOCK_ON = 0x0080;
    internal const uint ENHANCED_KEY = 0x0100;

    // ---- dwButtonState (low word) -----------------------------------------------------------
    internal const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
    internal const uint RIGHTMOST_BUTTON_PRESSED = 0x0002;
    internal const uint FROM_LEFT_2ND_BUTTON_PRESSED = 0x0004;
    internal const uint FROM_LEFT_3RD_BUTTON_PRESSED = 0x0008;
    internal const uint FROM_LEFT_4TH_BUTTON_PRESSED = 0x0010;
    internal const uint BUTTON_MASK = 0xFFFF;

    // ---- dwEventFlags -----------------------------------------------------------------------
    internal const uint MOUSE_MOVED = 0x0001;
    internal const uint DOUBLE_CLICK = 0x0002;
    internal const uint MOUSE_WHEELED = 0x0004;
    internal const uint MOUSE_HWHEELED = 0x0008;

    // ---- virtual keys that carry no character and only move the modifier state ---------------
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_CAPITAL = 0x14;
    internal const int VK_NUMLOCK = 0x90;
    internal const int VK_SCROLL = 0x91;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;

    /// <summary>One wheel notch, as reported in the high word of <c>dwButtonState</c>.</summary>
    internal const int WHEEL_DELTA = 120;

    /// <summary>The value kernel32 returns for a failed handle lookup.</summary>
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfConsoleInputEvents(IntPtr hConsoleInput, out uint lpcNumberOfEvents);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadConsoleInput(
        IntPtr hConsoleInput,
        [Out] INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", EntryPoint = "PeekConsoleInputW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekConsoleInput(
        IntPtr hConsoleInput,
        [Out] INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushConsoleInputBuffer(IntPtr hConsoleInput);
}

#pragma warning restore SYSLIB1054
