import io

# ------------------------------------------------------------------ addresses
p = 'D:/C#380login/src/Login38.Aux/Hunt/HuntAddresses.cs'
s = io.open(p, encoding='utf-8').read()
old = '''    /// <inheritdoc cref="GridCellAttribute"/>
    internal const ushort GridBlocked = 1;'''
new = '''    /// <inheritdoc cref="GridCellAttribute"/>
    internal const ushort GridBlocked = 1;

    /// <summary>
    /// A bit the launcher writes into its own copy of the grid, meaning a creature is there.
    /// </summary>
    /// <remarks>
    /// The client's collision grid holds terrain and nothing else, but its walk engine will not
    /// step onto a square another creature is standing on — so a route worked out from terrain
    /// alone goes straight through the monster beside the one being hunted, and the character
    /// stands there being re-kicked until the leg times out. Read off the log as five seconds
    /// on one square with a destination four tiles away and nothing in the client's own state
    /// looking wrong.
    /// </remarks>
    internal const ushort GridCrowded = 0x8000;'''
assert s.count(old) == 1, 'crowded const'
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s)

# ----------------------------------------------------------------- GridWindow
p = 'D:/C#380login/src/Login38.Aux/Hunt/GridWindow.cs'
s = io.open(p, encoding='utf-8').read()

old = '''    /// <summary>
    /// Whether a cell index carries the passable bit, with anything off the end saying no.
    /// </summary>
    private bool Bit(int cell)
    {
        var at = cell * HuntAddresses.GridCellLength;

        if (at < 0 || at + HuntAddresses.GridCellAttribute + sizeof(ushort) > _cells.Length)
        {
            return false;
        }

        return (BitConverter.ToUInt16(_cells, at + HuntAddresses.GridCellAttribute)
            & HuntAddresses.GridBlocked) != 0;
    }'''
new = '''    /// <summary>
    /// Whether a cell index is one a step may not be taken into, off the end saying no.
    /// </summary>
    /// <remarks>
    /// Terrain and creatures both, because the client's walk engine refuses both. Sight is a
    /// different question and asks <see cref="Solid"/>.
    /// </remarks>
    private bool Bit(int cell) =>
        Attribute(cell, (ushort)(HuntAddresses.GridBlocked | HuntAddresses.GridCrowded));

    /// <summary>Whether a cell is terrain that nothing can be seen through.</summary>
    /// <remarks>
    /// Creatures are not counted. Something standing between the character and a monster is in
    /// the way of a step and not of an arrow, and refusing the shot would put this back where
    /// it was when line of sight was a walk: monsters in a pack unhittable because of each
    /// other.
    /// </remarks>
    private bool Solid(int cell) => Attribute(cell, HuntAddresses.GridBlocked);

    private bool Attribute(int cell, ushort mask)
    {
        var at = cell * HuntAddresses.GridCellLength;

        if (at < 0 || at + HuntAddresses.GridCellAttribute + sizeof(ushort) > _cells.Length)
        {
            return false;
        }

        return (BitConverter.ToUInt16(_cells, at + HuntAddresses.GridCellAttribute) & mask) != 0;
    }'''
assert s.count(old) == 1, 'bit'
s = s.replace(old, new)

old = '''    public bool Blocked(int x, int y) => !Holds(x, y) || Bit(Cell(x, y));'''
new = '''    public bool Blocked(int x, int y) => !Holds(x, y) || Solid(Cell(x, y));'''
assert s.count(old) == 1, 'blocked'
s = s.replace(old, new)

old = '''    internal void Close(int x, int y)'''
new = '''    /// <summary>
    /// Says a creature is standing on a tile, in this copy of the grid only.
    /// </summary>
    /// <remarks>
    /// Both columns of it, because two make a tile across and a creature standing on one is
    /// standing on the other. Written into the copy and gone on the next
    /// <see cref="Refresh"/>, which is what makes it safe to be wrong about: a monster that
    /// moves costs one route and no more.
    /// </remarks>
    public void Crowd(int x, int y)
    {
        var column = x & ~1;

        Mark(column, y);
        Mark(column + 1, y);
    }

    private void Mark(int x, int y)
    {
        if (!Holds(x, y))
        {
            return;
        }

        var at = (Cell(x, y) * HuntAddresses.GridCellLength) + HuntAddresses.GridCellAttribute;
        var attribute = (ushort)(BitConverter.ToUInt16(_cells, at) | HuntAddresses.GridCrowded);

        BitConverter.TryWriteBytes(_cells.AsSpan(at), attribute);
    }

    internal void Close(int x, int y)'''
assert s.count(old) == 1, 'crowd'
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s)

# ----------------------------------------------------------------- PathFinder
p = 'D:/C#380login/src/Login38.Aux/Hunt/PathFinder.cs'
s = io.open(p, encoding='utf-8').read()

old = '''    public bool Search(RemoteProcess p, int startX, int startY, int budget) =>
        _grid.Refresh(p) && Flood(startX, startY, budget);'''
new = '''    public bool Search(RemoteProcess p, int startX, int startY, int budget) =>
        Search(p, startX, startY, budget, [], 0);

    /// <summary>
    /// The same, with the creatures on the map counted as things to walk round.
    /// </summary>
    /// <param name="crowd">Everything the scan found, the target included.</param>
    /// <param name="wanted">
    /// The one to walk <em>to</em>, whose own square is left open — a route that refuses the
    /// tile the monster is standing on is a route to nowhere, and the search is looking for a
    /// square to attack from rather than for the monster's own.
    /// </param>
    /// <remarks>
    /// The client's grid is terrain. Its walk engine also refuses to step onto a square another
    /// creature occupies, and nothing in the grid says where they are — so a route straight
    /// through the monster beside the target is one the character cannot walk, and what that
    /// looks like from outside is a destination four tiles away and five seconds of standing
    /// still with every kick answered and nothing moving.
    /// </remarks>
    public bool Search(
        RemoteProcess p,
        int startX,
        int startY,
        int budget,
        IReadOnlyList<HuntTarget> crowd,
        uint wanted)
    {
        ArgumentNullException.ThrowIfNull(crowd);

        if (!_grid.Refresh(p))
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
    }'''
assert s.count(old) == 1, 'search'
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
