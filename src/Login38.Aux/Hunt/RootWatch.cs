namespace Login38.Aux.Hunt;

/// <summary>
/// How long the character has been standing in one place.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StallWatch"/> counts the client's own attack cooldown as progress, and for
/// nearly everything that is right. It is wrong for the one case that matters here: the
/// client pushes that cooldown forward on every blow it <em>starts</em>, and it starts one
/// whether or not the server accepts it. The server drops an attack it will not allow —
/// no line of sight, a monster still burrowed, out of range — without a word, so the client
/// goes on starting blows for ever, the cooldown goes on moving, and a watch built on it is
/// reset for ever. The character stands there shooting a corner and nothing ever gives up.
/// </para>
/// <para>
/// Standing still is the thing the client cannot fake from the inside. If the character has
/// not left the spot it took the shot from and the monster it is aimed at is still alive,
/// nothing is happening, whatever the client says it is doing.
/// </para>
/// <para>
/// With slack, and that is not a detail. A character wedged against a wall or shuffling
/// between two squares does move, so an exact test is defeated by a single tile of jitter —
/// which is exactly what a stuck chase looks like, "moving a little every now and then".
/// Two tiles of slack is under any real approach and over any amount of shuffling.
/// </para>
/// </remarks>
internal sealed class RootWatch
{
    private int _x;
    private int _y;
    private TimeSpan _since;
    private bool _watching;

    /// <summary>Starts again, for a target just picked or just let go.</summary>
    public void Reset() => _watching = false;

    /// <summary>
    /// Takes a reading, and says how long the character has stayed put.
    /// </summary>
    /// <param name="x">Where the character is now, as the client stores it.</param>
    /// <param name="y">Where the character is now, as the client stores it.</param>
    /// <param name="slack">How far it may drift, in tiles, and still count as put.</param>
    /// <param name="now">A reading off a monotonic clock, not the wall clock.</param>
    /// <remarks>
    /// The box is the client's own — twice as wide as it is tall, because two columns make a
    /// tile across and one row makes one down. Measuring in squares instead would give half
    /// the slack sideways as up, which is not what anybody means by "it has not moved".
    /// </remarks>
    public TimeSpan Rooted(int x, int y, int slack, TimeSpan now)
    {
        if (!_watching
            || Math.Abs(x - _x) > slack * 2
            || Math.Abs(y - _y) > slack)
        {
            _watching = true;
            _x = x;
            _y = y;
            _since = now;

            return TimeSpan.Zero;
        }

        return now - _since;
    }
}
