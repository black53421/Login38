using System.Runtime.InteropServices;

namespace Login38.Interop.Win32;

/// <summary>What the notification area is told about an icon.</summary>
/// <remarks>
/// The character arrays are inline rather than marshalled strings: the source-generated
/// P/Invoke this project uses does not marshal strings inside structures, and a fixed
/// buffer is what the structure actually is.
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NotifyIconData
{
    internal uint Size;
    internal nint Window;
    internal uint Id;
    internal uint Flags;
    internal uint CallbackMessage;
    internal nint Icon;
    internal fixed char Tip[128];
    internal uint State;
    internal uint StateMask;
    internal fixed char Info[256];
    internal uint TimeoutOrVersion;
    internal fixed char InfoTitle[64];
    internal uint InfoFlags;
    internal Guid Item;
    internal nint BalloonIcon;
}

internal static unsafe partial class Shell32
{
    private const string Library = "shell32.dll";

    internal const uint Add = 0x00000000;
    internal const uint Modify = 0x00000001;
    internal const uint Delete = 0x00000002;

    internal const uint FlagMessage = 0x00000001;
    internal const uint FlagIcon = 0x00000002;
    internal const uint FlagTip = 0x00000004;

    [LibraryImport(Library, EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool NotifyIcon(uint message, ref NotifyIconData data);

    /// <summary>Pulls an icon out of an executable.</summary>
    /// <remarks>
    /// The launcher's own, so the icon in the notification area is the icon the player
    /// double-clicked. Reading it back out of the executable avoids shipping the same
    /// artwork twice and avoids a drawing library to convert it.
    /// </remarks>
    [LibraryImport(Library, EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint ExtractIconEx(string file, int index, nint* large, nint* small, uint count);
}
