using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Hunt;

/// <summary>
/// Puts <see cref="ChaseDetour"/> into the client and aims it.
/// </summary>
/// <remarks>
/// <para>
/// One of these belongs to one game. The reference kept the equivalent in a process-wide
/// static, which with two clients running means the second one's hook overwrites the
/// first one's record of what it displaced.
/// </para>
/// <para>
/// The cave outlives the hook on purpose. Taking the jump out puts the client's own bytes
/// back, but a thread can be part way through the trampoline at that moment and has to be
/// able to finish returning through it. A leaked page is the cheaper mistake.
/// </para>
/// </remarks>
public sealed class ChaseHook
{
    private readonly ILogger<ChaseHook> _logger;

    private GameAddress? _cave;
    private ChaseDetour.Layout _layout;
    private byte[]? _jump;
    private bool _reported;

    public ChaseHook(ILogger<ChaseHook> logger) => _logger = logger;

    /// <summary>Whether the jump is in the client right now.</summary>
    public bool Installed => _jump is not null;

    /// <summary>
    /// Puts the hook in, if it is not already there.
    /// </summary>
    /// <returns>Whether the client is now chasing through this hook.</returns>
    /// <remarks>
    /// Safe to call on every pass. What is asked is whether the client still holds this
    /// launcher's jump, not whether this object thinks it wrote one: the game can be
    /// restarted and another tool can take the site.
    /// </remarks>
    public bool Install(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (Installed && ChaseDetour.Site.Read(process, _jump) == SiteState.Ours)
            {
                return true;
            }

            _jump = null;

            var state = ChaseDetour.Site.Read(process, ours: null);

            if (state != SiteState.Stock)
            {
                // Either another tool is standing on the re-lock routine, or this is not
                // the client this was built against. Writing over either one breaks
                // something that is not ours to break.
                return Report(
                    $"the re-lock routine at {ChaseDetour.Site.Address} does not hold the bytes " +
                    "this client shipped, so the chase hook has been left out.");
            }

            _cave ??= process.AllocateExecutable(ChaseDetour.LayoutFor(new GameAddress(0)).Size);
            _layout = ChaseDetour.LayoutFor(_cave.Value);

            // Nothing is aimed at yet, so the detour is a no-op from the moment it is
            // reachable. That ordering is the point: the cave has to be complete and inert
            // before the jump starts sending the client through it.
            process.WriteCode(_cave.Value, ChaseDetour.Build(_cave.Value));

            var jump = ChaseDetour.Site.JumpTo(_cave.Value);
            process.WriteCode(ChaseDetour.Site.Address, jump);
            _jump = jump;
            _reported = false;

            _logger.LogInformation(
                "chase hook installed; {Site} now goes through {Cave}",
                ChaseDetour.Site.Address, _cave.Value);

            return true;
        }
        catch (GameProcessException e)
        {
            return Report(e.Message);
        }
    }

    /// <summary>
    /// Pins the client to a monster, or lets it go when given zero.
    /// </summary>
    /// <remarks>
    /// Cheap enough to call on every pass. Writing the same value again costs one crossing
    /// and keeps the caller from having to remember what it last asked for — though the
    /// hunt does remember, because it has to know when the target changed for other
    /// reasons.
    /// </remarks>
    public bool Aim(RemoteProcess process, uint target)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!Installed)
        {
            return false;
        }

        try
        {
            process.WriteBytes(_layout.Target, BitConverter.GetBytes(target));

            return true;
        }
        catch (GameProcessException e)
        {
            return Report(e.Message);
        }
    }

    /// <summary>
    /// Takes the hook out and puts the client's own prologue back.
    /// </summary>
    /// <remarks>
    /// Aims at nothing first. Between restoring the bytes and the client's next re-lock
    /// there is no detour left to read the slot, so clearing it afterwards would leave the
    /// client chasing whatever it was last told about.
    /// </remarks>
    public void Remove(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!Installed)
        {
            return;
        }

        try
        {
            Aim(process, 0);
            process.WriteCode(ChaseDetour.Site.Address, ChaseDetour.Site.Stock);
            _jump = null;

            _logger.LogInformation("chase hook removed");
        }
        catch (GameProcessException e)
        {
            Report(e.Message);
        }
    }

    /// <summary>Logs once per spell of trouble rather than once per pass.</summary>
    private bool Report(string message)
    {
        if (!_reported)
        {
            _logger.LogWarning("chase hook: {Message}", message);
            _reported = true;
        }

        return false;
    }
}
