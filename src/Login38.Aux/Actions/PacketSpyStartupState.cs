using System.Collections.Concurrent;
using Login38.Interop;

namespace Login38.Aux.Actions;

/// <summary>
/// Shares startup packet-recorder state between the startup patch and the helper loop.
/// </summary>
public sealed class PacketSpyStartupState
{
    private readonly ConcurrentDictionary<uint, Session> _sessions = new();

    internal void Set(uint processId, Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[processId] = session;
    }

    internal bool TryGet(uint processId, out Session session) =>
        _sessions.TryGetValue(processId, out session!);

    internal void Remove(uint processId) => _sessions.TryRemove(processId, out _);

    internal sealed record Session(GameAddress Cave, byte[] Jump);
}
