using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// Finds what the character can actually get to, by searching rather than by guessing.
/// </summary>
/// <remarks>
/// <para>
/// The client's walk engine hill climbs. It scores the eight headings by how near each leaves
/// it and takes the best one, with no memory and no lookahead, so it cannot go round anything
/// — a wall between it and where it is going is a wall it presses against. Every attempt so
/// far to decide what is worth attacking has been built on replaying that climb, and every
/// one of them has inherited its blindness: a monster one wall away reads as unreachable when
/// there is a door, and a monster behind a wall reads as reachable when it is close enough
/// that the climb never has to take a step.
/// </para>
/// <para>
/// This searches instead. One flood fill from the character covers the whole screen at once,
/// over the client's own step deltas and the client's own corner rule — see
/// <see cref="GridWindow"/> — and what comes back is the truth about the map rather than the
/// truth about one greedy walker.
/// </para>
/// <para>
/// The question it answers is deliberately not "can I get to the monster". It is <em>can I
/// get to a square I could attack it from</em>, which is the one question that means the same
/// thing for both weapons: for a sword that square is the next one over, and for a bow it is
/// anywhere within the weapon's reach with a clear line. That is where the two used to
/// diverge and why ranged behaved nothing like melee — the client stops on the first square
/// inside its reach, so a bow would stop on the near side of a wall and shoot it for ever.
/// </para>
/// </remarks>
internal sealed class PathFinder
{
    /// <summary>Nothing has reached this square.</summary>
    private const int Unreached = -1;

    /// <summary>
    /// What a blocked heading scores, which is the client's own number.
    /// </summary>
    /// <remarks>
    /// It matters that it is a number and not a skip. When every heading is blocked they all
    /// score this, the first wins on the strict comparison, and the client walks into the wall
    /// rather than standing still — so a replay that skipped them would model a client that
    /// stops, and that client is not this one.
    /// </remarks>
    private const int Impassable = 99_999_999;

    private readonly GridWindow _grid;
    private readonly int[] _steps =
        new int[HuntAddresses.GridStride * HuntAddresses.GridRows];

    private readonly Queue<(int X, int Y)> _queue = new();

    /// <summary>One kept per hunt, because both of its buffers are big.</summary>
    /// <remarks>
    /// The distance map is 134KB and the window it searches is 670KB, so a fresh pair every
    /// pass would be four megabytes a second on the large object heap — which is not
    /// collected by compaction and would grow for as long as the session lasted. Everything
    /// either of them holds is overwritten by <see cref="Search"/>, so keeping them costs
    /// nothing in staleness.
    /// </remarks>
    public PathFinder() => _grid = new GridWindow();

    private PathFinder(GridWindow grid) => _grid = grid;

    /// <summary>One over a window somebody else built, for tests.</summary>
    internal static PathFinder Over(GridWindow grid) => new(grid);

    /// <summary>The window this last searched, for a caller that wants to read the map.</summary>
    public GridWindow Grid => _grid;

    /// <summary>
    /// Floods out from where the character is standing.
    /// </summary>
    /// <param name="grid">A snapshot of the client's collision window.</param>
    /// <param name="startX">Where the character is, as the client stores it.</param>
    /// <param name="startY">Where the character is, as the client stores it.</param>
    /// <param name="budget">How many steps out to go before stopping.</param>
    /// <remarks>
    /// Breadth first, so the first time a square is reached is by the fewest steps, and every
    /// step costs one — the client's own eight headings all move it one square whatever the
    /// shape of that square on screen.
    /// </remarks>
    public bool Search(RemoteProcess process, int startX, int startY, int budget)
    {
        ArgumentNullException.ThrowIfNull(process);

        return _grid.Refresh(process) && Flood(startX, startY, budget);
    }

    /// <summary>
    /// The same, with the creatures on the map counted as things to walk round.
    /// </summary>
    /// <param name="crowd">Everything the scan found, the target included.</param>
    /// <param name="wanted">
    /// The one to walk <em>to</em>, whose own square is left open — a search that refuses the
    /// tile the monster is standing on is looking for a way to nowhere, and what it wants is a
    /// square to attack from rather than the monster's own.
    /// </param>
    /// <remarks>
    /// The client's grid is terrain. Its walk engine also refuses to step onto a square another
    /// creature occupies, and nothing in the grid says where they are — so a route straight
    /// through the monster standing beside the target is one the character cannot walk, and
    /// what that looks like from outside is a destination four tiles away, every kick answered,
    /// and five seconds of not moving.
    /// </remarks>
    public bool Search(
        RemoteProcess process,
        int startX,
        int startY,
        int budget,
        IReadOnlyList<HuntTarget> crowd,
        uint wanted)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(crowd);

        if (!_grid.Refresh(process))
        {
            return false;
        }

        foreach (var creature in crowd)
        {
            if (creature.Id != wanted)
            {
                _grid.Crowd(creature.X, creature.Y);
            }
        }

        return Flood(startX, startY, budget);
    }

    /// <summary>
    /// Takes a fresh copy of the client's grid without searching it.
    /// </summary>
    /// <remarks>
    /// For the question that has to be asked before anything else and does not need a search:
    /// can the character attack from where it is already standing. Answering that costs one
    /// copy and a line trace; the flood fill it would otherwise sit behind is thirty thousand
    /// squares of work to establish something about one of them.
    /// </remarks>
    public bool Refresh(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return _grid.Refresh(process);
    }

    /// <summary>
    /// Whether a blow from this square would land on that target.
    /// </summary>
    /// <remarks>
    /// The weapon's box and then the line, which together are the whole of "can I hit it from
    /// here" — the same pair the search applies to every candidate square, asked of one.
    /// </remarks>
    public bool Hits(int x, int y, HuntTarget target, int reach) =>
        Within(x, y, target, reach) && Clear(x, y, target.X, target.Y);

    /// <summary>
    /// Whether the target is inside the reach at all, ignoring anything in the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's own test and the whole of it: <c>InRange_check</c> at <c>0x4F79F0</c> is
    /// <c>|dx| &lt;= range*2 &amp;&amp; |dy| &lt;= range</c>, twice as wide as it is tall
    /// because two grid columns make a tile across. Nothing in the client's cast path asks
    /// about a wall.
    /// </para>
    /// <para>
    /// Kept apart from <see cref="Hits"/> because the line is the wrong question for a cast.
    /// <see cref="Clear"/> traces the client's blocked bit, and that bit belongs to an edge
    /// rather than to a square — <c>TileBlocked</c> only means anything through the corner
    /// tables and a heading. Read raw it is set on better than half the squares around a
    /// character, so a six-tile line hits one most of the time, and whether it does depends on
    /// which way the monster happens to lie. That is exactly the shape of the complaint it
    /// caused: a whole fight with no skill cast, and the next monster along perfectly fine.
    /// </para>
    /// <para>
    /// Being wrong this way costs one refused cast. Being wrong the other way costs every
    /// skill for the length of a fight, which is what it did.
    /// </para>
    /// </remarks>
    public static bool Within(int x, int y, HuntTarget target, int reach) =>
        Math.Abs(x - target.X) <= reach * 2
        && Math.Abs(y - target.Y) <= reach;

    /// <inheritdoc cref="Search"/>
    /// <remarks>The half a test can drive, with the window already in hand.</remarks>
    internal bool Flood(int startX, int startY, int budget)
    {
        Array.Fill(_steps, Unreached);
        _queue.Clear();

        if (!_grid.Holds(startX, startY))
        {
            return false;
        }

        // Not gated on the start square being passable. A character can stand where the grid
        // says nothing may pass — on a boundary the server put it on, or on a cell the window
        // has half recycled — and refusing to search from there is refusing to hunt for as
        // long as it lasts.
        Set(startX, startY, 0);
        _queue.Enqueue((startX, startY));

        while (_queue.Count > 0)
        {
            var (x, y) = _queue.Dequeue();
            var taken = At(x, y);

            if (taken >= budget)
            {
                continue;
            }

            for (var heading = 0; heading < WalkProbe.Headings; heading++)
            {
                if (!_grid.Passable(x, y, heading))
                {
                    continue;
                }

                var (nx, ny) = _grid.Step(x, y, heading);

                // Refused here rather than through the grid's own bits, because those describe
                // leaving a cell and not entering one — see GridWindow.Crowded. A creature is
                // the one obstacle the client's tables cannot be made to express.
                if (_grid.Crowded(nx, ny))
                {
                    continue;
                }

                if (!_grid.Holds(nx, ny) || At(nx, ny) != Unreached)
                {
                    continue;
                }

                Set(nx, ny, taken + 1);
                _queue.Enqueue((nx, ny));
            }
        }

        return true;
    }

    /// <summary>
    /// How many steps to a square this target could be attacked from, or null for none.
    /// </summary>
    /// <param name="target">Where the monster is.</param>
    /// <param name="reach">How far the weapon hits from, in tiles.</param>
    /// <remarks>
    /// Nearest first, so the answer is the number of steps the character would actually take,
    /// and so that the line — which is the expensive half — is traced from the square most
    /// likely to have one.
    /// </remarks>
    public int? Attack(HuntTarget target, int reach) =>
        Stand(target, reach) is { } spot ? Taken(spot.X, spot.Y) : null;

    /// <summary>
    /// The nearest square the character could both get to and attack this one from.
    /// </summary>
    /// <remarks>
    /// Nearest first, so the answer is the number of steps the character would actually take
    /// and so that the line — which is the expensive half — is traced from the square most
    /// likely to have one.
    /// </remarks>
    private (int X, int Y)? Stand(HuntTarget target, int reach)
    {
        var ways = new List<(int Taken, int X, int Y)>();

        for (var y = target.Y - reach; y <= target.Y + reach; y++)
        {
            for (var x = target.X - (reach * 2); x <= target.X + (reach * 2); x++)
            {
                if (!_grid.Holds(x, y))
                {
                    continue;
                }

                var taken = At(x, y);

                if (taken != Unreached)
                {
                    ways.Add((taken, x, y));
                }
            }
        }

        ways.Sort((a, b) => a.Taken.CompareTo(b.Taken));

        // Every one of them, nearest first. There used to be a cap here, back when each of
        // these was a walk over the eight headings; a line is cheap enough that the whole box
        // costs less than the cap used to, and the cap was doing real harm — the squares with
        // a clear shot at a monster against a wall are all on one side of it, so the nearest
        // few dozen are exactly the ones that fail.
        foreach (var (_, x, y) in ways)
        {
            if (Clear(x, y, target.X, target.Y))
            {
                return (x, y);
            }
        }

        return null;
    }

    /// <summary>Any neighbour of this square that the search reached one step sooner.</summary>
    private (int X, int Y)? Back(int x, int y, int taken)
    {
        for (var heading = 0; heading < WalkProbe.Headings; heading++)
        {
            var (px, py) = _grid.Step(x, y, heading);

            // Asked of the square being stepped back to, because that is the direction the
            // character will travel and the grid's rules are about leaving a square rather
            // than about entering one.
            if (!_grid.Holds(px, py) || At(px, py) != taken - 1)
            {
                continue;
            }

            if (_grid.Passable(px, py, Opposite(heading)))
            {
                return (px, py);
            }
        }

        return null;
    }

    /// <summary>The heading that undoes another, which is four round an eight-point compass.</summary>
    private static int Opposite(int heading) => (heading + (WalkProbe.Headings / 2))
        % WalkProbe.Headings;

    /// <summary>
    /// The squares to walk, from where the character stands to somewhere it could attack from.
    /// </summary>
    /// <returns>
    /// The route without its first square, so the first entry is somewhere to go rather than
    /// where the character already is. Empty when it is already in position, null when there
    /// is no way at all.
    /// </returns>
    /// <remarks>
    /// Walked backwards out of the distance map: from the square that answered
    /// <see cref="Attack"/>, step to any neighbour one nearer the start, and repeat. Any such
    /// neighbour will do — they are all on some shortest route, and the client is going to be
    /// steered leg by leg rather than square by square anyway.
    /// </remarks>
    public IReadOnlyList<(int X, int Y)>? Route(HuntTarget target, int reach)
    {
        if (Stand(target, reach) is not { } end)
        {
            return null;
        }

        var route = new List<(int X, int Y)>();
        var x = end.X;
        var y = end.Y;

        for (var taken = At(x, y); taken > 0; taken--)
        {
            route.Add((x, y));

            if (Back(x, y, taken) is not { } previous)
            {
                return null;
            }

            x = previous.X;
            y = previous.Y;
        }

        route.Reverse();

        return route;
    }

    /// <summary>
    /// Whether the client's own walk engine would get from one square to another by itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ComputeStepHeading</c> at <c>0x5A4D60</c>, and faithfully rather than
    /// approximately, because everything this is used for is a decision about what the client
    /// can be trusted with. Two details were wrong in the first version and both of them fail
    /// in the same direction — towards "it cannot get there" — which turns steering off and
    /// hands the character back to the very engine that presses into walls.
    /// </para>
    /// <para>
    /// First, the engine does not require a step to be an improvement. It scores all eight,
    /// blocked ones at <see cref="Impassable"/>, and takes the strict minimum whatever that
    /// minimum is — so it will walk away from where it is going if that is the least bad
    /// heading available, and it wedges by oscillating rather than by standing still. The
    /// budget is what catches that; a "no heading improves" test catches something else.
    /// </para>
    /// <para>
    /// Second, arrival is the engine's own test and not equality. Mode three stops within one
    /// square, and the destination column is masked to even before it is walked at, so exact
    /// equality is a square the engine frequently never lands on — asking for it declares
    /// half the map unreachable.
    /// </para>
    /// </remarks>
    public bool Climbs(int fromX, int fromY, int toX, int toY, int budget)
    {
        var x = fromX;
        var y = fromY;

        for (var taken = 0; taken <= budget; taken++)
        {
            if (Reached(x, y, toX, toY))
            {
                return true;
            }

            if (!Downhill(ref x, ref y, toX, toY))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether leaving the client to chase this target itself would end with a blow landing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Climbs"/> answers "would its walk reach that square", and that is not what
    /// the client is about to be asked to do. Left alone it chases the <em>monster</em> —
    /// <c>ReTarget_fromHover</c> puts the monster's own column and row in <c>g_dest_x</c> and
    /// the engine stops on the weapon's reach — so a route's far end and the client's
    /// destination are different points, arrived at by different paths. Asking about the wrong
    /// one is how a target got handed back to an engine that then walked left and right in
    /// front of a wall: the way round to the firing square was clear, and the way to the
    /// monster went through the wall.
    /// </para>
    /// <para>
    /// Arriving is not the end of it either, because stopping within reach of something is not
    /// the same as being able to hit it — which for a bow is most of the problem. The shot is
    /// traced from wherever the chase would have come to rest.
    /// </para>
    /// </remarks>
    public bool Chases(int fromX, int fromY, HuntTarget target, int reach, int budget)
    {
        // Masked to even, because the re-lock masks the monster's column before walking at it.
        var toX = target.X & ~1;
        var toY = target.Y;
        var x = fromX;
        var y = fromY;

        for (var taken = 0; taken <= budget; taken++)
        {
            // The engine's own stopping test in the chase mode, which is the weapon's reach
            // rather than one square. See ComputeStepHeading, where mode one takes the reach.
            if (Math.Abs(x - toX) <= reach * 2 && Math.Abs(y - toY) <= reach)
            {
                return Hits(x, y, target, reach);
            }

            if (!Downhill(ref x, ref y, toX, toY))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes the step the client's walk engine would take, and says whether it moved at all.
    /// </summary>
    /// <remarks>
    /// A step that lands where it started is a climb with nowhere to go, which the client
    /// answers by standing still for as long as the destination holds. Everything else about
    /// wedging — the two squares walked between for ever in front of a wall — looks like
    /// progress from in here and is caught by the caller running out of budget.
    /// </remarks>
    private bool Downhill(ref int x, ref int y, int toX, int toY)
    {
        var best = int.MaxValue;
        var stepX = x;
        var stepY = y;

        for (var heading = 0; heading < WalkProbe.Headings; heading++)
        {
            var (nx, ny) = _grid.Step(x, y, heading);
            var distance = _grid.Passable(x, y, heading)
                ? GridWindow.Distance(nx, ny, toX, toY)
                : Impassable;

            if (distance < best)
            {
                best = distance;
                stepX = nx;
                stepY = ny;
            }
        }

        if (stepX == x && stepY == y)
        {
            return false;
        }

        x = stepX;
        y = stepY;

        return true;
    }

    /// <summary>
    /// The client's arrival test, which is a box rather than a square.
    /// </summary>
    /// <remarks>
    /// <c>InRangeCheck</c> at range one: two columns across, one row down, because two
    /// columns make a tile. The same test <see cref="RouteWalk.Arrived"/> uses, so the two
    /// agree about when a leg is over.
    /// </remarks>
    private static bool Reached(int x, int y, int toX, int toY) =>
        Math.Abs(x - toX) <= 2 && Math.Abs(y - toY) <= 1;

    /// <summary>
    /// The furthest square of a route the client would walk to in one go.
    /// </summary>
    /// <remarks>
    /// From the far end, because the point is to give the client the longest run it can
    /// manage: a leg per corner rather than a click per square is the difference between
    /// walking round something and shuffling round it. Null when even the first square is
    /// somewhere its climb will not go, which is not expected — consecutive squares of a
    /// route are neighbours — and is treated as "do not go" rather than guessed at.
    /// </remarks>
    public (int X, int Y)? Leg(IReadOnlyList<(int X, int Y)> route, int fromX, int fromY, int budget)
    {
        ArgumentNullException.ThrowIfNull(route);

        for (var i = route.Count - 1; i >= 0; i--)
        {
            // Somewhere already stood on is not somewhere to go. Handing one back is a pass
            // that kicks the client at where it is, achieves nothing, and — because a leg
            // that goes nowhere reads as "no leg" — quietly gives the walk back to the engine
            // that cannot round the corner. Scanning from the far end means this only ever
            // skips the tail of a route that has already been walked.
            if (Reached(fromX, fromY, route[i].X, route[i].Y))
            {
                continue;
            }

            if (Climbs(fromX, fromY, route[i].X, route[i].Y, budget))
            {
                return route[i];
            }
        }

        return null;
    }

    /// <summary>How many steps this square took to reach, for a tool to draw.</summary>
    public int? Taken(int x, int y)
    {
        if (!_grid.Holds(x, y))
        {
            return null;
        }

        var taken = At(x, y);

        return taken == Unreached ? null : taken;
    }

    /// <summary>
    /// Whether the line between two squares is clear of anything solid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A line, drawn as a line. The first version of this was a greedy walk over the eight
    /// headings that had to be <see cref="GridWindow.Passable"/> and had to get strictly
    /// nearer on every step, and both halves of that are wrong for a blow. Passability is
    /// about <em>walking</em> out of a square, and the corner rule it applies refuses exactly
    /// the diagonals that hug a wall; strict improvement refuses a step the quantised distance
    /// happens to score flat. The two together made a monster standing against a wall
    /// unhittable from almost everywhere, which is what "it stops near walls and will not go"
    /// was: nowhere passed, so there was no square to stand on, so there was no route, so the
    /// target went back to a client whose own climb then pressed into the wall.
    /// </para>
    /// <para>
    /// Bresenham over cells, testing the blocked bit and nothing else. Non-supercover on
    /// purpose: where the line clips a corner exactly it passes, which is the permissive
    /// reading, and permissive is the right way to be wrong here — the cost of allowing a shot
    /// the server then refuses is one swing and the Unhittable watch, and the cost of refusing
    /// a good one is a target abandoned and a character stood still.
    /// </para>
    /// <para>
    /// Both ends are excluded. Whatever bit the character's own square carries it can still
    /// swing, and whatever bit the monster's carries it is standing there.
    /// </para>
    /// </remarks>
    private bool Clear(int fromX, int fromY, int toX, int toY)
    {
        if (fromX == toX && fromY == toY)
        {
            return true;
        }

        var dx = Math.Abs(toX - fromX);
        var dy = Math.Abs(toY - fromY);
        var sx = fromX < toX ? 1 : -1;
        var sy = fromY < toY ? 1 : -1;
        var error = dx - dy;
        var x = fromX;
        var y = fromY;

        while (true)
        {
            var doubled = error * 2;

            if (doubled > -dy)
            {
                error -= dy;
                x += sx;
            }

            if (doubled < dx)
            {
                error += dx;
                y += sy;
            }

            if (x == toX && y == toY)
            {
                return true;
            }

            if (_grid.Blocked(x, y))
            {
                return false;
            }
        }
    }

    private int At(int x, int y) => _steps[Index(x, y)];

    private void Set(int x, int y, int taken) => _steps[Index(x, y)] = taken;

    private int Index(int x, int y) =>
        ((y - _grid.OriginY) * HuntAddresses.GridStride) + (x - _grid.OriginX);
}
