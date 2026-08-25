using System.Globalization;
using Login38.Aux.Actions;
using Login38.Aux.Game;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Tells the player what each kill was worth.
/// </summary>
/// <remarks>
/// <para>
/// Written into the game's own chat window rather than shown in the launcher, because a
/// player watching this is watching the game. Four numbers on one green line: what the kill
/// gave, how much more to the next percent, how much more to the next level, and the total
/// since the helper started.
/// </para>
/// <para>
/// No line is written for levelling up. The last two numbers jump to the new level's range
/// on their own, which is more obvious than a message and does not need one.
/// </para>
/// </remarks>
public sealed class ExperienceTask : IAuxTask
{
    /// <summary>The client's own mark for green.</summary>
    internal const string Green = GameChat.GreenMark;

    private readonly GameChat _chat;
    private readonly ILogger<ExperienceTask> _logger;
    private readonly ExperienceWatch _watch = new();

    public ExperienceTask(GameChat chat, ILogger<ExperienceTask> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "experience";

    /// <summary>
    /// Ten times a second.
    /// </summary>
    /// <remarks>
    /// The fastest thing the helper does. Two kills half a second apart would otherwise be
    /// reported as one, and what a single kill was worth is the number this exists for.
    /// </remarks>
    public TimeSpan Interval => AuxHost.Cadence;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var wanted = context.Settings.ShowExperience;

        // Switching off works anywhere, including on the character screen. Switching on has
        // to wait for the world: before then the total reads as zero, and the first kill
        // after that would be reported as the whole of the character's experience.
        if (!context.IsInWorld)
        {
            if (!wanted && _watch.Watching)
            {
                _watch.Stop();
            }

            return;
        }

        if (!wanted)
        {
            if (_watch.Watching)
            {
                _watch.Stop();
            }

            return;
        }

        if (Experience.Read(context.Process) is not { } total)
        {
            return;
        }

        if (!_watch.Watching)
        {
            _watch.Start(total);
            _logger.LogInformation(
                "Watching experience from {Total} (level {Level})", total, Experience.LevelOf(total));

            return;
        }

        if (_watch.Look(total) is { } report)
        {
            _chat.Write(context.Process, Line(report));
        }
    }

    /// <summary>The line as the player sees it.</summary>
    internal static string Line(ExperienceReport report) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Green}{report.Gained} / {report.ToNextPercent} / {report.ToLevel} / {report.Session}");
}
