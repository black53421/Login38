using System.Text;
using Login38.Core.Servers;
using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class InventoryLimitPatchTests
{
    /// <summary><c>"%d / 180\0"</c>, as the client stores it.</summary>
    private static readonly byte[] FormatString = Encoding.ASCII.GetBytes("%d / 180\0");

    private const int At = 0x120;

    private static InventoryLimitPatch Patch() => new(NullLogger<InventoryLimitPatch>.Instance);

    private static string ReadFormat(SyntheticClient client) =>
        Encoding.ASCII.GetString(client.Read(At, FormatString.Length));

    [Fact]
    public void RewritesTheNumberInTheFormatString()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(At, FormatString);

        Patch().Apply(client.NewContext(new AuxConfig { InventoryLimitValue = 255 }));

        ReadFormat(client).ShouldBe("%d / 255\0");
    }

    // The number is written in place, so a shorter one has to terminate the string where
    // it ends rather than leave a digit of the old value behind.
    [Fact]
    public void TerminatesTheStringWhenTheNewNumberIsShorter()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(At, FormatString);

        Patch().Apply(client.NewContext(new AuxConfig { InventoryLimitValue = 99 }));

        ReadFormat(client).ShouldBe("%d / 99\0\0");
    }

    // Three characters is all the space there is; a wider number would run into whatever
    // string the linker put next.
    [Theory]
    [InlineData(5000u, "999\0")]
    [InlineData(0u, "1\0\0\0")]
    public void ClampsToWhatFits(uint configured, string expected)
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(At, FormatString);

        Patch().Apply(client.NewContext(new AuxConfig { InventoryLimitValue = configured }));

        ReadFormat(client).ShouldBe($"%d / {expected}");
    }

    [Fact]
    public void RunningTwiceIsHarmless()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(At, FormatString);

        Patch().Apply(client.NewContext(new AuxConfig { InventoryLimitValue = 255 }));
        Patch().Apply(client.NewContext(new AuxConfig { InventoryLimitValue = 255 }));

        ReadFormat(client).ShouldBe("%d / 255\0");
    }

    [Fact]
    public void FailsWhenTheFormatStringIsNotThere()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var context = client.NewContext();

        Should.Throw<GameProcessException>(() => Patch().Apply(context));
    }

    [Fact]
    public void FollowsTheConfiguredSwitch()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();

        Patch().ShouldSatisfyAllConditions(
            p => p.ShouldApply(client.NewContext(new AuxConfig { InventoryLimitEnabled = true })).ShouldBeTrue(),
            p => p.ShouldApply(client.NewContext(new AuxConfig { InventoryLimitEnabled = false })).ShouldBeFalse());
    }
}
