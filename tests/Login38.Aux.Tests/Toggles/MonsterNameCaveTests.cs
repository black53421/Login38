using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the detours that draw a coloured monster name readably.
/// </summary>
/// <remarks>
/// Pinned byte for byte against an independent transcription of the reference's builder.
/// Two of these three sites are the client's own text routine, which every string in the
/// game goes through several times a frame — a wrong displacement here does not fail a
/// test, it takes the client down while somebody is playing it.
/// </remarks>
public sealed class MonsterNameCaveTests
{
    /// <summary>Somewhere for the cave to be, which nothing here depends on.</summary>
    private static readonly GameAddress Cave = new(0x0300_0000);

    /// <summary>And somewhere for the table.</summary>
    private static readonly GameAddress Table = new(0x0400_0000);

    // ---- the table -------------------------------------------------------------------

    [Fact]
    public void WritesTheCountBeforeTheAddresses()
    {
        var raw = MonsterNameCave.MarkerTable([new GameAddress(0x11223344), new GameAddress(0x55667788)]);

        BitConverter.ToUInt32(raw).ShouldBe(2u);
        BitConverter.ToUInt32(raw, 4).ShouldBe(0x11223344u);
        BitConverter.ToUInt32(raw, 8).ShouldBe(0x55667788u);
        raw.Length.ShouldBe(12);
    }

    [Fact]
    public void WritesAZeroCountForNothingAtAll() =>
        MonsterNameCave.MarkerTable([]).ShouldBe([0, 0, 0, 0]);

    // The detours walk this list inside the client's render path, so its length is a cost
    // paid per name drawn. More than the cap is dropped rather than searched.
    [Fact]
    public void StopsAtWhatTheDetoursWillWalk()
    {
        var many = Enumerable.Range(1, MonsterNameCave.MarkerCapacity + 40)
            .Select(i => new GameAddress((uint)i)).ToList();

        var raw = MonsterNameCave.MarkerTable(many);

        BitConverter.ToUInt32(raw).ShouldBe((uint)MonsterNameCave.MarkerCapacity);
        raw.Length.ShouldBe(4 + (MonsterNameCave.MarkerCapacity * 4));
    }

    [Fact]
    public void NeverWritesMoreThanWasReservedForIt() =>
        MonsterNameCave.MarkerTable(
                [.. Enumerable.Range(1, 500).Select(i => new GameAddress((uint)i))])
            .Length.ShouldBeLessThanOrEqualTo(MonsterNameCave.MarkerTableBytes);

    // ---- the name render detour ------------------------------------------------------

    [Fact]
    public void ReadsTheEntityOutOfTheRenderFrameFirst()
    {
        byte[] expected =
        [
            0x8B, 0x85, 0xC8, 0xFD, 0xFF, 0xFF,   // mov eax, [ebp-0x238]   the entity
            0x85, 0xC0,                           // test eax, eax
        ];

        MonsterNameCave.BuildNameRender(Cave, Table)[..expected.Length].ShouldBe(expected);
    }

    [Fact]
    public void TestsTheColourWordAgainstWhiteAndThenTheFour()
    {
        byte[] expected =
        [
            0x0F, 0xB7, 0x48, 0x30,               // movzx ecx, word [eax+0x30]
            0x81, 0xF9, 0xDF, 0xFF, 0x00, 0x00,   // cmp ecx, white
        ];

        MonsterNameCave.BuildNameRender(Cave, Table)[14..24].ShouldBe(expected);
    }

    // One comparison against white, not two. The reference has a "client default" constant
    // and a White band that are the same 0xFFDF, and emits both.
    [Fact]
    public void ComparesAgainstWhiteOnce() =>
        Occurrences(MonsterNameCave.BuildNameRender(Cave, Table),
            [0x81, 0xF9, 0xDF, 0xFF, 0x00, 0x00]).ShouldBe(1);

    [Theory]
    [InlineData(0x8800)]
    [InlineData(0xF800)]
    [InlineData(0x001F)]
    [InlineData(0x07E0)]
    public void ComparesAgainstEachColourItWrites(int colour) =>
        Occurrences(MonsterNameCave.BuildNameRender(Cave, Table),
            [0x81, 0xF9, (byte)colour, (byte)(colour >> 8), 0x00, 0x00]).ShouldBe(1);

    // A colour word on its own is not enough — the client keeps colours on players too, so
    // an object id below the spawn floor sends the name back through the client's path.
    [Fact]
    public void RefusesAnObjectIdBelowTheSpawnFloor()
    {
        byte[] expected =
        [
            0x8B, 0x50, 0x0C,                     // mov edx, [eax+0x0C]
            0x81, 0xFA, 0x00, 0x00, 0x00, 0x01,   // cmp edx, 0x01000000
        ];

        MonsterNameCave.BuildNameRender(Cave, Table)[83..92].ShouldBe(expected);
    }

    [Fact]
    public void WalksTheMarkerTableForTheEntityBeingDrawn()
    {
        byte[] expected =
        [
            0x56, 0x57,                           // push esi; push edi
            0xBE, 0x00, 0x00, 0x00, 0x04,         // mov esi, table
            0x8B, 0x3E,                           // mov edi, [esi]      the count
            0x83, 0xC6, 0x04,                     // add esi, 4
            0x85, 0xFF,                           // test edi, edi
            0x74, 0x0A,                           // jz empty
            0x39, 0x06,                           // cmp [esi], eax
            0x74, 0x0D,                           // je found
            0x83, 0xC6, 0x04,                     // add esi, 4
            0x4F,                                 // dec edi
            0x75, 0xF6,                           // jnz next
            0x5F, 0x5E,                           // empty: pop edi; pop esi
        ];

        var code = MonsterNameCave.BuildNameRender(Cave, Table);

        code[98..126].ShouldBe(expected);
        code[126].ShouldBe((byte)0xE9);
        code[131..133].ShouldBe([0x5F, 0x5E]);
    }

    [Fact]
    public void DrawsTheNameWhiteInsideAndColouredOutside()
    {
        byte[] expected =
        [
            0x6A, 0x00,                           // push 0
            0x51,                                 // push ecx        outer: the entity colour
            0x68, 0xDF, 0xFF, 0x00, 0x00,         // push white      inner
            0x8B, 0x95, 0xA4, 0xFD, 0xFF, 0xFF,   // mov edx, [ebp-0x25C]
            0x52,                                 // push y
            0x8B, 0x85, 0xA0, 0xFD, 0xFF, 0xFF,   // mov eax, [ebp-0x260]
            0x50,                                 // push x
            0x8B, 0x8D, 0xA8, 0xFD, 0xFF, 0xFF,   // mov ecx, [ebp-0x258]
            0x51,                                 // push length
            0x8B, 0x95, 0xC8, 0xFD, 0xFF, 0xFF,   // mov edx, [ebp-0x238]
            0x8B, 0x42, 0x60,                     // mov eax, [edx+0x60]
            0x50,                                 // push name
            0x8B, 0x0D, 0xE0, 0x84, 0x9A, 0x00,   // mov ecx, [0x009A84E0]
            0x51,                                 // push surface
        ];

        MonsterNameCave.BuildNameRender(Cave, Table)[133..179].ShouldBe(expected);
    }

    [Fact]
    public void CallsTheClientsOwnTextRoutineAndCleansUpAfterIt()
    {
        var code = MonsterNameCave.BuildNameRender(Cave, Table);

        code[179].ShouldBe((byte)0xE8);
        Target(code, 179).ShouldBe(0x0046F150u);
        code[184..187].ShouldBe([0x83, 0xC4, 0x20]);          // add esp, 0x20
    }

    // Past the client's own call rather than back to it: the name has been drawn.
    [Fact]
    public void CarriesOnPastTheCallItStoodInFor()
    {
        var code = MonsterNameCave.BuildNameRender(Cave, Table);

        Target(code, 187).ShouldBe(0x004F2BE2u);
    }

    // Everything it decides not to touch is handed back to the client with the bytes the
    // detour displaced replayed first.
    [Fact]
    public void ReplaysWhatItDisplacedForEveryNameItLeavesAlone()
    {
        var code = MonsterNameCave.BuildNameRender(Cave, Table);

        code[192..200].ShouldBe(MonsterNameCave.NameRender.Stock);
        Target(code, 200).ShouldBe(MonsterNameCave.NameRender.Resume.Value);
        code.Length.ShouldBe(205);
    }

    [Fact]
    public void FitsInWhatIsReservedForTheNameRenderDetour() =>
        MonsterNameCave.BuildNameRender(Cave, Table)
            .Length.ShouldBeLessThan(MonsterNameCave.NameCaveSize);

    // Six ways of deciding this is not a coloured monster, and all of them land on the
    // replayed bytes rather than anywhere in the middle of them.
    [Fact]
    public void GivesUpToTheSamePlaceHoweverItGivesUp()
    {
        var code = MonsterNameCave.BuildNameRender(Cave, Table);

        Near(code, 8).ShouldBe(192);      // null entity
        Near(code, 24).ShouldBe(192);     // already white
        Near(code, 78).ShouldBe(192);     // not one of the four
        Near(code, 92).ShouldBe(192);     // object id too low
        Near(code, 126).ShouldBe(192);    // not in the marker table
    }

    // ---- the selected-name detours ---------------------------------------------------

    [Fact]
    public void ReplaysThePrologueItDisplacedBeforeAnythingElse()
    {
        var code = Selected();

        code[..5].ShouldBe(MonsterNameCave.TextDraw.Stock);
        code[5..8].ShouldBe([0x8B, 0x45, 0x04]);              // mov eax, [ebp+4]
    }

    // The whole client draws text through this routine. Twelve four-byte comparisons on
    // every string is what the reference does; a pair of bounds throws out everything that
    // is not the selected-name panel in twelve bytes.
    [Fact]
    public void RejectsEverythingOutsideThePanelWithTwoComparisons()
    {
        byte[] expected =
        [
            0x3D, 0x8C, 0x2E, 0x4F, 0x00,         // cmp eax, 0x004F2E8C   the lowest
            0x0F, 0x82,                           // jb done
        ];

        var code = Selected();

        code[8..15].ShouldBe(expected);
        code[19..26].ShouldBe([0x3D, 0xAB, 0x39, 0x4F, 0x00, 0x0F, 0x87]);   // cmp highest; ja done
    }

    [Fact]
    public void KeepsEveryCallSiteTheBoundsLetThrough()
    {
        var code = Selected();

        foreach (var caller in MonsterNameCave.SelectedReturns)
        {
            Occurrences(code, [0x3D, .. BitConverter.GetBytes(caller)])
                .ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    // The bounds are only a prefilter if every call site is inside them. One outside would
    // be silently dropped, and the panel would draw that one name the old way.
    [Fact]
    public void BoundsCoverEveryCallSiteItKnowsAbout()
    {
        Bounded(MonsterNameCave.SelectedReturns.ToArray());
        Bounded(MonsterNameCave.CompactReturns.ToArray());

        static void Bounded(uint[] callers) =>
            callers.ShouldAllBe(caller => caller >= callers.Min() && caller <= callers.Max());
    }

    [Fact]
    public void MovesTheColourFromTheGlyphToTheOutline()
    {
        byte[] expected =
        [
            0x89, 0x45, 0x20,                     // mov [ebp+0x20], eax   outer := the colour
            0xC7, 0x45, 0x1C, 0xDF, 0xFF, 0x00, 0x00,   // mov [ebp+0x1C], white
        ];

        var code = Selected();

        code[268..278].ShouldBe(expected);
    }

    [Fact]
    public void ChecksWhatIsSelectedAgainstTheMarkerTable()
    {
        byte[] expected =
        [
            0x8B, 0x15, 0x40, 0xF4, 0xAB, 0x00,   // mov edx, [0x00ABF440]
            0x85, 0xD2,                           // test edx, edx
            0x0F, 0x84,                           // je done
        ];

        var code = Selected();

        code[219..229].ShouldBe(expected);
        code[233..235].ShouldBe([0x56, 0x57]);                // the marker walk, on edx
        code[249..251].ShouldBe([0x39, 0x16]);                // cmp [esi], edx
    }

    [Fact]
    public void AlwaysEndsUpBackInTheRoutineItStoodInFrontOf()
    {
        Target(Selected(), 278).ShouldBe(MonsterNameCave.TextDraw.Resume.Value);
        Target(Compact(), 196 - 5).ShouldBe(MonsterNameCave.CompactTextDraw.Resume.Value);
    }

    // The compact form has one more displaced byte and eight fewer call sites, and is
    // otherwise the same code.
    [Fact]
    public void BuildsTheCompactFormFromItsOwnPrologueAndCallSites()
    {
        var code = Compact();

        code[..6].ShouldBe(MonsterNameCave.CompactTextDraw.Stock);
        code.Length.ShouldBe(196);
    }

    [Fact]
    public void FitsInWhatIsReservedForTheTextDetours()
    {
        Selected().Length.ShouldBeLessThan(MonsterNameCave.TextCaveSize);
        Compact().Length.ShouldBeLessThan(MonsterNameCave.TextCaveSize);
    }

    [Fact]
    public void RefusesADetourWithNoCallSitesToActOn() =>
        Should.Throw<ArgumentException>(() =>
            MonsterNameCave.BuildTextColour(Cave, Table, MonsterNameCave.TextDraw, []));

    // ---- the sites -------------------------------------------------------------------

    [Theory]
    [InlineData(0x004F2BA0u, 8, 0x004F2BA8u)]
    [InlineData(0x0046F150u, 5, 0x0046F155u)]
    [InlineData(0x0046F980u, 6, 0x0046F986u)]
    public void ComesBackToTheInstructionAfterWhatItDisplaced(uint address, int length, uint resume)
    {
        var site = All().First(s => s.Address.Value == address);

        site.Length.ShouldBe(length);
        site.Resume.Value.ShouldBe(resume);
        (site.Address.Value + (uint)site.Length).ShouldBe(resume);
    }

    [Fact]
    public void HasRoomForAJumpAtEverySite() => All().ShouldAllBe(site => site.Length >= 5);

    private static HookSite[] All() =>
        [MonsterNameCave.NameRender, MonsterNameCave.TextDraw, MonsterNameCave.CompactTextDraw];

    private static byte[] Selected() => MonsterNameCave.BuildTextColour(
        Cave, Table, MonsterNameCave.TextDraw, MonsterNameCave.SelectedReturns);

    private static byte[] Compact() => MonsterNameCave.BuildTextColour(
        Cave, Table, MonsterNameCave.CompactTextDraw, MonsterNameCave.CompactReturns);

    /// <summary>Where a <c>call</c> or <c>jmp rel32</c> at an offset actually goes.</summary>
    private static uint Target(byte[] code, int at) =>
        (uint)(Cave.Value + at + 5 + BitConverter.ToInt32(code, at + 1));

    /// <summary>Where a two-byte-opcode branch at an offset lands, as an offset in the cave.</summary>
    private static int Near(byte[] code, int at)
    {
        // 0F 8x rel32 for a conditional, E9 rel32 for a jump.
        var length = code[at] == 0xE9 ? 5 : 6;

        return at + length + BitConverter.ToInt32(code, at + length - 4);
    }

    private static int Occurrences(byte[] code, ReadOnlySpan<byte> pattern)
    {
        var found = 0;

        for (var at = 0; at + pattern.Length <= code.Length; at++)
        {
            if (code.AsSpan(at, pattern.Length).SequenceEqual(pattern))
            {
                found++;
            }
        }

        return found;
    }
}
