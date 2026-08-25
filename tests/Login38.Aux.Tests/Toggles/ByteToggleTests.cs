using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the shared behaviour of a switch the player can move mid-session.
/// </summary>
/// <remarks>
/// Run against a real page in this process rather than a stand-in, so the reads and writes
/// are the ones that would happen against a game. What is being pinned is the decision:
/// when to write, when to leave it, and when to refuse.
/// </remarks>
public sealed class ByteToggleTests : IDisposable
{
    private static readonly byte[] Off = [0x0F, 0x84, 0xA1, 0x00, 0x00, 0x00];
    private static readonly byte[] On = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RemoteBuffer _page;

    public ByteToggleTests() => _page = _process.AllocateScratch(0x100);

    public void Dispose()
    {
        _page.Dispose();
        _process.Dispose();
    }

    [Fact]
    public void SwitchesOn()
    {
        Write(Off);

        Toggle().Apply(_process, wanted: true).ShouldBeTrue();

        Read().ShouldBe(On);
    }

    [Fact]
    public void SwitchesOffAgain()
    {
        Write(On);

        Toggle().Apply(_process, wanted: false).ShouldBeTrue();

        Read().ShouldBe(Off);
    }

    // Called on every pass, so most passes have nothing to do.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeavesItAloneWhenItAlreadyMatches(bool wanted)
    {
        Write(wanted ? On : Off);

        Toggle().Apply(_process, wanted).ShouldBeTrue();

        Read().ShouldBe(wanted ? On : Off);
    }

    // What some of these switch is data the client writes for its own reasons. Reading the
    // state back each pass rather than remembering it is what puts the switch back.
    [Fact]
    public void PutsItBackWhenTheGameOverwritesIt()
    {
        var toggle = Toggle();
        Write(Off);

        toggle.Apply(_process, wanted: true);
        Write(Off);

        toggle.Apply(_process, wanted: true).ShouldBeTrue();
        Read().ShouldBe(On);
    }

    // Anything that is neither state is somebody else's patch, or a client this port has
    // not seen. Overwriting it would be a guess; "restoring" it would be worse.
    [Fact]
    public void RefusesToTouchSomethingItDoesNotRecognise()
    {
        byte[] foreign = [0xE9, 0x11, 0x22, 0x33, 0x44, 0x90];
        Write(foreign);

        Toggle().Apply(_process, wanted: true).ShouldBeFalse();

        Read().ShouldBe(foreign);
    }

    [Fact]
    public void RefusesToRestoreSomethingItDidNotWrite()
    {
        byte[] foreign = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];
        Write(foreign);

        Toggle().Apply(_process, wanted: false).ShouldBeFalse();

        Read().ShouldBe(foreign);
    }

    // Nothing about the applied state is kept, so two games cannot decide for each other.
    // The reference kept it in a process-global and the second client silently got nothing.
    [Fact]
    public void KeepsNoStateBetweenGames()
    {
        Write(Off);
        Toggle().Apply(_process, wanted: true);

        // A second toggle, as a second game would have, over a page already switched on.
        Toggle().Apply(_process, wanted: false).ShouldBeTrue();

        Read().ShouldBe(Off);
    }

    // Some of what these switch is a value the client keeps up to date itself, and it has
    // no original to put back. The sea-water flag reads 0 on a map with no sea in it, and
    // writing the "off" value over that told dry ground to draw water — twice a second,
    // for as long as the switch was off, which is to say for almost the whole game.
    [Fact]
    public void WritesNothingAtAllWhenSwitchedOffIfTheClientOwnsTheValue()
    {
        byte[] whateverTheMapCallsFor = On;
        Write(whateverTheMapCallsFor);

        new ClientOwnedToggle(_page.Address).Apply(_process, wanted: false).ShouldBeTrue();

        Read().ShouldBe(whateverTheMapCallsFor);
    }

    // Including after this launcher has switched it on, because there is no rule for what
    // to put back: it cannot tell whether this map's own value is the one it overwrote or
    // the other one. The client sets the flag again next time the player touches water.
    [Fact]
    public void StillWritesNothingAfterItHadSwitchedItOn()
    {
        var toggle = new ClientOwnedToggle(_page.Address);
        Write(Off);

        toggle.Apply(_process, wanted: true).ShouldBeTrue();
        Read().ShouldBe(On);

        toggle.Apply(_process, wanted: false).ShouldBeTrue();
        Read().ShouldBe(On);
    }

    // Switching one on is unchanged: that is the half the player asked for.
    [Fact]
    public void StillSwitchesOnWhenTheClientOwnsTheValue()
    {
        Write(Off);

        new ClientOwnedToggle(_page.Address).Apply(_process, wanted: true).ShouldBeTrue();

        Read().ShouldBe(On);
    }

    [Fact]
    public void ReportsThatItCouldNotRead() =>
        new TestToggle(new GameAddress(0x10)).Apply(_process, wanted: true).ShouldBeFalse();

    private TestToggle Toggle() => new(_page.Address);

    private void Write(ReadOnlySpan<byte> bytes) => _process.WriteBytes(_page.Address, bytes);

    private byte[] Read() => _process.ReadBytes(_page.Address, Off.Length);

    /// <summary>A toggle over a page this test owns, so the decisions can be exercised.</summary>
    private class TestToggle : ByteToggle
    {
        private readonly GameAddress _address;

        public TestToggle(GameAddress address) : base(NullLogger.Instance) => _address = address;

        public override string Name => "test";

        public override bool WantedBy(AuxSettings settings) => true;

        public override GameAddress Address => _address;

        public override ReadOnlySpan<byte> SwitchedOff => Off;

        public override ReadOnlySpan<byte> SwitchedOn => On;

        // Written as data: the page belongs to this test, not to a code section.
        public override bool IsCode => false;
    }

    /// <summary>A toggle over something the client rewrites for its own reasons.</summary>
    private sealed class ClientOwnedToggle : TestToggle
    {
        public ClientOwnedToggle(GameAddress address) : base(address)
        {
        }

        public override bool ClientOwned => true;
    }
}
