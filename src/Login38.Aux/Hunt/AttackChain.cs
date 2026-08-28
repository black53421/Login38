using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Hunt;

/// <summary>
/// Starts, steers and stops the client's own attack chain.
/// </summary>
/// <remarks>
/// <para>
/// Five globals say what the client should be doing. Writing them is not enough on its own
/// and never was, which is the whole of why the character used to stand still until the
/// player clicked: the walk engine at <c>0x5A51A0</c> is a scheduled callback, and while
/// nothing is scheduled nothing reads them. A left click is what starts one —
/// <c>ClickHandler</c> at <c>0x4F6990</c> re-locks and then calls the engine outright.
/// </para>
/// <para>
/// So this asks <see cref="ClickHook"/> for a click and the client does the rest, on its
/// own thread, in its own order. What is left here is one flag and one request.
/// </para>
/// <para>
/// The interaction mode is why the request has to be repeatable rather than a one-off. It
/// is consumed: the engine puts it back to zero the moment it acts on it, whether the blow
/// landed or was refused by a cooldown that had not come round, and nothing re-arms it. A
/// hunt that set it once attacked once and then walked into the monster for ever.
/// </para>
/// <para>
/// Nothing here times a swing or tracks a cooldown, and nothing here decides a fight is
/// over. The client has two self-sustaining chains and between them they do all of it:
/// </para>
/// <para>
/// <c>Attack_dispatch</c> at <c>0x5A3770</c> lands one blow and, if
/// <see cref="HuntAddresses.AutoAttack"/> is set, posts <c>AutoAttack_funcA</c> at
/// <c>0x5A3010</c>. That one re-posts itself every weapon interval for as long as the fight
/// is worth continuing, and gives up on its own — putting its running flag back to zero and
/// not rescheduling — the moment the target pointer is null, the record is flagged gone at
/// <c>+0x58</c>, the record is playing its death at <c>+0x14</c>, the character is out of
/// range, or the auto-attack flag has been cleared. Every one of those is a condition this
/// would otherwise have had to detect and time for itself.
/// </para>
/// <para>
/// <c>WalkEngine_tick</c> at <c>0x5A51A0</c> does the walking, and its tail re-posts itself
/// while either the auto-attack flag or <see cref="HuntAddresses.WalkTargetValid"/> is set.
/// On arriving it re-locks from the pinned hover target, which is how a monster that walks
/// away is followed without anything here steering.
/// </para>
/// <para>
/// The two do not overlap. Every path in the walk engine that lands a blow returns before
/// that tail, so while the character is swinging the walk chain is dead and the attack chain
/// is alive; when the attack chain gives up, both are dead and nothing is scheduled. That
/// gap is the only thing this has to notice.
/// </para>
/// </remarks>
public sealed class AttackChain
{
    private readonly ClickHook _click;
    private readonly ILogger<AttackChain> _logger;

    public AttackChain(ClickHook click, ILogger<AttackChain> logger)
    {
        _click = click;
        _logger = logger;
    }

    /// <summary>
    /// Aims the client at a monster and starts it moving.
    /// </summary>
    /// <remarks>
    /// The chase hook has to be pinned to the same monster <em>before</em> this runs. The
    /// first thing the walk engine does on reaching its destination is re-lock, and a
    /// re-lock that reads an unpinned hover target clears the attack target and stops.
    /// </remarks>
    public bool Engage(RemoteProcess process, HuntTarget target)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            // The one thing a click sets that the re-lock does not. Everything else — what
            // to walk to, which interaction mode, which monster — the re-lock works out
            // from the pinned hover target, and it is better at it than this was.
            process.Write<uint>(HuntAddresses.AutoAttack, 1);

            // Asked for outright, where a nudge would ask the scheduler first. The flags
            // this just wrote have changed what anything already queued is going to do:
            // letting go of a target clears them, and the walk engine returns without
            // rescheduling when it finds them clear. So a tick in the queue at this moment
            // is a tick about to die, and waiting for it is waiting for the thing that was
            // supposed to happen to be cancelled.
            //
            // This was "it stops dead when something hits it". Changing target is a release
            // and an engage on the same pass, and the release leaves the queue holding a
            // tick it has just condemned. Asking the queue then gets "busy" for a tick that
            // is about to return and not come back, so the engage was refused and nothing
            // was left running: the flag set, the target cleared, and nothing scheduled to
            // notice either.
            _click.Request(process);

            _logger.LogDebug(
                "engaged {Name} ({Id:X8}) at ({X},{Y})", target.Name, target.Id, target.X, target.Y);

            return true;
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning("could not engage {Name}: {Message}", target.Name, e.Message);

            return false;
        }
    }

    /// <summary>
    /// Points the walk engine at where the target is now, and starts it again.
    /// </summary>
    /// <returns>Whether anything was done.</returns>
    /// <remarks>
    /// <para>
    /// Only when the engine has actually stopped, which is the whole of what this learned
    /// the hard way. A posted tick is one step. Posting one into a queue that already holds
    /// one gives the character two steps where the client wanted one, and what that looks
    /// like on screen is a character that suddenly moves at double speed.
    /// </para>
    /// <para>
    /// So the caller decides the character has gone quiet — nothing moving and no blow
    /// landing — and this refuses anyway if the scheduler says otherwise. Between the two
    /// there is no pass on which the client is stepping and this adds a step.
    /// </para>
    /// <para>
    /// While the engine is running there is nothing to do here at all: it re-locks onto the
    /// pinned hover target every time it arrives, so a monster that walks away is followed
    /// by the client, off its own re-lock, exactly as it is for someone playing by hand.
    /// </para>
    /// </remarks>
    public bool Resume(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            return Kick(process);
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning("could not restart the walk: {Message}", e.Message);

            return false;
        }
    }

    /// <summary>
    /// Cuts the chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All of them, because the client only clears some. Read straight after a kill it had
    /// dropped the attack target and the hover target and put the interaction mode back to 1
    /// by itself — and left <see cref="HuntAddresses.AutoAttack"/> and
    /// <see cref="HuntAddresses.WalkTargetValid"/> set. "The chain stopped" and "the flags
    /// are clean" are not the same state.
    /// </para>
    /// <para>
    /// <see cref="HuntAddresses.AttackInFlight"/> last, and it is the one that matters most.
    /// Leaving it set is not an untidy flag, it is a wedge: the re-lock will not run while
    /// it points at anything, so nothing can be attacked, so the monster it points at cannot
    /// die, so nothing will ever clear it. A player clears it by clicking; this has no
    /// clicks to spare, so it clears it the same way it sets everything else.
    /// </para>
    /// <para>
    /// <see cref="HuntAddresses.HoverTarget"/> is <em>not</em> cleared here, and that is a
    /// rule rather than an oversight. Writing zero to it crashes the game: the drawing code
    /// at <c>0x4F2D64</c> loads it and dereferences it at <c>+0x64</c> with no null check at
    /// all, so the next frame after the write faults on <c>[0x64]</c>. Read off the crash
    /// records — <c>0xC0000005</c> at offset <c>0x000F2D6A</c>, twice, and only in the
    /// builds that cleared it.
    /// </para>
    /// <para>
    /// The client's own thirty-five clearing sites evidently do more than write the global,
    /// or are reached only where that drawing path cannot run. Whatever the guard is, it is
    /// not this write, and the symptom it was meant to fix — a dead monster's name staying
    /// on screen — is worth very much less than the game staying up.
    /// </para>
    /// </remarks>
    public void Stop(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            process.Write<uint>(HuntAddresses.AutoAttack, 0);
            process.Write<byte>(HuntAddresses.WalkTargetValid, 0);
            process.Write<uint>(HuntAddresses.AttackTarget, 0);
            process.Write<uint>(HuntAddresses.AttackInFlight, 0);

            // The client's own interlock between its two attack halves. Tearing the chain
            // down without lowering it leaves the next Attack_dispatch refusing at its
            // entry guard, which is a character that locks on to things and never swings.
            // Safe to lower here because g_auto_attack has just been cleared, so a funcA
            // still scheduled will give up on its next run anyway.
            process.Write<uint>(HuntAddresses.AutoAttackRunning, 0);
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning("could not stop the chain: {Message}", e.Message);
        }
    }

    /// <summary>Whether the client still holds a target.</summary>
    /// <remarks>
    /// Five places write it and only one of them sets it. It is cleared when the record is
    /// removed from the world — which is what a kill ends as — when a re-lock finds nothing
    /// worth locking onto, and when the player cancels. So it doubles as "is the fight over"
    /// without anything here having to watch for a death, and it is deliberately not the
    /// same question as "is the client busy": between two swings the target is still held.
    /// </remarks>
    public static bool Engaged(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return process.TryRead<uint>(HuntAddresses.AttackTarget, out var target) && target != 0;
    }

    /// <summary>Whether the client's own attack chain intends to run again.</summary>
    /// <remarks>
    /// <para>
    /// <c>AutoAttack_funcA</c> at <c>0x5A3010</c> keeps this set for as long as it is going to
    /// reschedule itself, and writes zero on every path it gives up on. One of those paths is
    /// the whole of the problem this exists for: when the target has stepped out of range it
    /// does not walk after it — it clears this, returns, and schedules nothing at all. Closing
    /// a gap is the walk engine's job, and by then the walk engine has finished too.
    /// </para>
    /// <para>
    /// So this is the difference between "between two swings" and "nothing further is going to
    /// happen". <see cref="Engaged"/> cannot tell those apart, because that give-up path
    /// leaves <see cref="HuntAddresses.AttackTarget"/> still pointing at the monster — which
    /// is what left a character standing at a bow's standoff for a second at a time, every
    /// time the thing it was shooting took a step.
    /// </para>
    /// <para>
    /// Unreadable counts as running. The expensive mistake is an unwanted click and the cheap
    /// one is a late nudge, which the idle timer picks up anyway.
    /// </para>
    /// </remarks>
    public static bool Running(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return !process.TryRead<uint>(HuntAddresses.AutoAttackRunning, out var running)
            || running != 0;
    }

    /// <summary>
    /// Whether the client already has a walk-engine tick waiting to run.
    /// </summary>
    /// <remarks>
    /// There is no flag for it, so this reads the scheduler's queue: a count, a pointer to
    /// the entries, and a callback in each. Anything unreadable answers yes, because the
    /// expensive mistake is posting a second tick and the cheap one is missing a pass.
    /// </remarks>
    internal static bool TickQueued(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.TryRead<int>(HuntAddresses.Scheduler, out var count)
            || !process.TryRead<uint>(
                HuntAddresses.Scheduler + HuntAddresses.SchedulerEntries, out var entries))
        {
            return true;
        }

        if (count <= 0)
        {
            return false;
        }

        if (count > HuntAddresses.SchedulerLimit || entries == 0)
        {
            return true;
        }

        var queue = new byte[count * HuntAddresses.SchedulerEntryLength];

        return !process.TryReadBytes(new GameAddress(entries), queue) || HoldsTick(queue);
    }

    /// <summary>Whether a copy of the scheduler's entries names the walk engine.</summary>
    /// <remarks>
    /// Entries are twelve bytes — due time, callback, argument — and the callback is the
    /// middle one. Reading the stride or the offset wrong finds a tick that is not there,
    /// or misses one that is, and either way the character's speed is what tells you.
    /// </remarks>
    internal static bool HoldsTick(ReadOnlySpan<byte> queue)
    {
        for (var at = HuntAddresses.SchedulerCallback;
             at + sizeof(uint) <= queue.Length;
             at += HuntAddresses.SchedulerEntryLength)
        {
            if (BitConverter.ToUInt32(queue[at..]) == HuntAddresses.WalkEngineTick.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts the engine again, if it has actually stopped.</summary>
    /// <remarks>
    /// The queue is asked first, because one run of the engine is one step of the character
    /// and asking for one while the client is already stepping is what a character moving
    /// at double speed looks like. That is worth having here and nowhere else: this is the
    /// periodic nudge, sent while the flags say the chain should be running, so a queued
    /// tick really is the chain running. <see cref="Engage"/> has just changed those flags
    /// and cannot assume the same.
    /// </remarks>
    private bool Kick(RemoteProcess process) =>
        !TickQueued(process) && _click.Request(process);
}
