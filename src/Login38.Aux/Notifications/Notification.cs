namespace Login38.Aux.Notifications;

/// <summary>Which number is drifting up the middle of the screen.</summary>
public enum DriftKind
{
    /// <summary>Experience from a kill.</summary>
    Experience,

    /// <summary>Coins picked up.</summary>
    Gold,
}

/// <summary>
/// Something the server said that is worth showing the player.
/// </summary>
/// <remarks>
/// The client already knows about all of this — it is in the packets it receives — and
/// already chooses not to draw most of it. These are the two it stays quiet about that a
/// player wants to see: what was just picked up, and what the last kill was worth.
/// </remarks>
public abstract record Notification
{
    private Notification()
    {
    }

    /// <summary>An item collected, shown in the bottom-left corner.</summary>
    /// <param name="Sprite">Which icon the client draws for it.</param>
    /// <param name="Name">Its name, in the client's own code page.</param>
    public sealed record Toast(ushort Sprite, byte[] Name) : Notification;

    /// <summary>A number that drifts up the middle of the screen and fades.</summary>
    public sealed record Drift(DriftKind Kind, uint Amount) : Notification;
}

/// <summary>How a number is written where a player is meant to read it at a glance.</summary>
public static class Amounts
{
    /// <summary>Groups the digits in threes.</summary>
    /// <remarks>
    /// Its own function because the invariant culture's own grouping is what this wants and
    /// the current culture's is not: the number is drawn over a game, not in a form, and it
    /// should look the same wherever the launcher is running.
    /// </remarks>
    public static string WithCommas(uint amount) =>
        amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
