namespace Login38.Core.Diagnostics;

/// <summary>
/// A live feed of formatted log lines, for the in-app log view.
/// </summary>
/// <remarks>
/// Subscribers are independent and never block the logger: a subscriber that stops
/// reading loses old lines rather than stalling the thread that is writing them.
/// </remarks>
public interface ILogBroadcast
{
    /// <summary>
    /// The most recent lines, oldest first. Lets a log window opened mid-session
    /// show what already happened instead of starting blank.
    /// </summary>
    IReadOnlyList<string> Recent { get; }

    /// <summary>
    /// Streams lines emitted from now on. Enumerating twice yields two independent
    /// streams; disposing the enumerator unsubscribes.
    /// </summary>
    IAsyncEnumerable<string> SubscribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Publishes a line to every subscriber and to the recent-line buffer.</summary>
    void Publish(string line);
}
