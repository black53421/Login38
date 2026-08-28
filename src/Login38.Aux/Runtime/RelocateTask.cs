using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Hunt;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Reads a teleport scroll when the spot has stopped being worth standing in.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="EscapeTask"/> because the two are answers to different questions
/// and want different endings — see <see cref="RelocateRule"/>. This one leaves the hunt
/// running: landing somewhere else and carrying on is the entire point.
/// </para>
/// <para>
/// It watches from outside the hunt rather than being told by it. Everything it needs is in
/// the client — where the character is standing, and what the picker would accept — and asking
/// directly means it keeps working when the hunt has given up so thoroughly that it is no
/// longer reporting anything, which is the case it exists for.
/// </para>
/// </remarks>
public sealed class RelocateTask : IAuxTask
{
    private readonly GameActions _actions;
    private readonly TargetScan _scan;
    private readonly ILogger<RelocateTask> _logger;
    private readonly Func<TimeSpan> _clock;

    private readonly Dictionary<uint, byte> _health = [];

    private uint? _swung;

    private (int X, int Y)? _stood;
    private TimeSpan _stoodSince;
    private TimeSpan? _barrenSince;
    private bool _said;

    private readonly Func<RemoteProcess, (int X, int Y)?> _standing;

    /// <param name="standing">
    /// Where the character is. A seam rather than a call, because everything else this task
    /// does is reachable from a stub and this one read would otherwise make the whole of it
    /// untestable — the address it reads belongs to the game and there is no game in a test.
    /// </param>
    /// <param name="clock">
    /// The same, for time. What is being tested here is entirely about minutes passing, and a
    /// test that has to wait them out is a test nobody runs.
    /// </param>
    public RelocateTask(
        GameActions actions,
        TargetScan scan,
        ILogger<RelocateTask> logger,
        Func<RemoteProcess, (int X, int Y)?>? standing = null,
        Func<TimeSpan>? clock = null)
    {
        var started = Stopwatch.StartNew();

        _actions = actions;
        _scan = scan;
        _logger = logger;
        _standing = standing ?? ClientState.Where;
        _clock = clock ?? (() => started.Elapsed);
    }

    /// <inheritdoc/>
    public string Name => "relocate";

    /// <summary>Twice a second, which is often enough for something measured in minutes.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hunt = context.Settings.Hunt;

        // Only while the hunt is meant to be working. A player who has turned it off is
        // standing still on purpose, and a character parked in town is the most rooted thing
        // there is.
        if (!hunt.Enabled || hunt.Relocate is not { Ready: true } rule || !context.IsInWorld)
        {
            Forget();

            return;
        }

        var now = _clock();
        var wanted = TargetPicker.Wanted(_scan.All(context.Process), hunt, new HashSet<uint>());

        // Progress first, because it resets the clock the other two are read against.
        //
        // Three ways of not being stuck, and a character only counts as stuck when none of
        // them is happening. Standing still is not one of them — a bow's whole job is to
        // stand still, and reading that as being stuck is what had a character teleport out
        // of a fight it was winning.
        var fighting = Swinging(context.Process) | Hurting(wanted);
        var moved = Moved(context.Process, now);
        var barren = Barren(wanted, now);

        if (fighting)
        {
            _stoodSince = now;
        }

        var reason = Rooted(rule, now, moved) ? "has not moved and is killing nothing"
            : Empty(rule, barren) ? "has nothing to hunt"
            : null;

        if (reason is null)
        {
            return;
        }

        if (InventoryReader.FindByName(context.Bag, rule.Item) is not { } scroll)
        {
            // Said once per spell of trouble rather than twice a second. The clocks are
            // left running, so the scroll goes in on the pass after one is picked up.
            if (!_said)
            {
                _said = true;

                _logger.LogInformation(
                    "the character {Reason} and it is time to read {Item}, but there is none "
                    + "in the bag", reason, rule.Item);
            }

            return;
        }

        _actions.UseTeleportScroll(context.Process, scroll.Param);

        // The server sends the offer after the scroll and waits for the answer before moving
        // anything. Sending it when nothing is pending is ignored.
        _actions.ConfirmTeleport(context.Process);

        _logger.LogInformation("the character {Reason}; read {Item}", reason, rule.Item);

        // Both clocks, because the teleport answers both questions at once and the character
        // has not landed yet. Without this the next pass still reads the old spot and the old
        // empty room, and the whole stack goes in.
        Forget();
    }

    /// <summary>Whether the character has moved since it was last looked at.</summary>
    private bool Moved(RemoteProcess process, TimeSpan now)
    {
        var player = _standing(process);

        if (player is null)
        {
            // Unreadable is not stuck. A pass that cannot see the character is a pass with
            // nothing to say, and treating it as another second of not moving would spend a
            // scroll on a game that is loading.
            _stoodSince = now;

            return true;
        }

        if (_stood == player)
        {
            return false;
        }

        _stood = player;
        _stoodSince = now;

        return true;
    }

    /// <summary>
    /// Whether the character has started a blow since the last pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client keeps the tick its next attack may go out on and pushes it forward every
    /// time it starts one, so a number that has moved is a character that swung and a number
    /// that has not is a character that stood there. This is the cheap half of the question
    /// and the one that answers "did it even try".
    /// </para>
    /// <para>
    /// It cannot be the whole answer. The client pushes this forward for blows the server
    /// then refuses — which is the case the hunt has its own watchdogs for — and a rotation
    /// of nothing but skills never touches it at all. So the other half asks what actually
    /// happened to anything, and either one counts.
    /// </para>
    /// </remarks>
    private bool Swinging(RemoteProcess process)
    {
        if (!process.TryRead<uint>(HuntAddresses.NextAttackTick, out var tick))
        {
            return false;
        }

        var swung = _swung is { } last && tick != last;

        _swung = tick;

        return swung;
    }

    /// <summary>
    /// Whether anything nearby has lost health since the last pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason this exists is a character that stood still and read a scroll in the middle
    /// of a fight. Standing still is what a bow does — the standoff is the whole point of it —
    /// so "has not moved" on its own says nothing at all about whether the spot is working,
    /// and for a ranged character it says the opposite of the truth.
    /// </para>
    /// <para>
    /// Health rather than the client's attack flags. Its give-up path leaves the attack target
    /// pointing at the monster and clears only its own running flag, so every flag out here
    /// reads "in progress" for exactly as long as a wedge lasts. Health is the server's word
    /// and a wedged character cannot make it move.
    /// </para>
    /// <para>
    /// Only downwards, and only for something already seen. A monster walking on screen at
    /// full health is not progress, and one healing is somebody else's problem.
    /// </para>
    /// </remarks>
    private bool Hurting(IReadOnlyList<HuntTarget> wanted)
    {
        var hurt = false;

        foreach (var monster in wanted)
        {
            if (_health.TryGetValue(monster.Id, out var was) && monster.Health < was)
            {
                hurt = true;
            }

            _health[monster.Id] = monster.Health;
        }

        // Everything that has gone, whether it died or walked off. Left alone the map grows
        // for as long as the character stands there and a monster that comes back at full
        // health reads as having healed.
        if (_health.Count > wanted.Count)
        {
            var here = wanted.Select(monster => monster.Id).ToHashSet();

            foreach (var id in _health.Keys.Where(id => !here.Contains(id)).ToList())
            {
                _health.Remove(id);
            }
        }

        return hurt;
    }

    /// <summary>Since when there has been nothing worth attacking, or null if there is.</summary>
    private TimeSpan? Barren(IReadOnlyList<HuntTarget> wanted, TimeSpan now)
    {
        if (wanted.Count > 0)
        {
            _barrenSince = null;

            return null;
        }

        _barrenSince ??= now;

        return _barrenSince;
    }

    private bool Rooted(RelocateRule rule, TimeSpan now, bool moved) =>
        rule.StuckSeconds > 0
        && !moved
        && now - _stoodSince >= TimeSpan.FromSeconds(rule.StuckSeconds);

    private bool Empty(RelocateRule rule, TimeSpan? barren) =>
        rule.BarrenSeconds > 0
        && barren is { } since
        && _clock() - since >= TimeSpan.FromSeconds(rule.BarrenSeconds);

    /// <summary>Starts both clocks again, for a character that is somewhere else now.</summary>
    private void Forget()
    {
        _stood = null;
        _stoodSince = _clock();
        _barrenSince = null;
        _said = false;
        _swung = null;
        _health.Clear();
    }
}
