using System.Runtime.InteropServices;

namespace Login38.Interop.Win32;

/// <summary>Window style bits this launcher cares about.</summary>
[Flags]
internal enum WindowStyles : uint
{
    /// <summary>Excludes child windows from the parent's painting region.</summary>
    ClipChildren = 0x0200_0000,

    /// <summary>Excludes overlapping siblings from each other's painting region.</summary>
    ClipSiblings = 0x0400_0000,
}

[Flags]
internal enum SetWindowPosFlags : uint
{
    NoSize = 0x0001,
    NoMove = 0x0002,
    NoZOrder = 0x0004,
    FrameChanged = 0x0020,
    NoActivate = 0x0010,
}

internal static partial class User32
{
    private const string Library = "user32.dll";

    /// <summary>Index of the style word in the window's extra data.</summary>
    internal const int GwlStyle = -16;

    internal delegate bool EnumWindowsProc(nint window, nint parameter);

    [LibraryImport(Library, EntryPoint = "EnumWindows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(
        [MarshalAs(UnmanagedType.FunctionPtr)] EnumWindowsProc callback, nint parameter);

    [LibraryImport(Library, EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport(Library, EntryPoint = "GetWindowThreadProcessId")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport(Library, EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(nint window);

    /// <remarks>
    /// A raw pointer rather than an array: source-generated marshalling only handles
    /// blittable signatures, and an array of characters is not one.
    /// </remarks>
    [LibraryImport(Library, EntryPoint = "GetWindowTextW")]
    internal static unsafe partial int GetWindowText(nint window, char* text, int maxCount);

    [LibraryImport(Library, EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowText(nint window, string text);

    [LibraryImport(Library, EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static partial int GetWindowLong(nint window, int index);

    [LibraryImport(Library, EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static partial int SetWindowLong(nint window, int index, int value);

    [LibraryImport(Library, EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, SetWindowPosFlags flags);

    // ---- the low-level keyboard hook ------------------------------------------------

    /// <summary>A hook that sees every keystroke before the window it is meant for.</summary>
    internal const int WhKeyboardLowLevel = 13;

    internal const uint WmKeyDown = 0x0100;

    internal const uint WmSysKeyDown = 0x0104;

    internal const uint WmQuit = 0x0012;

    /// <summary>Look at the queue without taking anything off it.</summary>
    internal const uint PeekNoRemove = 0x0000;

    internal delegate nint HookProc(int code, nint wparam, nint lparam);

    [LibraryImport(Library, EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static partial nint SetWindowsHookEx(
        int hookId,
        [MarshalAs(UnmanagedType.FunctionPtr)] HookProc callback,
        nint module,
        uint threadId);

    [LibraryImport(Library, EntryPoint = "UnhookWindowsHookEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport(Library, EntryPoint = "CallNextHookEx")]
    internal static partial nint CallNextHookEx(nint hook, int code, nint wparam, nint lparam);

    [LibraryImport(Library, EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(out Message message, nint window, uint first, uint last);

    [LibraryImport(Library, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(
        out Message message, nint window, uint first, uint last, uint remove);

    [LibraryImport(Library, EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in Message message);

    [LibraryImport(Library, EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in Message message);

    [LibraryImport(Library, EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint threadId, uint message, nint wparam, nint lparam);

    [LibraryImport(Library, EntryPoint = "GetForegroundWindow")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport(Library, EntryPoint = "DestroyIcon", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint icon);

    [LibraryImport(Library, EntryPoint = "GetAsyncKeyState")]
    internal static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport(Library, EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport(Library, EntryPoint = "GetClientRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint window, out Rectangle rectangle);

    [LibraryImport(Library, EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint window, ref Point point);
}
