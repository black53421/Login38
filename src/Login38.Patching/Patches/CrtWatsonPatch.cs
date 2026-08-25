using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Stops the Visual C++ 2008 runtime terminating the client over an invalid argument.
/// </summary>
/// <remarks>
/// <para>
/// The CRT's parameter validation ends in <c>_invoke_watson</c>, which does not return:
/// it tears the process down with <c>0xC0000417</c>, no message and no chance to
/// recover. The client trips it on inputs it has always produced — a string routine
/// handed a length it never checked — and the older runtimes it shipped against were
/// more forgiving. On a modern machine that shows up as the game vanishing mid-session.
/// </para>
/// <para>
/// Replacing the whole function with <c>ret</c> makes validation failures no-ops. That
/// is the right trade here: whatever the caller does with a bad result is survivable, and
/// killing the process is not. <c>_invoke_watson</c> is <c>__cdecl</c>, so the caller
/// cleans the stack and a bare <c>ret</c> is balanced.
/// </para>
/// </remarks>
public sealed class CrtWatsonPatch : IGamePatch
{
    private const string CrtModule = "msvcr90.dll";

    private const string WatsonExport = "_invoke_watson";

    private const byte Return = 0xC3;

    /// <summary>Statically imported, so it is loaded before the client's own code runs.</summary>
    private static readonly TimeSpan ModuleWaitTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ModuleWaitPoll = TimeSpan.FromMilliseconds(100);

    private readonly ILogger<CrtWatsonPatch> _logger;

    public CrtWatsonPatch(ILogger<CrtWatsonPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "crt-watson-bypass";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;

        var crt = process.WaitForModuleAsync(CrtModule, ModuleWaitTimeout, ModuleWaitPoll, cancellationToken)
            .GetAwaiter().GetResult()
            ?? throw new GameProcessException(
                $"{CrtModule} was not loaded within {ModuleWaitTimeout.TotalSeconds:0}s.");

        var watson = process.FindExport(crt, WatsonExport)
            ?? throw new GameProcessException($"{CrtModule} at {crt} does not export {WatsonExport}.");

        // Reported rather than silently skipped: this patch reporting success against a
        // client it never touched is how a crash gets misattributed later.
        if (process.Read<byte>(watson) == Return)
        {
            _logger.LogDebug("{Export} at {Address} was already neutralised", WatsonExport, watson);
            return;
        }

        process.WriteCode(watson, [Return]);
        _logger.LogInformation("{Export} at {Address} now returns instead of terminating", WatsonExport, watson);
    }
}
