using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class OwnerCompanionPassThroughPatchTests
{
    private static readonly GameAddress Cave = new(0x0200_0000);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuildsShellcodeForEveryEnabledCombination(bool owner, bool other)
    {
        var code = OwnerCompanionPassThroughPatch.BuildShellcode(Cave, owner, other);

        code.ShouldNotBeEmpty();
        code.Length.ShouldBeLessThan(0x200);
    }

    [Fact]
    public void RejectsAConfigurationThatEnablesNeitherPath() =>
        Should.Throw<ArgumentException>(() =>
            OwnerCompanionPassThroughPatch.BuildShellcode(Cave, false, false));
}
