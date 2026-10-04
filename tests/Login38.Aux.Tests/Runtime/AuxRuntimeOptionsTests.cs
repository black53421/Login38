using Login38.Aux.Runtime;
using Login38.Core.Servers;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

public sealed class AuxRuntimeOptionsTests
{
    [Theory]
    [InlineData(50, 100)]
    [InlineData(750, 750)]
    [InlineData(9000, 5000)]
    public void ClampsOperatorFunctionKeyCooldown(uint given, int expected)
    {
        var options = new AuxRuntimeOptions();

        options.Load(new AuxConfig { FunctionKeyCooldownMs = given });

        options.FunctionKeyCooldown.ShouldBe(TimeSpan.FromMilliseconds(expected));
    }
}
