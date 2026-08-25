using Login38.Interop;
using Login38.Patching;

namespace Login38.Aux.Game;

/// <summary>
/// What the player looks like right now.
/// </summary>
/// <param name="HitPoints">Current and maximum.</param>
/// <param name="ManaPoints">Current and maximum.</param>
/// <param name="FoodPercent">How full, as the client shows it.</param>
/// <param name="WeightPercent">How loaded, as the client shows it.</param>
/// <param name="MapId">Which map they are standing on.</param>
/// <remarks>
/// A snapshot, taken by reading the client's memory rather than by hooking anything. The
/// helper features poll it, so nothing has to wait for the game to call out and nothing
/// runs on the client's own threads.
/// </remarks>
public readonly record struct PlayerState(
    Gauge HitPoints, Gauge ManaPoints, byte FoodPercent, byte WeightPercent, uint MapId)
{
    /// <summary>Whether this is a reading of a character rather than of an empty world.</summary>
    /// <remarks>
    /// A character always has a maximum above zero. Before the player picks one, and for a
    /// moment after a map change, the fields are zero — and a rule like "drink below 50%"
    /// would fire on every one of those ticks.
    /// </remarks>
    public bool IsInWorld => HitPoints.Maximum > 0;
}

/// <summary>A current value against its maximum.</summary>
/// <param name="Current">What it is.</param>
/// <param name="Maximum">What it can be.</param>
public readonly record struct Gauge(uint Current, uint Maximum)
{
    /// <summary>How full, rounded down, or zero when there is no maximum yet.</summary>
    public uint Percent => Maximum == 0 ? 0 : Math.Min(100, Current * 100 / Maximum);

    /// <inheritdoc/>
    public override string ToString() => $"{Current}/{Maximum}";
}

/// <summary>Reads <see cref="PlayerState"/> out of the running client.</summary>
public static class PlayerStateReader
{
    /// <summary>Takes a snapshot.</summary>
    /// <returns>
    /// A default <see cref="PlayerState"/> when the player is not in the world, which is
    /// what every caller has to handle anyway.
    /// </returns>
    public static PlayerState Read(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.TryRead<uint>(GameAddresses.GameState, out var state)
            || state != GameAddresses.InWorld)
        {
            return default;
        }

        return new PlayerState(
            ReadGauge(process, GameStructures.HitPointsObject, GameStructures.MaximumHitPoints),
            ReadGauge(process, GameStructures.ManaPointsObject, GameStructures.MaximumManaPoints),
            Scale(process, GameStructures.FoodLevel, GameStructures.FoodLevelFull),
            Scale(process, GameStructures.Weight, GameStructures.WeightFull),
            process.TryRead<uint>(GameStructures.MapId, out var map) ? map : 0);
    }

    private static Gauge ReadGauge(RemoteProcess process, GameAddress current, GameAddress maximum) =>
        new(ObfuscatedStat.TryRead(process, current, out var value) ? value : 0,
            process.TryRead<uint>(maximum, out var limit) ? limit : 0);

    /// <summary>
    /// Turns one of the client's raw bytes into the percentage it displays.
    /// </summary>
    /// <remarks>
    /// The divisors are not 255. They are what the client itself divides by, so a rule
    /// written against what the player can see means what they think it means.
    /// </remarks>
    private static byte Scale(RemoteProcess process, GameAddress address, uint full)
    {
        Span<byte> raw = stackalloc byte[1];

        return process.TryReadBytes(address, raw) ? (byte)Math.Min(100u, raw[0] * 100u / full) : (byte)0;
    }
}
