namespace Login38.Aux.Hunt;

/// <summary>
/// Chooses what to attack next.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, because they are answered in different places. Which monsters the player is
/// willing to fight is settings and arithmetic and belongs here. Which of them the character
/// can actually get to is a question only the client can answer, so it is asked of the
/// client — see <see cref="WalkProbe"/> — and the answer arrives here as a step count.
/// </para>
/// <para>
/// This used to hold two models of the client's walk written by hand, and both were wrong in
/// ways that showed. A flood fill answered whether a path existed, which the client cannot
/// use: its walk engine hill climbs, so a way round a corner is not a way it can take. A
/// hand-written hill climb was closer and still differed on the order it tried headings in,
/// which decides every tie — and ties are most squares. Between them they picked monsters
/// through walls and refused monsters standing next to the character.
/// </para>
/// <para>
/// What the walk did not reach is not picked. That is the client's own answer about its own
/// walk, not a model's opinion, and the alternative was tried and is worse in a way that is
/// obvious on screen: a monster behind a wall is a monster the character shoots at for
/// nothing, and with the probe asked about the weapon's reach instead of arm's length every
/// one of them arrives in no steps and so outranks everything worth walking to.
/// </para>
/// <para>
/// The one thing the refusal must not do is decide nothing at all for ever, because a
/// character that does not move never changes the answer. So the caller may ask again
/// without it — see <paramref name="desperate"/> — and that is where "unreachable" stops
/// being a verdict and starts being a preference.
/// </para>
/// </remarks>
internal static class TargetPicker
{
    /// <summary>
    /// Everything the settings allow, in the order it was found.
    /// </summary>
    /// <remarks>
    /// Separate from the choosing because the step counts have to be asked for in one go —
    /// one question about a whole screen rather than one per monster — so the list has to
    /// exist before anything knows how far away any of it is.
    /// </remarks>
    /// <param name="candidates">Everything the scan found.</param>
    /// <param name="settings">What the player has asked for.</param>
    /// <param name="ignored">Ids being left alone after a stall.</param>
    /// <summary>
    /// Whether the server would let anything at all land on this one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Burrowed and airborne monsters. The server drops every blow and every spell aimed at
    /// one without saying so — <c>npcBlocksDirectPlayerAttackLikeJava</c> refuses both attack
    /// paths for <c>NpcHiddenSink</c> and <c>NpcHiddenFly</c>, and returns nil — so a hunt
    /// that picks one stands there swinging at it until a watchdog gives up, and a rotation
    /// spends its whole cast list on nothing.
    /// </para>
    /// <para>
    /// The state is in the client. The spawn packet carries the action byte the server works
    /// out in <c>NpcActionStatus</c>, and the client keeps it at
    /// <see cref="HuntAddresses.EntityAction"/> — all four of its hidden values, which is
    /// what <see cref="HuntAddresses.IsHiddenAction"/> lists. Read off three live monsters
    /// standing about, all three zero.
    /// </para>
    /// <para>
    /// All four rather than the burrowed one alone. Two of them mean something else as well
    /// for the one pass after a monster surfaces, which costs a pass; leaving them in cost
    /// the whole rotation, every time, for as long as the monster stayed under.
    /// </para>
    /// <para>
    /// Not every skill is refused a sunk monster — four of them exist to dig one out, and the
    /// server lists them by id — but the hunt has no way to know which of a player's rotation
    /// those are, and a target it can only hurt with one row of six is a worse answer than
    /// leaving it alone.
    /// </para>
    /// </remarks>
    internal static bool Hittable(HuntTarget target) =>
        !HuntAddresses.IsHiddenAction(target.Action);

    internal static IReadOnlyList<HuntTarget> Wanted(
        IReadOnlyList<HuntTarget> candidates,
        HuntSettings settings,
        IReadOnlySet<uint> ignored)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ignored);

        return
        [
            .. candidates
                .Where(candidate => !ignored.Contains(candidate.Id))
                .Where(Hittable)
                .Where(candidate => Allowed(candidate.Name, settings)),
        ];
    }

    /// <summary>
    /// The best of them: one the walk can get to if there is one, and the nearest thing
    /// worth shooting if there is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordinarily two things decide it and the first is absolute: a monster the client's own
    /// walk did not arrive at is not a candidate. Everything else is fewest steps, then
    /// nearest — distance settles the ties, and with a bow almost everything ties, so without
    /// it the pick would be whichever record the scan happened to walk first.
    /// </para>
    /// <para>
    /// Asked with <paramref name="desperate"/>, the refusal becomes an ordering: walked to
    /// first, then close enough to shoot from here, then everything else. That is for the
    /// caller to use when the refusal has left it with nothing, and only then — a character
    /// standing still cannot get a different answer out of a probe that starts from where it
    /// is standing.
    /// </para>
    /// <para>
    /// Then fewest steps, then nearest. Distance settles the ties, and with a bow almost
    /// everything ties: without it the pick would be whichever record the scan happened to
    /// walk first, which changes as the client reallocates and is not a choice at all.
    /// </para>
    /// </remarks>
    /// <param name="wanted">What <see cref="Wanted"/> returned.</param>
    /// <param name="steps">
    /// How many steps the walk took to get to each of them, in the same order, with null
    /// for the ones it never arrived at.
    /// </param>
    /// <param name="player">Where the character is, as the client stores it.</param>
    /// <param name="swing">How far the character can hit from, in tiles.</param>
    /// <param name="desperate">
    /// Whether to take one the walk never arrived at. False is the rule; true is the way out
    /// of the deadlock the rule creates, and the caller owns when that is.
    /// </param>
    internal static HuntTarget? Nearest(
        IReadOnlyList<HuntTarget> wanted,
        IReadOnlyList<int?> steps,
        (int X, int Y) player,
        int swing,
        bool desperate = false)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(steps);

        HuntTarget? best = null;
        var chosen = (Rank: int.MaxValue, Steps: int.MaxValue, Away: int.MaxValue);

        // Fewer than there are candidates when the question was truncated to what one run
        // holds. The ones past the end are not unreachable, they are unasked, and the
        // difference matters only in that neither can be chosen.
        for (var i = 0; i < wanted.Count && i < steps.Count; i++)
        {
            if (steps[i] is null && !desperate)
            {
                continue;
            }

            var candidate = (
                Rank: Rank(wanted[i], steps[i], player, swing),
                Steps: steps[i] ?? int.MaxValue,
                Away: Distance(wanted[i], player));

            if (best is null || Better(candidate, chosen))
            {
                best = wanted[i];
                chosen = candidate;
            }
        }

        return best;
    }

    /// <summary>Whether the first is the one to take.</summary>
    private static bool Better(
        (int Rank, int Steps, int Away) candidate,
        (int Rank, int Steps, int Away) chosen)
    {
        if (candidate.Rank != chosen.Rank)
        {
            return candidate.Rank < chosen.Rank;
        }

        return candidate.Steps != chosen.Steps
            ? candidate.Steps < chosen.Steps
            : candidate.Away < chosen.Away;
    }

    /// <summary>How much this one is worth preferring, lowest first.</summary>
    /// <remarks>
    /// Nought is one the client's own walk arrived at, which is the only one of the three
    /// that is known rather than hoped. One is one it did not arrive at but which is already
    /// inside the weapon's reach — across a fence, over water — so it can be shot without
    /// going anywhere. Two is everything else, and is taken only when there is nothing else
    /// at all.
    /// </remarks>
    private static int Rank(HuntTarget target, int? steps, (int X, int Y) player, int swing)
    {
        if (steps is not null)
        {
            return 0;
        }

        return InReach(target, player, swing) ? 1 : 2;
    }

    /// <summary>Whether the character could hit this one from where it stands.</summary>
    /// <remarks>
    /// <c>InRangeCheck</c>'s test at <c>0x40EC00</c>: a box twice as wide as it is tall,
    /// because two columns make a tile across and one row makes one down.
    /// </remarks>
    private static bool InReach(HuntTarget target, (int X, int Y) player, int swing) =>
        Math.Abs(target.X - player.X) <= swing * 2 && Math.Abs(target.Y - player.Y) <= swing;

    /// <summary>How far apart two points are, the way the client's walk engine counts it.</summary>
    /// <remarks>
    /// <c>GridDistance</c> at <c>0x554950</c>: the column difference squared over four, plus
    /// the row difference squared. The four is the grid being twice as fine across as it is
    /// down, and using anything else here would sort the candidates differently from the way
    /// the client walks to them.
    /// </remarks>
    private static int Distance(HuntTarget target, (int X, int Y) player)
    {
        var across = target.X - player.X;
        var down = target.Y - player.Y;

        return (across * across / 4) + (down * down);
    }

    /// <summary>Whether the lists let this one be attacked.</summary>
    /// <remarks>
    /// The blacklist wins. A name on both is a player who has changed their mind and not
    /// finished tidying up, and the safe reading of that is the one that attacks less.
    /// </remarks>
    internal static bool Allowed(string name, HuntSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Blacklist.Contains(name, StringComparer.Ordinal))
        {
            return false;
        }

        return settings.Whitelist.Count == 0
               || settings.Whitelist.Contains(name, StringComparer.Ordinal);
    }
}
