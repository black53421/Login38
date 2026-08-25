using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// Shows what each hit was worth, over the target or under it.
/// </summary>
/// <remarks>
/// <para>
/// Two switches and two sets of detours. The first draws the numbers at all; the second
/// moves them from over the target's head to under its feet, and only means anything when
/// the first is on — so asking for the second turns the first on with it.
/// </para>
/// <para>
/// Both caves are kept for the life of the game once allocated, including while the switch
/// is off. Freeing one is not safe: a thread can be inside it at the moment the detour is
/// taken out, and there is no way to be told when the last one has left.
/// </para>
/// </remarks>
public sealed class DamageToggle : IGameToggle
{
    private readonly ILogger<DamageToggle> _logger;

    private Cave? _numbers;
    private Cave? _feet;
    private bool _reported;

    public DamageToggle(ILogger<DamageToggle> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "damage-numbers";

    /// <inheritdoc/>
    public bool WantedBy(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Under the feet is a way of showing them, so asking for it asks for them.
        return settings.Misc.ShowAttackDamage || settings.Misc.DamageAtFeet;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Where the numbers go is not decided by this form, which has only the one answer to
    /// go on. Called this way they go over the target's head, which is where they go
    /// without the second switch.
    /// </remarks>
    public bool Apply(RemoteProcess process, bool wanted) => Apply(process, wanted, feet: false);

    /// <inheritdoc/>
    public bool Apply(RemoteProcess process, AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Apply(process, WantedBy(settings), settings.Misc.DamageAtFeet);
    }

    /// <summary>Applies both switches.</summary>
    /// <param name="numbers">Whether to draw the numbers.</param>
    /// <param name="feet">Whether to draw them under the target.</param>
    public bool Apply(RemoteProcess process, bool numbers, bool feet)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            // The one before the other in both directions: the numbers have to exist before
            // there is anything to move, and moving has to stop before they do or the last
            // bubble is left with its tail upside down.
            var ok = numbers ? Want(process, ref _numbers, Numbers) : Drop(process, ref _numbers);

            ok &= feet && numbers ? Want(process, ref _feet, Feet) : Drop(process, ref _feet);

            if (ok)
            {
                _reported = false;
            }

            return ok;
        }
        catch (GameProcessException e)
        {
            Complain(e);

            return false;
        }
    }

    /// <summary>What one set of detours needs to be able to take itself out again.</summary>
    private sealed record Cave(GameAddress Address, IReadOnlyList<(HookSite Site, byte[] Jump)> Sites);

    /// <summary>Puts a set of detours in, if it is not in already.</summary>
    private bool Want(RemoteProcess process, ref Cave? held, Func<RemoteProcess, Cave> install)
    {
        if (held is { } already && StillThere(process, already))
        {
            return true;
        }

        if (held is not null)
        {
            _logger.LogInformation("{Toggle}: the detours are no longer in place", Name);
            held = null;
        }

        held = install(process);

        _logger.LogInformation(
            "{Toggle}: {Count} detours in, cave at {Cave}", Name, held.Sites.Count, held.Address);

        return true;
    }

    /// <summary>Takes one out, in the opposite order to how it went in.</summary>
    private bool Drop(RemoteProcess process, ref Cave? held)
    {
        if (held is not { } cave)
        {
            return true;
        }

        var ok = true;

        foreach (var (site, jump) in cave.Sites.Reverse())
        {
            switch (site.Read(process, jump))
            {
                case SiteState.Ours:
                    process.WriteCode(site.Address, site.Stock);
                    break;

                case SiteState.Stock:
                    break;

                default:
                    // Somebody else's now. Writing the client's bytes over it would break
                    // whatever put it there, which is worse than leaving this on.
                    _logger.LogWarning(
                        "{Toggle}: {Address} is not what this launcher wrote; leaving it",
                        Name, site.Address);
                    ok = false;
                    break;
            }
        }

        // The cave itself stays allocated — a thread may still be running in it.
        held = null;

        return ok;
    }

    /// <summary>Whether every one of a set's sites still holds this launcher's jump.</summary>
    private static bool StillThere(RemoteProcess process, Cave cave) =>
        cave.Sites.All(entry => entry.Site.Read(process, entry.Jump) == SiteState.Ours);

    /// <summary>Writes the cave that draws the numbers, and detours into it.</summary>
    private static Cave Numbers(RemoteProcess process)
    {
        Expect(process, DamageCave.Single);
        Expect(process, DamageCave.Area);

        var cave = process.AllocateExecutable(DamageCave.CaveSize);
        var (code, entries) = DamageCave.Build(cave, TickCount(process));

        Fits(code, DamageCave.CaveSize, "damage numbers");
        process.WriteCode(cave, code);

        return new Cave(cave,
        [
            Detour(process, DamageCave.Single, cave + entries.Single),
            Detour(process, DamageCave.Area, cave + entries.Area),
        ]);
    }

    /// <summary>And the one that moves them.</summary>
    private static Cave Feet(RemoteProcess process)
    {
        Expect(process, FeetCave.Remote);
        Expect(process, FeetCave.Local);
        Expect(process, FeetCave.Tail);
        Expect(process, FeetCave.Keep);

        var cave = process.AllocateExecutable(FeetCave.CaveSize);
        var (code, entries) = FeetCave.Build(cave);

        Fits(code, FeetCave.CaveSize, "damage at feet");
        process.WriteCode(cave, code);

        return new Cave(cave,
        [
            Detour(process, FeetCave.Remote, cave + entries.Remote),
            Detour(process, FeetCave.Local, cave + entries.Local),
            Detour(process, FeetCave.Tail, cave + entries.Tail),
            Detour(process, FeetCave.Keep, cave + entries.Keep),
        ]);
    }

    /// <summary>
    /// Refuses to detour anything that is not what the client shipped.
    /// </summary>
    /// <remarks>
    /// Checked for every site before any of them is written, so a client this port does not
    /// recognise is left entirely alone rather than half detoured.
    /// </remarks>
    private static void Expect(RemoteProcess process, HookSite site)
    {
        if (site.Read(process, null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{site.Address} does not hold what this client is expected to have there.");
        }
    }

    private static void Fits(byte[] code, int size, string what)
    {
        if (code.Length > size)
        {
            throw new GameProcessException(
                $"The {what} cave is {code.Length} bytes and only {size} were reserved.");
        }
    }

    private static (HookSite, byte[]) Detour(RemoteProcess process, HookSite site, GameAddress target)
    {
        var jump = site.JumpTo(target);

        process.WriteCode(site.Address, jump);

        return (site, jump);
    }

    /// <summary>
    /// The game's own <c>kernel32!GetTickCount</c>.
    /// </summary>
    /// <remarks>
    /// Read out of the game's module and export tables rather than resolved here. The
    /// reference calls <c>GetProcAddress</c> in the launcher and writes that address into
    /// the client, on the grounds that the two processes share a base — which they usually
    /// do, and which is not something to bet a call inside somebody else's packet handler
    /// on.
    /// </remarks>
    private static GameAddress TickCount(RemoteProcess process)
    {
        var kernel = process.FindModule("kernel32.dll")
            ?? throw new GameProcessException("kernel32.dll is not loaded in the game.");

        return process.FindExport(kernel, "GetTickCount")
            ?? throw new GameProcessException($"kernel32.dll at {kernel} does not export GetTickCount.");
    }

    /// <summary>Says what went wrong, once per run of the same trouble.</summary>
    private void Complain(GameProcessException e)
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        _logger.LogWarning(e, "{Toggle} could not be applied", Name);
    }
}
