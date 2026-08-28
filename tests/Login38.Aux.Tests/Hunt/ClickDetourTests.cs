using Login38.Aux.Hunt;
using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers the cave that replays a click on the game's own thread.
/// </summary>
/// <remarks>
/// This runs at the entry to the scheduler's pump, which the main loop calls every frame,
/// and from there it calls into the client. There is no way to observe it going wrong
/// except as the game misbehaving, so what a test can do is hold the shape: the right site,
/// the client's own prologue put back, the client's own refusals, and a slot that makes the
/// whole thing inert while it holds zero.
/// </remarks>
public sealed class ClickDetourTests
{
    private static readonly GameAddress Cave = new(0x0900_0000);

    // push ebp; mov ebp, esp; sub esp, 0x14 — the pump's prologue, and three whole
    // instructions, which is what makes it safe to displace.
    [Fact]
    public void TakesTheSchedulerPumpsPrologue()
    {
        ClickDetour.Site.Address.ShouldBe(new GameAddress(0x00590B30));
        ClickDetour.Site.Stock.ShouldBe([0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14]);
    }

    [Fact]
    public void ComesBackAfterWhatItDisplaced() =>
        ClickDetour.Site.Resume.ShouldBe(ClickDetour.Site.Address + ClickDetour.Site.Stock.Length);

    [Fact]
    public void HasRoomForTheJump() =>
        ClickDetour.Site.JumpTo(Cave).Length.ShouldBe(ClickDetour.Site.Stock.Length);

    // Two slots now: what is being asked for, and somewhere to keep it while the guards
    // clobber the register it arrived in.
    [Fact]
    public void PutsTheSlotsAfterTheCode()
    {
        var layout = ClickDetour.LayoutFor(Cave);

        layout.Request.ShouldBe(Cave + layout.Code);
        layout.Kind.ShouldBe(layout.Request + sizeof(uint));
        layout.Size.ShouldBe(layout.Code + (sizeof(uint) * 2));
        ClickDetour.Build(Cave).Length.ShouldBe(layout.Size);
    }

    // The two things it can be asked for. A whole click re-locks on what is pinned and then
    // walks; a steer walks at a destination the launcher wrote and must not re-lock, because
    // the re-lock would replace that destination with the monster and send the character back
    // into the wall the destination exists to get round.
    [Fact]
    public void TellsTheTwoRequestsApart()
    {
        ClickDetour.Clicking.ShouldNotBe(ClickDetour.Walking);
        ClickDetour.Clicking.ShouldNotBe(0u);
        ClickDetour.Walking.ShouldNotBe(0u);
    }

    // The slot starts empty, so the detour does nothing on the frame it becomes reachable.
    [Fact]
    public void StartsAskingForNothing()
    {
        var cave = ClickDetour.Build(Cave);

        cave[^4..].ShouldAllBe(value => value == 0);
    }

    [Fact]
    public void PutsTheClientsOwnPrologueBackBeforeReturning()
    {
        var cave = ClickDetour.Build(Cave);
        var stock = ClickDetour.Site.Stock.ToArray();

        Index(cave, stock).ShouldBeGreaterThan(0);
        Index(cave, [.. stock, 0xE9]).ShouldBeGreaterThan(0);      // and then jmp rel32
    }

    // Both halves of a click, in the order ClickHandler does them. The re-lock decides what
    // to walk to and re-arms the interaction mode, which the engine consumes every time it
    // acts on it; running the engine without it is a step taken towards nothing.
    [Fact]
    public void ReLocksAndThenRunsTheWalkEngine()
    {
        var cave = ClickDetour.Build(Cave);

        Calls(cave, HuntAddresses.ReTargetFromHover).ShouldBe(1);
        Calls(cave, HuntAddresses.WalkEngineTick).ShouldBe(1);

        Index(cave, Call(HuntAddresses.ReTargetFromHover))
            .ShouldBeLessThan(Index(cave, Call(HuntAddresses.WalkEngineTick)));
    }

    // Every condition ClickHandler tests before it starts the engine. One missing is one
    // state the client refuses to run the walk engine from and this one does not.
    [Fact]
    public void RefusesEveryStateTheClientRefuses()
    {
        var cave = ClickDetour.Build(Cave);

        Calls(cave, HuntAddresses.PlayerBusy).ShouldBe(1);
        Reads(cave, HuntAddresses.WalkTargetValid).ShouldBe(1);
        Reads(cave, HuntAddresses.TickPending).ShouldBe(1);
        Reads(cave, HuntAddresses.KickBlockerA).ShouldBe(1);
        Reads(cave, HuntAddresses.KickBlockerB).ShouldBe(1);
    }

    // And one the client does not, because the client never gets here at this moment. The
    // engine's tail is what fires a queued cast; running it from the pump catches the cast
    // half built and wedges a mode bit that is never cleared. Asked before the re-lock, so
    // the pass is abandoned rather than half taken.
    [Fact]
    public void StaysOutOfTheWayOfAQueuedCast()
    {
        var cave = ClickDetour.Build(Cave);

        Reads(cave, HuntAddresses.CastQueued).ShouldBe(1);
        Index(cave, [0xA0, .. BitConverter.GetBytes(HuntAddresses.CastQueued.Value)])
            .ShouldBeLessThan(Index(cave, Call(HuntAddresses.ReTargetFromHover)));
    }

    // Taken before the work rather than after. The pump can be reached again while the
    // engine this runs posts and drains further ticks, and a request still sitting there
    // would be a second click nobody asked for.
    [Fact]
    public void TakesTheRequestBeforeActingOnIt()
    {
        var cave = ClickDetour.Build(Cave);
        var layout = ClickDetour.LayoutFor(Cave);
        var clear = Index(
            cave, [0xC7, 0x05, .. BitConverter.GetBytes(layout.Request.Value), 0, 0, 0, 0]);

        clear.ShouldBeGreaterThan(0);
        clear.ShouldBeLessThan(Index(cave, Call(HuntAddresses.ReTargetFromHover)));
    }

    /// <summary>
    /// Clears the attack latch before the re-lock, which is what a real click does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReTarget_fromHover</c> refuses outright while <c>0xABF33C</c> holds anything —
    /// its first guard — and it is also what sets it: on the pass where it dispatches a
    /// blow it writes the target there and clears <c>WalkTargetValid</c> on the way out.
    /// Every call after that returns at the guard having done nothing except switch the
    /// walk off again, so the client can never go back to chasing.
    /// </para>
    /// <para>
    /// The client's own way out is the input handler at <c>0x4F7050</c>, reached when the
    /// player presses the button. Nothing here presses anything. Melee hid it — the monster
    /// dies, its record is removed, and removal clears the latch — but a bow fires from ten
    /// tiles at something that does not die this second, and then the character will not
    /// walk anywhere ever again.
    /// </para>
    /// </remarks>
    [Fact]
    public void ClearsTheAttackLatchTheWayAClickWould()
    {
        var cave = ClickDetour.Build(Cave);
        var clear = Index(
            cave,
            [0xC7, 0x05, .. BitConverter.GetBytes(HuntAddresses.AttackInFlight.Value), 0, 0, 0, 0]);

        clear.ShouldBeGreaterThan(0);

        // Before, not after: the guard it exists to get past is the first thing the re-lock
        // tests.
        clear.ShouldBeLessThan(Index(cave, Call(HuntAddresses.ReTargetFromHover)));
    }

    [Fact]
    public void SavesAndRestoresEveryRegisterAndTheFlags()
    {
        var cave = ClickDetour.Build(Cave);

        cave[0].ShouldBe((byte)0x60);                              // pushad
        cave[1].ShouldBe((byte)0x9C);                              // pushfd
        Index(cave, [0x9D, 0x61]).ShouldBeGreaterThan(0);          // popfd; popad
    }

    // The page is chosen by the allocator, so a relative call could not be right for
    // anything outside the cave. The only relative branch is the one back to the client.
    [Fact]
    public void CallsThroughARegisterRatherThanRelatively() =>
        Count(ClickDetour.Build(Cave), [0xE8]).ShouldBe(0);

    // Every refusal is a short jump forward, and so is the one that skips the re-lock for a
    // steer. One landing past the end of the code would run the request slots as though they
    // were instructions.
    [Fact]
    public void JumpsLandInsideTheCode()
    {
        var cave = ClickDetour.Build(Cave);
        var code = ClickDetour.LayoutFor(Cave).Code;
        var branches = 0;

        for (var i = 0; i + 1 < code; i++)
        {
            if (cave[i] is not (0x74 or 0x75))
            {
                continue;
            }

            var destination = i + 2 + (sbyte)cave[i + 1];

            destination.ShouldBeGreaterThan(i);
            destination.ShouldBeLessThan(code);
            branches++;
        }

        branches.ShouldBe(8);
    }

    private static byte[] Call(GameAddress target) =>
        [0xB8, .. BitConverter.GetBytes(target.Value), 0xFF, 0xD0];

    private static int Calls(byte[] code, GameAddress target) => Count(code, Call(target));

    private static int Reads(byte[] code, GameAddress address) =>
        Count(code, [0xA0, .. BitConverter.GetBytes(address.Value)]);

    private static int Index(byte[] code, byte[] pattern)
    {
        for (var i = 0; i + pattern.Length <= code.Length; i++)
        {
            if (code.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }

    private static int Count(byte[] code, byte[] pattern)
    {
        var found = 0;

        for (var i = 0; i + pattern.Length <= code.Length; i++)
        {
            if (code.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                found++;
            }
        }

        return found;
    }
}
