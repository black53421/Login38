namespace Login38.Core.Morph.SmoothRun;

/// <summary>What a pass over a morph table changed.</summary>
/// <param name="Sprites">How many sprite entries the table has.</param>
/// <param name="Walking">How many walk without a run cycle of their own.</param>
/// <param name="Running">How many carry a run cycle to be borrowed.</param>
/// <param name="Converted">How many came out with slots 98 and 99.</param>
public readonly record struct SmoothRunReport(int Sprites, int Walking, int Running, int Converted);

/// <summary>
/// Rewrites a morph table so the client can play a run animation.
/// </summary>
/// <remarks>
/// <para>
/// The client animates a character by picking one action slot and playing it. Walking is a
/// slot; running is not — the client has no concept of it, and under a speed effect it plays
/// the walk faster, which reads as a shuffle rather than a run.
/// </para>
/// <para>
/// A run cycle is two animations, one leading with each foot. This finds them wherever the
/// table happens to keep them, writes them into two slots the client does not otherwise use,
/// and leaves the walk alone. A hook inside the client alternates between the two slots
/// while a speed effect is up, and falls back to the untouched walk when it is not.
/// </para>
/// <para>
/// Five stages, each of which can be read and tested on its own:
/// </para>
/// <list type="number">
/// <item><see cref="SpriteFileParser"/> — what is on each line.</item>
/// <item><see cref="SpriteClassifier"/> — which sprites walk and which run.</item>
/// <item><see cref="RunPairExtractor"/> — where each run cycle's two halves are.</item>
/// <item><see cref="RunPairMatcher"/> — which sprite gets which run cycle.</item>
/// <item><see cref="SmoothRunEmitter"/> — the table, written back out.</item>
/// </list>
/// </remarks>
public static class SmoothRunPreprocessor
{
    /// <summary>Rewrites a table.</summary>
    /// <returns>The new table and a summary of what it changed.</returns>
    public static (string Text, SmoothRunReport Report) Process(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var file = SpriteFileParser.Parse(text);
        var roles = SpriteClassifier.Classify(file);
        var extracted = RunPairExtractor.Extract(file, roles);
        var matched = RunPairMatcher.Match(file, roles, extracted);

        var report = new SmoothRunReport(
            file.Sprites.Count,
            roles.Values.Count(role => role == SpriteRole.Walk),
            roles.Values.Count(role => role is SpriteRole.Run or SpriteRole.Both),
            matched.Count);

        return (SmoothRunEmitter.Emit(file, matched), report);
    }
}
