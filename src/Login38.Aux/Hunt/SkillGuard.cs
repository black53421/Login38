using Login38.Aux.Game;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Hunt;

/// <summary>What the character looked like at the moment a cast was refused.</summary>
/// <param name="WeightPercent">How loaded, as the client shows it.</param>
/// <param name="Mana">Current mana against its maximum.</param>
/// <param name="Health">Current health against its maximum.</param>
/// <param name="X">Where the character stood, in client coordinates.</param>
/// <param name="Y">The other half of that.</param>
/// <remarks>
/// The refusal says what went wrong; this is what has to change for it to be over. Read by
/// the hunt, which is holding all of it already, and compared here so the deciding can be
/// tested without a game running.
/// </remarks>
public readonly record struct CastConditions(
    byte WeightPercent, Gauge Mana, Gauge Health, int X, int Y);

/// <summary>
/// Stands the skill rotation down while the server is refusing to cast, and puts it back up
/// when whatever was wrong has been put right.
/// </summary>
/// <remarks>
/// <para>
/// Without this the hunt does the worst possible thing with a refusal: it casts, is refused,
/// takes the next turn, casts, is refused, and carries on doing that for as long as the
/// condition lasts — several times a second, killing nothing. A character that had looted
/// itself overweight spent whole fights that way, because every single cast was answered with
/// 「你攜帶太多物品，因此無法使用法術。」 and nothing was reading it.
/// </para>
/// <para>
/// So a refusal latches. While it is latched the rotation casts nothing and the weapon swings
/// instead, which is the one thing that still works. The question this exists to answer is the
/// other half: when to stop.
/// </para>
/// <para>
/// A timer is the wrong answer. Overweight lasts until something is dropped, which may be an
/// hour; a wall lasts until the character walks two steps, which is a moment. Both would be
/// served badly by the same number. So each reason is held against the thing that would end
/// it, taken from what the hunt reads every pass anyway:
/// </para>
/// <list type="bullet">
/// <item><description>too heavy — until the weight the client shows falls</description></item>
/// <item><description>out of mana or health — until the gauge genuinely recovers</description></item>
/// <item><description>nothing in the way of it — until the character has moved</description></item>
/// </list>
/// <para>
/// The rest — interrupted, some state forbidding it, out of reagents, the wrong attribute,
/// invisible — have nothing to watch. Those are answered the only way they can be: by letting
/// one cast through every so often and seeing whether it draws the same answer, waiting twice
/// as long each time it does. A condition that never clears settles at one wasted cast every
/// <see cref="LongestProbe"/>, which is a rounding error; one that clears quietly is picked up
/// within <see cref="FirstProbe"/>.
/// </para>
/// <para>
/// The probe also backs up the three that are watched directly, so a condition read wrongly
/// costs a slow rotation rather than a rotation that never comes back.
/// </para>
/// </remarks>
public sealed class SkillGuard
{
    /// <summary>How long to fight with the weapon before spending a cast on a question.</summary>
    internal static readonly TimeSpan FirstProbe = TimeSpan.FromSeconds(15);

    /// <summary>The longest the wait ever grows to, for a condition that never clears.</summary>
    internal static readonly TimeSpan LongestProbe = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How much of a gauge has to come back before short of it counts as over.
    /// </summary>
    /// <remarks>
    /// A fifth. Any rise at all would clear a mana refusal on the first regeneration tick,
    /// which is a second later and nowhere near enough to cast with — so the rotation would
    /// spend one cast per tick finding out that nothing had changed, which is the loop this
    /// whole class exists to stop.
    /// </remarks>
    internal const int Recovered = 5;

    private readonly ILogger _logger;

    private CastRefusal? _why;
    private CastConditions _at;
    private TimeSpan _since;
    private TimeSpan _wait = FirstProbe;

    public SkillGuard(ILogger logger) => _logger = logger;

    /// <summary>Whether the rotation is standing down, so the weapon has to carry the fight.</summary>
    /// <remarks>
    /// Stays true through a probe. The probe is one cast, not a resumption, and flipping the
    /// weapon off for the pass it goes out on would drop a swing for nothing.
    /// </remarks>
    public bool Silenced => _why is not null;

    /// <summary>Why, for the hunt to say so once.</summary>
    public CastRefusal? Why => _why;

    /// <summary>Whether this pass is the one cast that finds out if it is over.</summary>
    public bool Probing { get; private set; }

    /// <summary>Takes in what the server said and what the character looks like.</summary>
    /// <param name="refused">What the client's chat window carried this pass, if anything.</param>
    /// <param name="look">The character as it stands.</param>
    /// <param name="now">The hunt's clock.</param>
    public void Read(CastRefusal? refused, CastConditions look, TimeSpan now)
    {
        if (refused is { } why)
        {
            Hold(why, look, now);

            return;
        }

        if (_why is not { } held)
        {
            return;
        }

        if (Over(held, _at, look))
        {
            _logger.LogInformation(
                "the {Why} that stopped the skills has gone, so the rotation is back on", held);

            Clear();

            return;
        }

        // Nothing to read, or nothing has changed. Every so often, one cast asks.
        Probing = now - _since >= _wait;
    }

    /// <summary>Notes that the probe has gone out, so the next answer belongs to it.</summary>
    public void Cast(TimeSpan now)
    {
        if (!Probing)
        {
            return;
        }

        Probing = false;
        _since = now;
    }

    /// <summary>Notes that a cast was paid for, which is the server accepting one.</summary>
    /// <remarks>
    /// The one signal that means it is definitely over whatever the reason was, so it clears
    /// anything — including the reasons that have a condition of their own, whose condition
    /// this has just proved wrong.
    /// </remarks>
    public void Landed()
    {
        if (_why is not { } held)
        {
            return;
        }

        _logger.LogInformation("a cast went through, so the {Why} is over", held);

        Clear();
    }

    /// <summary>Forgets everything, for a character who has just walked in.</summary>
    public void Reset()
    {
        Clear();
        _since = TimeSpan.Zero;
    }

    private void Hold(CastRefusal why, CastConditions look, TimeSpan now)
    {
        // The same answer again, which is either the probe coming back or the rotation not
        // having stood down yet. Either way asking again soon would get the same thing.
        var again = _why == why;

        _wait = again ? Longer(_wait) : FirstProbe;
        _at = look;
        _since = now;
        Probing = false;

        if (!again)
        {
            _logger.LogInformation(
                "the server refused a cast: {Why}. The hunt will fight with the weapon until "
                + "that changes", why);
        }

        _why = why;
    }

    private void Clear()
    {
        _why = null;
        Probing = false;
        _wait = FirstProbe;
    }

    private static TimeSpan Longer(TimeSpan was) =>
        was + was > LongestProbe ? LongestProbe : was + was;

    /// <summary>Whether what the refusal was about has changed.</summary>
    /// <returns>
    /// False for the reasons that leave nothing to look at, which are left to the probe.
    /// </returns>
    private static bool Over(CastRefusal why, CastConditions at, CastConditions look) => why switch
    {
        CastRefusal.Weight => look.WeightPercent < at.WeightPercent,
        CastRefusal.Mana => Recovers(look.Mana, at.Mana),
        CastRefusal.Health => Recovers(look.Health, at.Health),
        CastRefusal.Blocked => look.X != at.X || look.Y != at.Y,
        _ => false,
    };

    private static bool Recovers(Gauge now, Gauge was) =>
        now.Current >= was.Current + Math.Max(now.Maximum / Recovered, 1);
}
