using Login38.Core.Configuration;
using Login38.Core.Servers;

namespace Login38.App.Services;

/// <summary>What the player asked for when they pressed start.</summary>
/// <param name="GameDirectory">Where the client and its data files live.</param>
/// <param name="Server">The server to connect to.</param>
/// <param name="Aux">The operator's feature switches, from the server list.</param>
/// <param name="Windowed">Whether to run in a window rather than fullscreen.</param>
/// <param name="WindowMode">Which window size, when windowed.</param>
public sealed record GameLaunchRequest(
    string GameDirectory,
    ServerInfo Server,
    AuxConfig Aux,
    bool Windowed,
    WindowMode WindowMode);

/// <summary>Why a launch did not start.</summary>
public enum LaunchOutcome
{
    Started,

    /// <summary>Every slot in the multi-instance limit is taken.</summary>
    InstanceLimitReached,
}

/// <param name="Outcome">Whether the game started.</param>
/// <param name="Session">The running game, when it did.</param>
/// <param name="InstanceLimit">The cap that refused it, when it did not.</param>
public sealed record LaunchResult(LaunchOutcome Outcome, GameSession? Session, uint InstanceLimit = 0)
{
    internal static LaunchResult Refused(uint limit) => new(LaunchOutcome.InstanceLimitReached, null, limit);
}
