using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// Walks the character round things its own client cannot go round.
/// </summary>
/// <remarks>
/// <para>
/// The client's walk engine hill climbs: eight headings, best score, no memory, no lookahead.
/// Given a monster on the far side of a wall it presses into the wall and stays there, which
/// is what twenty seconds stood over a monster four rows away with the destination correctly
/// set actually was.
/// </para>
/// <para>
/// It does not have to be replaced to be got round. What it is good at is going somewhere it
/// can see its way to; what it cannot do is choose that somewhere. So the route comes from
/// <see cref="PathFinder"/>, which searches, and it is handed over one leg at a time — each
/// leg being the furthest square along the route the climb would reach on its own. On open
/// ground that is the whole route in one go; at a corner it is as far as the corner, and then
/// the next leg starts from round it. The character walks the whole thing without stopping,
/// because at no point is it asked to do anything its engine cannot.
/// </para>
/// <para>
/// This is the one place the launcher steers rather than letting the client decide, and it is
/// deliberately the smallest possible version of that: a destination, a mode, and a kick. The
/// stepping, the animation, the collision and the packets stay where they were.
/// </para>
/// <para>
/// Nothing is remembered between passes. The route is recomputed from where the character
/// actually is, so a leg that went wrong, a monster that moved and a client that ignored a
/// kick all correct themselves on the next pass rather than accumulating.
/// </para>
/// </remarks>
public sealed class RouteWalk
{
    /// <summary>
    /// The interaction mode a destination that is not a creature is walked at.
    /// </summary>
    /// <remarks>
    /// <c>ComputeStepHeading</c> takes three paths. Mode 1 is the attack chase and stops on
    /// the weapon's reach, which is wrong here by up to eight tiles. Mode 3 stops when it is
    /// within one square of the destination, which is what walking somewhere means, and it is
    /// what the client's own re-lock uses for anything it does not intend to attack.
    /// </remarks>
    private const uint Walking = 3;

    private readonly ClickHook _click;

    public RouteWalk(ClickHook click) => _click = click;

    /// <summary>
    /// Sends the character at one square, and says whether it was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order matters and is the client's own: where to go, then that there is somewhere
    /// to go, then the mode, and only then the kick. A kick that arrives before the
    /// destination walks to wherever the last one was.
    /// </para>
    /// <para>
    /// The attack target is dropped first. Leaving it set means the engine's arrival path
    /// takes the attack branch and re-locks, which is how a leg ends with the character
    /// pointed back at the monster and pressing into the wall again. The hover target is
    /// <em>not</em> touched — writing zero to it crashes the client's drawing code, which is
    /// recorded at <see cref="AttackChain.Stop"/>.
    /// </para>
    /// <para>
    /// <see cref="HuntAddresses.MoveRequested"/> is the one that turns all of this from a
    /// description into a movement. It is set by every path the client itself starts a walk
    /// from, and it is the first thing the engine tests; a version of this without it wrote
    /// every other global correctly and left the character standing still for a minute at a
    /// time with nothing in its state looking wrong.
    /// </para>
    /// </remarks>
    /// <param name="process">The running game.</param>
    /// <param name="x">Where to walk to, as the client stores it.</param>
    /// <param name="y">Where to walk to, as the client stores it.</param>
    public bool To(RemoteProcess process, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            process.Write<uint>(HuntAddresses.AttackTarget, 0);
            process.Write<uint>(HuntAddresses.AutoAttack, 0);

            // Masked to even, because the client masks its own before it walks at it — the
            // re-lock does it to a monster's column and the stop helper does it to the
            // player's. An odd destination is one the engine never quite arrives at.
            process.Write<int>(HuntAddresses.DestinationX, x & ~1);
            process.Write<int>(HuntAddresses.DestinationY, y);
            process.Write<byte>(HuntAddresses.WalkTargetValid, 1);
            process.Write<uint>(HuntAddresses.InteractionMode, Walking);

            // Last of the writes and not optional. See MoveRequested: the engine reads it
            // before it will step, every one of the client's own ways in sets it, and without
            // it all of the above is a destination the engine reads and declines to act on.
            process.Write<byte>(HuntAddresses.MoveRequested, 1);

            return _click.Steer(process);
        }
        catch (GameProcessException)
        {
            // A game on its way out, or a page that moved. The next pass recomputes the whole
            // thing from where the character is, so there is nothing here worth recovering.
            return false;
        }
    }

    /// <summary>
    /// Keeps the attack chain out of the way for a pass, without touching the walk.
    /// </summary>
    /// <remarks>
    /// The walk engine re-locks on arrival whenever <see cref="HuntAddresses.AutoAttack"/> is
    /// set, and a re-lock works the destination out from the pinned monster — so one pass that
    /// leaves the flag up is a leg that ends by walking back into the wall it was going round.
    /// The flag has to be down on every pass of a walk, and most passes of a walk must not
    /// kick the engine, so the two cannot be the same call.
    /// </remarks>
    public static void Hold(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            process.Write<uint>(HuntAddresses.AutoAttack, 0);
            process.Write<uint>(HuntAddresses.AttackTarget, 0);
        }
        catch (GameProcessException)
        {
            // A game on its way out. The next pass works the whole thing out again.
        }
    }

    /// <summary>Whether the character has arrived, by the client's own test.</summary>
    /// <remarks>
    /// <c>InRangeCheck</c> at range one — a box two columns wide and one row tall, because
    /// two columns make a tile across. The same test the engine stops on in mode
    /// <see cref="Walking"/>, so this agrees with the client about when a leg is over.
    /// </remarks>
    public static bool Arrived((int X, int Y) player, (int X, int Y) waypoint) =>
        Math.Abs(player.X - waypoint.X) <= 2 && Math.Abs(player.Y - waypoint.Y) <= 1;
}
