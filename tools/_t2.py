import io

p = 'D:/C#380login/tests/Login38.Aux.Tests/Hunt/SkillVolleyTests.cs'
s = io.open(p, encoding='utf-8').read()

old = '''    /// <summary>One swing, and whatever the rotation did with it.</summary>
    private string? Turn(
        SkillVolley volley,
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        TimeSpan? at = null) =>
        volley.Fire(
            _process,
            settings,
            Wolf(target),
            (100, 200),
            mana ?? new Gauge(100, 100),
            casting,
            ++_swing,
            at ?? TimeSpan.Zero);'''

new = '''    /// <summary>One swing, and whatever the rotation did with it.</summary>
    private string? Turn(
        SkillVolley volley,
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        TimeSpan? at = null,
        bool blocked = false) =>
        volley.Fire(
            _process,
            settings,
            Wolf(target),
            (100, 200),
            mana ?? new Gauge(100, 100),
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
    /// the way — is what <c>blocked</c> stands in for, since a grid is not something these
    /// tests have or should need.
    /// </remarks>
    private static bool Box((int X, int Y)? target, int range) =>
        Math.Abs((target?.X ?? 100) - 100) <= range * 2
        && Math.Abs((target?.Y ?? 200) - 200) <= range;'''
assert s.count(old) == 1, 'turn'
s = s.replace(old, new)

old = '''    private string? Lap(
        SkillVolley volley,
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        TimeSpan? at = null)
    {
        string? fired = null;

        for (var i = 0; i <= HuntSettings.SkillRows; i++)
        {
            fired ??= Turn(volley, settings, target, mana, casting, at);
        }

        return fired;
    }'''
new = '''    private string? Lap(
        SkillVolley volley,
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        TimeSpan? at = null,
        bool blocked = false)
    {
        string? fired = null;

        for (var i = 0; i <= HuntSettings.SkillRows; i++)
        {
            fired ??= Turn(volley, settings, target, mana, casting, at, blocked);
        }

        return fired;
    }'''
assert s.count(old) == 1, 'lap'
s = s.replace(old, new)

old = '''    private string? Fire(
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false) =>
        Lap(Volley(), settings, target: target, mana: mana, casting: casting);'''
new = '''    private string? Fire(
        HuntSettings settings,
        (int X, int Y)? target = null,
        Gauge? mana = null,
        bool casting = false,
        bool blocked = false) =>
        Lap(Volley(), settings, target: target, mana: mana, casting: casting, blocked: blocked);'''
assert s.count(old) == 1, 'fire'
s = s.replace(old, new)

old = '''        volley.Fire(
            _process,
            settings,
            Wolf(),
            (100, 200),
            new Gauge(100, 100),
            casting: false,
            _swing,
            TimeSpan.Zero).ShouldBeNull();'''
new = '''        volley.Fire(
            _process,
            settings,
            Wolf(),
            (100, 200),
            new Gauge(100, 100),
            casting: false,
            _swing,
            _ => true,
            TimeSpan.Zero).ShouldBeNull();'''
assert s.count(old) == 1, 'no new swing'
s = s.replace(old, new)

old = '''    [Fact]
    public void DoesNothingWhenEveryRowIsEmpty() => Fire(new HuntSettings()).ShouldBeNull();'''
new = '''    // A corner. The rotation takes its turn while the character is still walking round one,
    // and the distance to something on the other side of a wall is short — so the box says
    // yes, the server says nothing at all, and the cast is spent. This is the whole reason
    // the reach test is the hunt's own rather than a subtraction.
    [Fact]
    public void RefusesASkillWithAWallInTheWay()
    {
        _spells.Known[Bolt] = new Spell(0x40, 10, Attack);

        Fire(Settings(Row(Bolt)), blocked: true).ShouldBeNull();
        _actions.Casts.ShouldBeEmpty();
    }

    // The client will not walk a character into range of a skill: its spell book path checks
    // the range and waits for another click. So the hunt has to stand where the rotation can
    // be taken, and that is the tightest range in it rather than the weapon's own.
    [Fact]
    public void SaysHowNearTheRotationNeedsToBe()
    {
        _spells.Known[Bolt] = new Spell(0x40, 3, Attack);
        _spells.Known[Blessing] = new Spell(0x50, 7, Attack);

        Volley().Closest(_process, Settings(Row(Blessing), Row(Bolt))).ShouldBe(3);
    }

    [Fact]
    public void AsksForNothingWhenTheRotationHasNoSkillInIt()
    {
        _spells.Known[Bolt] = new Spell(0x40, 3, Attack);

        Volley().Closest(_process, Settings(Weapon())).ShouldBeNull();
        Volley().Closest(_process, Settings(Row(Bolt, enabled: false))).ShouldBeNull();
        Volley().Closest(_process, new HuntSettings()).ShouldBeNull();
    }

    // Nothing that could not go off anyway. Dragging a bow into melee for a skill this
    // character never learned is a standoff thrown away for nothing.
    [Fact]
    public void DoesNotCloseInForASkillItCouldNotCast()
    {
        _spells.Known[Blessing] = new Spell(0x50, 2, NotAnAttack);

        Volley().Closest(_process, Settings(Row("never learned"), Row(Blessing))).ShouldBeNull();
    }

    [Fact]
    public void DoesNothingWhenEveryRowIsEmpty() => Fire(new HuntSettings()).ShouldBeNull();'''
assert s.count(old) == 1, 'new tests'
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
