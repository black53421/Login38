using Login38.Interop;

namespace Login38.Aux.Actions;

/// <summary>How a cast decides what it is aimed at.</summary>
/// <remarks>
/// Four ways, because the client has more than one casting path and they disagree about
/// where the target comes from. Picking the wrong one does not fail — it sends a packet
/// the server answers with an error, or puts up a dialog the player has to dismiss.
/// </remarks>
public enum SkillAim
{
    /// <summary>
    /// Whatever the client currently thinks. What the player's own click would do.
    /// </summary>
    /// <remarks>
    /// The client still reads its target global, so this is only right when the player has
    /// just aimed at something. For a buff on oneself, say so explicitly instead.
    /// </remarks>
    Whatever,

    /// <summary>
    /// The player, sent as a packet rather than through the client's casting path.
    /// </summary>
    /// <remarks>
    /// For the buffs that can be cast on someone else. The client's own path re-reads
    /// whatever the mouse is over and overwrites anything written into the target global,
    /// so aiming those at oneself means going around it. That skips the client's mana and
    /// cooldown checks, so callers need a cooldown of their own.
    /// </remarks>
    Self,

    /// <summary>An item in the bag, sent as a packet.</summary>
    /// <remarks>
    /// An item's id is not a world object's id, so the client's path looks it up, finds
    /// nothing and falls back to whatever the interface is hovering. The server has no
    /// such trouble: it looks the id up in the player's own inventory.
    /// </remarks>
    Item,

    /// <summary>Something in the world, through the client's attack path.</summary>
    /// <remarks>
    /// Goes through the client so the animation and the cooldown run, which is the whole
    /// point of not sending the packet directly here.
    /// </remarks>
    Entity,
}

/// <summary>What a cast is aimed at.</summary>
/// <param name="Aim">Which of the client's paths to take.</param>
/// <param name="Id">The object it names, when the path needs one.</param>
public readonly record struct SkillTarget(SkillAim Aim, uint Id = 0)
{
    /// <summary>Leave the client's own idea of the target alone.</summary>
    public static SkillTarget Whatever => new(SkillAim.Whatever);

    /// <summary>The player.</summary>
    public static SkillTarget Self => new(SkillAim.Self);

    /// <summary>An item in the bag, by the id the bag knows it as.</summary>
    public static SkillTarget Item(uint itemId) => new(SkillAim.Item, itemId);

    /// <summary>Something in the world, by its object id.</summary>
    public static SkillTarget Entity(uint objectId) => new(SkillAim.Entity, objectId);
}

/// <summary>
/// Builds the code that casts a skill.
/// </summary>
/// <remarks>
/// A skill id is "packed": the low three bits and the rest go into the packet as two
/// separate bytes, which is why the two paths that build a packet split it and the two
/// that call the client do not.
/// </remarks>
internal static class SkillCast
{
    /// <summary>The low part of a packed skill id.</summary>
    internal static uint Low(uint packed) => packed & 7;

    /// <summary>The high part.</summary>
    internal static uint High(uint packed) => packed >> 3;

    /// <summary>Builds the call for one cast.</summary>
    internal static byte[] Build(uint packed, SkillTarget target) => target.Aim switch
    {
        SkillAim.Self => PacketCall.Send(
            GameFunctions.SkillFormat,
            PacketArgument.Number(GameFunctions.SkillOpcode),
            PacketArgument.Number(High(packed)),
            PacketArgument.Number(Low(packed)),
            PacketArgument.From(GameFunctions.SelfId)),

        SkillAim.Item => PacketCall.Send(
            GameFunctions.SkillFormat,
            PacketArgument.Number(GameFunctions.SkillOpcode),
            PacketArgument.Number(High(packed)),
            PacketArgument.Number(Low(packed)),
            PacketArgument.Number(target.Id)),

        SkillAim.Entity => ThroughTheClient(packed, GameFunctions.AttackTarget, target.Id),

        _ => ThroughTheClient(packed, GameFunctions.CastTarget, null),
    };

    /// <summary>
    /// Calls the client's own casting path, with the target global put back afterwards.
    /// </summary>
    /// <remarks>
    /// Saving and restoring it is not politeness. The client writes that global as the
    /// mouse moves and reads it on the player's next click; leaving the helper's value
    /// there means the player's next cast goes at whatever the helper was aiming at.
    /// </remarks>
    private static byte[] ThroughTheClient(uint packed, GameAddress targetSlot, uint? aimAt)
    {
        var code = new ShellcodeBuilder(new GameAddress(0))
            .PushAd()
            .MovEaxFrom(targetSlot)
            .PushEax();

        if (aimAt is { } id)
        {
            code.MovDwordPtr(targetSlot, id);
        }

        return code
            .PushImm8((byte)GameFunctions.ManualCast)
            .PushImm32(packed)
            .MovEcxFrom(GameFunctions.SpellBookPointer)
            .MovEax(GameFunctions.SpellBookCast.Value)

            // thiscall, and it takes its own two arguments off the stack.
            .CallEax()

            .PopEax()
            .MovEaxTo(targetSlot)
            .PopAd()
            .Ret()
            .Build();
    }
}
