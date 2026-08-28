using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// Asks the client whether its own walk would arrive, for a list of destinations at once.
/// </summary>
/// <remarks>
/// <para>
/// The client has no reachability query, because it never needs one: a player clicks a
/// monster and the character either gets there or jiggles against a wall. Its walk engine
/// hill climbs — <c>ComputeStepHeading</c> at <c>0x5A4D60</c> scores all eight headings by
/// how near they leave it and takes the best one, with no memory and no lookahead. Nothing
/// in it can tell a wall from a detour.
/// </para>
/// <para>
/// So "would the client reach that monster" can only be answered by replaying the climb.
/// This replays it with the client's own four routines rather than a copy of them, which is
/// what makes the answer the client's answer: same corner rule, same distance, same order
/// through the headings, same stopping test. A copy would have to be kept in step with a
/// binary nobody here controls, and the step table it needs is not even in the file.
/// </para>
/// <para>
/// Oscillation needs no detecting. A climb that jiggles never arrives, so it runs out of
/// steps and reports nothing, which is the same answer by a cheaper route.
/// </para>
/// <para>
/// Two questions, not one, because "the walk arrives" and "the blow lands" are the same
/// question only for melee. The client stops on the <em>first</em> square inside the
/// weapon's reach — see <see cref="WeaponReach"/> — so a sword ends up next to what it is
/// hitting and a bow ends up eight tiles away, which may be eight tiles with a wall in the
/// middle. That is the whole of what "handing the movement back to the client" quietly stops
/// buying for a bow: the client will not walk a step it does not think it needs, so the
/// ground between never gets crossed and never gets tested.
/// </para>
/// <para>
/// So the climb is followed by a trace from the square it stopped on to the target, over the
/// same grid and through the same routines, and a candidate whose line is blocked is
/// reported as one the walk did not reach. Every step of the trace has to be passable
/// <em>and</em> get strictly nearer: rounding a wall needs one step that does not, and an
/// arrow has no way to take one, so the refusal is the line of sight test rather than an
/// approximation of it.
/// </para>
/// <para>
/// One sentence then covers both weapons — can the walk reach the square this will attack
/// from, and is the line from that square clear — and melee passes the second half for
/// nothing, because its square is the next one over.
/// </para>
/// <para>
/// An earlier attempt at this refused every monster on screen and was removed as too strict.
/// The reason is still not settled and is worth naming rather than explaining away: a check
/// and the machinery it sits on can share a bug, and then the evidence for deleting the check
/// is manufactured by the bug. If this refuses everything again, suspect the shared half
/// first.
/// </para>
/// <para>
/// Coordinates go in and step counts come out. Nothing else: no record pointers, and nothing
/// that reads what the game's own thread writes. That is the whole licence for running this
/// on a thread of the launcher's, and it was learned by breaking it. Asking the client's own
/// <c>Attackable</c> about each candidate crashed the game wherever there was most going on,
/// twice over — the routine walks the player's live effect collection, and the pointer handed
/// to it came from a scan the client had already overtaken, in a place where records are
/// freed by the second. Both are now decided outside the client, from a record the scan
/// already read.
/// </para>
/// <para>
/// The rule that came out of it: a routine is safe to call from here only if everything it
/// touches is either an argument or something nothing else writes, and that has to hold all
/// the way down its call tree rather than at the top of it. A pointer that was valid when it
/// was found is not an argument in that sense.
/// </para>
/// <para>
/// One run answers a whole screen of candidates. The alternative — a call per monster — is
/// a thread per monster, and the thread is most of the cost.
/// </para>
/// </remarks>
internal static class WalkProbe
{
    /// <summary>How long one candidate is in the question.</summary>
    internal const int Entry = 8;

    /// <summary>How many destinations one run can be asked about.</summary>
    /// <remarks>
    /// A generous screen's worth. The cost of the cave is this times twelve bytes, and the
    /// cost of a run is this times the step budget times eight, so both want a bound.
    /// </remarks>
    internal const int Capacity = 32;

    /// <summary>What the client's walk engine tries every step.</summary>
    internal const int Headings = 8;

    /// <summary>
    /// How far the sight trace will follow a line before giving up on it.
    /// </summary>
    /// <remarks>
    /// The trace starts on a square already inside the weapon's reach, so what it has left to
    /// cross is at most that reach - fourteen tiles for the longest bow, twice that in
    /// columns. This is comfortably past any of them, and it is a backstop rather than a
    /// limit: every step strictly reduces an integer distance, so the trace ends on its own.
    /// </remarks>
    private const int SightSteps = 48;

    /// <summary>What a blocked heading scores, so that it loses to every legal one.</summary>
    /// <remarks>
    /// The client's own number. It matters that it is a number and not a skip: when every
    /// heading is blocked they all score this, the first one wins on the strict comparison,
    /// and the character walks into the wall rather than standing still. Skipping them
    /// instead would model a client that stops, and that client is not this one.
    /// </remarks>
    private const uint Impassable = 99_999_999;

    /// <summary>Where each field sits, relative to the cave.</summary>
    /// <param name="Cave">Where the allocation starts.</param>
    /// <param name="Code">How long the code is, which is where the fields go.</param>
    internal readonly record struct Layout(GameAddress Cave, int Code)
    {
        /// <summary>How many destinations are being asked about.</summary>
        public GameAddress Count => Cave + Code;

        /// <summary>How many steps to follow a climb before calling it lost.</summary>
        public GameAddress MaxSteps => Count + 4;

        /// <summary>
        /// How close counts as arrived, in tiles.
        /// </summary>
        /// <remarks>
        /// A field rather than the constant one it used to be. The client's walk engine
        /// stops on its own reach — see <see cref="WeaponReach"/> — so a probe that always
        /// asked about one was answering a different question from the one the client goes
        /// on to act out: for a bow, thirteen tiles' worth of a different question.
        /// </remarks>
        public GameAddress Reach => MaxSteps + 4;

        /// <summary>Where the character is, as the client stores it.</summary>
        public GameAddress Start => Reach + 4;

        /// <summary>Where the replay currently stands.</summary>
        private GameAddress Position => Start + 8;

        /// <summary>Where a candidate step lands, before it is taken.</summary>
        private GameAddress Step => Position + 8;

        /// <summary>The destination being worked on.</summary>
        private GameAddress Destination => Step + 8;

        /// <summary>The best score this step, and which heading gave it.</summary>
        private GameAddress Best => Destination + 8;

        /// <inheritdoc cref="Best"/>
        private GameAddress BestHeading => Best + 4;

        /// <summary>The heading being scored.</summary>
        private GameAddress Heading => BestHeading + 4;

        /// <summary>How many steps the replay has taken.</summary>
        private GameAddress Taken => Heading + 4;

        /// <summary>Which destination is being worked on.</summary>
        private GameAddress Index => Taken + 4;

        /// <summary>Where the next destination and the next answer go.</summary>
        private GameAddress DestinationCursor => Index + 4;

        /// <inheritdoc cref="DestinationCursor"/>
        private GameAddress AnswerCursor => DestinationCursor + 4;

        /// <summary>How many steps the sight trace has taken.</summary>
        /// <remarks>
        /// Its own counter and not the climb's. The climb's is the answer about to be
        /// reported, and spending it here would report the length of the line instead of the
        /// length of the walk.
        /// </remarks>
        private GameAddress SightTaken => AnswerCursor + 4;

        /// <summary>The candidates, as pairs of client coordinates.</summary>
        /// <remarks>
        /// Coordinates and not records. Whether the thing standing there is worth attacking
        /// is decided before the question is asked, out where a pointer going stale is a
        /// wrong answer rather than a crash.
        /// </remarks>
        public GameAddress Destinations => SightTaken + 4;

        /// <summary>How many steps each one took, or -1 for one not worth walking to.</summary>
        public GameAddress Answers => Destinations + (Capacity * Entry);

        /// <summary>How long the whole allocation has to be.</summary>
        public int Size => (int)(Answers.Value - Cave.Value) + (Capacity * sizeof(uint));

        /// <summary>The scratch fields, which the code addresses absolutely.</summary>
        internal (GameAddress Position, GameAddress Step, GameAddress Destination,
            GameAddress Best, GameAddress BestHeading, GameAddress Heading,
            GameAddress Taken, GameAddress Index,
            GameAddress DestinationCursor, GameAddress AnswerCursor,
            GameAddress SightTaken) Scratch =>
            (Position, Step, Destination, Best, BestHeading, Heading, Taken, Index,
                DestinationCursor, AnswerCursor, SightTaken);
    }

    /// <summary>Where everything sits for a cave at <paramref name="cave"/>.</summary>
    internal static Layout LayoutFor(GameAddress cave) =>
        new(cave, Emit(new Layout(cave, 0)).Length);

    /// <summary>The whole allocation: the code, then room for the question and the answer.</summary>
    internal static byte[] Build(GameAddress cave)
    {
        var layout = LayoutFor(cave);

        return [.. Emit(layout), .. new byte[layout.Size - layout.Code]];
    }

    /// <summary>
    /// The replay itself.
    /// </summary>
    /// <remarks>
    /// Everything lives in the cave rather than on the stack, which costs nothing here — a
    /// run is one thread at a time — and buys absolute addressing throughout, so the code is
    /// the same length whatever the fields turn out to be. That is what lets the layout be
    /// measured by emitting once against a cave of zero.
    /// </remarks>
    private static byte[] Emit(Layout layout)
    {
        var s = layout.Scratch;
        var code = new ShellcodeBuilder(layout.Cave);

        code.MovDwordPtr(s.DestinationCursor, layout.Destinations.Value)
            .MovDwordPtr(s.AnswerCursor, layout.Answers.Value)
            .MovDwordPtr(s.Index, 0);

        var candidate = code.Here();

        code.MovEaxFrom(s.Index).CmpEaxPtr(layout.Count);

        var finished = code.NearJump(0x83);                          // jae

        // The candidate this time round, and back to the start for it. Every one is walked
        // from where the character actually is; the climb is not shared between them.
        code.MovEcxFrom(s.DestinationCursor)
            .MovEaxFromEcx().MovEaxTo(s.Destination)
            .MovEaxFromEcx(4).MovEaxTo(s.Destination + 4)
            .MovEaxFrom(layout.Start).MovEaxTo(s.Position)
            .MovEaxFrom(layout.Start + 4).MovEaxTo(s.Position + 4)
            .MovDwordPtr(s.Taken, 0);


        var step = code.Here();

        // Close enough to swing is where the walk stops, and the range is the client's
        // own — one for a fist, the weapon's for a bow. Asked before the budget, so a
        // monster already in reach costs no steps at all.
        code.PushPtr(layout.Reach)
            .PushPtr(s.Destination + 4)
            .PushPtr(s.Destination)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.InRangeCheck.Value).CallEax()
            .TestAlAl();

        var arrived = code.NearJump(0x85);                           // jnz

        code.MovEaxFrom(s.Taken).CmpEaxPtr(layout.MaxSteps);

        var lost = code.NearJump(0x83);                              // jae

        code.MovDwordPtr(s.Best, uint.MaxValue)
            .MovDwordPtr(s.BestHeading, 0)
            .MovDwordPtr(s.Heading, 0);

        var heading = code.Here();

        code.PushPtr(s.Heading)
            .PushPtr(s.Position + 4)
            .PushPtr(s.Position)
            .MovEax(HuntAddresses.TileBlocked.Value).CallEax()
            .AddEsp(12)                                              // cdecl, so this cleans
            .TestAlAl();

        // Non-zero is the refusal, which is the client's own reading of its own routine:
        // ComputeStepHeading scores a heading only when this returns zero and gives it the
        // impassable score otherwise. Reading it the other way round scores every wall as a
        // route and every open heading as stone, and a search built on it walks through walls.
        var blocked = code.ShortJumpIfNotZero();

        code.PushPtr(s.Heading)
            .PushImm32(s.Step)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.StepByHeading.Value).CallEax()
            .PushImm32(s.Destination)
            .MovEcx(s.Step.Value)
            .MovEax(HuntAddresses.GridDistance.Value).CallEax();

        var score = code.ShortJumpAlways();

        code.MarkLabel(blocked).MovEax(Impassable);
        code.MarkLabel(score);

        // Unsigned, and strictly better. Both are the client's: it holds the best in an
        // unsigned and takes a new heading only on a strict improvement, so a tie goes to
        // the lower heading. Comparing signed, or taking ties, walks a different route from
        // the same square — and one different step is a different answer.
        code.CmpEaxPtr(s.Best);

        var keep = code.ShortJump(0x73);                             // jae

        code.MovEaxTo(s.Best)
            .MovEaxFrom(s.Heading).MovEaxTo(s.BestHeading);

        code.MarkLabel(keep);
        code.MovEaxFrom(s.Heading).AddEax(1).MovEaxTo(s.Heading)
            .CmpEax(Headings)
            .NearJumpBack(0x82, heading);                            // jb

        code.PushPtr(s.BestHeading)
            .PushImm32(s.Step)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.StepByHeading.Value).CallEax();

        // A step that lands where it started is a climb that has nowhere better to be. The
        // client would stand there for as long as the destination held, so this is lost
        // rather than merely slow.
        code.MovEaxFrom(s.Step).CmpEaxPtr(s.Position);

        var moved = code.ShortJumpIfNotEqual();

        code.MovEaxFrom(s.Step + 4).CmpEaxPtr(s.Position + 4);

        var stuck = code.NearJump(0x84);                             // jz

        code.MarkLabel(moved);
        code.MovEaxFrom(s.Step).MovEaxTo(s.Position)
            .MovEaxFrom(s.Step + 4).MovEaxTo(s.Position + 4)
            .MovEaxFrom(s.Taken).AddEax(1).MovEaxTo(s.Taken)
            .NearJumpBackAlways(step);

        // Arrived, which for a bow means "stopped eight tiles off" and says nothing yet about
        // whether the shot crosses what is in between. So the line is walked from the square
        // the climb stopped on, and a candidate whose line is blocked is reported as one the
        // walk did not reach - for the purpose of choosing a target it is not one, and this
        // has exactly one channel to say so through.
        code.MarkLabel(arrived);
        code.MovDwordPtr(s.SightTaken, 0);

        var sight = code.Here();

        // Clear once the line stands on the target's own tile. One rather than the weapon's
        // reach: what is being tested is the ground between the two, and stopping short of it
        // would pass a line that never had to cross the wall.
        code.PushImm8(1)
            .PushPtr(s.Destination + 4)
            .PushPtr(s.Destination)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.InRangeCheck.Value).CallEax()
            .TestAlAl();

        var clear = code.NearJump(0x85);                             // jnz

        code.MovEaxFrom(s.SightTaken).CmpEax(SightSteps);

        var walled = code.NearJump(0x83);                            // jae

        // The distance to beat is the one from where the line stands, so every step has to
        // get strictly nearer. That is what makes this a line rather than a second walk:
        // rounding a corner needs one step that does not get nearer, and an arrow cannot take
        // one.
        code.PushImm32(s.Destination)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.GridDistance.Value).CallEax()
            .MovEaxTo(s.Best)
            .MovDwordPtr(s.BestHeading, Headings)                    // nothing chosen yet
            .MovDwordPtr(s.Heading, 0);

        var look = code.Here();

        code.PushPtr(s.Heading)
            .PushPtr(s.Position + 4)
            .PushPtr(s.Position)
            .MovEax(HuntAddresses.TileBlocked.Value).CallEax()
            .AddEsp(12)                                              // cdecl, so this cleans
            .TestAlAl();

        var refused = code.ShortJumpIfNotZero();

        code.PushPtr(s.Heading)
            .PushImm32(s.Step)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.StepByHeading.Value).CallEax()
            .PushImm32(s.Destination)
            .MovEcx(s.Step.Value)
            .MovEax(HuntAddresses.GridDistance.Value).CallEax()
            .CmpEaxPtr(s.Best);

        var noNearer = code.ShortJump(0x73);                         // jae

        code.MovEaxTo(s.Best)
            .MovEaxFrom(s.Heading).MovEaxTo(s.BestHeading);

        code.MarkLabel(refused).MarkLabel(noNearer);
        code.MovEaxFrom(s.Heading).AddEax(1).MovEaxTo(s.Heading)
            .CmpEax(Headings)
            .NearJumpBack(0x82, look);                               // jb

        // Nothing passable got nearer, which is a wall across the line.
        code.MovEaxFrom(s.BestHeading).CmpEax(Headings);

        var acrossTheLine = code.NearJump(0x83);                     // jae

        code.PushPtr(s.BestHeading)
            .PushImm32(s.Step)
            .MovEcx(s.Position.Value)
            .MovEax(HuntAddresses.StepByHeading.Value).CallEax()
            .MovEaxFrom(s.Step).MovEaxTo(s.Position)
            .MovEaxFrom(s.Step + 4).MovEaxTo(s.Position + 4)
            .MovEaxFrom(s.SightTaken).AddEax(1).MovEaxTo(s.SightTaken)
            .NearJumpBackAlways(sight);

        code.MarkLabel(clear);
        code.MovEaxFrom(s.Taken);

        var answer = code.ShortJumpAlways();

        code.MarkLabel(lost).MarkLabel(stuck).MarkLabel(walled).MarkLabel(acrossTheLine)
            .MovEax(unchecked((uint)-1));
        code.MarkLabel(answer);

        code.MovEcxFrom(s.AnswerCursor).MovEcxPtrFromEax()
            .MovEaxFrom(s.AnswerCursor).AddEax(4).MovEaxTo(s.AnswerCursor)
            .MovEaxFrom(s.DestinationCursor).AddEax(Entry).MovEaxTo(s.DestinationCursor)
            .MovEaxFrom(s.Index).AddEax(1).MovEaxTo(s.Index)
            .NearJumpBackAlways(candidate);

        code.MarkLabel(finished);

        // Nothing useful in the exit code: the answers are in the cave, and a thread that
        // returned at all is the only thing worth learning from it.
        code.XorEaxEax().RetAndPop(4);

        return code.Build();
    }
}
