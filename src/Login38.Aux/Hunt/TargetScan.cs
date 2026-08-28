using Login38.Aux.Game;
using Login38.Core.Text;
using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// Finds the monsters standing near the player.
/// </summary>
/// <remarks>
/// <para>
/// Everything in the world — the player, other players, monsters, dropped items, the
/// client's own sparkles — is one class behind one vtable, so finding the records is easy
/// and telling them apart is the whole job.
/// </para>
/// <para>
/// The client has a classifier for that, <c>Entity_combatTypeClass</c> at <c>0x5AEBE0</c>,
/// and reusing it looked right for the same reason reusing the collision grid was. It does
/// not work. A live monster two tiles away comes out <c>0x0A</c>, which is not in the
/// <c>{6, 0xC, 0xF, 0x12}</c> set the walk engine's first attack branch accepts — the engine
/// reaches ordinary monsters through a later branch. Copying "is it attackable" therefore
/// means copying all three branches, and a partial copy drops every monster in silence.
/// </para>
/// <para>
/// What is worth taking from that routine is the fields it reads, and the disassembly hands
/// over their offsets. Five of them answer the question directly, and the label answers two
/// more that the classifier never returns at all: what the thing is called, and which
/// creature it is.
/// </para>
/// </remarks>
public class TargetScan
{
    /// <summary>A label is short, and reading more of one costs a crossing.</summary>
    private const int LabelLength = 64;

    /// <summary>What separates the name from the template id inside a label.</summary>
    private const char LabelSeparator = '#';

    private readonly ILegacyTextCodec _codec;

    public TargetScan(ILegacyTextCodec codec) => _codec = codec;

    /// <summary>
    /// Every monster in the client's heap, alive and not the player.
    /// </summary>
    /// <remarks>
    /// Unfiltered by distance: the caller has the collision grid and can say what "near"
    /// means far better than a straight line can.
    /// </remarks>
    public virtual IReadOnlyList<HuntTarget> All(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.TryRead<uint>(HuntAddresses.SpriteTypeTable, out var table) || table == 0)
        {
            return [];
        }

        var self = process.TryRead<uint>(HuntAddresses.LocalPlayer, out var local) ? local : 0;
        var types = new Dictionary<ushort, byte>();
        var found = new List<HuntTarget>();

        HeapWalk.Records(
            process,
            HeapWalk.PlayerVtable,
            HuntAddresses.EntityLength,
            HeapWalk.HeapStart,
            HeapWalk.HeapEnd,
            (address, record) =>
            {
                if (address.Value == self)
                {
                    return;
                }

                if (Read(process, table, types, address, record) is { } target)
                {
                    found.Add(target);
                }
            });

        return found;
    }

    /// <summary>
    /// Whether a record is a monster, given the four fields and its sprite's type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each of these was watched separating on a live client. <c>+0x27</c> was set on the
    /// player and clear on both monsters beside it; <c>+0x58</c> was set on five corpses and
    /// clear on everything living; the sprite type was <c>0x0A</c> on every actor, 0 on
    /// thirty dropped items and 9 on the client's own effects.
    /// </para>
    /// <para>
    /// <c>+0x14</c> is an action code rather than a state, and only its dying value is worth
    /// anything: the same player record read 3 in one sample and 0 in another. Testing it
    /// against 8 is a test for "playing its death", not for "alive".
    /// </para>
    /// </remarks>
    internal static bool IsMonster(
        byte spriteType, byte action, byte isPlayer, byte isGone, uint flags = 0) =>
        (flags & HuntAddresses.Untouchable) == 0
        && spriteType == HuntAddresses.ActorType
        && isPlayer == 0
        && isGone == 0
        && action != HuntAddresses.DyingAction;

    /// <summary>
    /// The name out of a label.
    /// </summary>
    /// <remarks>
    /// A label reads <c>name#templateid:sprite</c>, though not always — one monster in the
    /// same scan as two others of its kind carried the bare name. The number in it is the
    /// template shared by every monster of a kind and is no use for telling two apart; the
    /// id for that is a field of its own.
    /// </remarks>
    internal static string NameFrom(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        var separator = label.IndexOf(LabelSeparator, StringComparison.Ordinal);

        return (separator < 0 ? label : label[..separator]).Trim();
    }

    private HuntTarget? Read(
        RemoteProcess process,
        uint table,
        Dictionary<ushort, byte> types,
        GameAddress address,
        ReadOnlySpan<byte> record)
    {
        var sprite = BitConverter.ToUInt16(record[HuntAddresses.EntitySprite..]);

        if (!types.TryGetValue(sprite, out var spriteType))
        {
            // One crossing per distinct sprite rather than per record: a screen holds a few
            // kinds of thing and a great many of each.
            spriteType = process.TryRead<byte>(new GameAddress(table + sprite), out var read)
                ? read
                : (byte)0;

            types[sprite] = spriteType;
        }

        if (!IsMonster(
                spriteType,
                record[HuntAddresses.EntityAction],
                record[HuntAddresses.EntityIsPlayer],
                record[HuntAddresses.EntityIsGone],
                BitConverter.ToUInt32(record[HuntAddresses.EntityFlags..])))
        {
            return null;
        }

        var label = BitConverter.ToUInt32(record[HuntAddresses.EntityLabel..]);

        if (label == 0)
        {
            // An actor with no label is the death animation or an effect the client is
            // still holding. The living all carry one.
            return null;
        }

        Span<byte> text = stackalloc byte[LabelLength];

        if (!process.TryReadBytes(new GameAddress(label), text))
        {
            return null;
        }

        var name = NameFrom(_codec.DecodeNullTerminated(text));

        if (name.Length == 0)
        {
            return null;
        }

        return new HuntTarget(
            address,
            BitConverter.ToUInt32(record[HuntAddresses.EntityId..]),
            name,
            BitConverter.ToInt32(record[HuntAddresses.EntityX..]),
            BitConverter.ToInt32(record[HuntAddresses.EntityY..]),
            record[HuntAddresses.EntityHealth],
            record[HuntAddresses.EntityAction]);
    }
}
