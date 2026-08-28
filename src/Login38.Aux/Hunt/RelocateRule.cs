using System.Text.Json.Serialization;

namespace Login38.Aux.Hunt;

/// <summary>
/// Reading a scroll when the spot has stopped being worth standing in.
/// </summary>
/// <remarks>
/// <para>
/// Not <see cref="EscapeRule"/>, and the difference is the whole point of having two. Escaping
/// is about the character: the health is going and the answer is to stop being here, so the
/// hunt is switched off afterwards because a character that had to run is not a character that
/// should carry on. This is about the <em>place</em>: nothing is wrong with the character, the
/// room has simply run out — every monster in it is dead, or something in the geometry has the
/// character wedged — and the answer is somewhere else to hunt, with the hunt left running.
/// </para>
/// <para>
/// Two ways of noticing, and they are different failures. Rooted to the spot is the launcher's
/// own bug showing through, or a monster on the other side of something neither engine can go
/// round; barren is a room that has been cleared. Either can be turned off on its own by
/// setting its seconds to zero, because a player who wants one of them rarely wants both at
/// the same length.
/// </para>
/// </remarks>
public sealed class RelocateRule
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Read it after this long without the character moving at all. Zero turns it off.
    /// </summary>
    /// <remarks>
    /// Long. The hunt has its own five and twenty second watchdogs for a target it cannot
    /// get to, and they work by dropping that target and picking another — so anything short
    /// enough to overlap them spends scrolls on a problem that was about to solve itself. This
    /// is for the case where every one of those has already fired and the character is still
    /// standing exactly where it was.
    /// </remarks>
    [JsonPropertyName("stuck_seconds")]
    public uint StuckSeconds { get; set; } = 90;

    /// <summary>
    /// Read it after this long with nothing in range worth attacking. Zero turns it off.
    /// </summary>
    /// <remarks>
    /// Counted over what the picker would accept rather than over everything the scan sees, so
    /// a room full of blacklisted things is as barren as an empty one — which is what the
    /// player meant by putting them on the list.
    /// </remarks>
    [JsonPropertyName("barren_seconds")]
    public uint BarrenSeconds { get; set; } = 60;

    /// <summary>What to read, by the name in the bag.</summary>
    /// <remarks>See <see cref="EscapeRule.Item"/> for why there is nothing here about where
    /// it lands.</remarks>
    [JsonPropertyName("item")]
    public string Item { get; set; } = string.Empty;

    /// <summary>Whether it is set up enough to act on.</summary>
    public bool Ready =>
        Enabled
        && !string.IsNullOrWhiteSpace(Item)
        && (StuckSeconds > 0 || BarrenSeconds > 0);
}
