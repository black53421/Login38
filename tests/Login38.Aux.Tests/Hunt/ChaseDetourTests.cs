using Login38.Aux.Hunt;
using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers the cave that keeps the client chasing a monster that moves.
/// </summary>
/// <remarks>
/// This code runs on the client's own thread at the entry to one of its routines. There is
/// no way to observe it going wrong except as the game misbehaving, so what a test can do
/// is hold the shape: the right site, the client's own prologue put back, and a slot that
/// makes the whole thing inert while it holds zero.
/// </remarks>
public sealed class ChaseDetourTests
{
    private static readonly GameAddress Cave = new(0x0900_0000);

    // push ebp; mov ebp, esp; sub esp, 0x1C — the re-lock routine's prologue, and three
    // whole instructions, which is what makes it safe to displace.
    [Fact]
    public void TakesTheRelockRoutinesPrologue()
    {
        ChaseDetour.Site.Address.ShouldBe(new GameAddress(0x004F5DE0));
        ChaseDetour.Site.Stock.ShouldBe([0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C]);
    }

    [Fact]
    public void ComesBackAfterWhatItDisplaced() =>
        ChaseDetour.Site.Resume.ShouldBe(ChaseDetour.Site.Address + ChaseDetour.Site.Stock.Length);

    // Six bytes is a five-byte jump and a no-op. Anything shorter could not hold one.
    [Fact]
    public void HasRoomForTheJump() =>
        ChaseDetour.Site.JumpTo(Cave).Length.ShouldBe(ChaseDetour.Site.Stock.Length);

    [Fact]
    public void PutsTheSlotAfterTheCode()
    {
        var layout = ChaseDetour.LayoutFor(Cave);

        layout.Target.ShouldBe(Cave + layout.Code);
        layout.Size.ShouldBe(layout.Code + sizeof(uint));
        ChaseDetour.Build(Cave).Length.ShouldBe(layout.Size);
    }

    // The slot starts empty, so the detour does nothing from the moment it is reachable.
    // The jump goes in after the cave, and a cave that did something on its first pass
    // would act before anything had asked it to.
    [Fact]
    public void StartsAimedAtNothing()
    {
        var code = ChaseDetour.Build(Cave);
        var layout = ChaseDetour.LayoutFor(Cave);

        BitConverter.ToUInt32(code, layout.Code).ShouldBe(0u);
    }

    // The displaced instructions, replayed before control goes back. Leaving them out
    // would drop the routine into its own body with no frame.
    [Fact]
    public void ReplaysThePrologueBeforeReturning()
    {
        var code = ChaseDetour.Build(Cave);
        var prologue = code.AsSpan().LastIndexOf(ChaseDetour.Site.Stock.AsSpan());

        prologue.ShouldBeGreaterThan(0);
        code[prologue + ChaseDetour.Site.Stock.Length].ShouldBe((byte)0xE9);
    }

    // Flags as well as the register. Whether a compiler treats them as live across a call
    // is not something worth being right about for the sake of two bytes.
    [Fact]
    public void SavesTheFlagsAsWellAsTheRegister()
    {
        var code = ChaseDetour.Build(Cave);

        code[0].ShouldBe((byte)0x9C);                          // pushfd
        code[1].ShouldBe((byte)0x50);                          // push eax
    }

    // Whatever else changes, the value has to end up in the global the re-lock routine
    // reads — that address is the entire reason this exists.
    [Fact]
    public void WritesTheHoverTargetTheRelockRoutineReads()
    {
        var code = ChaseDetour.Build(Cave);
        var store = new byte[] { 0xA3, 0x40, 0xF4, 0xAB, 0x00 };   // mov [0x00ABF440], eax

        code.AsSpan().IndexOf(store).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void AsksForTheSameRoomWhereverItLands() =>
        ChaseDetour.LayoutFor(new GameAddress(0)).Size.ShouldBe(ChaseDetour.LayoutFor(Cave).Size);
}
