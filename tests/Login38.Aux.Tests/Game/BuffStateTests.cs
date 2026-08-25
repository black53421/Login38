using Login38.Aux.Game;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers reading what the character is currently under the effect of.
/// </summary>
/// <remarks>
/// This is what stops the buff list recasting things that are already up. Reading it wrong
/// in one direction leaves a character unbuffed; in the other it has them casting the same
/// spell for ever and getting an error back each time.
/// </remarks>
public sealed class BuffStateTests : IDisposable
{
    private const byte Knight = 2;
    private const byte Wizard = 3;
    private const byte Elf = 4;
    private const byte Illusionist = 6;
    private const byte Prince = 0;

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    [Fact]
    public void SaysNothingIsUpWhenTheTableIsEmpty() =>
        Table(Prince).Active(37).ShouldBeFalse();

    [Fact]
    public void SeesAnEffectInTheFirstHalf() =>
        Table(Prince, (37, 1)).Active(37).ShouldBeTrue();

    // Two halves, one directly after the other, and an effect showing in either counts.
    [Fact]
    public void SeesAnEffectInTheSecondHalf() =>
        Table(Prince, (BuffState.Half + 37, 1)).Active(37).ShouldBeTrue();

    // The client gates about ten of its effects on the character's class and writes a
    // different byte for each. A player's settings hold the ordinary number, so without
    // this a knight's haste never reads as up and the helper recasts it for ever.
    [Theory]
    [InlineData(Knight, 37, 24)]
    [InlineData(Wizard, 37, 42)]
    [InlineData(Elf, 14, 31)]
    [InlineData(Elf, 15, 30)]
    [InlineData(Elf, 16, 32)]
    [InlineData(Elf, 17, 29)]
    [InlineData(Elf, 140, 121)]
    [InlineData(Illusionist, 16, 116)]
    public void LooksInTheByteTheClassActuallyUses(byte characterClass, int settings, int actual)
    {
        Table(characterClass, (actual, 1)).Active(settings).ShouldBeTrue();
        Table(characterClass, (settings, 1)).Active(settings).ShouldBeFalse();
    }

    [Fact]
    public void LeavesEverybodyElsesNumbersAlone() =>
        Table(Prince, (37, 1)).Active(37).ShouldBeTrue();

    // Most effects are the same number for everyone, and remapping one that is not on the
    // list would look in the wrong place for every class.
    [Theory]
    [InlineData(Wizard, 44)]
    [InlineData(Knight, 10)]
    [InlineData(Elf, 37)]
    public void PassesOverAnEffectThatIsNotClassSpecific(byte characterClass, int stateId) =>
        BuffState.Remap(characterClass, stateId).ShouldBe(stateId);

    [Theory]
    [InlineData(-1)]
    [InlineData(BuffState.Half)]
    [InlineData(9999)]
    public void SaysNoToANumberOutsideTheTable(int stateId) =>
        Table(Prince).Active(stateId).ShouldBeFalse();

    // Used for the transform flag, which is the client's own numbering rather than a
    // player's, and must not go through the class remapping.
    [Fact]
    public void ReadsAByteOutrightWithoutRemappingIt() =>
        Table(Wizard, (37, 1)).At(37).ShouldBeTrue();

    // Against a process that is not the game. Whether that address happens to be mapped in
    // a test host is not something a test can arrange — and the answer changes between
    // runs — so what this covers is that either answer comes back rather than an exception
    // through the middle of a pass.
    [Fact]
    public void ComesBackWithAnAnswerRatherThanFailing() =>
        Should.NotThrow(() => BuffState.Read(_process));

    [Fact]
    public void SaysTheCharacterIsNotPoisonedWhereThereIsNoPlayer() =>
        Poison.IsDamaging(_process).ShouldBeFalse();

    private static BuffState Table(byte characterClass, params (int Index, byte Value)[] set)
    {
        var bytes = new byte[BuffState.Half * 2];

        foreach (var (index, value) in set)
        {
            bytes[index] = value;
        }

        return BuffState.From(bytes, characterClass);
    }
}
