using Login38.Aux.Toggles;
using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// A left click on a monster, replayed on the game's own thread.
/// </summary>
/// <remarks>
/// <para>
/// Everything a click does — re-lock, then run the walk engine — has to happen where the
/// client does it. Doing it from a thread of the launcher's own works often enough to look
/// right and is wrong in ways that surface far from their cause: the engine steps the
/// character, drives the animation state machine and sends packets, and two threads writing
/// one packet stream is how a session gets dropped.
/// </para>
/// <para>
/// So this sits on the scheduler's pump at <c>0x590B30</c> — the routine that drains the
/// due callbacks, which the main loop calls every frame. That is the game's thread, and it
/// is the point in the frame where the client itself is about to run this kind of work.
/// Asking for a click costs the launcher one four-byte write.
/// </para>
/// <para>
/// The guards are <c>ClickHandler</c>'s own at <c>0x4F6990</c>, condition for condition.
/// Each is a state the client refuses to start the engine from, and running it from one of
/// them is a character part way through an animation being told to take another step.
/// </para>
/// <para>
/// It does nothing at all while the slot holds zero, which is every frame the hunt is not
/// asking for anything.
/// </para>
/// </remarks>
internal static class ClickDetour
{
    /// <summary>Ask for a whole click: re-lock on what is pinned, then walk.</summary>
    internal const uint Clicking = 1;

    /// <summary>
    /// Ask only for the walk, at a destination the launcher has already written.
    /// </summary>
    /// <remarks>
    /// The difference is the re-lock. It works out where to go from the pinned hover target,
    /// which is right for chasing a monster and wrong for crossing a room to get round a wall
    /// — there it would replace the waypoint with the monster and send the character straight
    /// back into the stone it was walking around.
    /// </remarks>
    internal const uint Walking = 2;

    /// <summary>
    /// The pump's prologue, which the jump replaces.
    /// </summary>
    /// <remarks>
    /// <c>push ebp; mov ebp, esp; sub esp, 0x14</c> — six bytes, three whole instructions,
    /// and nothing branches into the middle of them.
    /// </remarks>
    internal static HookSite Site => new(
        HuntAddresses.SchedulerPump,
        [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14],
        HuntAddresses.SchedulerPump + 6);

    /// <summary>Where the request slot sits, relative to the cave.</summary>
    /// <param name="Cave">Where the allocation starts.</param>
    /// <param name="Code">How long the code is, which is where the slot goes.</param>
    internal readonly record struct Layout(GameAddress Cave, int Code)
    {
        /// <summary>
        /// What is being asked for, or zero for nothing. The detour clears it as it takes it.
        /// </summary>
        /// <remarks>
        /// Cleared inside the cave rather than by the launcher afterwards, so that one
        /// request is one click however the two threads interleave. A launcher that cleared
        /// it itself would sometimes clear a request the pump had not read yet.
        /// </remarks>
        public GameAddress Request => Cave + Code;

        /// <summary>The request, kept past the point where the guards clobber it.</summary>
        /// <remarks>
        /// The guards below test bytes into <c>AL</c>, which is the low end of the register
        /// the request arrived in. Somewhere to put it is cheaper than reordering the guards
        /// around it, and the cave is the only place a second thread cannot disturb.
        /// </remarks>
        public GameAddress Kind => Request + 4;

        /// <summary>How long the whole allocation has to be.</summary>
        public int Size => Code + (sizeof(uint) * 2);
    }

    /// <summary>How much room the cave needs, which does not depend on where it goes.</summary>
    internal static Layout LayoutFor(GameAddress cave) =>
        new(cave, Emit(new Layout(cave, 0)).Length);

    /// <summary>Builds the cave, code and slot together.</summary>
    internal static byte[] Build(GameAddress cave)
    {
        var layout = LayoutFor(cave);

        return [.. Emit(layout), .. new byte[sizeof(uint) * 2]];
    }

    private static byte[] Emit(Layout layout)
    {
        var code = new ShellcodeBuilder(layout.Cave);

        // Everything, because what follows calls into the client. Which registers a routine
        // may use is a question about a compiler's conventions, and the answer costs two
        // bytes each way.
        code.PushAd().PushFd();

        code.MovEaxFrom(layout.Request).TestEaxEax();
        var done = code.ShortJumpIfZero();

        // Taken before the work, not after: the pump can reach here again while the engine
        // it is about to run posts and drains further ticks.
        code.MovDwordPtr(layout.Request, 0);
        code.MovEaxTo(layout.Kind);

        // Before anything else, and before the re-lock rather than merely before the engine.
        // A queued cast belongs to the client's own sequence: the engine's tail is what fires
        // it, and firing it from here catches it half built. Nothing this cave does is worth
        // landing in the middle of that, so the whole pass is given up rather than guarded
        // step by step.
        code.MovAlFrom(HuntAddresses.CastQueued).TestAlAl();
        var casting = code.ShortJumpIfNotZero();

        // What a real click does first, and the step this cave used to skip.
        //
        // The re-lock's very first guard is that this is null, and the re-lock is also what
        // sets it: on the pass where it dispatches a blow it writes the target here, drops
        // the interaction mode to zero, and clears WalkTargetValid on the way out. From
        // then on every call returns at that guard having done nothing but switch the walk
        // off again — so the client can never go back to chasing, whatever it is asked.
        //
        // The client's own answer is the input handler at 0x4F7050, which clears this as
        // the player presses the button. A launcher that never sends input never reaches
        // it. Melee hid the problem: the blow lands, the monster dies, the record is
        // removed, and removal clears this as a side effect. A bow fires from ten tiles
        // away at something that does not die this second, and the latch simply stays.
        //
        // Read off a stopped character: mode 1, WalkTargetValid 0, no walk tick scheduled,
        // and a live monster four tiles away it would not walk to.
        // Both halves of a click are wanted for a monster and neither is wanted for a
        // destination the launcher has just written down itself. The re-lock's whole job is
        // to decide where to go from what the mouse is over, so letting it run would throw
        // the waypoint away and send the character back at the monster it cannot reach — and
        // pressing into a wall is the thing the waypoint exists to avoid.
        code.MovEaxFrom(layout.Kind).CmpEax(Walking);
        var steering = code.ShortJumpIfZero();

        code.MovDwordPtr(HuntAddresses.AttackInFlight, 0);

        code.MovEax(HuntAddresses.ReTargetFromHover.Value).CallEax();

        // Nothing to walk to. The re-lock says so itself, by clearing this on every path
        // where it decided against the target.
        code.MarkLabel(steering);
        code.MovAlFrom(HuntAddresses.WalkTargetValid).TestAlAl();
        var idle = code.ShortJumpIfZero();

        code.MovEcxFrom(HuntAddresses.LocalPlayer)             // __fastcall
            .MovEax(HuntAddresses.PlayerBusy.Value)
            .CallEax()
            .TestAlAl();
        var busy = code.ShortJumpIfNotZero();

        code.MovAlFrom(HuntAddresses.TickPending).TestAlAl();
        var pending = code.ShortJumpIfNotZero();

        code.MovAlFrom(HuntAddresses.KickBlockerA).TestAlAl();
        var blockedA = code.ShortJumpIfNotZero();

        code.MovAlFrom(HuntAddresses.KickBlockerB).TestAlAl();
        var blockedB = code.ShortJumpIfNotZero();

        code.MovEax(HuntAddresses.WalkEngineTick.Value).CallEax();

        code.MarkLabel(done)
            .MarkLabel(casting)
            .MarkLabel(idle)
            .MarkLabel(busy)
            .MarkLabel(pending)
            .MarkLabel(blockedA)
            .MarkLabel(blockedB)
            .PopFd()
            .PopAd();

        // The prologue this displaced, put back, and then straight into the pump. Nothing
        // here changes what the pump does — it only borrows the moment before it runs.
        code.Bytes(Site.Stock)
            .JumpTo(Site.Resume);

        return code.Build();
    }
}
