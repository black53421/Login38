using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the switch that turns the damage numbers on, and the sites it detours.
/// </summary>
public sealed class DamageToggleTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    [Fact]
    public void IsWantedWhenTheNumbersAreAskedFor() =>
        new DamageToggle(NullLogger<DamageToggle>.Instance)
            .WantedBy(new AuxSettings { Misc = { ShowAttackDamage = true } }).ShouldBeTrue();

    // Under the feet is a way of showing them, so asking for that asks for them.
    [Fact]
    public void IsWantedWhenOnlyTheFeetSwitchIsOn() =>
        new DamageToggle(NullLogger<DamageToggle>.Instance)
            .WantedBy(new AuxSettings { Misc = { DamageAtFeet = true } }).ShouldBeTrue();

    [Fact]
    public void IsNotWantedWhenNeitherIs() =>
        new DamageToggle(NullLogger<DamageToggle>.Instance)
            .WantedBy(new AuxSettings()).ShouldBeFalse();

    // Against a process that is not the game: none of the sites holds what the client is
    // expected to have there, so nothing is written and the failure is reported rather
    // than thrown through the middle of a pass.
    [Fact]
    public void RefusesToDetourAClientItDoesNotRecognise() =>
        new DamageToggle(NullLogger<DamageToggle>.Instance).Apply(_process, wanted: true).ShouldBeFalse();

    // Nothing to take out is not a failure — it is the ordinary state of a game whose
    // player has never turned this on.
    [Fact]
    public void HasNothingToDoWhenItIsOffAndWasNeverOn() =>
        new DamageToggle(NullLogger<DamageToggle>.Instance).Apply(_process, wanted: false).ShouldBeTrue();

    [Fact]
    public void ReplacesASiteWithAJumpToTheCave()
    {
        var site = DamageCave.Single;
        var patch = site.JumpTo(new GameAddress(0x0300_0000));

        patch.Length.ShouldBe(site.Length);
        patch[0].ShouldBe((byte)0xE9);
        (site.Address.Value + 5 + (uint)BitConverter.ToInt32(patch, 1)).ShouldBe(0x0300_0000u);
    }

    // The bytes after the jump are still reachable: the client branches into the middle of
    // some of these runs from elsewhere, and half an instruction left there is executed.
    [Fact]
    public void PadsTheRestOfTheRunWithNoOps() =>
        DamageCave.Single.JumpTo(new GameAddress(0x0300_0000))[5..].ShouldAllBe(b => b == 0x90);

    [Fact]
    public void RefusesASiteTooShortToHoldAJump() =>
        Should.Throw<InvalidOperationException>(() =>
            new HookSite(new GameAddress(0x1000), [0x90, 0x90], new GameAddress(0x1002))
                .JumpTo(new GameAddress(0x2000)));

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 }, SiteState.Stock)]
    [InlineData(new byte[] { 0xE9, 0x00, 0x00 }, SiteState.Ours)]
    [InlineData(new byte[] { 0xCC, 0xCC, 0xCC }, SiteState.Foreign)]
    public void TellsTheClientsBytesFromItsOwnAndFromEverybodyElses(byte[] found, SiteState state) =>
        HookSite.Classify(found, [0x01, 0x02, 0x03], [0xE9, 0x00, 0x00]).ShouldBe(state);

    // On the way out, a site holding something unrecognised belongs to whatever put it
    // there. Writing the client's bytes over it would break that, which is worse than
    // leaving this feature on.
    [Fact]
    public void WillNotRestoreOverSomebodyElsesPatch() =>
        HookSite.Classify([0xCC], [0x01], [0xE9]).ShouldBe(SiteState.Foreign);

    // Every site is a run of whole instructions, so the detour can replay them and carry on.
    [Theory]
    [InlineData(0x005295D9u, 10, 0x005295E3u)]
    [InlineData(0x0052A821u, 6, 0x0052A827u)]
    [InlineData(0x0042B9FBu, 6, 0x0042BA01u)]
    [InlineData(0x0042B9D2u, 6, 0x0042B9D8u)]
    [InlineData(0x0042BAFBu, 5, 0x0042BB00u)]
    [InlineData(0x0042AE0Cu, 7, 0x0042AE13u)]
    public void ComesBackToTheInstructionAfterWhatItDisplaced(uint address, int length, uint resume)
    {
        var site = All().First(s => s.Address.Value == address);

        site.Length.ShouldBe(length);
        site.Resume.Value.ShouldBe(resume);
        (site.Address.Value + (uint)site.Length).ShouldBe(resume);
    }

    [Fact]
    public void HasRoomForAJumpAtEverySite() =>
        All().ShouldAllBe(site => site.Length >= 5);

    private static HookSite[] All() =>
    [
        DamageCave.Single, DamageCave.Area,
        FeetCave.Remote, FeetCave.Local, FeetCave.Tail, FeetCave.Keep,
    ];
}
