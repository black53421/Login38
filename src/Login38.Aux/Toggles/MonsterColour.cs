namespace Login38.Aux.Toggles;

/// <summary>How dangerous a monster is, relative to the player looking at it.</summary>
public enum MonsterColour
{
    /// <summary>At or below the player's level — the client's ordinary name colour.</summary>
    White,

    /// <summary>One to ten levels above.</summary>
    Green,

    /// <summary>Eleven to nineteen.</summary>
    Blue,

    /// <summary>Twenty to twenty-nine.</summary>
    LightRed,

    /// <summary>Thirty or more.</summary>
    DarkRed,
}

/// <summary>
/// The five colours a monster's name can be drawn in, and the rule that picks one.
/// </summary>
/// <remarks>
/// <para>
/// The client draws names through a surface in RGB565, so these are 16-bit values rather
/// than the 24-bit ones the rest of the launcher deals in. White is the client's own
/// default, which is why it is both "no danger" and "this launcher has not touched it".
/// </para>
/// <para>
/// The bands are the reference's, and they are asymmetric on purpose: everything at or
/// below the player is one colour, and the interesting resolution is all above.
/// </para>
/// </remarks>
public static class MonsterColours
{
    /// <summary>The client's own name colour, and this feature's "nothing to say".</summary>
    public const ushort White = 0xFFDF;

    /// <summary>One to ten levels up.</summary>
    public const ushort Green = 0x07E0;

    /// <summary>Eleven to nineteen.</summary>
    public const ushort Blue = 0x001F;

    /// <summary>Twenty to twenty-nine.</summary>
    public const ushort LightRed = 0xF800;

    /// <summary>Thirty or more.</summary>
    public const ushort DarkRed = 0x8800;

    /// <summary>
    /// The four colours this feature writes, in the order the detours compare them.
    /// </summary>
    /// <remarks>
    /// White is not one of them. A monster the player has outlevelled is left exactly as
    /// the client drew it, so there is no colour to recognise and nothing to restore.
    /// </remarks>
    public static ReadOnlySpan<ushort> Feature => [DarkRed, LightRed, Blue, Green];

    /// <summary>The 16-bit value for a band.</summary>
    public static ushort Rgb565(MonsterColour colour) => colour switch
    {
        MonsterColour.DarkRed => DarkRed,
        MonsterColour.LightRed => LightRed,
        MonsterColour.Blue => Blue,
        MonsterColour.Green => Green,
        _ => White,
    };

    /// <summary>
    /// Which band a monster falls in.
    /// </summary>
    /// <remarks>
    /// The difference is clamped at zero rather than signed: a monster twenty levels below
    /// the player is no more interesting than one at their level.
    /// </remarks>
    public static MonsterColour For(uint monsterLevel, uint playerLevel)
    {
        var above = monsterLevel > playerLevel ? monsterLevel - playerLevel : 0;

        return above switch
        {
            >= 30 => MonsterColour.DarkRed,
            >= 20 => MonsterColour.LightRed,
            >= 11 => MonsterColour.Blue,
            >= 1 => MonsterColour.Green,
            _ => MonsterColour.White,
        };
    }

    /// <summary>The colour to write, or null where the answer is "leave it alone".</summary>
    public static ushort? PatchFor(uint monsterLevel, uint playerLevel)
    {
        var colour = Rgb565(For(monsterLevel, playerLevel));

        return IsFeature(colour) ? colour : null;
    }

    /// <summary>Whether a colour found on an entity is one this feature writes.</summary>
    public static bool IsFeature(ushort colour) => Feature.Contains(colour);
}
