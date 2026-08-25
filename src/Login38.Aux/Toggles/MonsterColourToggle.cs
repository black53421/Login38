using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// Colours a monster's name by how far above the player it is.
/// </summary>
/// <remarks>
/// <para>
/// Two halves. This one puts the detours in the client and holds the table they consult;
/// <see cref="MonsterScan"/> decides what each monster's colour should be and writes it.
/// They are separate because they run on different clocks — the detours go in once and
/// stay, and the colours change as the world moves.
/// </para>
/// <para>
/// Nothing is written to the client until every site has been checked, and any jump that
/// did go in is taken out again if a later one fails. A half-installed set is worse than
/// none: the client would be jumping into a cave that was never finished.
/// </para>
/// </remarks>
public sealed class MonsterColourToggle : IGameToggle
{
    private readonly MonsterScan _scan;
    private readonly ILogger<MonsterColourToggle> _logger;

    private Installed? _installed;
    private bool _reported;

    public MonsterColourToggle(MonsterScan scan, ILogger<MonsterColourToggle> logger)
    {
        _scan = scan;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "monster-colours";

    /// <summary>Where the list of coloured entities lives, once the detours are in.</summary>
    /// <remarks>Null while the feature is off, which is the scanner's cue to do nothing.</remarks>
    public GameAddress? MarkerTable => _installed?.MarkerTable;

    /// <inheritdoc/>
    public bool WantedBy(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Misc.MonsterLevelColour;
    }

    /// <inheritdoc/>
    public bool Apply(RemoteProcess process, bool wanted)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            var ok = wanted ? Want(process) : Drop(process);

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

    /// <summary>What the detours need to be able to take themselves out again.</summary>
    private sealed record Installed(GameAddress MarkerTable, IReadOnlyList<Detour> Sites);

    /// <summary>One site, and the jump this launcher put there.</summary>
    private readonly record struct Detour(HookSite Site, byte[] Jump);

    /// <summary>Puts the detours in, if they are not in already.</summary>
    private bool Want(RemoteProcess process)
    {
        if (_installed is { } already
            && already.Sites.All(entry => entry.Site.Read(process, entry.Jump) == SiteState.Ours))
        {
            return true;
        }

        if (_installed is not null)
        {
            _logger.LogInformation("{Toggle}: the detours are no longer in place", Name);
            _installed = null;
        }

        // Every site first, before anything is written: a client this port does not
        // recognise is left entirely alone rather than half detoured.
        Expect(process, MonsterNameCave.NameRender);
        Expect(process, MonsterNameCave.TextDraw);
        Expect(process, MonsterNameCave.CompactTextDraw);

        var markers = process.AllocateData(MonsterNameCave.MarkerTableBytes);

        process.WriteBytes(markers, MonsterNameCave.MarkerTable([]));

        var name = Cave(process, MonsterNameCave.NameCaveSize, "monster name render",
            cave => MonsterNameCave.BuildNameRender(cave, markers));

        var text = Cave(process, MonsterNameCave.TextCaveSize, "selected name colour",
            cave => MonsterNameCave.BuildTextColour(
                cave, markers, MonsterNameCave.TextDraw, MonsterNameCave.SelectedReturns));

        var compact = Cave(process, MonsterNameCave.TextCaveSize, "selected name colour (compact)",
            cave => MonsterNameCave.BuildTextColour(
                cave, markers, MonsterNameCave.CompactTextDraw, MonsterNameCave.CompactReturns));

        _installed = new Installed(markers, Detours(process,
        [
            (MonsterNameCave.NameRender, name),
            (MonsterNameCave.TextDraw, text),
            (MonsterNameCave.CompactTextDraw, compact),
        ]));

        _logger.LogInformation(
            "{Toggle}: {Count} detours in, marker table at {Table}", Name, _installed.Sites.Count, markers);

        return true;
    }

    /// <summary>Takes them out, colours first.</summary>
    /// <remarks>
    /// That order matters: with the detours gone, a monster still carrying one of the four
    /// colours is drawn in it, unreadably, by the client's own path.
    /// </remarks>
    private bool Drop(RemoteProcess process)
    {
        if (_installed is not { } installed)
        {
            return true;
        }

        var restored = _scan.RestoreAll(process);
        var ok = true;

        foreach (var (site, jump) in installed.Sites.Reverse())
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
                        "{Toggle}: {Address} is not what this launcher wrote; leaving it", Name, site.Address);
                    ok = false;
                    break;
            }
        }

        // The caves and the marker table stay allocated — a thread may still be in one.
        _installed = null;

        _logger.LogInformation("{Toggle}: out, {Restored} colours put back", Name, restored);

        return ok;
    }

    /// <summary>
    /// Writes each jump, undoing the ones already written if a later one fails.
    /// </summary>
    private static List<Detour> Detours(
        RemoteProcess process, IReadOnlyList<(HookSite Site, GameAddress Cave)> sites)
    {
        List<Detour> written = [];

        try
        {
            foreach (var (site, cave) in sites)
            {
                var jump = site.JumpTo(cave);

                process.WriteCode(site.Address, jump);
                written.Add(new Detour(site, jump));
            }
        }
        catch (GameProcessException)
        {
            foreach (var (site, jump) in written.AsEnumerable().Reverse())
            {
                if (site.Read(process, jump) == SiteState.Ours)
                {
                    process.WriteCode(site.Address, site.Stock);
                }
            }

            throw;
        }

        return written;
    }

    /// <summary>Allocates a cave, builds its code and writes it.</summary>
    private static GameAddress Cave(
        RemoteProcess process, int size, string what, Func<GameAddress, byte[]> build)
    {
        var cave = process.AllocateExecutable(size);
        var code = build(cave);

        if (code.Length > size)
        {
            throw new GameProcessException(
                $"The {what} cave is {code.Length} bytes and only {size} were reserved.");
        }

        process.WriteCode(cave, code);

        return cave;
    }

    /// <summary>Refuses to detour anything that is not what the client shipped.</summary>
    private static void Expect(RemoteProcess process, HookSite site)
    {
        if (site.Read(process, null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{site.Address} does not hold what this client is expected to have there.");
        }
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
