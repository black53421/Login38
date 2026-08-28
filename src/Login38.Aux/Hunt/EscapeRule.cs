using System.Text.Json.Serialization;

namespace Login38.Aux.Hunt;

/// <summary>
/// Reading a scroll when the fight has gone badly.
/// </summary>
/// <remarks>
/// <para>
/// Armed by health rather than timed. It fires on the way down through the threshold and
/// will not fire again until the character has climbed back above it — which, once the
/// scroll has worked, happens on its own. A rule that fired on every pass below the line
/// would read the whole stack in a second.
/// </para>
/// <para>
/// It switches the hunt off afterwards. Running away is not a change of hunting ground — the
/// health was going and landing somewhere else fixes none of that — so carrying on would walk
/// the same character into the same trouble with less to spend on it. A spot that has merely
/// run out is <see cref="RelocateRule"/>, and that one leaves the hunt running.
/// </para>
/// </remarks>
public sealed class EscapeRule
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Read it below this much health, as a percentage.</summary>
    [JsonPropertyName("hp_below")]
    public uint HitPointsBelow { get; set; } = 30;

    /// <summary>What to read, by the name in the bag.</summary>
    /// <remarks>
    /// Whatever the scroll does is the scroll's business. Where it lands is decided between
    /// the character's teleport control ring and the server, and there is nothing useful for
    /// the launcher to say about it — a named destination without the ring is ignored, and
    /// with the ring the client asks and answers by itself.
    /// </remarks>
    [JsonPropertyName("item")]
    public string Item { get; set; } = string.Empty;

    /// <summary>Whether it is set up enough to act on.</summary>
    public bool Ready => Enabled && !string.IsNullOrWhiteSpace(Item);
}
