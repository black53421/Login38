using System.Text.Json.Serialization;

namespace Login38.Aux.Hunt;

/// <summary>
/// One attack skill to throw at whatever the hunt is fighting.
/// </summary>
/// <remarks>
/// <para>
/// One turn of a rotation. The reference kept a cooldown table, a priority queue and a
/// per-skill range copied out of a spreadsheet, all of which the client already knows: the
/// spell book carries the range and the dispatch table says whether a skill acts on a target
/// at all. What is left for a player to decide is the order, how often, and how much mana to
/// leave alone.
/// </para>
/// <para>
/// Nothing here picks a target. The hunt has already chosen one and the client is already
/// walking to it; a skill that chose its own would be the second driver that made the
/// character shake.
/// </para>
/// </remarks>
public enum HuntStep
{
    /// <summary>Cast the skill named on the row.</summary>
    Skill,

    /// <summary>Swing the weapon, which means leaving the client to get on with it.</summary>
    Weapon,
}

public sealed class HuntSkill
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// What the row is: a skill to cast, or a turn of the weapon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A weapon row casts nothing. The client's attack chain swings on its own schedule and
    /// always has — nothing here starts or stops it — so what a weapon row actually does is
    /// take up a turn in the rotation, which is how a player says "two swings between these
    /// two skills" rather than "both skills the moment they are up".
    /// </para>
    /// <para>
    /// Skill by default, so a settings file written before this existed reads back as the
    /// list of skills it was.
    /// </para>
    /// </remarks>
    [JsonPropertyName("step")]
    public HuntStep Step { get; set; }

    /// <summary>The skill, by the name the player sees in their own spell book.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The shortest gap between two casts of it.
    /// </summary>
    /// <remarks>
    /// The client's cast goes through its own path, so its cooldown is honoured — this is
    /// the player's own throttle on top, for a skill worth using but not worth using every
    /// time it comes up.
    /// </remarks>
    [JsonPropertyName("interval_sec")]
    public double IntervalSeconds { get; set; } = 5;

    /// <summary>Leave the skill alone below this much mana, as a percentage.</summary>
    /// <remarks>
    /// Zero for "whenever there is enough to cast it", which the server decides. A number
    /// here is the player reserving mana for something else — usually the heal.
    /// </remarks>
    [JsonPropertyName("mp_at_least")]
    public uint ManaAtLeast { get; set; }
}
