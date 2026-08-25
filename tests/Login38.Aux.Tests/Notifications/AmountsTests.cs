using System.Globalization;
using Login38.Aux.Notifications;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers writing a number where a player reads it at a glance.
/// </summary>
public sealed class AmountsTests
{
    [Theory]
    [InlineData(0u, "0")]
    [InlineData(99u, "99")]
    [InlineData(100u, "100")]
    [InlineData(999u, "999")]
    [InlineData(1000u, "1,000")]
    [InlineData(6260u, "6,260")]
    [InlineData(1_000_000u, "1,000,000")]
    [InlineData(uint.MaxValue, "4,294,967,295")]
    public void GroupsTheDigitsInThrees(uint amount, string expected) =>
        Amounts.WithCommas(amount).ShouldBe(expected);

    // Drawn over a game rather than in a form, so it reads the same wherever the launcher
    // happens to be running. A culture that groups in fours or separates with a space would
    // otherwise change what is on screen.
    [Fact]
    public void ReadsTheSameWhateverTheMachineIsSetTo()
    {
        var was = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Amounts.WithCommas(1_234_567).ShouldBe("1,234,567");
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
