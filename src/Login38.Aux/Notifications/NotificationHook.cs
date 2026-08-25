using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Notifications;

/// <summary>
/// Keeps the detour in the game and reads what it caught.
/// </summary>
/// <remarks>
/// One per game. The reference keeps the cave address and the read position in a
/// process-wide static, and hands the caller a second copy of both that is never advanced —
/// so with two clients up, one player's pickups are drained by the other's launcher.
/// </remarks>
public sealed class NotificationHook
{
    private readonly ILogger<NotificationHook> _logger;

    private GameAddress? _cave;
    private byte[]? _patch;
    private uint _read;

    public NotificationHook(ILogger<NotificationHook> logger) => _logger = logger;

    /// <summary>Whether the detour is in the game.</summary>
    public bool Installed => _cave is not null;

    /// <summary>
    /// Puts the detour in, if it is not in already.
    /// </summary>
    /// <remarks>
    /// The six bytes are written with the client's threads suspended. It is one branch, and
    /// a thread stopped part way through reading it while it is half rewritten jumps to a
    /// displacement that is half of each.
    /// </remarks>
    /// <returns>Whether the detour is in the game now.</returns>
    public bool Install(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (_cave is not null && NotificationHookCave.Site.Read(process, _patch) == SiteState.Ours)
        {
            return true;
        }

        if (NotificationHookCave.Site.Read(process, null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{NotificationHookCave.Site.Address} does not hold the branch this was written against.");
        }

        var cave = process.AllocateExecutable(NotificationHookCave.CaveSize);
        var code = NotificationHookCave.Build(cave);

        if (code.Length > NotificationHookCave.CodeSize)
        {
            throw new GameProcessException(
                $"The notification cave is {code.Length} bytes and only {NotificationHookCave.CodeSize} were reserved.");
        }

        // The whole cave, so the ring starts empty rather than holding whatever was last at
        // that address — which would read as caught packets.
        process.WriteCode(cave, NotificationHookCave.Empty());
        process.WriteCode(cave + NotificationHookCave.CodeOffset, code);

        var patch = NotificationHookCave.Redirect(cave);

        using (process.SuspendThreads())
        {
            process.WriteCode(NotificationHookCave.Site.Address, patch);
        }

        _cave = cave;
        _patch = patch;
        _read = 0;

        _logger.LogInformation("Watching for pickups and kills, ring at {Cave}", cave);

        return true;
    }

    /// <summary>Takes it out again.</summary>
    /// <remarks>The cave stays allocated: a thread may be inside it.</remarks>
    public void Remove(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (_cave is null)
        {
            return;
        }

        if (NotificationHookCave.Site.Read(process, _patch) == SiteState.Ours)
        {
            using (process.SuspendThreads())
            {
                process.WriteCode(NotificationHookCave.Site.Address, NotificationHookCave.Site.Stock);
            }

            _logger.LogInformation("No longer watching for pickups and kills");
        }
        else
        {
            _logger.LogWarning(
                "{Address} is not what this launcher wrote; leaving it", NotificationHookCave.Site.Address);
        }

        _cave = null;
        _patch = null;
    }

    /// <summary>Forgets the detour without touching the game.</summary>
    public void Forget()
    {
        _cave = null;
        _patch = null;
    }

    /// <summary>
    /// Reads whatever has arrived since the last pass.
    /// </summary>
    /// <remarks>
    /// The whole ring in one crossing rather than one per slot. Sixteen slots is a
    /// kilobyte and a quarter, which is one read either way.
    /// </remarks>
    public IReadOnlyList<Notification> Drain(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (_cave is not { } cave
            || !process.TryRead<uint>(cave + NotificationHookCave.TailOffset, out var written)
            || written == _read)
        {
            return [];
        }

        var behind = written - _read;

        if (behind > NotificationHookCave.RingLength)
        {
            _logger.LogWarning(
                "{Count} packets arrived faster than they could be read", behind - NotificationHookCave.RingLength);

            _read = written - NotificationHookCave.RingLength;
        }

        var ring = new byte[NotificationHookCave.RingLength * NotificationHookCave.SlotSize];

        if (!process.TryReadBytes(cave + NotificationHookCave.RingOffset, ring))
        {
            return [];
        }

        List<Notification> caught = [];

        for (; _read != written; _read++)
        {
            var slot = (int)(_read % NotificationHookCave.RingLength) * NotificationHookCave.SlotSize;

            if (Read(ring.AsSpan(slot, NotificationHookCave.SlotSize), _read) is { } notification)
            {
                caught.Add(notification);
            }
        }

        return caught;
    }

    /// <summary>
    /// Turns one slot back into a packet the reader understands.
    /// </summary>
    /// <remarks>
    /// The sequence number the detour claimed has to be the one being asked for. A slot the
    /// ring has come back round to still holds a whole packet from its last lap, and a slot
    /// claimed for a packet with no pointer to copy holds nothing at all.
    /// </remarks>
    internal static Notification? Read(ReadOnlySpan<byte> slot, uint expected)
    {
        if (slot.Length < NotificationHookCave.SlotSize
            || BitConverter.ToUInt32(slot[NotificationHookCave.SequenceOffset..]) != expected)
        {
            return null;
        }

        // The opcode is the sub-id the reader keys on, and the payload follows it — which
        // is the shape it would have had on the wire.
        Span<byte> packet = stackalloc byte[1 + NotificationHookCave.PayloadBytes];

        packet[0] = slot[0];
        slot.Slice(NotificationHookCave.PayloadOffset, NotificationHookCave.PayloadBytes).CopyTo(packet[1..]);

        return PacketBox.Parse(packet);
    }
}
