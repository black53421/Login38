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

    /// <summary>Something in the world, by object id, without touching the interface.</summary>
    /// <remarks>
    /// <para>
    /// Neither of the other two ways works for this. The client's own path leaves the
    /// interface half-armed, and a bare packet has no cooldown — so this one is assembled
    /// out of the client's own pieces instead. See <see cref="SkillCast"/>.
    /// </para>
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
    internal static byte[] Build(uint packed, SkillTarget target, GameAddress record = default) =>
        target.Aim switch
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

        SkillAim.Entity => Assembled(packed, target.Id, record),

        _ => ThroughTheClient(packed, GameFunctions.CastTarget),
    };

    /// <summary>
    /// Casts at something in the world the way the client would, minus the interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's own entry point, <c>FUN_0073ECE0</c>, does three things and a helper only
    /// wants two of them. It works out how long the cast takes, it hands off to
    /// <c>FUN_0073C260</c> which sends the packet, and then it arms the interface: the word at
    /// <c>0xC31304</c> is left at fifteen, meaning "a cast is waiting for somewhere to go",
    /// and if the target did not resolve — a monster that died between choosing and casting is
    /// enough — <c>FUN_0073C260</c> registers a click handler and the cursor becomes "choose a
    /// target".
    /// </para>
    /// <para>
    /// That is a helper reaching into the player's hands. The next left click anywhere in the
    /// world casts the helper's skill instead of doing what the player meant, which is easy to
    /// walk into while typing or looking through a bag. It runs the other way too: on that
    /// path the client aims at whatever the mouse is over, so where the player happens to be
    /// pointing decides what the helper casts at.
    /// </para>
    /// <para>
    /// Sending the packet alone avoids all of it and loses the cooldown, which is worse: the
    /// delay is not in the packet, it is three words the client writes for itself, and without
    /// them a rotation is limited by nothing.
    /// </para>
    /// <para>
    /// So both halves are taken and the interface is left alone. The delay is worked out the
    /// way the client works it out — a number belonging to the character plus a signed word
    /// per skill — the packet is byte for byte the one <c>FUN_0073C260</c> sends, and the
    /// client's own routine stamps the cooldown afterwards. Nothing here writes a target
    /// global, so nothing has to be put back, and the player's aim is never touched.
    /// </para>
    /// <para>
    /// The skill's own icon is greyed out too, when the record it lives in can be shown to
    /// still be that skill's — see <c>Spells.Icon</c>. The client finds that record by
    /// walking the book, which is a loop nobody should be assembling by hand; the address the
    /// book was last read at is used instead, and checked before it is written to.
    /// </para>
    /// </remarks>
    private static byte[] Assembled(uint packed, uint objectId, GameAddress record)
    {
        var code = new ShellcodeBuilder(new GameAddress(0))
            .PushAd()

            // How long this cast takes: the character's own part, then the skill's.
            .PushImm8(GameFunctions.CastSpeedKind)
            .MovEcxFrom(GameFunctions.LocalPlayer)
            .MovEax(GameFunctions.DerivedStat.Value)
            .CallEax()
            .MovsxEdxWordFrom(GameFunctions.SkillDelays + (int)(packed * 2))
            .AddEaxEdx()
            .MovEaxTo(GameFunctions.CastDelay);

        // Before the cast rather than after it, which is the order the client does it in.
        if (!record.IsNull)
        {
            code
                .MovBytePtr(record + GameFunctions.RecordCooling, 0)
                .MovEcx(record.Value)
                .MovEax(GameFunctions.StampIconCooldown.Value)
                .CallEax();
        }

        return code

            // The cast itself, which is all the server ever sees.
            .PushImm32(objectId)
            .PushImm32(SkillCast.Low(packed))
            .PushImm32(SkillCast.High(packed))
            .PushImm32(GameFunctions.SkillOpcode)
            .PushImm32(GameFunctions.SkillFormat)
            .MovEax(GameFunctions.SendPacketData.Value)
            .CallEax()
            .AddEsp(0x14)

            // And the cooldown, so the rotation is paced by the client rather than by luck.
            .MovEax(GameFunctions.StampCastCooldown.Value)
            .CallEax()

            .PopAd()
            .Ret()
            .Build();
    }

    /// <summary>
    /// Calls the client's own casting path, with the target global put back afterwards.
    /// </summary>
    /// <remarks>
    /// Saving and restoring it is not politeness. The client writes that global as the
    /// mouse moves and reads it on the player's next click; leaving the helper's value
    /// there means the player's next cast goes at whatever the helper was aiming at.
    /// </remarks>
    private static byte[] ThroughTheClient(uint packed, GameAddress targetSlot)
    {
        var code = new ShellcodeBuilder(new GameAddress(0))
            .PushAd()
            .MovEaxFrom(targetSlot)
            .PushEax();

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
