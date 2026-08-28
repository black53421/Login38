using Login38.Aux.Game;
using Login38.Aux.Hunt;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers when the rotation stops casting and, which is the harder half, when it starts again.
/// </summary>
/// <remarks>
/// Standing down is easy to get right and easy to test. Coming back up is where a guard like
/// this fails in the two ways that matter: it comes back too eagerly and spends a cast per
/// regeneration tick on being told the same thing, or it never comes back at all and the
/// character fights the rest of the evening with its weapon for no reason.
/// </remarks>
public sealed class SkillGuardTests
{
    private static readonly TimeSpan Later = SkillGuard.FirstProbe + TimeSpan.FromSeconds(1);

    private readonly SkillGuard _guard = new(NullLogger<SkillGuardTests>.Instance);

    [Fact]
    public void LeavesTheRotationAloneWhileTheServerIsSayingNothing()
    {
        _guard.Read(null, Look(), TimeSpan.Zero);

        _guard.Silenced.ShouldBeFalse();
        _guard.Probing.ShouldBeFalse();
    }

    [Fact]
    public void StandsTheRotationDownAsSoonAsACastIsRefused()
    {
        _guard.Read(CastRefusal.Weight, Look(weight: 90), TimeSpan.Zero);

        _guard.Silenced.ShouldBeTrue();
        _guard.Why.ShouldBe(CastRefusal.Weight);
    }

    // The reason this exists. Overweight lasts until something is dropped, and a rotation
    // that came back on a timer would spend the whole time being refused all over again.
    [Fact]
    public void StaysDownForAsLongAsTheCharacterIsStillCarryingIt()
    {
        _guard.Read(CastRefusal.Weight, Look(weight: 90), TimeSpan.Zero);
        _guard.Read(null, Look(weight: 90), TimeSpan.FromMinutes(5));

        _guard.Silenced.ShouldBeTrue();
    }

    [Fact]
    public void ComesBackUpWhenTheWeightFalls()
    {
        _guard.Read(CastRefusal.Weight, Look(weight: 90), TimeSpan.Zero);
        _guard.Read(null, Look(weight: 89), TimeSpan.FromSeconds(1));

        _guard.Silenced.ShouldBeFalse();
    }

    // A wall is a property of where the character is standing, and the hunt moves it
    // constantly, so this one clears itself within a step or two.
    [Fact]
    public void ComesBackUpOnceTheCharacterHasMoved()
    {
        _guard.Read(CastRefusal.Blocked, Look(x: 100, y: 200), TimeSpan.Zero);
        _guard.Read(null, Look(x: 100, y: 200), TimeSpan.FromSeconds(1));

        _guard.Silenced.ShouldBeTrue();

        _guard.Read(null, Look(x: 102, y: 200), TimeSpan.FromSeconds(2));

        _guard.Silenced.ShouldBeFalse();
    }

    // The loop this is here to avoid: mana comes back a point at a time, and treating any
    // rise as recovery would let a cast through on every tick to be refused on every tick.
    [Fact]
    public void WaitsForRealRecoveryRatherThanOneRegenerationTick()
    {
        _guard.Read(CastRefusal.Mana, Look(mana: new Gauge(4, 100)), TimeSpan.Zero);
        _guard.Read(null, Look(mana: new Gauge(9, 100)), TimeSpan.FromSeconds(1));

        _guard.Silenced.ShouldBeTrue();

        _guard.Read(null, Look(mana: new Gauge(24, 100)), TimeSpan.FromSeconds(2));

        _guard.Silenced.ShouldBeFalse();
    }

    // Some refusals leave nothing to watch at all. Those get one cast every so often, which
    // is the only way to find out whether whatever it was has passed.
    [Fact]
    public void LetsOneCastThroughWhenThereIsNothingToWatch()
    {
        _guard.Read(CastRefusal.State, Look(), TimeSpan.Zero);
        _guard.Read(null, Look(), TimeSpan.FromSeconds(1));

        _guard.Probing.ShouldBeFalse();

        _guard.Read(null, Look(), Later);

        _guard.Probing.ShouldBeTrue();

        // And still standing down, because one cast is a question and not an answer: the
        // weapon should not drop a swing over it.
        _guard.Silenced.ShouldBeTrue();
    }

    [Fact]
    public void SpendsTheProbeOnTheCastThatGoesOut()
    {
        _guard.Read(CastRefusal.State, Look(), TimeSpan.Zero);
        _guard.Read(null, Look(), Later);
        _guard.Cast(Later);

        _guard.Probing.ShouldBeFalse();
    }

    [Fact]
    public void WaitsTwiceAsLongWhenTheProbeDrawsTheSameAnswer()
    {
        _guard.Read(CastRefusal.State, Look(), TimeSpan.Zero);
        _guard.Read(null, Look(), Later);
        _guard.Cast(Later);
        _guard.Read(CastRefusal.State, Look(), Later);

        // What used to be long enough no longer is.
        _guard.Read(null, Look(), Later + SkillGuard.FirstProbe + TimeSpan.FromSeconds(1));

        _guard.Probing.ShouldBeFalse();

        _guard.Read(null, Look(), Later + (SkillGuard.FirstProbe * 2) + TimeSpan.FromSeconds(1));

        _guard.Probing.ShouldBeTrue();
    }

    [Fact]
    public void NeverWaitsLongerThanTheCap()
    {
        var at = TimeSpan.Zero;

        for (var round = 0; round < 12; round++)
        {
            _guard.Read(CastRefusal.Attribute, Look(), at);
            at += SkillGuard.LongestProbe + TimeSpan.FromSeconds(1);
            _guard.Read(null, Look(), at);
        }

        _guard.Probing.ShouldBeTrue();
    }

    // Whatever the reason was and whatever it was being held against, the server taking
    // payment for a cast is the server casting.
    [Fact]
    public void ComesBackUpWhenACastIsPaidFor()
    {
        _guard.Read(CastRefusal.Reagent, Look(), TimeSpan.Zero);
        _guard.Landed();

        _guard.Silenced.ShouldBeFalse();
    }

    [Fact]
    public void StartsOverForACharacterWhoHasJustWalkedIn()
    {
        _guard.Read(CastRefusal.Weight, Look(weight: 90), TimeSpan.Zero);
        _guard.Reset();

        _guard.Silenced.ShouldBeFalse();
    }

    // A second reason arriving is a different condition, not the same one holding out, so it
    // starts its own wait rather than inheriting one that has already been doubled.
    [Fact]
    public void TreatsADifferentReasonAsANewOne()
    {
        _guard.Read(CastRefusal.State, Look(), TimeSpan.Zero);
        _guard.Read(CastRefusal.State, Look(), TimeSpan.FromSeconds(1));
        _guard.Read(CastRefusal.Reagent, Look(), TimeSpan.FromSeconds(2));

        _guard.Why.ShouldBe(CastRefusal.Reagent);

        _guard.Read(null, Look(), TimeSpan.FromSeconds(2) + Later);

        _guard.Probing.ShouldBeTrue();
    }

    private static CastConditions Look(
        byte weight = 50, Gauge? mana = null, Gauge? health = null, int x = 100, int y = 200) =>
        new(weight, mana ?? new Gauge(100, 100), health ?? new Gauge(100, 100), x, y);
}
