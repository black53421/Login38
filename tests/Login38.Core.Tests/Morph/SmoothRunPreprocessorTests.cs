using Login38.Core.Morph.SmoothRun;
using Shouldly;

namespace Login38.Core.Tests.Morph;

/// <summary>
/// Covers the run-cycle pass end to end: what it recognises, what it writes, and what it
/// leaves alone.
/// </summary>
/// <remarks>
/// The cases come from real tables. Operators publish morph tables built for several
/// different clients and the run cycles arrive in whatever shape that client used, so the
/// recognition rules are the substance of this code — each one is a different table's idea
/// of how to store the same two animations.
/// </remarks>
public sealed class SmoothRunPreprocessorTests
{
    private const string FileHeader = "300 0 41210\n";

    private static string Process(string text) => SmoothRunPreprocessor.Process(text).Text;

    private static SmoothRunReport Report(string text) => SmoothRunPreprocessor.Process(text).Report;

    /// <summary>Eight frames from one row, which is what a walk or a run half looks like.</summary>
    private static string Cycle(uint row) =>
        $"1 8,{row}.0:2 {row}.1:2 {row}.2:2 {row}.3:2 {row}.4:2 {row}.5:2 {row}.6:2 {row}.7:2";

    // ---- what counts as a run cycle -------------------------------------------------

    // The most explicit form: the table says outright which action is which half.
    [Fact]
    public void ReadsDashVariants()
    {
        var output = Process(
            $"{FileHeader}#7 32=7 ranger\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"\t0-1.RunL({Cycle(16)})\n" +
            $"\t0-2.RunR({Cycle(24)})\n");

        output.ShouldContain($"98.walk({Cycle(16)})");
        output.ShouldContain($"99.walk({Cycle(24)})");
    }

    // The client's parser does not understand the dash syntax, so those lines cannot
    // survive into a table it is about to read.
    [Fact]
    public void RemovesTheDashVariantLines()
    {
        var output = Process(
            $"{FileHeader}#7 32=7 ranger\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"\t0-1.RunL({Cycle(16)})\n");

        output.ShouldNotContain("0-1.RunL");
    }

    // A dash number this code does not recognise is somebody else's convention. Deleting it
    // would change a table in a way the operator did not ask for.
    [Fact]
    public void LeavesUnrecognisedDashVariantsAlone() =>
        Process(
            $"{FileHeader}#7 32=7 ranger\n" +
            $"\t0.walk({Cycle(0)})\n" +
            "\t0-3.spell no direction(1 4,40.0:2 40.1:2 40.2:2 40.3:2)\n")
        .ShouldContain("0-3.spell no direction");

    [Fact]
    public void ReadsActionsNamedForRunning()
    {
        var output = Process(
            $"{FileHeader}#42 32=42 keina\n" +
            $"\t0.runL({Cycle(16)})\n" +
            $"\t4.runR({Cycle(24)})\n");

        output.ShouldContain($"98.walk({Cycle(16)})");
        output.ShouldContain($"99.walk({Cycle(24)})");
    }

    // Tables in the wild put run cycles in slots as high as 137. The slot says nothing
    // about what the frames are.
    [Fact]
    public void ReadsRunNamesInAnySlot() =>
        Process(
            $"{FileHeader}#18853 32=18853 shield axe\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"\t119.RunL shield axe({Cycle(16)})\n")
        .ShouldContain($"98.walk({Cycle(16)})");

    // Some tables have had every action name stripped. Actions 32 and 33 drawing from rows
    // exactly eight apart is a run cycle whatever it is called.
    [Fact]
    public void ReadsActions32And33ByTheirLayout()
    {
        var output = Process(
            $"{FileHeader}#99 32=99 stripped\n" +
            $"\t32.({Cycle(100)})\n" +
            $"\t33.({Cycle(108)})\n");

        output.ShouldContain($"98.walk({Cycle(100)})");
        output.ShouldContain($"99.walk({Cycle(108)})");
    }

    // Which half is which comes from the rows, not from the action numbers: both orders
    // occur in real tables.
    [Fact]
    public void TakesTheLowerRowAsTheLeftHalf()
    {
        var output = Process(
            $"{FileHeader}#99 32=99 stripped\n" +
            $"\t32.({Cycle(108)})\n" +
            $"\t33.({Cycle(100)})\n");

        output.ShouldContain($"98.walk({Cycle(100)})");
        output.ShouldContain($"99.walk({Cycle(108)})");
    }

    // Rows that are not eight apart are two separate animations, not one cycle.
    [Fact]
    public void IgnoresActions32And33AtTheWrongDistance() =>
        Process(
            $"{FileHeader}#99 32=99 unrelated\n" +
            $"\t32.({Cycle(100)})\n" +
            $"\t33.({Cycle(120)})\n")
        .ShouldNotContain("98.walk");

    // ---- matching a walk to a run ---------------------------------------------------

    // The usual arrangement: one entry walks, a second entry with the same graphic id holds
    // the frames it cannot.
    [Fact]
    public void MatchesSpritesThatShareAGraphic()
    {
        var output = Process(
            $"{FileHeader}#16140 72=5373 keina walk\n" +
            $"\t0.walk({Cycle(8)})\n" +
            $"\t4.walk onehandsword({Cycle(8)})\n" +
            $"#16141 72=5373 keina run\n" +
            $"\t0.RunL({Cycle(16)})\n" +
            $"\t4.RunR({Cycle(24)})\n");

        var walkEntry = output[..output.IndexOf("#16141", StringComparison.Ordinal)];

        walkEntry.ShouldContain($"98.walk({Cycle(16)})");
        walkEntry.ShouldContain($"99.walk({Cycle(24)})");
    }

    // A run-only entry whose graphic id is another sprite's id is not sharing a graphic —
    // it is naming its target.
    [Fact]
    public void MatchesARunSpriteThatNamesItsTarget()
    {
        var output = Process(
            $"{FileHeader}#4910 32=4910 knight\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"#10641 56=4910 knight run\n" +
            $"\t0.RunL({Cycle(16)})\n" +
            $"\t4.RunR({Cycle(24)})\n");

        var target = output[..output.IndexOf("#10641", StringComparison.Ordinal)];

        target.ShouldContain($"98.walk({Cycle(16)})");
    }

    // A sprite with both keeps its own; there is nothing to look for elsewhere.
    [Fact]
    public void LeavesASpriteThatCarriesBothAlone()
    {
        var text =
            $"{FileHeader}#5 32=5 wolf\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"\t11.runL({Cycle(16)})\n";

        Process(text).ShouldContain($"98.walk({Cycle(16)})");
    }

    // Nothing to borrow from and nothing of its own. The table has to come back unchanged.
    [Fact]
    public void PassesThroughATableWithNoRunCycles()
    {
        var text =
            $"{FileHeader}#1 32=1 wolf\n" +
            $"\t0.walk({Cycle(0)})\n" +
            "\t4.attack(1 4,40.0:2 40.1:2 40.2:2 40.3:2)\n";

        Process(text).ShouldBe(text);
        Report(text).Converted.ShouldBe(0);
    }

    // ---- timing ---------------------------------------------------------------------

    // A run cycle played at the walk's frame timing is the shuffle this whole pass exists
    // to remove, so the source's timing travels with its frames.
    [Fact]
    public void CarriesTheTimingWithTheFrames() =>
        Process(
            $"{FileHeader}#100 32=777 wolf walk\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"#200 40=777 wolf run\n" +
            "\t110.framerate(1 8,1 1 1 1 1 1 1 1)\n" +
            $"\t0.runL({Cycle(8)})\n" +
            $"\t4.runR({Cycle(16)})\n")
        .ShouldContain("110.framerate(1 8,1 1 1 1 1 1 1 1)\n\t98.walk");

    // Timing on its own would speed up the walk that is already there, which is a change
    // nobody asked for.
    [Fact]
    public void DoesNotEmitTimingWithoutFrames()
    {
        var text =
            $"{FileHeader}#1 32=1 wolf\n" +
            "\t110.framerate(1 8,1 1 1 1 1 1 1 1)\n" +
            $"\t0.walk({Cycle(0)})\n";

        Process(text).ShouldBe(text);
    }

    // ---- the file the client has to be able to read ---------------------------------

    // The client's action table stops at 120. A table built for a later client carries
    // higher numbers, and they are dropped rather than written past the end of it.
    [Fact]
    public void DropsActionsTheClientCannotAddress()
    {
        var output = Process(
            $"{FileHeader}#1 32=1 wolf\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"\t121.something({Cycle(40)})\n" +
            $"\t130.something else({Cycle(48)})\n");

        output.ShouldNotContain("121.something");
        output.ShouldNotContain("130.something");
        output.ShouldContain("0.walk");
    }

    // A borrowed run cycle draws from rows the target's own header does not cover, and the
    // client stops reading at the count in that header.
    [Fact]
    public void WidensATargetHeaderToCoverTheBorrowedFrames() =>
        Process(
            $"{FileHeader}#100 32=777 wolf walk\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"#200 40=777 wolf run\n" +
            $"\t0.runL({Cycle(8)})\n" +
            $"\t4.runR({Cycle(16)})\n")
        .ShouldContain("#100 40=777 wolf walk");

    // The count is the number after the id. Searching the line for its digits finds them
    // inside the id first whenever the id contains them, which rewrote the id instead.
    [Fact]
    public void WidensTheImageCountAndNotTheSpriteId()
    {
        var output = Process(
            $"{FileHeader}#3201 320=777 wolf walk\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"#200 360=777 wolf run\n" +
            $"\t0.runL({Cycle(8)})\n" +
            $"\t4.runR({Cycle(16)})\n");

        output.ShouldContain("#3201 360=777 wolf walk");
        output.ShouldNotContain("#3601");
    }

    // Some tables put a whole entry on its header line. Widening that line and appending to
    // it are the same line, and returning early after widening left those sprites with a
    // header claiming frames that were never added.
    [Fact]
    public void AppendsToASpriteWrittenEntirelyOnItsHeader()
    {
        var output = Process(
            $"{FileHeader}#1353 24=777 great dane 0.run_one({Cycle(0)}) 4.run_two({Cycle(8)})\n" +
            $"#200 48=777 great dane run\n" +
            $"\t0.runL({Cycle(16)})\n" +
            $"\t4.runR({Cycle(24)})\n");

        output.ShouldContain("#1353 48=777 great dane");
        output.ShouldContain($"98.walk({Cycle(16)})");
    }

    // ---- the parts of the file this code has no business touching -------------------

    [Fact]
    public void PreservesCommentsAndUnknownDirectives()
    {
        var text =
            $"{FileHeader}# a note the operator left\n" +
            $"#1 32=1 wolf\n" +
            $"\t0.walk({Cycle(0)})\n" +
            "\t200 something entirely different\n";

        Process(text).ShouldBe(text);
    }

    [Theory]
    [InlineData("300 0 41210\n#1 32=1 a\n\t0.walk(1 4,0.0:2 0.1:2 0.2:2 0.3:2)\n")]
    [InlineData("300 0 41210\n#1 32=1 a\n\t0.walk(1 4,0.0:2 0.1:2 0.2:2 0.3:2)")]
    public void PreservesWhetherTheFileEndedWithANewline(string text) =>
        Process(text).ShouldBe(text);

    [Fact]
    public void HandlesAnEmptyTable() => Process(string.Empty).ShouldBe(string.Empty);

    [Fact]
    public void HandlesATableWithNoSprites() => Process(FileHeader).ShouldBe(FileHeader);

    // Running the pass over its own output must not append a second copy of the slots.
    [Fact]
    public void IsIdempotent()
    {
        var text =
            $"{FileHeader}#7 32=7 ranger\n" +
            $"\t0.walk({Cycle(0)})\n" +
            $"\t0-1.RunL({Cycle(16)})\n" +
            $"\t0-2.RunR({Cycle(24)})\n";

        var once = Process(text);

        Process(once).ShouldBe(once);
    }

    // ---- the report -----------------------------------------------------------------

    [Fact]
    public void ReportsWhatItDid()
    {
        var report = Report(
            $"{FileHeader}#16140 72=5373 keina walk\n" +
            $"\t0.walk({Cycle(8)})\n" +
            $"#16141 72=5373 keina run\n" +
            $"\t0.RunL({Cycle(16)})\n" +
            $"\t4.RunR({Cycle(24)})\n");

        report.Sprites.ShouldBe(2);
        report.Walking.ShouldBe(1);
        report.Running.ShouldBe(1);
        report.Converted.ShouldBe(2);
    }
}
