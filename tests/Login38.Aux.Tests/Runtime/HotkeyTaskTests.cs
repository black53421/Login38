using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers what happens between a function key going down and a packet going out.
/// </summary>
/// <remarks>
/// The keystroke itself arrives on an operating system thread that must not be kept
/// waiting, so everything worth checking is here: which key belongs to which row, whether a
/// row may fire yet, and what becomes of a press that cannot be acted on.
/// </remarks>
public sealed class HotkeyTaskTests
{
    [Theory]
    [InlineData(0x70, 0)]
    [InlineData(0x71, 1)]
    [InlineData(0x72, 2)]
    [InlineData(0x73, 3)]
    public void KnowsWhichRowAKeyBelongsTo(int key, int row) => HotkeyTask.RowOf(key).ShouldBe(row);

    [Theory]
    [InlineData(0x74)]
    [InlineData(0x41)]
    public void HasNoRowForAnyOtherKey(int key) => HotkeyTask.RowOf(key).ShouldBeNull();

    // The first press after the game starts should do the thing, not wait out a cooldown
    // against a press that never happened.
    [Fact]
    public void LetsARowFireTheFirstTime() =>
        HotkeyTask.Ready(null, TimeSpan.Zero).ShouldBeTrue();

    // A key held down repeats about thirty times a second, and each repeat is a press as
    // far as the hook is concerned. Without this, leaning on F1 empties a bag.
    [Fact]
    public void HoldsARowBackWhileTheKeyIsStillDown() =>
        HotkeyTask.Ready(TimeSpan.Zero, TimeSpan.FromMilliseconds(499)).ShouldBeFalse();

    [Fact]
    public void LetsItFireAgainAfterHalfASecond() =>
        HotkeyTask.Ready(TimeSpan.Zero, TimeSpan.FromMilliseconds(500)).ShouldBeTrue();

    [Fact]
    public void TakesThePressOnARowWithSomethingBoundToIt()
    {
        var wanted = Pressed(1);

        HotkeyTask.Take(Macros("", "加速術/ME"), wanted, Never(), TimeSpan.Zero).ShouldBe(1);
        wanted[1].ShouldBeFalse();
    }

    [Fact]
    public void HasNothingToTakeWhenNothingWasPressed() =>
        HotkeyTask.Take(Macros("加速術/ME"), Pressed(), Never(), TimeSpan.Zero).ShouldBeNull();

    // Forgotten rather than held: binding something to that key later should not fire it
    // the moment it is bound.
    [Fact]
    public void ForgetsAPressOnAKeyWithNothingBoundToIt()
    {
        var wanted = Pressed(0);

        HotkeyTask.Take(Macros(""), wanted, Never(), TimeSpan.Zero).ShouldBeNull();
        wanted[0].ShouldBeFalse();
    }

    [Fact]
    public void ForgetsAPressOnARowWhoseSwitchIsOff()
    {
        var macros = Macros("加速術/ME");
        macros[0].Enabled = false;

        HotkeyTask.Take(macros, Pressed(0), Never(), TimeSpan.Zero).ShouldBeNull();
    }

    [Fact]
    public void ForgetsARepeatRatherThanQueueingIt()
    {
        var fired = Never();
        fired[0] = TimeSpan.Zero;

        HotkeyTask.Take(Macros("加速術/ME"), Pressed(0), fired, TimeSpan.FromMilliseconds(100))
            .ShouldBeNull();
    }

    // One per pass, for the reason the timers fire one per pass: the client casts one skill
    // at a time. The second press is still wanted and goes a tenth of a second later.
    [Fact]
    public void KeepsTheSecondOfTwoKeysPressedTogether()
    {
        var wanted = Pressed(0, 1);

        HotkeyTask.Take(Macros("加速術/ME", "治癒術/ME"), wanted, Never(), TimeSpan.Zero).ShouldBe(0);

        wanted[1].ShouldBeTrue();
    }

    [Fact]
    public void RunsOnEveryPassBecauseSomebodyIsWaitingOnIt() =>
        HotkeyTask.Keys.Length.ShouldBe(AuxSettings.FunctionKeyMacros);

    private static bool[] Pressed(params int[] rows)
    {
        var wanted = new bool[AuxSettings.FunctionKeyMacros];

        foreach (var row in rows)
        {
            wanted[row] = true;
        }

        return wanted;
    }

    private static TimeSpan?[] Never() => new TimeSpan?[AuxSettings.FunctionKeyMacros];

    private static FunctionKeyMacro[] Macros(params string[] commands)
    {
        var macros = new FunctionKeyMacro[AuxSettings.FunctionKeyMacros];

        for (var i = 0; i < macros.Length; i++)
        {
            macros[i] = new FunctionKeyMacro
            {
                Enabled = true,
                Command = i < commands.Length ? commands[i] : string.Empty,
            };
        }

        return macros;
    }
}
