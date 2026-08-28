import io

p = 'D:/C#380login/src/Login38.Aux/Hunt/HuntTask.cs'
s = io.open(p, encoding='utf-8').read()

old = '''    /// <summary>The client's attack cooldown, which it pushes forward on every blow.</summary>
    private static uint Cooldown(RemoteProcess process) =>
        process.TryRead<uint>(HuntAddresses.NextAttackTick, out var tick) ? tick : 0;'''

new = '''    /// <summary>The client's attack cooldown, which it pushes forward on every blow.</summary>
    private static uint Cooldown(RemoteProcess process) =>
        process.TryRead<uint>(HuntAddresses.NextAttackTick, out var tick) ? tick : 0;

    /// <summary>
    /// The number everything here reads as "something happened", which is the weapon's
    /// cooldown until the weapon stops being used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cooldown is the client's own account of a blow being started, and three things are
    /// built on it: the rotation takes a turn per move of it, and the stall and frozen watches
    /// read a move of it as progress. All three are silently wrong for a rotation that never
    /// swings — it does not move at all, so the rotation takes one turn and then none, and the
    /// watches decide within seconds that a character casting steadily is wedged.
    /// </para>
    /// <para>
    /// So the clock changes with the weapon. A second a turn is slow enough to be a rotation
    /// rather than a spray and fast enough that the client's own cast cooldown is what limits
    /// the character rather than this. Nothing is lost by the watches going quiet: what they
    /// are for is a fight that is not happening, and <see cref="Unhittable"/> answers that from
    /// the target's health, which is the server's word and not the client's.
    /// </para>
    /// </remarks>
    private uint Progress(RemoteProcess process) => _swings
        ? Cooldown(process)
        : (uint)(_clock.Elapsed.Ticks / Turning.Ticks);'''
assert s.count(old) == 1, 'progress'
s = s.replace(old, new)

old = '''    /// <summary>How long a change of target has to stand before another one is allowed.</summary>'''
new = '''    /// <summary>How long one turn of a rotation lasts when no weapon is setting the pace.</summary>
    private static readonly TimeSpan Turning = TimeSpan.FromSeconds(1);

    /// <summary>How long a change of target has to stand before another one is allowed.</summary>'''
assert s.count(old) == 1, 'turning'
s = s.replace(old, new)

for old, new in [
    ('''        var frozen = _frozen.Idle(player.X, player.Y, Cooldown(context.Process), _clock.Elapsed);''',
     '''        var frozen = _frozen.Idle(player.X, player.Y, Progress(context.Process), _clock.Elapsed);'''),
    ('''                Cooldown(context.Process),''',
     '''                Progress(context.Process),'''),
    ('''        var idle = _stall.Idle(player.X, player.Y, Cooldown(process), _clock.Elapsed);''',
     '''        var idle = _stall.Idle(player.X, player.Y, Progress(process), _clock.Elapsed);'''),
]:
    assert s.count(old) == 1, old[:40]
    s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
