using Login38.Aux.Game;
using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// How close the client will get to something before it starts swinging at it.
/// </summary>
/// <remarks>
/// <para>
/// The client decides this in <c>ComputeStepHeading</c> at <c>0x5A4D60</c>, in the mode a
/// monster is chased in, and it decides it from two places in a fixed order:
/// </para>
/// <code>
/// local_38 = 1;
/// if (weapon &lt; 0x58) local_38 = DAT_008D2CB8[weapon];   // the weapon's own entry
/// if (DAT_00C2D2CA != 0) local_38 = DAT_00C2D2CA;        // what the server said, if anything
/// </code>
/// <para>
/// Read off a running client the table says 2 tiles for a melee weapon, 9 for a claw, 14 for
/// a bow and 30 for the classes at 16 to 19. Nothing here writes either of them. This reads
/// them in the client's own order, so that the hunt's idea of "in reach" and the walk probe's
/// idea of "arrived" are the number the client is about to act on rather than a guess at it.
/// </para>
/// <para>
/// Guessing is what went wrong for a whole session. The probe asked about a reach of one,
/// which for a bow is asking whether the character can walk up and touch something it was
/// going to shoot from ten tiles away — so it threw away every target it could not reach on
/// foot, and ranked the rest by a walk it was never going to take.
/// </para>
/// <para>
/// The other half of that bug was the server's, and is fixed there:
/// <c>playerAttackRangeReachesLikeJava</c> in <c>combat.go</c> now follows
/// <c>L1AttackPc.calcHit</c> — a ranged weapon has no fixed limit, only line of sight and
/// being on screen — where it used to refuse anything past ten tiles, and refuse it silently.
/// Those four tiles between ten and the bow's fourteen were where the character stood still,
/// fired, and was ignored, with nothing to tell it so.
/// </para>
/// <para>
/// Which leaves the override for what it is actually good for: deciding where to stand. The
/// walk engine stops the instant it is in range at all, so a bow left to itself fights from
/// fourteen tiles — the worst distance there is, because the monster takes one step and the
/// shot is out of range again, and the whole fight is spent drifting in and out. There is no
/// second knob: the arrival test reads this one byte and nothing else, so standing closer
/// means telling the client a smaller reach and letting its own walk engine close the gap,
/// exactly the way it does for a melee weapon. That is what makes ranged feel like melee
/// instead of like a stutter.
/// </para>
/// <para>
/// Never further than the weapon reaches, only nearer. A cap rather than a replacement is
/// what leaves melee alone — its entry is two, already under any sane standoff, so nothing
/// is written and the client keeps its own number.
/// </para>
/// </remarks>
internal static class WeaponReach
{
    /// <summary>What the client falls back to when it cannot index the table.</summary>
    internal const int Default = 1;

    /// <summary>
    /// Tells the client where to stand, and says where that turned out to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written only when it has to change. Five times a second into another process is a
    /// write worth not making, and a weapon the standoff does not affect is left with no
    /// override at all rather than an override that agrees with the table.
    /// </para>
    /// <para>
    /// The returned number is what the client will act on, so everything else —
    /// <c>InReach</c>, the walk probe — can ask this once and agree with the client by
    /// construction rather than by being kept in step.
    /// </para>
    /// </remarks>
    /// <param name="process">The running game.</param>
    /// <param name="standoff">How close to stand, in tiles. See
    /// <see cref="HuntSettings.StandoffTiles"/>.</param>
    internal static int Apply(RemoteProcess process, int standoff)
    {
        ArgumentNullException.ThrowIfNull(process);

        var table = Table(process);

        Write(process, Override(table, standoff));

        return Stand(table, standoff);
    }

    /// <summary>Gives the client its own reach back.</summary>
    internal static void Clear(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        Write(process, 0);
    }

    /// <summary>
    /// Half the distance again, or nothing when there is nowhere left to go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What to do about a target that is being swung at and not hurt. The server refuses an
    /// attack it will not allow and says nothing — no line of sight round a corner, a monster
    /// still burrowed — and the launcher cannot predict either: the client's collision grid
    /// holds walkability and nothing else, and its map files hold graphics, so there is no
    /// arrow bit anywhere to read.
    /// </para>
    /// <para>
    /// It does not need one. Both refusals are answered by the same thing a player would do,
    /// which is walk closer: a corner stops being a corner once you are round it, and a
    /// burrowed monster surfaces when somebody comes within two tiles of it. Collision is
    /// the client's problem and it is good at it, so the whole of this is telling it a
    /// shorter reach and letting its own walk engine do the rest.
    /// </para>
    /// <para>
    /// Halving rather than stepping down one at a time, so a bow gets there in three tries
    /// instead of thirteen, and stopping at one because there is nothing nearer than the
    /// next square. A target that cannot be hurt from there cannot be hurt.
    /// </para>
    /// </remarks>
    /// <param name="reach">Where the character is standing now, in tiles.</param>
    internal static int? Closer(int reach) => reach <= 1 ? null : Math.Max(1, reach / 2);

    /// <summary>Where the character will stand, for a weapon whose table entry is
    /// <paramref name="table"/>.</summary>
    /// <remarks>
    /// A standoff of zero or less is nobody having chosen one, and the weapon's own reach is
    /// the right answer to that rather than a character that walks into what it is shooting.
    /// </remarks>
    internal static int Stand(int table, int standoff) =>
        standoff > 0 && standoff < table ? standoff : table;

    /// <summary>
    /// What to leave in <see cref="HuntAddresses.AttackReach"/>.
    /// </summary>
    /// <remarks>
    /// Zero is not "no reach", it is "nobody has told me" — the client falls back to the
    /// table. So a weapon the standoff does not reach into is left alone entirely, which is
    /// what keeps melee, and the claws under a generous standoff, untouched by any of this.
    /// </remarks>
    internal static byte Override(int table, int standoff)
    {
        var stand = Stand(table, standoff);

        return stand == table ? (byte)0 : (byte)stand;
    }

    private static void Write(RemoteProcess process, byte reach)
    {
        try
        {
            if (process.TryRead<byte>(HuntAddresses.AttackReach, out var current)
                && current == reach)
            {
                return;
            }

            process.Write(HuntAddresses.AttackReach, reach);
        }
        catch (GameProcessException)
        {
            // A game on its way out. The next pass either finds it gone or writes again.
        }
    }

    /// <summary>
    /// What the client's own table says for the weapon in hand.
    /// </summary>
    /// <remarks>
    /// Unreadable is <see cref="Default"/> rather than a refusal. The consequence of getting
    /// this wrong is a hunt that walks slightly too close, and the consequence of refusing to
    /// answer is no hunt at all.
    /// </remarks>
    internal static int Table(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!ObfuscatedStat.TryRead(process, HuntAddresses.WeaponClass, out var weapon)
            || weapon >= HuntAddresses.ReachTableLength)
        {
            return Default;
        }

        return process.TryRead<uint>(
                   HuntAddresses.ReachTable + (int)(weapon * sizeof(uint)), out var tiles)
               && tiles > 0
            ? (int)tiles
            : Default;
    }
}
