using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Gives the game window a different title on every launch.
/// </summary>
/// <remarks>
/// <para>
/// Third-party tools locate this client with <c>FindWindow</c> against the fixed title it
/// ships with. Changing it costs nothing — the client never reads its own title — and
/// removes the simplest way of finding the window from outside.
/// </para>
/// <para>
/// Not a security measure, and not treated as one. Anything determined enough to enumerate
/// windows by process finds it anyway. It is here because the cheapest tools do not.
/// </para>
/// </remarks>
public sealed class WindowTitlePatch : IGamePatch
{
    private readonly ILogger<WindowTitlePatch> _logger;

    public WindowTitlePatch(ILogger<WindowTitlePatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "window-title";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.WindowVisible;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.AntiCheatBasic;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var window = context.Window
            ?? throw new GameProcessException("The game window is not known yet.");

        var title = GameWindow.RandomTitle(GameWindow.TitleSeed(window.ProcessId));
        window.SetTitle(title);

        _logger.LogInformation("Renamed the game window from {Old} to {New}", window.InitialTitle, title);
    }
}
