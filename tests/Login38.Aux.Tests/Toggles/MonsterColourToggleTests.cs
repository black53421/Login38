using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the switch that puts the monster colour detours in the client.
/// </summary>
public sealed class MonsterColourToggleTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    [Fact]
    public void IsWantedWhenTheColoursAreAskedFor() =>
        Toggle().WantedBy(new AuxSettings { Misc = { MonsterLevelColour = true } }).ShouldBeTrue();

    [Fact]
    public void IsNotWantedOtherwise() => Toggle().WantedBy(new AuxSettings()).ShouldBeFalse();

    // Against a process that is not the game: none of the three sites holds what the client
    // is expected to have there, so nothing is written and the failure is reported rather
    // than thrown through the middle of a pass.
    [Fact]
    public void RefusesToDetourAClientItDoesNotRecognise() =>
        Toggle().Apply(_process, wanted: true).ShouldBeFalse();

    // Nothing to take out is not a failure — it is the ordinary state of a game whose
    // player has never turned this on.
    [Fact]
    public void HasNothingToDoWhenItIsOffAndWasNeverOn() =>
        Toggle().Apply(_process, wanted: false).ShouldBeTrue();

    // And with nothing installed there is no table for the scanner to fill in, which is
    // what stops it walking the heap for a feature that is off.
    [Fact]
    public void HasNoTableToOfferUntilTheDetoursAreIn() => Toggle().MarkerTable.ShouldBeNull();

    [Fact]
    public void ReplacesASiteWithAJumpToTheCave()
    {
        var site = MonsterNameCave.NameRender;
        var patch = site.JumpTo(new GameAddress(0x0300_0000));

        patch.Length.ShouldBe(site.Length);
        patch[0].ShouldBe((byte)0xE9);
        (site.Address.Value + 5 + (uint)BitConverter.ToInt32(patch, 1)).ShouldBe(0x0300_0000u);
    }

    // The client branches into the middle of some of these runs from elsewhere, so the
    // bytes after the jump have to be whole instructions rather than half of one.
    [Fact]
    public void PadsTheRestOfTheRunWithNoOps() =>
        MonsterNameCave.NameRender.JumpTo(new GameAddress(0x0300_0000))[5..]
            .ShouldAllBe(b => b == 0x90);

    private static MonsterColourToggle Toggle() =>
        new(
            new MonsterScan(LegacyTextCodec.Auto, NullLogger<MonsterScan>.Instance),
            NullLogger<MonsterColourToggle>.Instance);
}
