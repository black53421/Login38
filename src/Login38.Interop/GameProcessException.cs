namespace Login38.Interop;

/// <summary>
/// An operation against the game process failed.
/// </summary>
/// <remarks>
/// Where the failure came from a Win32 call, the inner exception is a
/// <see cref="System.ComponentModel.Win32Exception"/> carrying the OS error. The outer
/// message says what was being attempted and at which address — the OS error alone
/// ("Access is denied") is rarely enough to act on.
/// </remarks>
public class GameProcessException : Exception
{
    public GameProcessException(string message)
        : base(message)
    {
    }

    public GameProcessException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public GameProcessException()
    {
    }
}
