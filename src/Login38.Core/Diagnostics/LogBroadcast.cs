using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Login38.Core.Diagnostics;

/// <summary>
/// Default <see cref="ILogBroadcast"/>: a bounded channel per subscriber plus a
/// fixed-size ring of recent lines.
/// </summary>
public sealed class LogBroadcast : ILogBroadcast
{
    private const int RecentCapacity = 500;
    private const int SubscriberCapacity = 1024;

    private readonly Lock _gate = new();
    private readonly Queue<string> _recent = new(RecentCapacity);
    private readonly List<Channel<string>> _subscribers = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent];
            }
        }
    }

    /// <inheritdoc/>
    public void Publish(string line)
    {
        lock (_gate)
        {
            if (_recent.Count == RecentCapacity)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(line);

            // DropOldest on the channel means this never fails and never blocks, so a
            // stalled UI thread cannot back-pressure the code that is logging.
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(line);
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return line;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }
        }
    }
}
