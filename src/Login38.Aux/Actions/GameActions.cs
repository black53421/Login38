using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Actions;

/// <summary>
/// Does, inside the game, the things a player would do by hand.
/// </summary>
/// <remarks>
/// <para>
/// Each one is a short piece of code written into the game, run on a thread of its own and
/// then taken away again. Nothing is left behind between actions — no hook, no patched
/// byte, nothing for the client to find — which is why this rather than a resident hook,
/// and it is also why every action pays for an allocation and a thread.
/// </para>
/// <para>
/// The alternative the reference tried first and abandoned is worth recording, because it
/// looks like the obvious approach: hook the client's packet dispatcher and drive it from
/// there. The dispatcher is dead code in the packed client, and a hook that did run found
/// the three function addresses it needed had all moved, because the packer rearranges
/// them. Reaching in from outside per action sidesteps both.
/// </para>
/// </remarks>
/// <remarks>
/// Not sealed, and every action is virtual. What a helper feature decides is "use this
/// now", and the only way to exercise that decision is to be able to see what it asked for
/// without a client to ask.
/// </remarks>
public class GameActions
{
    /// <summary>
    /// How long to wait for one action to come back.
    /// </summary>
    /// <remarks>
    /// Generous. Everything here should finish in a few milliseconds; a wait this long is
    /// only ever hit by an address that is wrong, and then the message matters more than
    /// the delay.
    /// </remarks>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>How much of the client's own function is watched for change.</summary>
    private const int WatchedLength = 16;

    private readonly ILegacyTextCodec _codec;
    private readonly ILogger<GameActions> _logger;

    private byte[]? _watched;

    public GameActions(ILegacyTextCodec codec, ILogger<GameActions> logger)
    {
        _codec = codec;
        _logger = logger;
    }

    /// <summary>Uses an item, the way a double-click in the bag does.</summary>
    /// <param name="entry">The item's entry in the bag, not its id.</param>
    /// <remarks>
    /// This one calls the client rather than sending a packet, so it does everything the
    /// client would: the animation, the sound, the count going down.
    /// </remarks>
    public virtual void UseItem(RemoteProcess process, GameAddress entry)
    {
        ArgumentNullException.ThrowIfNull(process);

        WatchTheFunction(process);

        Run(process, Calls.OneArgument(GameFunctions.UseItem, entry.Value), nameof(UseItem));
    }

    /// <summary>Uses an item by sending the packet instead of calling the client.</summary>
    /// <remarks>
    /// For the items whose client-side path expects state a thread arriving from outside
    /// does not have.
    /// </remarks>
    public virtual void SendUseItem(RemoteProcess process, uint itemId) =>
        Run(process, PacketCall.Send(
            "cdc"u8,
            PacketArgument.Number(GameFunctions.UseItemOpcode),
            PacketArgument.Number(itemId),
            PacketArgument.Number(0)), nameof(SendUseItem));

    /// <summary>Uses one item on another — a whetstone on a wielded weapon, and the like.</summary>
    /// <param name="source">The thing being used up.</param>
    /// <param name="target">The thing it is used on.</param>
    /// <remarks>
    /// The order is the reference's, and it was got wrong once: sent the other way round,
    /// the server reads the weapon as the source and unequips it.
    /// </remarks>
    public virtual void UseOn(RemoteProcess process, uint source, uint target) =>
        Run(process, PacketCall.Send(
            "cdd"u8,
            PacketArgument.Number(GameFunctions.UseItemOpcode),
            PacketArgument.Number(source),
            PacketArgument.Number(target)), nameof(UseOn));

    /// <summary>Uses a scroll that needs to be told what to turn into.</summary>
    /// <param name="choice">What to become, as the client spells it.</param>
    public virtual void UseWithOption(RemoteProcess process, uint itemId, string choice)
    {
        ArgumentException.ThrowIfNullOrEmpty(choice);

        Run(process, PacketCall.Send(
            "cds"u8,
            PacketArgument.Number(GameFunctions.UseItemOpcode),
            PacketArgument.Number(itemId),
            PacketArgument.Text8(_codec.Encode(choice, LegacyEncoding.Big5))), nameof(UseWithOption));
    }

    /// <summary>
    /// Uses a teleport scroll.
    /// </summary>
    /// <remarks>
    /// Zero for the destination is what the server reads as "somewhere random", which is
    /// what a scroll does with no bookmark chosen.
    /// </remarks>
    public virtual void UseTeleportScroll(RemoteProcess process, uint itemId) =>
        Run(process, PacketCall.Send(
            "cdhhh"u8,
            PacketArgument.Number(GameFunctions.UseItemOpcode),
            PacketArgument.Number(itemId),
            PacketArgument.Number(TeleportByScroll),
            PacketArgument.Number(0),
            PacketArgument.Number(0)), nameof(UseTeleportScroll));

    /// <summary>What the destination field holds for a scroll rather than a bookmark.</summary>
    private const uint TeleportByScroll = 4;

    /// <summary>
    /// Answers a teleport the server has offered.
    /// </summary>
    /// <remarks>
    /// Some servers send the offer after a scroll is used and wait for this before moving
    /// the character at all. Sending it when nothing is pending is harmless.
    /// </remarks>
    public virtual void ConfirmTeleport(RemoteProcess process) =>
        Run(process, PacketCall.Send(
            "c"u8, PacketArgument.Number(GameFunctions.TeleportOpcode)), nameof(ConfirmTeleport));

    /// <summary>Throws an item away.</summary>
    /// <param name="count">
    /// How many, which for anything that stacks must be the whole stack — the server reads
    /// zero as "throw away none of them".
    /// </param>
    /// <remarks>
    /// Sends the finished packet rather than opening the client's "how many?" dialog, which
    /// would need a second packet and an answer from the player.
    /// </remarks>
    public virtual void Drop(RemoteProcess process, uint itemId, uint count) =>
        Run(process, PacketCall.Send(
            "cdd"u8,
            PacketArgument.Number(GameFunctions.DeleteItemOpcode),
            PacketArgument.Number(itemId),
            PacketArgument.Number(count)), nameof(Drop));

    /// <summary>Says something.</summary>
    /// <remarks>The message is encoded for the client's own code page before it is embedded.</remarks>
    public virtual void Say(RemoteProcess process, ChatChannel channel, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        Run(process, PacketCall.Send(
            "ccs"u8,
            PacketArgument.Number(GameFunctions.ChatOpcode),
            PacketArgument.Number((byte)channel),
            PacketArgument.Text8(_codec.Encode(message, Encoding))), nameof(Say));
    }

    /// <summary>Casts a skill.</summary>
    /// <param name="packed">The id the client's own spell table gives it.</param>
    /// <param name="record">
    /// Where the skill's own book record is, when it has been checked to still be that
    /// skill's, so its icon cools with the cast. Left out and the cast is the same in every
    /// way the server can see.
    /// </param>
    public virtual void Cast(
        RemoteProcess process, uint packed, SkillTarget target, GameAddress record = default) =>
        Run(process, SkillCast.Build(packed, target, record), nameof(Cast));

    /// <summary>Which code page the client is reading text as.</summary>
    private LegacyEncoding Encoding =>
        _codec.Mode == TextEncodingMode.Gbk ? LegacyEncoding.Gbk : LegacyEncoding.Big5;

    /// <summary>
    /// Notices the client's own function moving out from under this.
    /// </summary>
    /// <remarks>
    /// The packer can re-encrypt or relocate a page between one login and the next. Calling
    /// a function that is no longer there crashes the game, so the first sixteen bytes are
    /// remembered and compared — this cannot say what the address should be, but it can say
    /// that it is not what it was.
    /// </remarks>
    private void WatchTheFunction(RemoteProcess process)
    {
        var current = process.ReadBytes(GameFunctions.UseItem, WatchedLength);

        if (_watched is null)
        {
            _watched = current;
            _logger.LogInformation(
                "UseItem at {Address} starts {Bytes}",
                GameFunctions.UseItem, BytePattern.Format(current));

            return;
        }

        if (!_watched.AsSpan().SequenceEqual(current))
        {
            throw new GameProcessException(
                $"UseItem at {GameFunctions.UseItem} now starts {BytePattern.Format(current)} " +
                $"rather than {BytePattern.Format(_watched)}; it has been moved or re-encrypted.");
        }
    }

    private void Run(RemoteProcess process, byte[] code, string what)
    {
        ArgumentNullException.ThrowIfNull(process);

        var exit = RemoteCall.Run(process, code, Timeout);

        _logger.LogDebug("{Action} returned {Exit:X8}", what, exit);
    }
}

/// <summary>Calls into the client, as opposed to sending it a packet.</summary>
internal static class Calls
{
    /// <summary>
    /// <c>function(argument)</c>, C calling convention.
    /// </summary>
    /// <remarks>
    /// Eighteen bytes, and every one of them earns its place: the registers are saved and
    /// restored because this runs on a thread the client did not make, and the stack is
    /// cleaned because a C function leaves that to its caller.
    /// </remarks>
    internal static byte[] OneArgument(GameAddress function, uint argument) =>
        new ShellcodeBuilder(new GameAddress(0))
            .PushAd()
            .PushImm32(argument)
            .MovEax(function.Value)
            .CallEax()
            .AddEsp(4)
            .PopAd()
            .Ret()
            .Build();
}
