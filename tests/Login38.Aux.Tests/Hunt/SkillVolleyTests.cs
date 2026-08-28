using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Hunt;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers what the hunt throws at what it is already fighting.
/// </summary>
/// <remarks>
/// The three ways this goes wrong all look the same from outside — a character standing
/// next to a monster casting nothing — so each is pinned separately: a name the character
/// has not learned, a skill that is not an attack at all, and one whose range the target is
/// outside of. Two of those are read from the client rather than from the player, which is
/// the whole reason a row only has three fields.
/// </remarks>
public sealed class SkillVolleyTests : IDisposable
{
    private const string Bolt = "energy bolt";

    private const string Blessing = "blessing of eva";


    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();
    private readonly StubSpells _spells = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void CastsTheFirstRowThatIsReady()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row(Bolt))).ShouldBe(Bolt);
        _actions.Casts.ShouldBe([(0x40u, SkillAim.Entity, TargetId)]);
    }

    // Through the client's own casting path, aimed by object id. Sending the packet
    // directly would skip the animation and the client's own cooldown, and the target
    // global would be left pointing at a monster the player did not choose.
    [Fact]
    public void AimsAtWhatTheHuntIsFighting()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row(Bolt)));

        _actions.Casts.Single().Aim.ShouldBe(SkillAim.Entity);
        _actions.Casts.Single().Id.ShouldBe(TargetId);
    }

    [Fact]
    public void LeavesADisabledRowAlone()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row(Bolt, enabled: false))).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    [Fact]
    public void SkipsARowWithNoNameInIt()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row("  "), Row(Bolt))).ShouldBe(Bolt);
    }

    // The client's dispatch table says which skills act on a target, and nothing in a name
    // does. A buff sent at a monster is a buff on the monster.
    [Fact]
    public void RefusesASkillThatDoesNotActOnATarget()
    {
        _spells.Known[Blessing] = new Spell(0x50, NotAnAttack);

        Fire(Settings(Row(Blessing))).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesASkillThisCharacterHasNotLearned()
    {
        Fire(Settings(Row(Bolt))).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    // One distance for everything, and it is the shortest range any non-melee attack skill
    // has. Standing further off is only safe for the skill that happens to reach, and the
    // server drops the rest without a word. The box is the client's own shape — twice as wide
    // as it is tall, because two grid columns make a tile across.
    [Theory]
    [InlineData(10, 0, true)]       // five tiles across
    [InlineData(12, 0, false)]      // six, too far for the shortest of them
    [InlineData(0, 5, true)]
    [InlineData(0, 6, false)]
    public void CastsOnlyFromWhereEverySkillReaches(int dx, int dy, bool expected)
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var fired = Fire(Settings(Row(Bolt)), target: (100 + dx, 200 + dy));

        (fired is not null).ShouldBe(expected);
    }

    // Three satisfies every skill on the list this was written against, but not necessarily
    // every skill on every server. A cast that goes out and costs nothing did not happen, and
    // that is worth learning from — the server takes mana or health for one it accepts and
    // takes nothing for one it drops, silently.
    [Fact]
    public void ClosesFurtherStillWhenACastCostsNothing()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt));
        var full = new Gauge(100, 100);

        Lap(volley, settings, target: (110, 200), mana: full, at: TimeSpan.Zero).ShouldBe(Bolt);

        Lap(volley, settings, target: (110, 200), mana: full, at: TimeSpan.FromSeconds(2))
            .ShouldBeNull();

        volley.Approach.ShouldBe(4);
    }

    [Fact]
    public void LeavesItAloneWhenTheCastWasPaidFor()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt));

        Lap(volley, settings, target: (110, 200), mana: new Gauge(100, 100), at: TimeSpan.Zero);
        Lap(volley, settings, target: (110, 200), mana: new Gauge(90, 100), at: TimeSpan.FromSeconds(2))
            .ShouldBe(Bolt);

        volley.Approach.ShouldBeNull();
    }

    [Fact]
    public void WaitsOutTheIntervalBeforeCastingAgain()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt, interval: 5));

        Lap(volley, settings, at: TimeSpan.Zero).ShouldBe(Bolt);
        Lap(volley, settings, at: TimeSpan.FromSeconds(4)).ShouldBeNull();
        Lap(volley, settings, at: TimeSpan.FromSeconds(5)).ShouldBe(Bolt);
    }

    [Fact]
    public void ForgetsTheIntervalForANewCharacter()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt, interval: 60));

        Lap(volley, settings, at: TimeSpan.Zero).ShouldBe(Bolt);
        volley.Reset();
        Lap(volley, settings, at: TimeSpan.FromSeconds(1)).ShouldBe(Bolt);
    }

    [Fact]
    public void LeavesTheSkillAloneBelowItsManaFloor()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row(Bolt, mana: 40)), mana: new Gauge(30, 100)).ShouldBeNull();
        Fire(Settings(Row(Bolt, mana: 40)), mana: new Gauge(40, 100)).ShouldBe(Bolt);
    }

    // A row that cannot go off must not stop the one below it, or a mana floor on the top
    // row would switch off the whole list.
    [Fact]
    public void ReachesARowBelowOneThatCannotGoOff()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);
        _spells.Known[Blessing] = new Spell(0x50, Attack);

        Fire(Settings(Row(Bolt, mana: 90), Row(Blessing)), mana: new Gauge(50, 100))
            .ShouldBe(Blessing);
    }

    // One row per swing and no more. The client fires one blow per cooldown, and a rotation
    // that walked forward to whatever happened to be ready would be the priority list this
    // replaced, in which the top row goes off every time and the bottom one never does.
    [Fact]
    public void TakesOneRowPerSwing()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);
        _spells.Known[Blessing] = new Spell(0x50, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt, mana: 90), Row(Blessing));
        var poor = new Gauge(50, 100);

        Turn(volley, settings, mana: poor);                          // learns the cooldown
        Turn(volley, settings, mana: poor).ShouldBeNull();           // row one, and too poor
        Turn(volley, settings, mana: poor).ShouldBe(Blessing);
    }

    // The same cooldown twice is one turn. The hunt runs five times a second and a character
    // swings rather less often than that, so most passes are a pass the rotation has already
    // spent — and one stepped per pass is round a six-row list in about a second.
    [Fact]
    public void SpendsNothingOnAPassWithNoNewSwing()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt));

        Turn(volley, settings);

        for (var again = 0; again < 2; again++)
        {
            volley.Fire(
                _process,
                settings,
                Wolf(),
                (100, 200),
                new Gauge(100, 100),
                new Gauge(100, 100),
                casting: false,
                _swing,
                _ => true,
                TimeSpan.Zero).ShouldBeNull();
        }

        _actions.Casts.ShouldBeEmpty();
    }

    // A weapon row casts nothing and is not a wasted row: it takes a turn, which is how a
    // player says "a swing between these two skills". The client is swinging anyway — nothing
    // here starts or stops that — so what the row buys is the gap.
    [Fact]
    public void SpendsATurnOnAWeaponRowWithoutCasting()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Weapon(), Row(Bolt));

        Turn(volley, settings);
        Turn(volley, settings).ShouldBeNull();
        Turn(volley, settings).ShouldBe(Bolt);
        _actions.Casts.ShouldHaveSingleItem();
    }

    // Round and round. A row reached once is a row reached again, which is the whole
    // difference between a rotation and a list read from the top every time.
    [Fact]
    public void ComesBackToTheFirstRowOnTheNextLap()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt), Weapon());

        Turn(volley, settings);
        Turn(volley, settings).ShouldBe(Bolt);
        Turn(volley, settings).ShouldBeNull();
        Turn(volley, settings).ShouldBe(Bolt);
    }

    // A row nobody ticked is not a slow turn, it is not a turn. Spending one on every row in
    // the list is what put a four-second silence between every pair of casts on a character
    // with no weapon setting the pace: two skills went off and then four empty rows went past.
    [Fact]
    public void SkipsStraightPastRowsNobodyTicked()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);
        _spells.Known[Blessing] = new Spell(0x50, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt), Row(Blessing));

        Turn(volley, settings);

        // Straight back round the two that are in it, with the four empty rows never taking
        // a turn between them.
        Turn(volley, settings).ShouldBe(Bolt);
        Turn(volley, settings).ShouldBe(Blessing);
        Turn(volley, settings).ShouldBe(Bolt);
        Turn(volley, settings).ShouldBe(Blessing);
    }

    [Fact]
    public void TakesNoTurnWhenNothingIsTicked()
    {
        var volley = Volley();

        Turn(volley, new HuntSettings());
        Turn(volley, new HuntSettings()).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    // A corner. The rotation takes its turn while the character is still walking round one,
    // and the distance to something on the other side of a wall is short — so a box says yes,
    // the server says nothing at all, and the cast is spent. That is what casting into a wall
    // for the whole of an approach was, and it is why the reach test is the hunt's own rather
    // than a subtraction.
    [Fact]
    public void RefusesASkillWithAWallInTheWay()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row(Bolt)), blocked: true).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    // The client will not walk a character into range of a skill: its spell book path checks
    // the range and then waits for another click. So the hunt has to stand where the rotation
    // can be taken, and that is the tightest range in it rather than the weapon's own.
    [Fact]
    public void SaysHowNearTheRotationNeedsToBe()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt));
        var full = new Gauge(100, 100);

        // Nothing has been refused yet, so nothing is known and the player's own standoff
        // stands — the rotation closes for itself when a row comes up.
        volley.Closest(_process, settings).ShouldBeNull();

        Lap(volley, settings, target: (110, 200), mana: full, at: TimeSpan.Zero);
        Lap(volley, settings, target: (110, 200), mana: full, at: TimeSpan.FromSeconds(2));

        volley.Closest(_process, settings).ShouldBe(4);
    }

    [Fact]
    public void AsksForNothingWhenTheRotationHasNoSkillInIt()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Volley().Closest(_process, Settings(Weapon())).ShouldBeNull();
        Volley().Closest(_process, Settings(Row(Bolt, enabled: false))).ShouldBeNull();
        Volley().Closest(_process, new HuntSettings()).ShouldBeNull();
    }

    // Nothing that could not go off anyway. Dragging a bow into melee for a skill this
    // character never learned is a standoff thrown away for nothing.
    [Fact]
    public void DoesNotCloseInForASkillItCouldNotCast()
    {
        _spells.Known[Blessing] = new Spell(0x50, NotAnAttack);

        Volley().Closest(_process, Settings(Row("never learned"), Row(Blessing))).ShouldBeNull();
    }

    // A weapon row used to be nothing but a gap, because the client's attack chain swings on
    // its own and neither half of this started or stopped it. Unticking every weapon row looks
    // like it should stop the character hitting things, so now it does.
    [Fact]
    public void SwingsOnlyWhenTheRotationAsksForIt()
    {
        SkillVolley.Swings(Settings(Row(Bolt))).ShouldBeFalse();
        SkillVolley.Swings(Settings(Row(Bolt), Weapon())).ShouldBeTrue();
    }

    // Somebody who has never opened the panel has a character that fights. Reading "no rows"
    // as "do not attack" would switch the hunt off for everyone who has not asked for
    // anything, which is a setting nobody made.
    [Fact]
    public void SwingsWhenNobodyHasWrittenARotation()
    {
        SkillVolley.Swings(new HuntSettings()).ShouldBeTrue();
        SkillVolley.Swings(Settings(Row(Bolt, enabled: false))).ShouldBeTrue();
    }

    // From the top for each monster, so the order a player wrote is the order every one of
    // them gets rather than wherever the last fight happened to leave off.
    [Fact]
    public void StartsTheRotationAgainForANewTarget()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Weapon(), Row(Bolt));

        Turn(volley, settings);
        Turn(volley, settings).ShouldBeNull();     // the weapon's turn

        volley.Restart();

        Turn(volley, settings).ShouldBeNull();     // and it is the weapon's turn again
        Turn(volley, settings).ShouldBe(Bolt);
    }

    // The turn only. An interval belongs to the skill rather than to what it was aimed at, and
    // forgetting it here would let a skill on a minute's throttle go off on every monster the
    // hunt walks up to.
    [Fact]
    public void KeepsTheThrottlesThroughARestart()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt, interval: 60));

        Lap(volley, settings, at: TimeSpan.Zero).ShouldBe(Bolt);
        volley.Restart();
        Lap(volley, settings, at: TimeSpan.FromSeconds(1)).ShouldBeNull();
    }

    [Fact]
    public void DoesNothingWhenEveryRowIsEmpty() => Fire(new HuntSettings()).ShouldBeNull();

    // Writing a second cast over a queued one loses both, and leaves the client's mode word
    // saying a cast is in progress — which is a word that is only ever ORed into.
    [Fact]
    public void HoldsOffWhileTheClientAlreadyHasACastQueued()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        Fire(Settings(Row(Bolt)), casting: true).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    /// <summary>A handler byte the client's own table lists as acting on a target.</summary>
    private const byte Attack = 0x02;

    /// <summary>And one it does not.</summary>
    private const byte NotAnAttack = 0x01;

    private const uint TargetId = 0x1234;

    /// <summary>The client's attack cooldown, which only ever goes forward.</summary>
    /// <remarks>
    /// One value per swing, because that is what the rotation counts. Held by the fixture so
    /// every turn in a test is a different number without any test having to say so.
    /// </remarks>
    private uint _swing;

    // Out of range used to be a turn thrown away: the row was skipped and the character
    // carried on standing exactly where it could not cast from, for the whole fight. A weapon
    // gets walked in by the client's own attack chain, and a skill should be no different.
    [Fact]
    public void AsksToBeWalkedInWhenTheRowIsOutOfRange()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt));

        Lap(volley, settings, target: (140, 200), mana: new Gauge(100, 100), at: TimeSpan.Zero)
            .ShouldBeNull();

        volley.Approach.ShouldBe(5);
    }

    [Fact]
    public void StopsAskingOnceSomethingHasGoneOff()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();
        var settings = Settings(Row(Bolt));
        var full = new Gauge(100, 100);

        Lap(volley, settings, target: (140, 200), mana: full, at: TimeSpan.Zero);
        Lap(volley, settings, target: (140, 200), mana: full, at: TimeSpan.FromSeconds(2));
        volley.Approach.ShouldNotBeNull();

        Lap(volley, settings, target: (104, 200), mana: full, at: TimeSpan.FromSeconds(3))
            .ShouldBe(Bolt);

        volley.Approach.ShouldBeNull();
    }

    // Walking does not answer a cooldown, a mana floor or a name the character never learned.
    [Fact]
    public void DoesNotAskToBeWalkedInForAnythingElse()
    {
        _spells.Known[Bolt] = new Spell(0x40, Attack);

        var volley = Volley();

        Lap(volley, Settings(Row(Bolt, mana: 90)), mana: new Gauge(10, 100)).ShouldBeNull();

        volley.Approach.ShouldBeNull();
    }

    private SkillVolley Volley() => new(_actions, _spells, NullLogger<SkillVolley>.Instance);

    /// <summary>One swing, and whatever the rotation did with it.</summary>
    private string? Turn(
        SkillVolley volley,
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        TimeSpan? at = null,
        bool blocked = false,
        Gauge? health = null) =>
        volley.Fire(
            _process,
            settings,
            Wolf(target),
            (100, 200),
            mana ?? new Gauge(100, 100),
            health ?? new Gauge(100, 100),
            casting,
            ++_swing,
            range => !blocked && Box(target, range),
            at ?? TimeSpan.Zero);

    /// <summary>
    /// The distance half of what the hunt asks its collision grid.
    /// </summary>
    /// <remarks>
    /// The client's own shape: a box twice as wide as it is tall, because two grid columns
    /// make a tile across and one row makes one down. The other half — whether a wall is in
    /// the way — is what <c>blocked</c> stands in for, since a collision grid is not something
    /// these tests have or should need.
    /// </remarks>
    private static bool Box((int X, int Y)? target, int range) =>
        Math.Abs((target?.X ?? 100) - 100) <= range * 2
        && Math.Abs((target?.Y ?? 200) - 200) <= range;

    /// <summary>A whole lap of the rotation, and the one thing that came out of it.</summary>
    /// <remarks>
    /// Rows are taken one per swing, so a test asking whether a row <em>can</em> go off has to
    /// let the rotation reach it — and a fresh volley spends its first call learning what the
    /// cooldown reads, which is not a turn. A lap of every row absorbs both without any test
    /// having to count.
    /// </remarks>
    private string? Lap(
        SkillVolley volley,
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        TimeSpan? at = null,
        bool blocked = false,
        Gauge? health = null)
    {
        string? fired = null;

        for (var i = 0; i <= HuntSettings.SkillRows; i++)
        {
            fired ??= Turn(volley, settings, target, mana, casting, at, blocked, health);
        }

        return fired;
    }

    private string? Fire(
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        bool blocked = false) =>
        Lap(Volley(), settings, target: target, mana: mana, casting: casting, blocked: blocked);

    private static HuntTarget Wolf((int X, int Y)? at = null) =>
        new(new GameAddress(0x1000),
            TargetId,
            "wolf",
            at?.X ?? 100,
            at?.Y ?? 200,
            HuntAddresses.HealthUnknown);

    private static HuntSettings Settings(params HuntSkill[] rows)
    {
        var settings = new HuntSettings();

        for (var i = 0; i < rows.Length && i < settings.Skills.Length; i++)
        {
            settings.Skills[i] = rows[i];
        }

        return settings;
    }

    /// <summary>A turn of the weapon, which is a row that casts nothing.</summary>
    private static HuntSkill Weapon() => new() { Enabled = true, Step = HuntStep.Weapon };

    private static HuntSkill Row(
        string name, bool enabled = true, double interval = 0, uint mana = 0) =>
        new() { Enabled = enabled, Name = name, IntervalSeconds = interval, ManaAtLeast = mana };

    /// <summary>Remembers what it was asked for rather than touching a client.</summary>
    private sealed class RecordingActions()
        : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<(uint Packed, SkillAim Aim, uint Id)> _cast = [];

        public IReadOnlyList<(uint Packed, SkillAim Aim, uint Id)> Casts => _cast;

        public override void Cast(
            RemoteProcess process, uint packed, SkillTarget target, GameAddress record = default) =>
            _cast.Add((packed, target.Aim, target.Id));
    }

    /// <summary>A spell book with whatever the test says the character has learned.</summary>
    private sealed class StubSpells() : Spells(LegacyTextCodec.Auto, NullLogger<Spells>.Instance)
    {
        public Dictionary<string, Spell> Known { get; } = new(StringComparer.Ordinal);

        public override Spell? Learn(RemoteProcess process, string name) =>
            Known.TryGetValue(name, out var spell) ? spell : null;
    }
}
