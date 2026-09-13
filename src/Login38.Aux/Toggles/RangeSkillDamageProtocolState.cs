using Login38.Interop;

namespace Login38.Aux.Toggles;

public sealed class RangeSkillDamageProtocolState
{
    public readonly record struct Session(
        GameAddress Cave,
        GameAddress SingleEntry,
        GameAddress AreaEnabled);

    private readonly object _gate = new();
    private readonly Dictionary<uint, Session> _sessions = [];

    public void Set(uint processId, Session session)
    {
        lock (_gate)
        {
            _sessions[processId] = session;
        }
    }

    public bool TryGet(uint processId, out Session session)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(processId, out session);
        }
    }
}
