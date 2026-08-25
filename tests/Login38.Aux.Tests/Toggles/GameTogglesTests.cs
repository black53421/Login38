using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Pins what each toggle switches, and which setting moves it.
/// </summary>
/// <remarks>
/// The bytes cannot be checked against a running client here, so what is checked is that
/// they are the ones that were verified against one — and that the switch in the window
/// reaches the toggle it is labelled for. Wiring two of these to each other is a fault
/// nothing else would catch: both toggles work, they are just the wrong way round.
/// </remarks>
public sealed class GameTogglesTests
{
    private static ShowClockToggle Clock() => new(NullLogger<ShowClockToggle>.Instance);

    private static UnderwaterPumpToggle Water() => new(NullLogger<UnderwaterPumpToggle>.Instance);

    [Fact]
    public void EachToggleHasItsOwnName()
    {
        Clock().Name.ShouldBe("show-clock");
        Water().Name.ShouldBe("underwater-pump");
    }

    [Fact]
    public void TheClockFollowsTheClockSwitch()
    {
        var settings = new AuxSettings();

        Clock().WantedBy(settings).ShouldBeFalse();

        settings.Misc.ShowClock = true;

        Clock().WantedBy(settings).ShouldBeTrue();
        Water().WantedBy(settings).ShouldBeFalse();
    }

    [Fact]
    public void TheWaterFollowsTheWaterSwitch()
    {
        var settings = new AuxSettings();

        Water().WantedBy(settings).ShouldBeFalse();

        settings.Misc.UnderwaterPump = true;

        Water().WantedBy(settings).ShouldBeTrue();
        Clock().WantedBy(settings).ShouldBeFalse();
    }

    // Both states have to be the same length, or reading the address back would compare a
    // different number of bytes depending on which way the switch was going.
    [Fact]
    public void BothStatesAreTheSameLength()
    {
        var clock = Clock();
        clock.SwitchedOn.Length.ShouldBe(clock.SwitchedOff.Length);
        clock.SwitchedOn.SequenceEqual(clock.SwitchedOff).ShouldBeFalse();

        var water = Water();
        water.SwitchedOn.Length.ShouldBe(water.SwitchedOff.Length);
        water.SwitchedOn.SequenceEqual(water.SwitchedOff).ShouldBeFalse();
    }

    // The clock is drawn already; all that is removed is the branch that skips it when the
    // pointer is somewhere else.
    [Fact]
    public void TheClockRemovesABranchAndNothingElse()
    {
        var clock = Clock();

        clock.SwitchedOff.ToArray().ShouldBe([0x0F, 0x84, 0xA1, 0x00, 0x00, 0x00]);

        // Every byte of it. Five would leave the tail of the branch behind, decoding as
        // whatever those bytes happen to be.
        clock.SwitchedOn.ToArray().ShouldAllBe(b => b == 0x90);
    }

    [Fact]
    public void TheWaterIsOneFlag()
    {
        var water = Water();

        water.SwitchedOff.ToArray().ShouldBe([1]);
        water.SwitchedOn.ToArray().ShouldBe([0]);
    }

    // The clock is an instruction and the flag is data. Only the first needs the page made
    // writable and the instruction cache flushed after.
    [Fact]
    public void OnlyTheOneThatIsCodeSaysSo()
    {
        Clock().IsCode.ShouldBeTrue();
        Water().IsCode.ShouldBeFalse();
    }

    [Fact]
    public void EachToggleIsWhereItWasFound()
    {
        Clock().Address.Value.ShouldBe(0x0078_AD50u);
        Water().Address.Value.ShouldBe(0x009A_B646u);
    }
}
