using System.Globalization;
using Login38.Aux.Game;
using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// Every global the hunt depends on, on one line.
/// </summary>
/// <remarks>
/// <para>
/// Written because the loop of diagnosing this from outside has cost more than the reading
/// ever would. A character that has locked on to something and will not walk to it looks
/// identical, from a screenshot, to one that cannot reach it, one the server has frozen,
/// and one whose walk engine is alive but aimed at the square it is already standing on.
/// The globals tell those four apart in one glance and nothing else does.
/// </para>
/// <para>
/// So the hunt says all of them at the moment it gives up on a target, rather than leaving
/// the next report to be answered with another guess. It is one line, and only on the
/// giving-up path — an ordinary fight never reaches it.
/// </para>
/// <para>
/// Names match <c>tools/state.ps1</c>, which reads the same addresses from outside, so a
/// log line and a hand dump can be compared without translating between them.
/// </para>
/// </remarks>
internal static class ClientState
{
    /// <summary>Everything, as <c>name=value</c> pairs.</summary>
    internal static string Describe(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var fields = new List<string>
        {
            $"auto_attack={Byte(process, HuntAddresses.AutoAttack)}",
            $"walk_target_valid={Byte(process, HuntAddresses.WalkTargetValid)}",
            $"attack_target={Word(process, HuntAddresses.AttackTarget)}",
            $"hover_target={Word(process, HuntAddresses.HoverTarget)}",
            $"interaction_mode={Word(process, HuntAddresses.InteractionMode)}",
            $"attack_in_flight={Word(process, HuntAddresses.AttackInFlight)}",
            $"autoattack_running={Word(process, HuntAddresses.AutoAttackRunning)}",
            $"cast_queued={Byte(process, HuntAddresses.CastQueued)}",
            $"tick_pending={Byte(process, HuntAddresses.TickPending)}",
            $"kick_a={Byte(process, HuntAddresses.KickBlockerA)}",
            $"kick_b={Byte(process, HuntAddresses.KickBlockerB)}",
            $"attack_reach={Byte(process, HuntAddresses.AttackReach)}",
            $"next_attack_tick={Word(process, HuntAddresses.NextAttackTick)}",
            $"dest=({Word(process, HuntAddresses.DestinationX)},"
                + $"{Word(process, HuntAddresses.DestinationY)})",
            $"movement_blocked={Obfuscated(process, HuntAddresses.MovementBlocked)}",
            $"attack_blocked={Obfuscated(process, HuntAddresses.AttackBlocked)}",
        };

        return string.Join(' ', fields);
    }

    private static string Byte(RemoteProcess process, GameAddress address) =>
        process.TryRead<byte>(address, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : "?";

    private static string Word(RemoteProcess process, GameAddress address) =>
        process.TryRead<uint>(address, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : "?";

    /// <summary>Through the client's own key-array obfuscation, or a question mark.</summary>
    private static string Obfuscated(RemoteProcess process, GameAddress address) =>
        ObfuscatedStat.TryRead(process, address, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : "?";

    /// <summary>Where the character is standing, in grid cells.</summary>
    /// <returns>Null when the client has no character, which is most of a loading screen.</returns>
    internal static (int X, int Y)? Where(RemoteProcess process) =>
        At(process, HuntAddresses.LocalPlayer);

    /// <summary>Where something pointed at by a global is standing.</summary>
    internal static (int X, int Y)? At(RemoteProcess process, GameAddress pointer)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.TryRead<uint>(pointer, out var record) || record == 0
            || !process.TryRead<int>(new GameAddress(record + HuntAddresses.EntityX), out var x)
            || !process.TryRead<int>(new GameAddress(record + HuntAddresses.EntityY), out var y))
        {
            return null;
        }

        return (x, y);
    }
}
