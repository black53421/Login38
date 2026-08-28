using Login38.Aux.Hunt;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers deciding what the character can actually get to and hit.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of target selection now, and it replaced three earlier answers that were
/// all the same answer wearing different clothes: replays of the client's own walk engine.
/// That engine hill climbs — eight headings, best score, no memory, no lookahead — so it
/// cannot go round anything, and asking it "would you get there" is not asking "is there a
/// way". Every version of the hunt built on it either refused monsters it could have walked to
/// or accepted monsters behind walls, and usually both in the same session.
/// </para>
/// <para>
/// The tables below are read from a running client, not invented. The step deltas come from
/// <c>DAT_00ACCDF8</c>, which is all <c>StepByHeading</c> is, and the four corner tables from
/// <c>0x965F88</c> onwards, which is all <c>TilePassable</c> is. Using the real ones is the
/// point: what is being asserted is that this applies the client's rule the way the client
/// applies it, and a tidy invented rule would assert nothing.
/// </para>
/// <para>
/// Note that two columns make a tile across and one makes a row down, so every map here is
/// drawn two characters wide per tile and every step across is two.
/// </para>
/// </remarks>
public sealed class PathFinderTests
{
    /// <summary>The eight, as <c>DAT_00ACCDF8</c> holds them.</summary>
    private static readonly (int X, int Y)[] Steps =
        [(0, -1), (2, -1), (2, 0), (2, 1), (0, 1), (-2, 1), (-2, 0), (-2, -1)];

    /// <summary>
    /// The four <c>TilePassable</c> indexes, as cell offsets.
    /// </summary>
    /// <remarks>
    /// Each is <c>dy * 0x100 + dx</c>. Straight headings read the last table alone; diagonals
    /// pass when either cell of either pair does, which is much looser than "both corners
    /// clear" and is the part that cannot be guessed.
    /// </remarks>
    private static readonly int[][] Corners =
    [
        [0, -255, 0, 258, 0, 255, 0, -2],
        [0, 2, 0, 257, 0, 254, 0, 0],
        [0, 1, 0, 256, 0, -1, 0, -257],
        [0, 0, 1, 1, 256, 256, -1, -1],
    ];

    private const int OriginX = 32_640;
    private const int OriginY = 32_704;

    // Open ground, so the search has nothing to work round and the answer is the straight
    // count. Five tiles across is ten columns, and each step across covers two of them.
    [Fact]
    public void CountsTheStepsAcrossOpenGround()
    {
        var grid = Open();
        var search = Search(grid, 32_700, 32_750, 200);

        search.Taken(32_710, 32_750).ShouldBe(5);
    }

    [Fact]
    public void SaysNothingAboutASquareOutsideTheWindow() =>
        Search(Open(), 32_700, 32_750, 200).Taken(40_000, 40_000).ShouldBeNull();

    // The one the client's own walk engine cannot do, and the reason this exists. A wall with
    // a gap in it is a wall the character walks round; a hill climb presses against it until
    // something notices and gives up on a perfectly good monster.
    [Fact]
    public void GoesRoundAWallThroughTheGapInIt()
    {
        var grid = Open();

        // A wall across every row of the search but one.
        for (var y = OriginY; y < OriginY + 120; y++)
        {
            if (y != 32_760)
            {
                Shut(grid, 32_712, y);
                Shut(grid, 32_713, y);
            }
        }

        var search = Search(grid, 32_700, 32_750, 400);

        search.Taken(32_720, 32_750).ShouldNotBeNull();
    }

    // And the other half: a wall with no gap is a wall, however near the thing behind it is.
    [Fact]
    public void RefusesWhatIsWalledOffCompletely()
    {
        var grid = Open();

        for (var y = OriginY; y < OriginY + HuntAddresses.GridRows; y++)
        {
            Shut(grid, 32_712, y);
            Shut(grid, 32_713, y);
        }

        Search(grid, 32_700, 32_750, 400).Taken(32_720, 32_750).ShouldBeNull();
    }

    // The question the hunt actually asks, and the one that means the same thing for both
    // weapons: not "can I get to it" but "can I get to a square I could hit it from".
    [Fact]
    public void AnswersWithTheStepsToASquareItCouldAttackFrom()
    {
        var search = Search(Open(), 32_700, 32_750, 200);

        // Ten tiles off with a bow that reaches eight: two tiles of walking, and no more.
        search.Attack(Monster(32_720, 32_750), 8).ShouldBe(2);
    }

    [Fact]
    public void CostsNothingForOneAlreadyInReach() =>
        Search(Open(), 32_700, 32_750, 200)
            .Attack(Monster(32_708, 32_750), 8).ShouldBe(0);

    // The case that had a bow standing still and shooting stone. The client stops on the
    // first square inside the weapon's reach, so a wall it could see over but not shoot
    // through is a wall it never walks round: it thinks it has arrived.
    [Fact]
    public void RefusesAShotThroughAWallEvenFromInsideTheWeaponsReach()
    {
        var grid = Open();

        for (var y = OriginY; y < OriginY + HuntAddresses.GridRows; y++)
        {
            Shut(grid, 32_712, y);
            Shut(grid, 32_713, y);
        }

        var search = Search(grid, 32_700, 32_750, 400);

        // Six tiles away and well inside a bow's reach, and there is no way to shoot it and
        // no way to walk to it either.
        search.Attack(Monster(32_720, 32_750), 8).ShouldBeNull();
    }

    // Melee is the same rule and the wall is the same wall. Nothing about this is a special
    // case for one weapon, which is the whole point of asking it this way.
    [Fact]
    public void RefusesTheSameWallForMelee()
    {
        var grid = Open();

        for (var y = OriginY; y < OriginY + HuntAddresses.GridRows; y++)
        {
            Shut(grid, 32_712, y);
            Shut(grid, 32_713, y);
        }

        Search(grid, 32_700, 32_750, 400)
            .Attack(Monster(32_720, 32_750), 2).ShouldBeNull();
    }

    // A budget is how far it is worth walking, not a claim about the map.
    [Fact]
    public void StopsLookingAtTheStepBudget()
    {
        var search = Search(Open(), 32_700, 32_750, 3);

        search.Taken(32_706, 32_750).ShouldBe(3);
        search.Taken(32_740, 32_750).ShouldBeNull();
    }

    // The character can be standing on a cell every heading is refused out of — the server
    // puts characters on boundaries, and the window recycles blocks underneath them. Refusing
    // to search from there would be refusing to hunt for as long as it lasted.
    [Fact]
    public void SearchesFromASquareTheGridCallsShut()
    {
        var grid = Open();

        Shut(grid, 32_700, 32_750);

        Search(grid, 32_700, 32_750, 200).Taken(32_710, 32_750).ShouldNotBeNull();
    }

    // What the client's own engine will and will not do, which is a different question from
    // whether a way exists and has to stay one. It hill climbs, so a wall between it and the
    // destination is a wall it presses into.
    [Fact]
    public void SaysTheClientsOwnClimbCrossesOpenGround() =>
        Search(Open(), 32_700, 32_750, 200)
            .Climbs(32_700, 32_750, 32_720, 32_750, 200).ShouldBeTrue();

    [Fact]
    public void SaysTheClientsOwnClimbWillNotRoundACorner()
    {
        var grid = Walled();

        Search(grid, 32_700, 32_750, 400)
            .Climbs(32_700, 32_750, 32_720, 32_750, 400).ShouldBeFalse();
    }

    // The route exists even though the climb refuses it, and that gap is the whole reason
    // there is a route at all: the search finds the way and the client is walked along it.
    [Fact]
    public void FindsARouteRoundAWallTheClimbRefuses()
    {
        var route = Search(Walled(), 32_700, 32_750, 400).Route(Monster(32_720, 32_750), 2);

        route.ShouldNotBeNull();
        route.Count.ShouldBeGreaterThan(0);

        // It goes through the gap rather than through the wall.
        route.ShouldContain(step => step.Y == 32_760);
    }

    // Every square of a route is a neighbour of the last, so the route is walkable by
    // definition; what matters is that it starts beside the character rather than on it.
    [Fact]
    public void StartsTheRouteSomewhereToGoRatherThanWhereItAlreadyIs()
    {
        var route = Search(Open(), 32_700, 32_750, 200).Route(Monster(32_720, 32_750), 2);

        route.ShouldNotBeNull();
        route[0].ShouldNotBe((32_700, 32_750));
    }

    [Fact]
    public void HasNothingToWalkForOneAlreadyInReach() =>
        Search(Open(), 32_700, 32_750, 200)
            .Route(Monster(32_704, 32_750), 8).ShouldBeEmpty();

    [Fact]
    public void HasNoRouteToWhatIsWalledOff() =>
        Search(Sealed(), 32_700, 32_750, 400)
            .Route(Monster(32_720, 32_750), 2).ShouldBeNull();

    // On open ground the whole route is one leg, because that is the longest run the client
    // can manage on its own — a click per square would be a character shuffling where it
    // should be walking.
    [Fact]
    public void HandsTheClientTheWholeRouteWhenItCanWalkIt()
    {
        var search = Search(Open(), 32_700, 32_750, 200);
        var route = search.Route(Monster(32_720, 32_750), 2);

        search.Leg(route!, 32_700, 32_750, 200).ShouldBe(route![^1]);
    }

    // And at a corner it is as far as the corner. The next leg starts from round it, so the
    // character crosses the whole thing without ever being asked for something its engine
    // cannot do.
    [Fact]
    public void StopsALegWhereTheClimbWouldStop()
    {
        var search = Search(Walled(), 32_700, 32_750, 400);
        var route = search.Route(Monster(32_720, 32_750), 2);
        var leg = search.Leg(route!, 32_700, 32_750, 400);

        leg.ShouldNotBeNull();
        leg.ShouldNotBe(route![^1]);
        search.Climbs(32_700, 32_750, leg.Value.X, leg.Value.Y, 400).ShouldBeTrue();
    }

    // Arrival is the engine's own box and not equality. Steps are two columns at a time and
    // the destination column is masked to even before it is walked at, so a square of the
    // other parity is one the client lands beside and never on — and demanding equality
    // declares it unreachable, which switches the steering off and hands the character back
    // to the engine that presses into walls.
    [Fact]
    public void ArrivesBesideASquareItCanNeverLandOn() =>
        Search(Open(), 32_700, 32_750, 200)
            .Climbs(32_700, 32_750, 32_711, 32_750, 200).ShouldBeTrue();

    // The engine takes the least bad heading, not only an improving one — it has no way to
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

    // A leg has to be somewhere to go. Handing back a square already stood on kicks the
    // client at where it is, achieves nothing, and — because a leg that goes nowhere reads as
    // no leg at all — quietly gives the walk back to the client's own engine.
    [Fact]
    public void NeverOffersALegAlreadyStoodOn()
    {
        var search = Search(Open(), 32_700, 32_750, 200);
        var route = search.Route(Monster(32_730, 32_750), 2);

        route.ShouldNotBeNull();

        // Standing on the third square of the route, as the character would be part way along.
        var (atX, atY) = route[2];
        var leg = search.Leg(route, atX, atY, 200);

        leg.ShouldNotBeNull();
        RouteWalk.Arrived((atX, atY), leg.Value).ShouldBeFalse();
    }

    // Asked before any walking, every pass. A leg is a commitment of several seconds, and a
    // target that becomes shootable half way along one has to be shot at then rather than
    // when the walk happens to finish — otherwise a bow rounds a corner with a clear shot and
    // carries on walking, and a bow already in range waits out a walk it does not need.
    [Fact]
    public void HitsWhatIsInReachWithAClearLine() =>
        Search(Open(), 32_700, 32_750, 200)
            .Hits(32_700, 32_750, Monster(32_712, 32_750), 8).ShouldBeTrue();

    [Fact]
    public void DoesNotHitWhatIsOutOfReach() =>
        Search(Open(), 32_700, 32_750, 200)
            .Hits(32_700, 32_750, Monster(32_740, 32_750), 8).ShouldBeFalse();

    // In range and behind a wall is the case the whole line trace exists for: the client
    // would stop here and shoot stone, because its own range check has no opinion about what
    // is in between.
    [Fact]
    public void DoesNotHitThroughAWall() =>
        Search(Sealed(), 32_700, 32_750, 400)
            .Hits(32_700, 32_750, Monster(32_720, 32_750), 8).ShouldBeFalse();

    // What a cast is measured against, which is the box and nothing else. Same wall, same
    // squares, and the answer has to be the other one: a rotation held back by the line trace
    // spent whole fights casting nothing, because the blocked bit is set on better than half
    // the squares around a character and whether a line clips one is a matter of which way
    // the monster lies.
    [Fact]
    public void CountsWhatIsInRangeThroughAWall()
    {
        // Eight tiles across, so inside a bow's box, and with the wall between.
        var behind = Monster(32_716, 32_750);

        Search(Sealed(), 32_700, 32_750, 400).Hits(32_700, 32_750, behind, 8).ShouldBeFalse();
        PathFinder.Within(32_700, 32_750, behind, 8).ShouldBeTrue();
    }

    // The box is the client's own, twice as wide as it is tall.
    [Theory]
    [InlineData(16, 0, true)]
    [InlineData(17, 0, false)]
    [InlineData(0, 8, true)]
    [InlineData(0, 9, false)]
    public void KeepsToTheClientsOwnBox(int dx, int dy, bool expected) =>
        PathFinder.Within(32_700, 32_750, Monster(32_700 + dx, 32_750 + dy), 8)
            .ShouldBe(expected);

    /// <summary>A wall across the search with one gap in it.</summary>
    // The one that had the character stopping wherever a monster stood near a wall. A blow is
    // not a walk: the corner rule that refuses a diagonal is about leaving a square on foot,
    // and applying it to a line refuses every shot that hugs a wall — which, for something
    // standing against one, is all of them. No square passed, so there was no square to stand
    // on, so there was no route, so the target went back to a client whose climb then pressed
    // into the wall and stayed there.
    [Fact]
    public void HitsAMonsterStandingHardAgainstAWall()
    {
        var search = Search(Sealed(), 32_700, 32_750, 400);

        search.Hits(32_704, 32_750, Monster(32_710, 32_750), 8).ShouldBeTrue();
        search.Attack(Monster(32_710, 32_750), 2).ShouldNotBeNull();
    }

    // Straight along the face of one, with the wall beside every square of the line.
    [Fact]
    public void HitsAlongTheFaceOfAWall() =>
        Search(Sealed(), 32_700, 32_750, 400)
            .Hits(32_710, 32_744, Monster(32_710, 32_752), 8).ShouldBeTrue();

    // Open ground, and the client is better at this than anything here is: it steps, it
    // animates, it sends the packets. Nothing worth taking off it.
    [Fact]
    public void LeavesTheClientToChaseWhatIsInFrontOfIt() =>
        Search(Open(), 32_700, 32_750, 200)
            .Chases(32_700, 32_750, Monster(32_720, 32_750), 2, 200).ShouldBeTrue();

    // Standing within reach of something is not being able to hit it, and a chase that ends
    // that way is a bow character stood at a wall with a full quiver.
    [Fact]
    public void RefusesAChaseThatWouldStopWithTheWallStillInTheWay() =>
        Search(Sealed(), 32_700, 32_750, 200)
            .Chases(32_700, 32_750, Monster(32_716, 32_750), 8, 200).ShouldBeFalse();

    // The one that was actually going wrong. Both of these are asked about the same map from
    // the same square, and they disagree, because they are asked about different destinations:
    // a square off to the side of a wall, which the client's climb walks straight to, and a
    // monster behind that wall, which it does not get to at all. The hand-back used to ask the
    // first question and act on the answer to the second, so a target whose firing square was
    // easy to reach was left to an engine that then paced in front of the wall — the climb
    // steps aside, finds that its old square was nearer after all, steps back, and does that
    // for as long as anyone lets it.
    [Fact]
    public void TellsReachingASquareApartFromChasingWhatIsBehindAWall()
    {
        var grid = Open();

        for (var y = 32_748; y <= 32_756; y++)
        {
            Shut(grid, 32_708, y);
            Shut(grid, 32_709, y);
        }

        var search = Search(grid, 32_700, 32_752, 200);

        search.Climbs(32_700, 32_752, 32_704, 32_744, 200).ShouldBeTrue();
        search.Chases(32_700, 32_752, Monster(32_724, 32_752), 8, 200).ShouldBeFalse();
    }

    // The client's grid is terrain, and its walk engine also refuses a square another creature
    // is standing on — so a route that goes straight through the monster beside the target is
    // one the character cannot walk. From outside that is a destination four tiles away, every
    // kick answered, and five seconds of not moving.
    [Fact]
    public void WalksRoundACreatureInTheWay()
    {
        var grid = Open();
        var finder = PathFinder.Over(grid);

        // Shoulder to shoulder across the only row the search may use.
        for (var y = 32_749; y <= 32_751; y++)
        {
            grid.Crowd(32_706, y);
        }

        finder.Flood(32_700, 32_750, 200);

        // Round them, and not through them. On open ground that is free — a diagonal costs the
        // same as a straight step — which is the point: what the creatures change is the way
        // taken, and the way taken is the thing the character can actually walk.
        finder.Taken(32_710, 32_750).ShouldNotBeNull();

        for (var y = 32_749; y <= 32_751; y++)
        {
            finder.Taken(32_706, y).ShouldBeNull();
            finder.Taken(32_707, y).ShouldBeNull();
        }
    }

    // A creature is in the way of a step and not of an arrow. Counting one as solid would put
    // this back where line of sight started, with monsters in a pack unhittable because of each
    // other.
    [Fact]
    public void ShootsPastACreatureItWouldWalkRound()
    {
        var grid = Open();

        grid.Crowd(32_706, 32_750);

        PathFinder.Over(grid).Hits(32_700, 32_750, Monster(32_712, 32_750), 8).ShouldBeTrue();
    }

    private static GridWindow Walled()
    {
        var grid = Open();

        for (var y = OriginY; y < OriginY + HuntAddresses.GridRows; y++)
        {
            if (y != 32_760)
            {
                Shut(grid, 32_712, y);
                Shut(grid, 32_713, y);
            }
        }

        return grid;
    }

    /// <summary>The same wall with no gap.</summary>
    private static GridWindow Sealed()
    {
        var grid = Open();

        for (var y = OriginY; y < OriginY + HuntAddresses.GridRows; y++)
        {
            Shut(grid, 32_712, y);
            Shut(grid, 32_713, y);
        }

        return grid;
    }

    /// <summary>A search already run over one of these maps.</summary>
    /// <remarks>
    /// The finder keeps its window and its distance map, so a test builds one and floods it
    /// rather than getting a fresh object back — which is the same seam the hunt uses and for
    /// the same reason: both buffers are large enough to matter.
    /// </remarks>
    private static PathFinder Search(GridWindow grid, int x, int y, int budget)
    {
        var finder = PathFinder.Over(grid);

        finder.Flood(x, y, budget);

        return finder;
    }

    /// <summary>A window with nothing in the way.</summary>
    /// <remarks>
    /// Every cell zero, because the bit means blocked — the client's walk engine scores a
    /// heading only when <c>TileBlocked</c> returns zero for it. An empty array is an empty
    /// field.
    /// </remarks>
    private static GridWindow Open() =>
        GridWindow.For(
            new byte[HuntAddresses.GridStride * HuntAddresses.GridRows
                * HuntAddresses.GridCellLength],
            OriginX,
            OriginY,
            Steps,
            Corners);

    /// <summary>Puts the blocked bit on one cell.</summary>
    /// <remarks>
    /// Reaching into the window rather than rebuilding it, because what a map here is made of
    /// is one bit per cell and the tests read better as "shut this column".
    /// </remarks>
    private static void Shut(GridWindow grid, int x, int y) => grid.Close(x, y);

    private static HuntTarget Monster(int x, int y) =>
        new(new GameAddress(0x1000), 1, "wall dweller", x, y, HuntAddresses.HealthUnknown);
}
