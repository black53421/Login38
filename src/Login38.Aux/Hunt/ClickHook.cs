using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Hunt;

/// <summary>
/// Puts <see cref="ClickDetour"/> into the client and asks it for clicks.
/// </summary>
/// <remarks>
/// <para>
/// One of these belongs to one game, for the same reason <see cref="ChaseHook"/> does: two
/// clients running means two caves, and a process-wide record of what was displaced would
/// describe one of them.
/// </para>
/// <para>
/// The cave outlives the hook on purpose. Taking the jump out puts the client's own bytes
/// back, but a thread can be part way through the trampoline at that moment and has to be
/// able to finish returning through it. A leaked page is the cheaper mistake.
/// </para>
/// </remarks>
public sealed class ClickHook
{
    private readonly ILogger<ClickHook> _logger;

    private GameAddress? _cave;
    private ClickDetour.Layout _layout;
    private byte[]? _jump;
    private bool _reported;

    public ClickHook(ILogger<ClickHook> logger) => _logger = logger;

    /// <summary>Whether the jump is in the client right now.</summary>
    public bool Installed => _jump is not null;

    /// <summary>
    /// Puts the hook in, if it is not already there.
    /// </summary>
    /// <returns>Whether the client can be asked for a click.</returns>
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
            if (Installed && ClickDetour.Site.Read(process, _jump) == SiteState.Ours)
            {
                return true;
            }

            _jump = null;

            var state = ClickDetour.Site.Read(process, ours: null);

            if (state != SiteState.Stock)
            {
                return Report(
                    $"the scheduler's pump at {ClickDetour.Site.Address} does not hold the bytes " +
                    "this client shipped, so the click hook has been left out.");
            }

            _cave ??= process.AllocateExecutable(ClickDetour.LayoutFor(new GameAddress(0)).Size);
            _layout = ClickDetour.LayoutFor(_cave.Value);

            // Nothing has been asked for yet, so the detour is a no-op from the moment it is
            // reachable. That ordering is the point: the cave has to be complete and inert
            // before the jump starts sending the client through it, and this site is reached
            // every frame rather than occasionally.
            process.WriteCode(_cave.Value, ClickDetour.Build(_cave.Value));

            var jump = ClickDetour.Site.JumpTo(_cave.Value);

            process.WriteCode(ClickDetour.Site.Address, jump);
            _jump = jump;
            _reported = false;

            _logger.LogInformation(
                "click hook installed; {Site} now goes through {Cave}",
                ClickDetour.Site.Address, _cave.Value);

            return true;
        }
        catch (GameProcessException e)
        {
            return Report(e.Message);
        }
    }

    /// <summary>
    /// Asks the client to act on what it has been told, at its next frame.
    /// </summary>
    /// <remarks>
    /// One write. Everything that happens because of it happens on the game's own thread,
    /// in the order the client does it, which is the whole reason the hook exists.
    /// </remarks>
    public bool Request(RemoteProcess process) => Ask(process, ClickDetour.Clicking);

    /// <summary>
    /// Asks for the walk alone, at a destination already written.
    /// </summary>
    /// <remarks>
    /// For crossing ground rather than for chasing. A whole click would re-lock first and
    /// decide for itself where to go, which throws away a waypoint chosen precisely because
    /// the client's own idea of where to go walks into a wall.
    /// </remarks>
    public bool Steer(RemoteProcess process) => Ask(process, ClickDetour.Walking);

    private bool Ask(RemoteProcess process, uint kind)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!Installed)
        {
            return false;
        }

        try
        {
            process.WriteBytes(_layout.Request, BitConverter.GetBytes(kind));

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
    /// The request is cleared first. Between restoring the bytes and the client's next frame
    /// there is no detour left to take one, so a request left behind would be one the client
    /// acts on the next time this launcher installs the hook.
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
            process.WriteBytes(_layout.Request, BitConverter.GetBytes(0u));
            process.WriteCode(ClickDetour.Site.Address, ClickDetour.Site.Stock);
            _jump = null;

            _logger.LogInformation("click hook removed");
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
            _logger.LogWarning("click hook: {Message}", message);
            _reported = true;
        }

        return false;
    }
}
