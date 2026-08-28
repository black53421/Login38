using Login38.Aux.Game;
using Login38.Aux.Toggles;
using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// The code that keeps the client chasing a moving monster.
/// </summary>
/// <remarks>
/// <para>
/// The walk engine re-aims at a target that has moved by calling <c>ReTarget_fromHover</c>
/// at <c>0x4F5DE0</c>, which decides whether to keep going purely from what it finds in
/// <see cref="HuntAddresses.HoverTarget"/>. Find a monster there and it re-reads that
/// monster's current tile and walks on; find zero and it clears the attack target and
/// stops.
/// </para>
/// <para>
/// The client clears that global from thirty-five places, so writing it from outside once
/// per pass loses the race, and the symptom is exact and familiar: the character walks
/// towards a monster, the monster takes a step, and the character stops half way. What
/// wins the race is writing it at the instant it is read — this detour sits on the
/// re-lock routine's own entry and pins the value back before the routine looks.
/// </para>
/// <para>
/// It does nothing at all while the slot holds zero, which is every moment the hunt is not
/// running. Clearing the slot is also how the hunt lets go: the next re-lock reads zero
/// and the client stops chasing by its own logic rather than by being interrupted.
/// </para>
/// <para>
/// The pin has to die with the monster, and that is why the record is checked here rather
/// than only from the launcher. Winning the race against thirty-five places that clear the
/// hover target means also winning it against the one that clears it <em>correctly</em> —
/// the client drops its target when the thing dies, and a pin that keeps writing it back
/// re-targets a corpse for ever. The character then stands over a body that is not on the
/// screen any more, which is exactly what it did. Two hundred milliseconds is far too slow
/// to notice that from outside; here it is three compares at the moment it matters.
/// </para>
/// </remarks>
internal static class ChaseDetour
{
    /// <summary>
    /// The re-lock routine's prologue, which the jump replaces.
    /// </summary>
    /// <remarks>
    /// <c>push ebp; mov ebp, esp; sub esp, 0x1C</c> — six bytes, three whole instructions,
    /// and nothing branches into the middle of them.
    /// </remarks>
    internal static HookSite Site => new(
        HuntAddresses.ReTargetFromHover,
        [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C],
        HuntAddresses.ReTargetFromHover + 6);

    /// <summary>Where the pinned target sits, relative to the cave.</summary>
    /// <param name="Cave">Where the allocation starts.</param>
    /// <param name="Code">How long the code is, which is where the slot goes.</param>
    internal readonly record struct Layout(GameAddress Cave, int Code)
    {
        /// <summary>
        /// The monster to pin, or zero to leave the client alone.
        /// </summary>
        /// <remarks>
        /// In the cave rather than in the client's own data because there is no spare
        /// global to borrow, and because a slot that dies with the allocation cannot
        /// outlive the hook that reads it.
        /// </remarks>
        public GameAddress Target => Cave + Code;

        /// <summary>How long the whole allocation has to be.</summary>
        public int Size => Code + sizeof(uint);
    }

    /// <summary>How much room the cave needs, which does not depend on where it goes.</summary>
    internal static Layout LayoutFor(GameAddress cave) =>
        new(cave, Emit(new Layout(cave, 0)).Length);

    /// <summary>Builds the cave, code and slot together.</summary>
    internal static byte[] Build(GameAddress cave)
    {
        // Once to learn how long the code is, then again knowing where the slot landed.
        // The length cannot move between the two: the slot's address is four bytes wherever
        // it is.
        var layout = LayoutFor(cave);

        return [.. Emit(layout), .. new byte[sizeof(uint)]];
    }

    private static byte[] Emit(Layout layout)
    {
        var code = new ShellcodeBuilder(layout.Cave);

        // The flags as well as the registers. Whether they are live on entry to a called
        // function is a question about a compiler's conventions, and the answer costs one
        // byte each way.
        code.PushFd().PushEax().PushEcx();

        code.MovEaxFrom(layout.Target)                        // mov eax, [target]
            .TestEaxEax();
        var idle = code.ShortJumpIfZero();

        // Still the record it was. A freed block keeps its contents until something else is
        // put there, so the id alone would go on matching a monster that no longer exists;
        // the vtable is what the client overwrites first when the memory is reused.
        code.MovEcxFromEax()                                  // mov ecx, [eax]
            .CmpEcx(HeapWalk.PlayerVtable.Value);
        var recycled = code.ShortJumpIfNotEqual();

        // Gone, in the client's own words.
        code.CmpBytePtrEax((byte)HuntAddresses.EntityIsGone, 0);
        var gone = code.ShortJumpIfNotEqual();

        // Playing its death. Pinning through this is what keeps a corpse selected for the
        // whole of the animation and then past the end of it.
        code.CmpBytePtrEax((byte)HuntAddresses.EntityAction, HuntAddresses.DyingAction);
        var dying = code.ShortJumpIfZero();

        code.MovEaxTo(HuntAddresses.HoverTarget);              // mov [hover_target], eax
        var pinned = code.ShortJumpAlways();

        // Let go, here rather than by telling the launcher. The client clears its own
        // target on the next re-lock and the hunt reads that as the kill it is.
        code.MarkLabel(recycled)
            .MarkLabel(gone)
            .MarkLabel(dying)
            .MovDwordPtr(layout.Target, 0);

        code.MarkLabel(idle)
            .MarkLabel(pinned)
            .PopEcx()
            .PopEax()
            .PopFd();

        // The prologue this displaced, put back, and then straight into the rest of the
        // routine. Nothing here inspects what the routine does — it only makes sure the
        // routine finds the right thing when it looks.
        code.Bytes(Site.Stock)
            .JumpTo(Site.Resume);

        return code.Build();
    }
}
