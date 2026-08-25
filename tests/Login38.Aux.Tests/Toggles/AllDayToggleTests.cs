using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the eleven places the all-day toggle writes and the code it runs to undo them.
/// </summary>
/// <remarks>
/// The addresses and bytes are facts about one client build, so they are pinned rather
/// than derived: a test that recomputed them from the same constants would agree with any
/// mistake in them.
/// </remarks>
public sealed class AllDayToggleTests
{
    /// <summary>
    /// The reference's own shellcode, transcribed byte for byte from its builder.
    /// </summary>
    /// <remarks>
    /// Independent of the port: written out from the Rust source's comments and constants
    /// rather than produced by anything the port shares. The three branch displacements
    /// the reference counted by hand are in it as it wrote them, so this also proves the
    /// builder resolves them to the same places.
    /// </remarks>
    private static ReadOnlySpan<byte> Reference =>
    [
        0x60, 0xC6, 0x05, 0xEF, 0xBC, 0x9A, 0x00, 0x00, 0xA1, 0x60, 0x5B, 0x96,
        0x00, 0x3D, 0x00, 0x40, 0x00, 0x00, 0x7D, 0x14, 0xA1, 0x64, 0x5B, 0x96,
        0x00, 0xBF, 0xF0, 0x55, 0x96, 0x00, 0xB9, 0x5A, 0x01, 0x00, 0x00, 0xFC,
        0xF2, 0xAF, 0x75, 0x07, 0xC6, 0x05, 0xEF, 0xBC, 0x9A, 0x00, 0x01, 0x0F,
        0xBE, 0x15, 0xEF, 0xBC, 0x9A, 0x00, 0x85, 0xD2, 0x74, 0x10, 0x6A, 0x01,
        0xB9, 0xA4, 0xC7, 0xBD, 0x00, 0xB8, 0x10, 0x9E, 0x57, 0x00, 0xFF, 0xD0,
        0xEB, 0x23, 0x6A, 0x00, 0x6A, 0x00, 0x6A, 0x00, 0xA1, 0x7C, 0x1E, 0xC3,
        0x00, 0x50, 0xB8, 0x70, 0x6D, 0x78, 0x00, 0xFF, 0xD0, 0x83, 0xC4, 0x10,
        0x50, 0xB9, 0xA4, 0xC7, 0xBD, 0x00, 0xB8, 0x10, 0x9E, 0x57, 0x00, 0xFF,
        0xD0, 0x61, 0xC3,
    ];

    [Fact]
    public void BuildsTheSameCodeTheReferenceDid() =>
        PaletteRefresh.Build().ShouldBe(Reference.ToArray());

    [Fact]
    public void IsAsLongAsTheReferenceSaidItWas() => PaletteRefresh.Build().Length.ShouldBe(111);

    // It runs on a thread of its own inside a game that is still drawing, so it has to
    // leave every register exactly as it found it.
    [Fact]
    public void SavesAndRestoresEveryRegister()
    {
        var code = PaletteRefresh.Build();

        code[0].ShouldBe((byte)0x60);
        code[^2..].ShouldBe(new byte[] { 0x61, 0xC3 });
    }

    // Both paths through it must reach the one popad; a branch that skipped it would
    // return with the caller's registers holding the palette routine's leftovers.
    [Fact]
    public void BothPathsMeetAtTheSameExit()
    {
        var code = PaletteRefresh.Build();

        // je .lit at 56, and jmp .done at 72.
        code[56].ShouldBe((byte)0x74);
        (58 + code[57]).ShouldBe(74);

        code[72].ShouldBe((byte)0xEB);
        (74 + code[73]).ShouldBe(109);

        code[109].ShouldBe((byte)0x61);
    }

    // A map id above the threshold means unlit outright; below it, the tileset is looked
    // up in a table and only a hit means unlit. Both write the same flag.
    [Fact]
    public void TakesBothWaysOfBeingUnderground()
    {
        var code = PaletteRefresh.Build();

        code[18].ShouldBe((byte)0x7D);
        (20 + code[19]).ShouldBe(40);

        code[38].ShouldBe((byte)0x75);
        (40 + code[39]).ShouldBe(47);

        // .cave writes 1 to the flag; the missed table lookup jumps past it, leaving the 0
        // written on entry.
        code[40..42].ShouldBe(new byte[] { 0xC6, 0x05 });
        code[46].ShouldBe((byte)0x01);
    }

    // The calculated path calls a four-argument cdecl function, so it has to take its own
    // arguments back off the stack. Missing this drifts esp by 16 bytes per call.
    [Fact]
    public void CleansUpAfterTheCalculatorItCalls()
    {
        var code = PaletteRefresh.Build();

        code[93..96].ShouldBe(new byte[] { 0x83, 0xC4, 0x10 });
    }

    [Fact]
    public void WritesElevenPlaces() => AllDayToggle.Sites.Length.ShouldBe(11);

    // Both states of a site have to be the same length, or writing one of them would run
    // past what the other occupies and clip the instruction after it.
    [Fact]
    public void GivesEverySiteTwoStatesOfTheSameLength() =>
        AllDayToggle.Sites.ShouldAllBe(s => s.SwitchedOff.Length == s.SwitchedOn.Length);

    [Fact]
    public void NeverWritesTheSamePlaceTwice()
    {
        var addresses = AllDayToggle.Sites.Select(s => s.Address).ToList();

        addresses.Distinct().Count().ShouldBe(addresses.Count);
    }

    [Fact]
    public void DescribesEverySiteForTheLog() =>
        AllDayToggle.Sites.ShouldAllBe(s => !string.IsNullOrWhiteSpace(s.What));

    // 15 is the brightest level the rest of the client knows how to render.
    [Fact]
    public void ReplacesTheLightCalculatorWithTheBrightestLevel()
    {
        var site = Site("brightness calculator");

        site.Address.Value.ShouldBe(0x00786D70u);
        site.SwitchedOn[..6].ShouldBe(new byte[] { 0xB8, 0x0F, 0x00, 0x00, 0x00, 0xC3 });
        site.SwitchedOn[6..].ShouldAllBe(b => b == 0x90);
    }

    // Rain, snow and fog cut visibility whatever the light level says, so the renderer
    // returns before drawing any of them.
    [Fact]
    public void StopsTheWeatherRendererAtItsFirstInstruction()
    {
        var site = Site("weather renderer");

        site.SwitchedOff.ShouldBe(new byte[] { 0x55 });
        site.SwitchedOn.ShouldBe(new byte[] { 0xC3 });
    }

    [Fact]
    public void AnswersTheDaylightCheckWithYes()
    {
        var site = Site("daylight check");

        site.SwitchedOn.ShouldBe(new byte[] { 0xB0, 0x01, 0xC3 });
    }

    // The client forces level 1 indoors rather than calculating one, so the constant it
    // forces is what has to change — not the branch that reaches it.
    [Fact]
    public void RaisesTheForcedIndoorLevelToTheMaximum()
    {
        var site = Site("indoor light level");

        site.Address.Value.ShouldBe(0x004EA6D4u);
        site.SwitchedOff.ShouldBe(new byte[] { 0x01 });
        site.SwitchedOn.ShouldBe(new byte[] { 0x0F });
    }

    // A branch that skips the palette refresh when the level has not changed. With the
    // level forced it never changes, so leaving this in place means it never refreshes.
    [Fact]
    public void StopsTheClientSkippingItsOwnRefresh()
    {
        var site = Site("light recompute skip");

        site.Address.Value.ShouldBe(0x004EAD19u);
        site.SwitchedOff.ShouldBe(new byte[] { 0x0F, 0x8D, 0x2B, 0x04, 0x00, 0x00 });
        site.SwitchedOn.ShouldAllBe(b => b == 0x90);
    }

    // Seven bytes replaced by a five-byte jump and two NOPs, because the instruction it
    // replaces is seven bytes and a shorter replacement would leave a tail behind.
    [Fact]
    public void JumpsPastTheDarkSceneOverlay()
    {
        var site = Site("environment overlay");

        site.SwitchedOn.ShouldBe(new byte[] { 0xE9, 0xA6, 0x00, 0x00, 0x00, 0x90, 0x90 });
        site.SwitchedOn.Length.ShouldBe(site.SwitchedOff.Length);
    }

    // push 15; pop edx, in the three bytes that loaded a local into edx.
    [Fact]
    public void ForcesTheLastLightLevelToReachTheRenderer()
    {
        var site = Site("final light argument");

        site.SwitchedOff.ShouldBe(new byte[] { 0x8B, 0x55, 0xAC });
        site.SwitchedOn.ShouldBe(new byte[] { 0x6A, 0x0F, 0x5A });
    }

    [Fact]
    public void FollowsTheAllDaySwitch()
    {
        var toggle = Toggle();

        toggle.WantedBy(new AuxSettings { Misc = new MiscToggles { AllDay = true } }).ShouldBeTrue();
        toggle.WantedBy(new AuxSettings { Misc = new MiscToggles { AllDay = false } }).ShouldBeFalse();
    }

    [Fact]
    public void HasAName() => Toggle().Name.ShouldBe("all-day");

    private static AllDayToggle Toggle() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<AllDayToggle>.Instance);

    private static AllDayToggle.Site Site(string what) =>
        AllDayToggle.Sites.Single(s => s.What == what);
}
