using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>Something in the world worth attacking.</summary>
/// <param name="Address">Where its record is, which is only good for this pass.</param>
/// <param name="Id">The id a packet aims at, from <c>+0x0C</c>.</param>
/// <param name="Name">What it is called, for the blacklist and the log.</param>
/// <param name="X">Its tile, as the client stores it.</param>
/// <param name="Y">Its tile, as the client stores it.</param>
/// <param name="Health">
/// How much of it is left, as a percentage, from <c>+0x59</c> — or
/// <see cref="HuntAddresses.HealthUnknown"/> if the server has never said, which means
/// nothing has ever hurt it.
/// </param>
/// <param name="Action">
/// What the server said it was doing when it spawned it, from
/// <see cref="HuntAddresses.EntityAction"/>. Zero for anything ordinary; the values that
/// matter are the hidden ones. See <see cref="TargetPicker.Hittable"/>.
/// </param>
public readonly record struct HuntTarget(
    GameAddress Address, uint Id, string Name, int X, int Y, byte Health, byte Action = 0);
