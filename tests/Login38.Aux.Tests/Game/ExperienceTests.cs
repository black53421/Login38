using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers the experience arithmetic.
/// </summary>
/// <remarks>
/// The reference got this wrong twice before working out that the level has to be derived
/// from the total rather than read: the byte that looks like the level is the level the
/// character started as and does not move when they level up. The cases below include the
/// two readings from a real client that found it.
/// </remarks>
public sealed class ExperienceTests
{
    [Theory]
    [InlineData(1u, 0ul)]
    [InlineData(10u, 10_000ul)]
    [InlineData(11u, 14_641ul)]
    [InlineData(64u, 364_575_461ul)]
    [InlineData(65u, 400_640_553ul)]
    public void KnowsWhereEachLevelStarts(uint level, ulong start) =>
        Experience.StartOf(level).ShouldBe(start);

    // Past sixty-five the table runs out and the levels are evenly spaced.
    [Theory]
    [InlineData(66u, 400_640_553ul + 36_065_092ul)]
    [InlineData(67u, 400_640_553ul + (2ul * 36_065_092ul))]
    public void CarriesOnEvenlyPastTheEndOfTheTable(uint level, ulong start) =>
        Experience.StartOf(level).ShouldBe(start);

    [Theory]
    [InlineData(1u, 125ul)]
    [InlineData(10u, 4_641ul)]
    [InlineData(70u, 36_065_092ul)]
    public void KnowsWhatAWholeLevelIsWorth(uint level, ulong range) =>
        Experience.RangeOf(level).ShouldBe(range);

    [Theory]
    [InlineData(0ul, 1u)]
    [InlineData(124ul, 1u)]
    [InlineData(125ul, 2u)]
    [InlineData(9_999ul, 9u)]
    [InlineData(10_000ul, 10u)]
    [InlineData(14_640ul, 10u)]
    [InlineData(14_641ul, 11u)]
    [InlineData(400_640_553ul, 65u)]
    [InlineData(400_640_553ul + 36_065_092ul, 66u)]
    public void WorksOutTheLevelFromTheTotal(ulong total, uint level) =>
        Experience.LevelOf(total).ShouldBe(level);

    // From a real client: level 10 at a total of 10,202 shows 4.35%, and 31 more takes it
    // to five.
    [Fact]
    public void MatchesWhatTheGameItselfShows()
    {
        var within = 10_202ul - Experience.StartOf(10);

        within.ShouldBe(202ul);
        Experience.RangeOf(10).ShouldBe(4_641ul);
        (within * 100 / Experience.RangeOf(10)).ShouldBe(4ul);
        Experience.ToNextPercent(within, Experience.RangeOf(10)).ShouldBe(31ul);
    }

    [Fact]
    public void HasNothingToSayAboutAPercentOfNothing() =>
        Experience.ToNextPercent(0, 0).ShouldBe(0ul);

    [Fact]
    public void SaysNothingUntilSomethingHasChanged()
    {
        var watch = new ExperienceWatch();

        watch.Start(10_202);

        watch.Look(10_202).ShouldBeNull();
    }

    [Fact]
    public void SaysNothingWhileItIsNotWatching() =>
        new ExperienceWatch().Look(10_202).ShouldBeNull();

    [Fact]
    public void ReportsWhatAKillWasWorth()
    {
        var watch = new ExperienceWatch();

        watch.Start(10_202);

        var report = watch.Look(10_277);

        report?.Gained.ShouldBe(75ul);
        report?.Session.ShouldBe(75ul);
        report?.Level.ShouldBe(10u);
        report?.LevelledUp.ShouldBeFalse();
    }

    [Fact]
    public void AddsUpTheWholeSession()
    {
        var watch = new ExperienceWatch();

        watch.Start(10_000);

        watch.Look(10_075)?.Session.ShouldBe(75ul);
        watch.Look(10_175)?.Session.ShouldBe(175ul);
        watch.Look(10_175).ShouldBeNull();
    }

    // The bug the derivation was written for. A character at 18,141 is level 11; reading the
    // level out of the client gives 10, and "how much to the next level" comes out as zero
    // for the rest of the evening.
    [Fact]
    public void KeepsCountingAfterALevelHasAlreadyBeenPassed()
    {
        var watch = new ExperienceWatch();

        watch.Start(17_454);

        var report = watch.Look(18_141);

        report?.Level.ShouldBe(11u);
        report?.ToLevel.ShouldBe(Experience.RangeOf(11) - (18_141 - Experience.StartOf(11)));
        report?.ToLevel.ShouldBeGreaterThan(0ul);
    }

    // Level eleven starts at 14,641, so this kill is the one that does it.
    [Fact]
    public void NoticesTheKillThatLevelsThemUp()
    {
        var watch = new ExperienceWatch();

        watch.Start(14_500);

        var report = watch.Look(14_700);

        report?.LevelledUp.ShouldBeTrue();
        report?.Level.ShouldBe(11u);
        report?.ToLevel.ShouldBe(Experience.RangeOf(11) - (14_700 - 14_641));
    }

    [Fact]
    public void ForgetsTheSessionWhenItStops()
    {
        var watch = new ExperienceWatch();

        watch.Start(10_000);
        watch.Stop();

        watch.Watching.ShouldBeFalse();
        watch.Look(10_075).ShouldBeNull();
    }

    // Four numbers and a colour mark, which is the whole line.
    [Fact]
    public void WritesTheLineTheReferenceWrote() =>
        ExperienceTask.Line(new ExperienceReport(11, 75, 31, 6_036, 175, false))
            .ShouldBe("\\F275 / 31 / 6036 / 175");
}
