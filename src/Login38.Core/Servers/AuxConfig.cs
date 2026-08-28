using Login38.Core.Text;

namespace Login38.Core.Servers;

/// <summary>
/// Feature switches distributed by the server operator, in the <c>[aux]</c> section
/// of <c>config.ini</c>.
/// </summary>
/// <remarks>
/// Property names match their config keys one for one, deliberately: these files are
/// hand-edited and cross-referenced against the encoder's checkboxes, so an
/// indirection between key and property would cost more than it buys.
/// <para>
/// Switches fall into two groups. Most are applied while the game starts. The
/// helper-window group only comes up once the player presses HOME in-game.
/// </para>
/// </remarks>
public sealed class AuxConfig
{
    /// <summary>Bounds for <see cref="InventoryLimitValue"/>.</summary>
    public static class InventoryLimitBounds
    {
        public const uint Default = 255;
        public const uint Min = 1;
        public const uint Max = 999;
    }

    /// <summary>Bounds for <see cref="MultiInstanceLimit"/>. Zero means unlimited.</summary>
    public static class MultiInstanceLimitBounds
    {
        public const uint Default = 0;
        public const uint Min = 0;
        public const uint Max = 32;
    }

    /// <summary>Bounds for <see cref="ImgLimitValue"/>.</summary>
    public static class ImgLimitBounds
    {
        public const uint Default = 50_000;
        public const uint Min = 6_295;
        public const uint Max = 500_000;
    }

    /// <summary>Encrypt packets to the server.</summary>
    public bool PacketEncrypt { get; set; }

    public bool AntiCheatBasic { get; set; }

    /// <summary>
    /// Always false. The feature was never implemented; the key is still parsed and
    /// written so that a config file round-trips without losing the operator's value.
    /// </summary>
    public bool AntiCheatAdvanced { get; set; }

    /// <summary>Load the morph <c>.pak</c>.</summary>
    public bool TransformFile { get; set; }

    /// <summary>Allow more than one client at a time.</summary>
    public bool MultiInstance { get; set; }

    /// <summary>Leave movement packets unencrypted even when <see cref="PacketEncrypt"/> is on.</summary>
    public bool MovePacketNoEncrypt { get; set; }

    /// <summary>The in-game helper window, opened with HOME.</summary>
    public bool LhxAuxEnabled { get; set; } = true;

    /// <summary>Raise the HP/MP display ceiling past the client's byte limit.</summary>
    public bool HpMpLimitEnabled { get; set; } = true;

    /// <summary>Raise the AC/MR display ceiling past the client's byte limit.</summary>
    public bool AcMrLimitEnabled { get; set; } = true;

    public bool InventoryLimitEnabled { get; set; } = true;

    /// <summary>The 7.6-style equipment panel.</summary>
    public bool EquipUiEnabled { get; set; } = true;

    public bool ImgLimitEnabled { get; set; } = true;

    public bool DynamicDialogEnabled { get; set; } = true;

    /// <summary>Animated item icons from a custom PNG <c>.pak</c>.</summary>
    public bool DynamicIconEnabled { get; set; }

    /// <summary>Pickup notifications in the lower-left corner.</summary>
    public bool PickupToastEnabled { get; set; } = true;

    /// <summary>Floating experience and coin text.</summary>
    public bool ExpDriftEnabled { get; set; } = true;

    /// <summary>
    /// Whether the helper offers automatic hunting at all.
    /// </summary>
    /// <remarks>
    /// The operator's switch rather than the player's: off, and the helper window has no
    /// hunting page, so there is nothing to turn on. On, and the page appears with the
    /// hunt still off until the player says otherwise.
    /// </remarks>
    public bool InternalBotEnabled { get; set; }

    /// <summary>Maximum simultaneous clients. Zero means unlimited.</summary>
    public uint MultiInstanceLimit { get; set; } = MultiInstanceLimitBounds.Default;

    public uint InventoryLimitValue { get; set; } = InventoryLimitBounds.Default;

    public uint ImgLimitValue { get; set; } = ImgLimitBounds.Default;

    /// <summary>Base name of the custom icon pak, e.g. <c>"123"</c> for <c>123.pak</c>/<c>123.idx</c>.</summary>
    public string DynamicIconPakName { get; set; } = "123";

    /// <summary>
    /// Which morph table to load, by name, without its extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty means the one named after the client executable, which is where the reference
    /// always looked and what an operator with a single table still gets for free.
    /// </para>
    /// <para>
    /// A name rather than a path, in the game's own directory. An operator zips that
    /// directory up and hands it to their players, and an absolute path from the machine
    /// it was built on would not survive the trip.
    /// </para>
    /// </remarks>
    public string TransformFileName { get; set; } = string.Empty;

    /// <summary>How to decode legacy text from this client.</summary>
    public TextEncodingMode TextEncoding { get; set; } = TextEncodingMode.Big5;

    /// <summary>
    /// Forces invariants that hold regardless of what the file said. Applied on both
    /// load and save so a stale file cannot re-enable an unimplemented feature.
    /// </summary>
    public AuxConfig Normalized()
    {
        AntiCheatAdvanced = false;
        return this;
    }
}
