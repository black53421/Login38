namespace Login38.Aux.Notifications;

/// <summary>
/// Reads the server's catch-all packet, which is where both of these arrive.
/// </summary>
/// <remarks>
/// The server sends a great many different things down one opcode with a sub-id as the
/// first byte. Two of those sub-ids are worth showing: one names an item that went into the
/// bag, and one carries experience or coin from a kill.
/// </remarks>
public static class PacketBox
{
    /// <summary>An item went into the bag.</summary>
    public const byte ItemBoard = 190;

    /// <summary>Something was gained from a kill.</summary>
    public const byte ShowDrop = 192;

    /// <summary>
    /// The longest item name that will be shown.
    /// </summary>
    /// <remarks>
    /// Not a limit the protocol has — a limit on what a server can make this launcher hold
    /// and draw. The client's own names are well inside it.
    /// </remarks>
    public const int LongestName = 64;

    /// <summary>
    /// Reads one, or null for anything this does not show.
    /// </summary>
    /// <param name="payload">From the sub-id byte onwards.</param>
    public static Notification? Parse(ReadOnlySpan<byte> payload) =>
        payload.IsEmpty ? null : payload[0] switch
        {
            ItemBoard => Picked(payload[1..]),
            ShowDrop => Gained(payload[1..]),
            _ => null,
        };

    /// <summary>A sprite id and a NUL-terminated name.</summary>
    /// <remarks>
    /// A name with no terminator is refused rather than shown up to the clamp. The packet
    /// is malformed either way, and a name that runs on is more likely to be a different
    /// packet shape than a long item.
    /// </remarks>
    private static Notification.Toast? Picked(ReadOnlySpan<byte> body)
    {
        if (body.Length < 3)
        {
            return null;
        }

        var name = body[2..];
        var end = name.IndexOf((byte)0);

        return end < 0
            ? null
            : new Notification.Toast(
                BitConverter.ToUInt16(body), name[..Math.Min(end, LongestName)].ToArray());
    }

    /// <summary>A kind byte and a four-byte amount.</summary>
    private static Notification.Drift? Gained(ReadOnlySpan<byte> body)
    {
        if (body.Length < 5)
        {
            return null;
        }

        return body[0] switch
        {
            0 => new Notification.Drift(DriftKind.Experience, BitConverter.ToUInt32(body[1..])),
            1 => new Notification.Drift(DriftKind.Gold, BitConverter.ToUInt32(body[1..])),
            _ => null,
        };
    }
}
