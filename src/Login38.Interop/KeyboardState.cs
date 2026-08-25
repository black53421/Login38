using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// Whether a key is down right now, anywhere on the machine.
/// </summary>
/// <remarks>
/// <para>
/// The other way of hearing about a key pressed inside the game is
/// <see cref="KeyboardHook"/>, and the two are not interchangeable. A hook takes the key —
/// it does not reach whatever has focus — which is right for a key the launcher owns and
/// wrong for one the game might also want. This asks after the fact and takes nothing.
/// </para>
/// <para>
/// The state is the machine's, not any one window's, so anything acting on it has to say
/// for itself which window was in front. <see cref="GameWindow.ForegroundProcessId"/> is
/// how: without that check a launcher driving one client would answer a key pressed in
/// another.
/// </para>
/// </remarks>
public static class KeyboardState
{
    /// <summary>The Home key.</summary>
    public const int Home = 0x24;

    /// <summary>The Insert key.</summary>
    public const int Insert = 0x2D;

    /// <summary>Whether a virtual key is held down at this moment.</summary>
    /// <param name="virtualKey">A virtual key code, such as <see cref="Home"/>.</param>
    /// <remarks>
    /// The high bit only. The low bit says the key has been pressed since somebody last
    /// asked, which is a shared flag any other caller in the process can clear first — so
    /// two readers of the same key would each see about half the presses.
    /// </remarks>
    public static bool IsDown(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
