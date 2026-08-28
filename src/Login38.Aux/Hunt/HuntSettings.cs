using System.Text.Json.Serialization;

namespace Login38.Aux.Hunt;

/// <summary>
/// What the player has asked the hunt to do.
/// </summary>
/// <remarks>
/// <para>
/// Small on purpose. The reference's settings carried a walk driver, a path replan budget
/// and a pathfinder iteration cap, all of which described work it was doing that the client
/// does here — and a six-tab window laid out for five phases that were never built.
/// </para>
/// <para>
/// What is left is the three decisions nobody but the player can make: how far to look,
/// what not to hit, and when to give up on something.
/// </para>
/// </remarks>
public sealed class HuntSettings
{
    /// <summary>The master switch.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// How far to look, in steps rather than tiles.
    /// </summary>
    /// <remarks>
    /// Steps, because that is what reachability counts and it is the honest measure: a
    /// monster four tiles away on the other side of a wall is not four tiles away. The
    /// grid also holds two columns per walkable tile, so a step sideways is half a tile and
    /// a number in tiles would have to be converted by somebody.
    /// </remarks>
    [JsonPropertyName("range_steps")]
    public int RangeSteps { get; set; } = 60;

    /// <summary>
    /// How close to stand before shooting, in tiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only ever closer than the weapon reaches, never further — it is a cap, so a melee
    /// weapon, whose own reach is two, ignores it entirely.
    /// </para>
    /// <para>
    /// The client stops the moment it is in range at all, which for a bow is fourteen tiles
    /// and is the worst place to stand: the monster takes one step and the shot is out of
    /// range again, so the whole fight is spent drifting in and out instead of shooting.
    /// The client has one knob for this and only one — the walk engine's arrival test in
    /// <c>ComputeStepHeading</c> reads its reach from
    /// <see cref="HuntAddresses.AttackReach"/> — so standing closer means telling the client
    /// a smaller reach and letting it walk in by itself.
    /// </para>
    /// <para>
    /// Eight is far enough to keep the point of carrying a bow and near enough that a
    /// monster wandering a tile or two does not end the fight. Lower it and the character
    /// closes like a melee one; raise it past the weapon's own reach and nothing changes.
    /// </para>
    /// </remarks>
    [JsonPropertyName("standoff_tiles")]
    public int StandoffTiles { get; set; } = 8;

    /// <summary>
    /// Whether to turn on whatever starts hitting the character.
    /// </summary>
    /// <remarks>
    /// The client keeps no record of who is attacking. The whole heap was searched for a
    /// pointer or an id leading back to the player and there is none — the server tells the
    /// client "this one hit that one" and the client just plays it. So the question is
    /// answered the way a person answers it: the hit points went down, and something is
    /// standing on top of us.
    /// </remarks>
    [JsonPropertyName("retaliate")]
    public bool Retaliate { get; set; } = true;

    /// <summary>Names never to attack.</summary>
    /// <remarks>Matched whole, against the name out of the client's own label.</remarks>
    [JsonPropertyName("blacklist")]
    public List<string> Blacklist { get; set; } = [];

    /// <summary>
    /// Only these names, when the list is not empty.
    /// </summary>
    /// <remarks>
    /// A separate list rather than a mode, so a player who fills one in and empties it
    /// again gets the old behaviour back without also having to find a switch.
    /// </remarks>
    [JsonPropertyName("whitelist")]
    public List<string> Whitelist { get; set; } = [];

    /// <summary>
    /// How long nothing may happen before the current target is given up on.
    /// </summary>
    /// <remarks>
    /// "Nothing" is neither moving nor landing a blow. Both are read from the client, so
    /// this does not have to guess what should be happening — see
    /// <see cref="StallWatch"/>.
    /// </remarks>
    [JsonPropertyName("stall_seconds")]
    public int StallSeconds { get; set; } = 6;

    /// <summary>How many turns a player can line the rotation up in.</summary>
    /// <remarks>
    /// Six rather than the four this was when every row was a skill. Weapon rows take turns
    /// of their own now — that is the whole point of them — so a rotation of three skills with
    /// a swing between each wants six, and four would have made the feature not fit its own
    /// most obvious use.
    /// </remarks>
    public const int SkillRows = 6;

    /// <summary>The rotation, in the order it is taken.</summary>
    [JsonPropertyName("skills")]
    public HuntSkill[] Skills { get; set; } = Rows();

    /// <summary>When to read a scroll instead of fighting on.</summary>
    [JsonPropertyName("escape")]
    public EscapeRule Escape { get; set; } = new();

    /// <summary>What to do when the spot has run out. See <see cref="RelocateRule"/>.</summary>
    [JsonPropertyName("relocate")]
    public RelocateRule Relocate { get; set; } = new();

    /// <summary>How long a target that stalled is left alone afterwards.</summary>
    /// <remarks>
    /// Long enough not to pick it straight back up, short enough that a monster that was
    /// briefly behind another one comes back into play.
    /// </remarks>
    [JsonPropertyName("ignore_seconds")]
    public int IgnoreSeconds { get; set; } = 30;

    /// <summary>Fills in anything an older or hand-edited file left out.</summary>
    /// <summary>The same settings with the master switch off.</summary>
    /// <remarks>
    /// For a build whose operator has not offered hunting at all: what the player last set
    /// is kept, so turning it back on in the server list brings their rotation back, but
    /// nothing hunts meanwhile.
    /// </remarks>
    public HuntSettings Off()
    {
        Enabled = false;

        return this;
    }

    public HuntSettings Normalise()
    {
        Blacklist ??= [];
        Whitelist ??= [];
        Escape ??= new EscapeRule();
        Relocate ??= new RelocateRule();

        // Indexed by the window, so a file written before these existed — or one somebody
        // edited by hand — would be read past its end the first time it is opened.
        if (Skills is null || Skills.Length != SkillRows || Array.Exists(Skills, row => row is null))
        {
            var sized = Rows();
            Array.Copy(Skills ?? [], sized, Math.Min(Skills?.Length ?? 0, SkillRows));

            for (var i = 0; i < SkillRows; i++)
            {
                sized[i] ??= new HuntSkill();
            }

            Skills = sized;
        }

        if (RangeSteps <= 0)
        {
            RangeSteps = 60;
        }

        if (StandoffTiles <= 0)
        {
            StandoffTiles = 8;
        }

        if (StallSeconds <= 0)
        {
            StallSeconds = 6;
        }

        if (IgnoreSeconds <= 0)
        {
            IgnoreSeconds = 30;
        }

        return this;
    }

    private static HuntSkill[] Rows()
    {
        var rows = new HuntSkill[SkillRows];

        for (var i = 0; i < SkillRows; i++)
        {
            rows[i] = new HuntSkill();
        }

        return rows;
    }
}
