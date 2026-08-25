namespace Login38.Core.Servers;

/// <summary>
/// Everything the operator-distributed config files describe.
/// </summary>
/// <remarks>
/// Historically this was one file. It is now split: the server list goes to
/// <c>list.txt</c> in the plain <c>[list]</c> format the stock client can also read,
/// and the switches go to <c>config.ini</c>. Both are still parsed by the same reader,
/// so an older combined file keeps working.
/// </remarks>
public sealed class ListFile
{
    /// <summary>The most servers a <c>list.txt</c> can hold.</summary>
    public const int MaxServers = 8;

    public IList<ServerInfo> Servers { get; init; } = [];

    public AuxConfig Aux { get; init; } = new();

    public LauncherConfig Launcher { get; init; } = new();
}
