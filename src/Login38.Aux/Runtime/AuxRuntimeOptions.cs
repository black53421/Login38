using Login38.Core.Servers;

namespace Login38.Aux.Runtime;

/// <summary>Operator-controlled helper values for one running game.</summary>
public sealed class AuxRuntimeOptions
{
    public TimeSpan FunctionKeyCooldown { get; private set; } =
        TimeSpan.FromMilliseconds(AuxConfig.FunctionKeyCooldownBounds.Default);

    public void Load(AuxConfig aux)
    {
        ArgumentNullException.ThrowIfNull(aux);

        var milliseconds = Math.Clamp(
            aux.FunctionKeyCooldownMs,
            AuxConfig.FunctionKeyCooldownBounds.Min,
            AuxConfig.FunctionKeyCooldownBounds.Max);

        FunctionKeyCooldown = TimeSpan.FromMilliseconds(milliseconds);
    }
}
