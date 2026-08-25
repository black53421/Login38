using System.Diagnostics;
using Login38.Core.Text;

namespace Login38.Aux.Notifications;

/// <summary>An item toast that is still on screen.</summary>
/// <param name="SpawnedAt">When it arrived.</param>
/// <param name="Sprite">Which icon the client draws for it.</param>
/// <param name="Name">Its name, already out of the client's code page.</param>
public readonly record struct LiveToast(TimeSpan SpawnedAt, ushort Sprite, string Name);

/// <summary>A number that is still drifting.</summary>
public readonly record struct LiveDrift(TimeSpan SpawnedAt, DriftKind Kind, uint Amount);

/// <summary>
/// What is on screen right now, and for how much longer.
/// </summary>
/// <remarks>
/// <para>
/// One per game rather than one per process. The reference keeps this in a process-wide
/// mutex, so two clients running side by side push into the same queue and each player sees
/// the other's pickups drift up their own screen.
/// </para>
/// <para>
/// Not thread-safe on its own: it is pushed from the pass that drains the hook and read by
/// the pass that draws, both on the helper loop.
/// </para>
/// </remarks>
public sealed class NotificationBoard
{
    /// <summary>How long a pickup toast stays up.</summary>
    public static readonly TimeSpan ToastLife = TimeSpan.FromSeconds(5);

    /// <summary>And a drifting number.</summary>
    public static readonly TimeSpan DriftLife = TimeSpan.FromMilliseconds(1_500);

    /// <summary>How many toasts are shown at once.</summary>
    /// <remarks>
    /// Ten stacked upwards from the bottom-left is most of the height of the screen. Past
    /// that the oldest goes, because a player who has just emptied a chest wants to see what
    /// came out last.
    /// </remarks>
    public const int MostToasts = 10;

    private readonly ILegacyTextCodec _codec;
    private readonly List<LiveToast> _toasts = [];
    private readonly List<LiveDrift> _drifts = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public NotificationBoard(ILegacyTextCodec codec) => _codec = codec;

    /// <summary>
    /// The clock everything on this board is timed against.
    /// </summary>
    /// <remarks>
    /// The board owns it so that whatever draws the board reads the same clock the entries
    /// were stamped with. Two clocks — one where things arrive and one where they are drawn
    /// — differ by however long the launcher took to start the second, and every fade is
    /// wrong by that much for the rest of the session.
    /// </remarks>
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>The toasts on screen, oldest first.</summary>
    public IReadOnlyList<LiveToast> Toasts => _toasts;

    /// <summary>And the numbers.</summary>
    public IReadOnlyList<LiveDrift> Drifts => _drifts;

    /// <summary>
    /// Takes one in.
    /// </summary>
    /// <remarks>
    /// The name is decoded here rather than where it is drawn. The reference decodes it on
    /// every frame of the five seconds a toast is up — thirty times a second, per toast,
    /// through the code-page heuristic — for a string that cannot change.
    /// </remarks>
    public void Push(Notification notification) => Push(notification, Elapsed);

    /// <inheritdoc cref="Push(Notification)"/>
    /// <param name="now">When it arrived.</param>
    public void Push(Notification notification, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        switch (notification)
        {
            case Notification.Toast toast:
                if (_toasts.Count >= MostToasts)
                {
                    _toasts.RemoveAt(0);
                }

                _toasts.Add(new LiveToast(now, toast.Sprite, _codec.DecodeNullTerminated(toast.Name)));
                break;

            case Notification.Drift number:
                Add(number, now);
                break;
        }
    }

    /// <summary>
    /// Adds a number to whichever one of its kind is already drifting.
    /// </summary>
    /// <remarks>
    /// There is never more than one of each kind. Killing quickly would otherwise stack a
    /// column of numbers up the screen, and what the player wants to know is what the run
    /// was worth rather than what each blow was. Landing on it also puts its clock back, so
    /// a total that is still growing does not fade halfway through.
    /// </remarks>
    private void Add(Notification.Drift number, TimeSpan now)
    {
        var at = _drifts.FindIndex(f => f.Kind == number.Kind);

        if (at < 0)
        {
            _drifts.Add(new LiveDrift(now, number.Kind, number.Amount));

            return;
        }

        var running = _drifts[at];
        var total = running.Amount + (ulong)number.Amount;

        _drifts[at] = running with
        {
            SpawnedAt = now,
            Amount = total > uint.MaxValue ? uint.MaxValue : (uint)total,
        };
    }

    /// <summary>Drops whatever has been up long enough.</summary>
    public void Tick() => Tick(Elapsed);

    /// <inheritdoc cref="Tick()"/>
    /// <param name="now">The moment to judge them against.</param>
    public void Tick(TimeSpan now)
    {
        _toasts.RemoveAll(t => now - t.SpawnedAt >= ToastLife);
        _drifts.RemoveAll(f => now - f.SpawnedAt >= DriftLife);
    }

    /// <summary>
    /// Everything on the board right now, as something another thread can read.
    /// </summary>
    /// <remarks>
    /// Taken on the thread that fills the board and handed to the one that draws it. The
    /// lists themselves are only ever touched here, so the copy is the whole of the
    /// synchronisation this needs.
    /// </remarks>
    public BoardSnapshot Snapshot() => new([.. _toasts], [.. _drifts]);

    /// <summary>Empties it, for a character leaving the world.</summary>
    public void Clear()
    {
        _toasts.Clear();
        _drifts.Clear();
    }
}

/// <summary>What was on the board at one moment.</summary>
/// <param name="Toasts">The pickups, oldest first.</param>
/// <param name="Drifts">And the numbers.</param>
public readonly record struct BoardSnapshot(
    IReadOnlyList<LiveToast> Toasts, IReadOnlyList<LiveDrift> Drifts)
{
    /// <summary>Whether there is anything to draw at all.</summary>
    public bool IsEmpty => Toasts.Count == 0 && Drifts.Count == 0;
}
