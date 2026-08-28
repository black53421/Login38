using Login38.Aux.Hunt;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers the code that replays the client's walk inside the client.
/// </summary>
/// <remarks>
/// There is no way to watch this go wrong except as the hunt choosing badly, which is what
/// it was written to stop, so what a test can do is hold the shape: the client's four
/// routines and no copies of them, the calling convention each one wants, unsigned
/// comparisons where the client uses unsigned, and a data area that starts empty.
/// </remarks>
public sealed class WalkProbeTests
{
    private static readonly GameAddress Cave = new(0x0900_0000);

    // The whole reason for the layout being measured rather than declared: the code
    // addresses its own fields absolutely, so every instruction is a fixed size and the
    // length cannot depend on where the allocator puts the page. If it ever did, the
    // measuring pass would disagree with the built one and the fields would move.
    [Fact]
    public void IsTheSameLengthWhereverItLands() =>
        WalkProbe.LayoutFor(new GameAddress(0x1000_0000)).Code
            .ShouldBe(WalkProbe.LayoutFor(Cave).Code);

    [Fact]
    public void PutsTheQuestionAndTheAnswerAfterTheCode()
    {
        var layout = WalkProbe.LayoutFor(Cave);

        layout.Count.ShouldBe(Cave + layout.Code);
        layout.Answers.ShouldBe(layout.Destinations + (WalkProbe.Capacity * WalkProbe.Entry));
        layout.Size.ShouldBe(
            (int)(layout.Answers.Value - Cave.Value) + (WalkProbe.Capacity * sizeof(int)));
        WalkProbe.Build(Cave).Length.ShouldBe(layout.Size);
    }

    [Fact]
    public void StartsWithNothingToAnswer() =>
        WalkProbe.Build(Cave)[WalkProbe.LayoutFor(Cave).Code..]
            .ShouldAllBe(value => value == 0);

    // The four the walk engine decides a step with. Anything missing here is a detail
    // reimplemented instead of asked about, which is what this exists to avoid.
    //
    // Counted twice over, because there are two passes: the climb, which walks the ground,
    // and the sight trace, which walks the line from where the climb stopped. Both are the
    // same four routines over the same grid, and that is the point - one rule, expressed
    // once, meaning the same thing for a sword and for a bow.
    [Fact]
    public void AsksTheClientRatherThanCopyingIt()
    {
        var cave = WalkProbe.Build(Cave);

        // Arrival for the climb, and "is the line on the target's tile" for the trace.
        Calls(cave, HuntAddresses.InRangeCheck).ShouldBe(2);

        // One heading test in each loop.
        Calls(cave, HuntAddresses.TileBlocked).ShouldBe(2);

        // The climb scores a candidate step; the trace scores one and measures where it
        // already stands, because every step of a line has to get strictly nearer.
        Calls(cave, HuntAddresses.GridDistance).ShouldBe(3);

        // Once to score a heading and once to take the winning one, in each pass. The engine
        // does the same: it does not remember which candidate step it liked, it recomputes
        // it. Four would be two per pass; three would be one of them keeping a step it
        // scored, which is a different route from the client's.
        Calls(cave, HuntAddresses.StepByHeading).ShouldBe(4);
    }

    // The licence for running this off the game's own thread is that nothing it calls reads
    // anything that thread writes, and nothing it is given can go stale into a fault. Both
    // were broken at once by asking the client whether a candidate was attackable: that
    // routine walks the player's live effect collection, and it wanted a record pointer from
    // a scan the client had already overtaken. It crashed the game where there was most
    // going on, which is where records are freed fastest.
    [Fact]
    public void CallsNothingThatWalksTheClientsLiveState()
    {
        var cave = WalkProbe.Build(Cave);

        Calls(cave, new GameAddress(0x005AF180)).ShouldBe(0);       // Attackable
        Calls(cave, new GameAddress(0x005AE780)).ShouldBe(0);       // and what it ends in
    }

    // Nothing in here dereferences anything the launcher passed in. The question is numbers.
    [Fact]
    public void NeverFollowsAPointerItWasHanded()
    {
        var code = WalkProbe.Build(Cave)[..WalkProbe.LayoutFor(Cave).Code];
        var cursor = BitConverter.GetBytes(WalkProbe.LayoutFor(Cave).Destinations.Value);

        // The one pointer it walks is its own question cursor, and only to read the two
        // numbers out of an entry it wrote itself.
        Count(code, [0x8B, 0x01]).ShouldBe(1);                      // mov eax, [ecx]
        Count(code, [0x8B, 0x41]).ShouldBe(1);                      // mov eax, [ecx+disp]
        Index(code, [0xC7, 0x05, .. cursor]).ShouldBe(-1);          // never written as data
    }

    // Only the passability test is cdecl, and only its arguments are the caller's to drop.
    // An add too many walks the stack pointer up through the return address; one too few
    // leaks twelve bytes per heading, which is eight per step and a fault before the run is
    // out. There is one of these per call and the client's other three clean up for
    // themselves.
    [Fact]
    public void CleansUpAfterEveryCdeclCallAndNoOthers()
    {
        var cave = WalkProbe.Build(Cave);

        Count(cave, [0x83, 0xC4]).ShouldBe(Calls(cave, HuntAddresses.TileBlocked));
        Count(cave, [0x83, 0xC4, 12]).ShouldBe(Calls(cave, HuntAddresses.TileBlocked));
    }

    // The client holds its best score in an unsigned and takes a new heading only on a
    // strict improvement. Signed comparisons would order 99999999 the same way but not the
    // 0xFFFFFFFF it starts from, and taking ties would pick a different heading from the
    // same square — either one is a different route and so a different answer.
    [Fact]
    public void ComparesTheWayTheClientCompares()
    {
        var cave = WalkProbe.Build(Cave);

        Count(cave, [0xC7, 0x05, .. BitConverter.GetBytes((Cave + cave.Length).Value)])
            .ShouldBe(0);                                       // nothing writes past the end
        Index(cave, BitConverter.GetBytes(99_999_999u)).ShouldBeGreaterThan(0);
        Index(cave, [0xB8, .. BitConverter.GetBytes(99_999_999u)]).ShouldBeGreaterThan(0);
        Count(cave, [0x73]).ShouldBeGreaterThan(0);              // jae, unsigned
        Count(cave, [0x0F, 0x82]).ShouldBeGreaterThan(0);        // jb, unsigned
        Count(cave, [0x7C]).ShouldBe(0);                         // jl, signed
        Count(cave, [0x7D]).ShouldBe(0);                         // jge, signed
    }

    // The client keeps its reach at 0xC2D2CA and its own walk engine stops on it. A climb
    // that arrived on a constant would answer a different question from the one the client
    // then acts out — by eight tiles, for a bow.
    [Fact]
    public void ClimbsToTheClientsOwnReachRatherThanAConstantOne() =>
        Index(WalkProbe.Build(Cave),
                [0xFF, 0x35, .. BitConverter.GetBytes(WalkProbe.LayoutFor(Cave).Reach.Value)])
            .ShouldBeGreaterThan(0);

    // The trace is the other way round and deliberately so: it ends on the target's own
    // tile, not at the weapon's reach. Ending at the reach would pass a line that stopped
    // before whatever is in the way, which is precisely the case it exists to catch — a bow
    // standing eight tiles off with a wall in the middle, and every shot dropped by the
    // server without a reply.
    [Fact]
    public void TracesAllTheWayToTheTargetRatherThanToTheWeaponsReach()
    {
        var cave = WalkProbe.Build(Cave);

        Count(cave, [0x6A, 0x01]).ShouldBe(1);                   // push 1, once, for the trace
        Index(cave, [0x6A, 0x01]).ShouldBeGreaterThan(
            Index(cave, [0xFF, 0x35, .. BitConverter.GetBytes(WalkProbe.LayoutFor(Cave).Reach.Value)]));
    }

    // Worse than any real score, so the first legal heading always wins the comparison and
    // the loop never has to special-case "nothing chosen yet".
    [Fact]
    public void StartsEveryStepFromTheWorstPossibleScore() =>
        Index(WalkProbe.Build(Cave), [0xC7, 0x05, .. BitConverter.GetBytes(Scratch(Cave, 44))])
            .ShouldBeGreaterThan(0);

    // The page is chosen by the allocator, so a relative call could not be right for
    // anything outside the cave, and there is nothing inside it worth calling.
    //
    // Said as "every call is one of these" rather than "no byte is 0xE8". The second is what
    // this used to say, and it was a coincidence rather than a check: 0xE8 turns up inside
    // any address that happens to contain it, so the test passed until a field moved and
    // then failed over a byte that was never an opcode.
    [Fact]
    public void CallsThroughARegisterRatherThanRelatively()
    {
        var cave = WalkProbe.Build(Cave);
        var routines = new[]
        {
            HuntAddresses.TileBlocked,
            HuntAddresses.StepByHeading,
            HuntAddresses.GridDistance,
            HuntAddresses.InRangeCheck,
        };

        Count(cave, [0xFF, 0xD0]).ShouldBe(routines.Sum(routine => Calls(cave, routine)));
    }

    // The polarity, and it is worth a test of its own because getting it wrong is silent and
    // expensive. 0x004F5910 answers "is this heading refused": non-zero is the wall. That is
    // not read off the routine, which is equally readable either way, but off its only
    // unarguable consumer — ComputeStepHeading at 0x5A4D60 scores a heading when the answer
    // is zero and hands it 99999999 otherwise.
    //
    // Inverted, it scores every wall as a route and every open heading as stone. The search
    // built on it then paths straight through walls, which reads from outside as a hunt that
    // will not stop locking onto monsters behind them — and cost several builds diagnosed as
    // everything except this.
    [Fact]
    public void TreatsTheClientsAnswerAsARefusalRatherThanPermission()
    {
        var cave = WalkProbe.Build(Cave);
        var call = Call(HuntAddresses.TileBlocked);
        var at = Index(cave, [.. call, 0x83, 0xC4, 12, 0x84, 0xC0]);

        at.ShouldBeGreaterThan(0);

        // jnz, so a non-zero answer is the one that goes to the impassable score.
        cave[at + call.Length + 5].ShouldBe((byte)0x75);
    }

    // CreateRemoteThread calls it as a stdcall taking one argument.
    [Fact]
    public void ReturnsLikeAThreadProcedure()
    {
        var code = WalkProbe.Build(Cave)[..WalkProbe.LayoutFor(Cave).Code];

        code[^3..].ShouldBe([0xC2, 0x04, 0x00]);
        code[^5..^3].ShouldBe([0x33, 0xC0]);                     // xor eax, eax
    }

    /// <summary>Where a scratch field sits, counted from the start of the fields.</summary>
    private static uint Scratch(GameAddress cave, int offset) =>
        (cave + WalkProbe.LayoutFor(cave).Code + offset).Value;

    private static byte[] Call(GameAddress target) =>
        [0xB8, .. BitConverter.GetBytes(target.Value), 0xFF, 0xD0];

    private static int Calls(byte[] code, GameAddress target) => Count(code, Call(target));

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
