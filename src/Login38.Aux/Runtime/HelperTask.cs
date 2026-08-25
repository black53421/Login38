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

    private readonly HelperDispatch _dispatch;
    private readonly ILogger<HelperTask> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<(EntryKind Kind, int StateId), TimeSpan> _lastSent = [];

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
            if (!Due(entry, state, _lastSent, now, castSomething))
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
                _lastSent[(entry.Kind, entry.StateId)] = now;
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
        IReadOnlyDictionary<(EntryKind Kind, int StateId), TimeSpan> lastSent,
        TimeSpan now,
        bool castSomething)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lastSent);

        if (entry.StateId < 0 || entry.StateId >= BuffState.Effects)
        {
            return false;
        }

        if (entry.Kind == EntryKind.Skill && castSomething)
        {
            return false;
        }

        if (lastSent.TryGetValue((entry.Kind, entry.StateId), out var last) && now - last < Cooldown)
        {
            return false;
        }

        return !state.Active(entry.StateId);
    }
}
