import io

# ---------------------------------------------------------------- the wrong test
p = 'D:/C#380login/tests/Login38.Aux.Tests/Hunt/PathFinderTests.cs'
s = io.open(p, encoding='utf-8').read()

old_start = s.index('''    // The engine takes the least bad heading, not only an improving one: it has no way to''')
old_end = s.index('''    // A leg has to be somewhere to go.''')

new = '''    // The engine takes the least bad heading, not only an improving one — it has no way to
    // decline a step — and it still does not get round a wall, because the step after the bad
    // one goes straight back. That is the whole reason routes exist, and it is worth holding
    // as a fact about the client rather than a hope: a stub of wall between the two ends the
    // climb, however short it is and however much open ground is beside it.
    [Fact]
    public void StillDoesNotGetRoundAWallByItself()
    {
        var grid = Open();

        for (var y = 32_748; y <= 32_752; y++)
        {
            Shut(grid, 32_706, y);
            Shut(grid, 32_707, y);
        }

        Search(grid, 32_700, 32_750, 200)
            .Climbs(32_700, 32_750, 32_712, 32_750, 200).ShouldBeFalse();
    }

'''

s = s[:old_start] + new + s[old_end:]
io.open(p, 'w', encoding='utf-8', newline='').write(s)

# ---------------------------------------------------------------- hold the leg
p = 'D:/C#380login/src/Login38.Aux/Hunt/HuntTask.cs'
s = io.open(p, encoding='utf-8').read()

old = '''    /// <summary>When the walk last refused every candidate. See <see cref="Boxed"/>.</summary>'''
if s.count(old):
    raise SystemExit('unexpected boxed field')

old = '''    private HuntTarget? _target;'''
new = '''    /// <summary>
    /// Where the character is being walked to, and when it was chosen.
    /// </summary>
    /// <remarks>
    /// Held rather than worked out again every pass, and that is the whole of the difference
    /// between walking round a wall and shuffling against it. Which square is the furthest one
    /// the client can reach unaided changes as the character moves — most of all near the
    /// corner, which is exactly where it is being sent — so recomputing it five times a second
    /// picks a different destination five times a second and the character is pulled back and
    /// forth across the same two tiles until it happens to escape.
    /// </remarks>
    private (int X, int Y)? _leg;

    /// <inheritdoc cref="_leg"/>
    private TimeSpan _legSince;

    private HuntTarget? _target;'''
assert s.count(old) == 1, 'leg field'
s = s.replace(old, new)

old = '''    /// <summary>How long a change of target has to stand before another one is allowed.</summary>'''
new = '''    /// <summary>
    /// How long a leg may go unreached before it is given up as the wrong one.
    /// </summary>
    /// <remarks>
    /// Long enough to walk one: a leg is at most a screen across and the client covers a tile
    /// in a few hundred milliseconds. Short enough that a leg the character cannot actually
    /// get to — because something moved into the way, or the window scrolled under the
    /// search — costs a few seconds rather than the whole fight.
    /// </remarks>
    private static readonly TimeSpan Legging = TimeSpan.FromSeconds(6);

    /// <summary>How long a change of target has to stand before another one is allowed.</summary>'''
assert s.count(old) == 1, 'legging'
s = s.replace(old, new)

old_start = s.index('''    private bool Steer(
        RemoteProcess process, HuntSettings settings, (int X, int Y) player, HuntTarget target)
    {''')
old_end = s.index('''    /// <summary>Whether the client is holding a cast its own next tick will fire.</summary>''')

new = '''    private bool Steer(
        RemoteProcess process, HuntSettings settings, (int X, int Y) player, HuntTarget target)
    {
        // Still on the way somewhere. Nothing is recomputed and nothing is written: the client
        // is walking, and the one thing that must not happen is a second opinion arriving
        // while it does. A leg is dropped on arrival, on taking too long, or with the target.
        if (_leg is { } going && !RouteWalk.Arrived(player, going))
        {
            if (_clock.Elapsed - _legSince < Legging)
            {
                // Only when the client has stopped of its own accord. One run of the engine is
                // one step, so kicking it while a tick is already queued is the same
                // double-step that made the character move at twice its speed — see
                // AttackChain.Resume, which learned it the same way.
                if (!AttackChain.TickQueued(process))
                {
                    _walk.To(process, going.X, going.Y);
                }

                return true;
            }

            _logger.LogInformation(
                "({X},{Y}) was not reached in {Seconds}s from ({PX},{PY}); working it out again",
                going.X, going.Y, (int)Legging.TotalSeconds, player.X, player.Y);

            _leg = null;
        }

        if (!_search.Search(process, player.X, player.Y, settings.RangeSteps)
            || _search.Route(target, _swing) is not { Count: > 0 } route)
        {
            _leg = null;

            return false;
        }

        var end = route[^1];

        // The client's own engine can finish this. Leave it alone: it steps, animates and
        // sends the packets, and two things steering one character is what this whole design
        // exists to avoid.
        if (_search.Climbs(player.X, player.Y, end.X, end.Y, settings.RangeSteps))
        {
            _leg = null;

            return false;
        }

        // Somewhere to go that the client can get to unaided. Leg refuses squares already
        // stood on, so this is never a kick at where the character is; null means the route's
        // first square is somewhere its climb will not go, which is not expected of
        // neighbours and is treated as "do not steer" rather than guessed at.
        if (_search.Leg(route, player.X, player.Y, settings.RangeSteps) is not { } leg)
        {
            _logger.LogWarning(
                "{Name} is {Steps} steps away and the client's own walk will not take the "
                + "first of them from ({X},{Y}); leaving the walk alone",
                target.Name, route.Count, player.X, player.Y);

            _leg = null;

            return false;
        }

        _logger.LogInformation(
            "{Name} is {Steps} steps away round a corner; walking to ({X},{Y}) from ({PX},{PY})",
            target.Name, route.Count, leg.X, leg.Y, player.X, player.Y);

        _leg = leg;
        _legSince = _clock.Elapsed;

        return _walk.To(process, leg.X, leg.Y);
    }

'''

s = s[:old_start] + new + s[old_end:]

old = '''        _closing = null;
        _seenHealth = null;
        _landed = _clock.Elapsed;'''
new = '''        _closing = null;
        _seenHealth = null;
        _landed = _clock.Elapsed;

        // With the target, because it was a way to that target. Carrying one over is a
        // character walking to where the last fight was.
        _leg = null;'''
assert s.count(old) == 1, 'release'
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('leg is held now')
