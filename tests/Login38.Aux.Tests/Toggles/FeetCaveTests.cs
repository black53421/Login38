using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the code that moves the damage numbers under the target.
/// </summary>
/// <remarks>
/// Pinned byte for byte against an independent transcription of the reference's builder.
/// This runs inside the client's own drawing loop, several times a frame — a wrong
/// register field or a displacement one short does not fail, it takes the game down while
/// somebody is playing it.
/// </remarks>
public sealed class FeetCaveTests
{
    /// <summary>Somewhere for the cave to be, which nothing here depends on.</summary>
    private static readonly GameAddress Cave = new(0x0300_0000);

    [Fact]
    public void MovesTheBubbleForEverybodyElse()
    {
        byte[] expected =
        [
            0x89, 0x81, 0x80, 0x03, 0x00, 0x00,   // mov [ecx+0x380], eax    the displaced write
            0x66, 0x81, 0x7D, 0x10, 0x00, 0xF8,   // cmp word [ebp+0x10], 0xF800
            0x75, 0x2A,                           // jne done
            0x8B, 0x91, 0x98, 0x03, 0x00, 0x00,   // mov edx, [ecx+0x398]    the sprite
            0x85, 0xD2,                           // test edx, edx
            0x74, 0x20,                           // je done
            0x0F, 0xBE, 0x42, 0x1D,               // movsx eax, byte [edx+0x1D]   which frame
            0x6B, 0xC0, 0x18,                     // imul eax, eax, 0x18
            0x8B, 0x92, 0x8C, 0x00, 0x00, 0x00,   // mov edx, [edx+0x8C]     the frame table
            0x85, 0xD2,                           // test edx, edx
            0x74, 0x0F,                           // je done
            0x8B, 0x54, 0x02, 0x14,               // mov edx, [edx+eax+0x14] its height
            0xF7, 0xDA,                           // neg edx
            0x83, 0xC2, 0x20,                     // add edx, 0x20
            0x01, 0x91, 0x80, 0x03, 0x00, 0x00,   // add [ecx+0x380], edx
        ];

        Segment(FeetCave.Build(Cave), e => e.Remote, expected.Length).ShouldBe(expected);
    }

    // The same thing again with the registers the other way round, which is how the client
    // has it: one copy for the player and one for everybody else.
    [Fact]
    public void MovesItForThePlayerToo()
    {
        byte[] expected =
        [
            0x89, 0x8A, 0x80, 0x03, 0x00, 0x00,   // mov [edx+0x380], ecx
            0x66, 0x81, 0x7D, 0x10, 0x00, 0xF8,   // cmp word [ebp+0x10], 0xF800
            0x75, 0x2A,
            0x8B, 0x8A, 0x98, 0x03, 0x00, 0x00,   // mov ecx, [edx+0x398]
            0x85, 0xC9,
            0x74, 0x20,
            0x0F, 0xBE, 0x41, 0x1D,               // movsx eax, byte [ecx+0x1D]
            0x6B, 0xC0, 0x18,
            0x8B, 0x89, 0x8C, 0x00, 0x00, 0x00,   // mov ecx, [ecx+0x8C]
            0x85, 0xC9,
            0x74, 0x0F,
            0x8B, 0x4C, 0x01, 0x14,               // mov ecx, [ecx+eax+0x14]
            0xF7, 0xD9,                           // neg ecx
            0x83, 0xC1, 0x20,
            0x01, 0x8A, 0x80, 0x03, 0x00, 0x00,   // add [edx+0x380], ecx
        ];

        Segment(FeetCave.Build(Cave), e => e.Local, expected.Length).ShouldBe(expected);
    }

    [Fact]
    public void TurnsTheTailUpwardsForARedBubble()
    {
        byte[] expected =
        [
            0x8B, 0x55, 0xE0,                                 // mov edx, [ebp-0x20]
            0x66, 0x81, 0xBA, 0x9C, 0x03, 0x00, 0x00, 0x00, 0xF8,   // cmp word [edx+0x39C], red
            0x75, 0x07,                                       // jne past
            0xC6, 0x82, 0x6A, 0x03, 0x00, 0x00, 0x04,         // mov byte [edx+0x36A], 4
            0x0F, 0xB6, 0xC0,                                 // movzx eax, al     the displaced pair
            0x85, 0xC0,                                       // test eax, eax
        ];

        Segment(FeetCave.Build(Cave), e => e.Tail, expected.Length).ShouldBe(expected);
    }

    // The client resets the tail every frame, which would undo the flip a frame after it
    // was made. Red bubbles are passed over; everything else is reset exactly as before.
    [Fact]
    public void LeavesARedBubblesTailAlone()
    {
        byte[] expected =
        [
            0x66, 0x81, 0xB9, 0x9C, 0x03, 0x00, 0x00, 0x00, 0xF8,   // cmp word [ecx+0x39C], red
            0x74, 0x07,                                       // je past
            0xC6, 0x81, 0x6A, 0x03, 0x00, 0x00, 0x00,         // mov byte [ecx+0x36A], 0
        ];

        Segment(FeetCave.Build(Cave), e => e.Keep, expected.Length).ShouldBe(expected);
    }

    // Every detour ends by jumping back to the instruction after the run it displaced. One
    // byte out here lands in the middle of an instruction.
    [Theory]
    [InlineData(0x0042BA01u)]
    [InlineData(0x0042BB00u)]
    [InlineData(0x0042AE13u)]
    public void ComesBackToWhereItLeftOff(uint resume)
    {
        var (code, _) = FeetCave.Build(Cave);
        var wanted = Returns(code).ToList();

        wanted.ShouldContain(resume);
    }

    [Fact]
    public void ComesBackFourTimesForFourDetours() =>
        Returns(FeetCave.Build(Cave).Code).Count().ShouldBe(4);

    [Fact]
    public void FitsInWhatIsReservedForIt() =>
        FeetCave.Build(Cave).Code.Length.ShouldBeLessThan(FeetCave.CaveSize);

    // Both position detours reach the same place from three different ways of giving up, so
    // this is worth stating on its own: the three displacements above all land on the jump.
    [Fact]
    public void GivesUpToTheSamePlaceHoweverItGivesUp()
    {
        var (code, entries) = FeetCave.Build(Cave);
        var start = entries.Remote;

        // jne at +12, je at +22, je at +39 — the jump back is at +56.
        (14 + code[start + 13]).ShouldBe(56);
        (24 + code[start + 23]).ShouldBe(56);
        (41 + code[start + 40]).ShouldBe(56);
        code[start + 56].ShouldBe((byte)0xE9);
    }

    /// <summary>The addresses every near jump in the cave goes to.</summary>
    private static IEnumerable<uint> Returns(byte[] code)
    {
        for (var at = 0; at + 5 <= code.Length; at++)
        {
            if (code[at] == 0xE9)
            {
                yield return (uint)(Cave.Value + at + 5 + BitConverter.ToInt32(code, at + 1));
            }
        }
    }

    private static byte[] Segment(
        (byte[] Code, FeetCave.Entries Entries) built, Func<FeetCave.Entries, int> start, int length) =>
        built.Code[start(built.Entries)..(start(built.Entries) + length)];
}
