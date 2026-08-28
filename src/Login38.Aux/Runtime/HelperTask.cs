using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Keeps the player's buffs up.
/// </summary>
/// <remarks>
/// <para>
/// The list a player builds once and leaves alone: haste, the defensive spells, the food
/// that stops them starving. Each line names an effect number as well as a command, and
/// that number is the whole reason this works — the client keeps a byte per effect, so the
/// helper can ask "am I hasted" rather than casting on a timer and hoping.
/// </para>
/// <para>
/// Items and skills are treated differently on purpose. Several items can be used in one
/// pass because using one is instant and does not stop the next. Only one skill goes per
/// pass, because the client casts one at a time and the server answers the rest with an
/// error — and an error means the effect byte never gets set, so the helper would try
/// again immediately, for ever.
/// </para>
/// <para>
/// That last sentence is also true across passes, which the cooldown alone does not fix: a
/// buff the server will not grant at all is asked for again every five seconds for as long
/// as the character stands there. <see cref="Wait"/> is the answer to it.
/// </para>
/// </remarks>
public class HelperTask : IAuxTask
{
    /// <summary>
    /// How long an entry waits after being acted on.
    /// </summary>
    /// <remarks>
    /// Long enough for the packet to reach the server and the answer to come back and set
    /// the effect byte. Without it, everything on the list would be sent again on the next
    /// pass while the first was still in flight.
    /// </remarks>
    internal static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    /// <summary>How many sends in a row may go unanswered before the helper eases off.</summary>
    /// <remarks>
    /// Three, so that a slow server or a packet lost on the way still gets the ordinary
    /// cooldown twice over before anything is read into it.
    /// </remarks>
    internal const int Tolerated = 3;

    /// <summary>The longest an entry is ever left, as a multiple of <see cref="Cooldown"/>.</summary>
    /// <remarks>
    /// A minute. Long enough to be out of the player's way, short enough that a buff which
    /// becomes grantable again — the transformation ended, the shrine was left, the level
    /// went up — comes back without the player having to touch anything.
    /// </remarks>
    private const int Longest = 12;

    /// <summary>What the helper remembers about asking for one effect.</summary>
    /// <param name="When">When it last asked.</param>
    /// <param name="Unanswered">
    /// How many times in a row it has asked without the effect appearing. Zero is not a
    /// state this holds: an effect that appears is forgotten about entirely.
    /// </param>
    /// <param name="Transformed">
    /// Whether the character was transformed for every one of those, which is the one piece
    /// of the game's state the helper can see that decides whether a buff is grantable.
    /// </param>
    internal readonly record struct Attempt(TimeSpan When, int Unanswered, bool Transformed);

    private readonly HelperDispatch _dispatch;
    private readonly ILogger<HelperTask> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<(EntryKind Kind, int StateId), Attempt> _sent = [];

    private byte? _class;

    public HelperTask(HelperDispatch dispatch, ILogger<HelperTask> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "buffs";

    /// <summary>Twice a second.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entries = context.Settings.HelperEntries;

        if (!context.Settings.HelperEnabled || entries.Count == 0)
        {
            return;
        }

        if (!context.IsInWorld)
        {
            return;
        }

        if (Read(context.Process) is not { } state)
        {
            return;
        }

        var now = _clock.Elapsed;
        var castSomething = false;

        if (_class != state.Class)
        {
            // Worth saying once. It is what decides whether an effect number in the
            // settings means what the settings say, and it is invisible otherwise.
            _logger.LogInformation("Keeping {Count} buffs up for class {Class}",
                entries.Count, state.Class);

            _class = state.Class;
        }

        foreach (var entry in entries)
        {
            var key = (entry.Kind, entry.StateId);

            // The effect is on the character, so everything remembered about trying to put
            // it there is about a character who no longer exists. Forgetting it rather than
            // only clearing the count is what lets a buff that runs out be replaced on the
            // pass that notices, instead of a cooldown after it.
            if (state.Active(entry.StateId))
            {
                _sent.Remove(key);
                continue;
            }

            if (!Due(entry, state, _sent, now, castSomething))
            {
                continue;
            }

            var result = _dispatch.Send(context.Process, entry, context.Bag);

            // The cooldown is for a packet in flight, so it starts when one was sent. It
            // used to be marked on a skip as well — which is what the reference did, to
            // keep the log quiet — and that turned "the item is not in the bag yet" into
            // five seconds of doing nothing about a buff the player had just asked for.
            // The log is kept quiet where the log is, instead.
            if (result != DispatchResult.Skipped)
            {
                _sent.TryGetValue(key, out var before);

                var transformed = state.At(BuffState.Transformed);

                _sent[key] = new Attempt(
                    now,
                    before.Unanswered + 1,

                    // Every send so far, not merely this one. One failure that happened to
                    // land inside a transformation says nothing about the two before it.
                    before.Unanswered == 0 ? transformed : before.Transformed && transformed);

                if (before.Unanswered + 1 == Tolerated)
                {
                    _logger.LogInformation(
                        "{Name} has been asked for {Count} times without arriving; easing off",
                        entry.Name, Tolerated);
                }
            }

            if (result == DispatchResult.Cast)
            {
                castSomething = true;
            }
        }
    }

    /// <summary>What the character is currently under the effect of.</summary>
    /// <remarks>
    /// Overridable so that a whole pass can be exercised against a table laid out by hand.
    /// The real one reads a fixed address, which a test has no way to arrange.
    /// </remarks>
    internal virtual BuffState? Read(RemoteProcess process) => BuffState.Read(process);

    /// <summary>
    /// Whether one entry should be acted on this pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An entry with no effect number is passed over rather than sent. There would be no
    /// way to tell whether it had worked, so it would be sent every cooldown for ever —
    /// which is what a timer row is for, and the player has those.
    /// </para>
    /// <para>
    /// The <paramref name="castSomething"/> flag is what limits a pass to one skill. It has
    /// to be checked before the effect table, or a second skill would be marked as sent
    /// without being sent.
    /// </para>
    /// </remarks>
    internal static bool Due(
        HelperEntry entry,
        BuffState state,
        IReadOnlyDictionary<(EntryKind Kind, int StateId), Attempt> sent,
        TimeSpan now,
        bool castSomething)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sent);

        if (entry.StateId < 0 || entry.StateId >= BuffState.Effects)
        {
            return false;
        }

        if (entry.Kind == EntryKind.Skill && castSomething)
        {
            return false;
        }

        if (state.Active(entry.StateId))
        {
            return false;
        }

        if (!sent.TryGetValue((entry.Kind, entry.StateId), out var last))
        {
            return true;
        }

        // Asked for over and over, every one of them while the character was transformed,
        // and it has never once arrived: the server is refusing, and it will go on refusing
        // for as long as the transformation lasts. Left alone until it ends rather than
        // asked for every cooldown underneath it.
        if (last.Unanswered >= Tolerated && last.Transformed && state.At(BuffState.Transformed))
        {
            return false;
        }

        return now - last.When >= Wait(last.Unanswered);
    }

    /// <summary>
    /// How long an entry waits before it is asked for again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordinary cooldown while the server is still answering, and then twice as long
    /// each time it does not, up to a minute.
    /// </para>
    /// <para>
    /// A buff that never arrives is not the helper's mistake to correct — the client sets
    /// the effect byte from a packet, with no condition on it whatsoever, so a byte that
    /// stays clear means the packet never came. Nothing the launcher can read says why. All
    /// it can sensibly do is stop asking so often, which is the difference between a
    /// character who casts something every five seconds all evening and one who tries it
    /// occasionally.
    /// </para>
    /// </remarks>
    internal static TimeSpan Wait(int unanswered)
    {
        if (unanswered < Tolerated)
        {
            return Cooldown;
        }

        // Capped before the shift as well as after it. The count keeps rising for an entry
        // the server never grants, and a shift wide enough to wrap would quietly hand back
        // the short wait again.
        var doublings = Math.Min(unanswered - Tolerated + 1, 8);

        return Cooldown * Math.Min(1 << doublings, Longest);
    }
}
