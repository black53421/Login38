using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>What a character has earned since the last time anyone looked.</summary>
/// <param name="Level">What level they are now.</param>
/// <param name="Gained">What the last kill was worth.</param>
/// <param name="ToNextPercent">How much more to move the percentage on by one.</param>
/// <param name="ToLevel">And how much more to the next level.</param>
/// <param name="Session">The total since the helper started watching.</param>
/// <param name="LevelledUp">Whether that last kill was the one.</param>
public readonly record struct ExperienceReport(
    uint Level, ulong Gained, ulong ToNextPercent, ulong ToLevel, ulong Session, bool LevelledUp);

/// <summary>
/// How much experience a character has, and what that means.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps one number: everything earned since level one. The level itself is
/// worked out from that rather than read, because the byte that looks like the level is the
/// level the character <i>started</i> as and does not move when they level up — which is
/// exactly the bug this arrangement was written to fix. A player who levelled saw "0 more
/// to go" for the rest of the evening.
/// </para>
/// <para>
/// The thresholds are the client's own, and match what the game shows to the percent.
/// </para>
/// </remarks>
public static class Experience
{
    /// <summary>Everything earned since level one, which is all the client stores.</summary>
    internal static readonly GameAddress Total = new(0x00C31EA4);

    /// <summary>
    /// What each level starts at.
    /// </summary>
    /// <remarks>
    /// Index zero is level one. Past the end of this the levels are evenly spaced, which is
    /// what the game does too.
    /// </remarks>
    private static readonly ulong[] Thresholds =
    [
        0, 125, 300, 500, 750, 1296, 2401, 4096, 6561, 10000, 14641, 20736, 28561, 38416,
        50625, 65536, 83521, 104976, 130321, 160000, 194481, 234256, 279841, 331776, 390625,
        456976, 531441, 614656, 707281, 810000, 923521, 1048576, 1185921, 1336336, 1500625,
        1679616, 1874161, 2085136, 2313441, 2560000, 2825761, 3111696, 3418801, 3748096,
        4100625, 4829985, 6338401, 9833664, 19745853, 31292598, 44473900, 59289759, 75740173,
        93825145, 113544672, 134898756, 157887397, 182510594, 208768347, 236660657, 266187523,
        297348946, 330144925, 364575461, 400640553,
    ];

    /// <summary>The last level the table covers.</summary>
    private const uint Tabulated = 65;

    /// <summary>Where that level starts, which is where the even spacing takes over.</summary>
    private const ulong HighBase = 400_640_553;

    /// <summary>And how far apart every level past it is.</summary>
    private const ulong HighRange = 36_065_092;

    /// <summary>Reads what the character has earned altogether.</summary>
    public static uint? Read(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return process.TryRead<uint>(Total, out var total) ? total : null;
    }

    /// <summary>What a level starts at.</summary>
    public static ulong StartOf(uint level) => level switch
    {
        <= 1 => 0,
        <= Tabulated => Thresholds[level - 1],
        _ => HighBase + ((level - Tabulated) * HighRange),
    };

    /// <summary>How much a whole level is worth.</summary>
    public static ulong RangeOf(uint level) => level == 0 ? 0 : StartOf(level + 1) - StartOf(level);

    /// <summary>
    /// What level a total means.
    /// </summary>
    /// <remarks>
    /// Worked out rather than read, for the reason in the type's own notes. The table is
    /// sixty-five entries, so searching it backwards costs nothing worth avoiding.
    /// </remarks>
    public static uint LevelOf(ulong total)
    {
        if (total >= HighBase)
        {
            return Tabulated + (uint)((total - HighBase) / HighRange);
        }

        for (var level = Tabulated; level >= 1; level--)
        {
            if (total >= StartOf(level))
            {
                return level;
            }
        }

        return 1;
    }

    /// <summary>
    /// How much more is needed to move the percentage on by one.
    /// </summary>
    /// <remarks>
    /// The number a player watches while deciding whether to stay for one more. Rounded up,
    /// because a fraction short still shows the old percentage.
    /// </remarks>
    public static ulong ToNextPercent(ulong within, ulong range)
    {
        if (range == 0)
        {
            return 0;
        }

        var target = ((range * ((within * 100 / range) + 1)) + 99) / 100;

        return target > within ? target - within : 0;
    }
}

/// <summary>
/// Watches the number go up.
/// </summary>
/// <remarks>
/// Deliberately holds nothing but numbers, so the arithmetic can be checked without a game
/// anywhere near it. It is also the part that was got wrong twice in the reference before
/// anybody worked out that the level had to be derived.
/// </remarks>
public sealed class ExperienceWatch
{
    private uint? _last;
    private uint? _level;
    private uint? _baseline;

    /// <summary>Whether it is currently watching.</summary>
    public bool Watching { get; private set; }

    /// <summary>Starts, taking the current total as the session's starting point.</summary>
    /// <remarks>Says nothing itself: the first report should be about a kill, not about starting.</remarks>
    public void Start(uint total)
    {
        Watching = true;
        _baseline = total;
        _last = total;
        _level = Experience.LevelOf(total);
    }

    /// <summary>Stops, and forgets where the session started.</summary>
    public void Stop()
    {
        Watching = false;
        _baseline = null;
        _last = null;
        _level = null;
    }

    /// <summary>
    /// Takes a reading.
    /// </summary>
    /// <returns>Null when nothing has changed, which is almost every time it is asked.</returns>
    public ExperienceReport? Look(uint total)
    {
        if (!Watching || _last is not { } last || _level is not { } was || _baseline is not { } start)
        {
            return null;
        }

        if (total == last)
        {
            return null;
        }

        var level = Experience.LevelOf(total);
        var range = Experience.RangeOf(level);
        var within = total - Experience.StartOf(level);

        _last = total;
        _level = level;

        return new ExperienceReport(
            level,
            total > last ? total - last : 0,
            Experience.ToNextPercent(within, range),
            range > within ? range - within : 0,
            total > start ? total - start : 0,
            level != was);
    }
}
