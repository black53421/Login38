using System.Diagnostics;
using Login38.Aux.Actions;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Says the player's messages on a loop.
/// </summary>
/// <remarks>
/// <para>
/// For the hours a character spends standing in a market with something to sell. The
/// messages go round in turn rather than repeating one, so a stall can list several things
/// without anyone reading the same line twice in a row.
/// </para>
/// <para>
/// On the ordinary channel, not the shouting one, despite what the setting is called. That
/// is what the player asked for: a shout goes to the whole map and gets an account muted;
/// this is meant to be heard by whoever is standing there.
/// </para>
/// </remarks>
public sealed class ShoutTask : IAuxTask
{
    private readonly GameActions _actions;
    private readonly ILogger<ShoutTask> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan? _lastSaid;
    private int _next;

    public ShoutTask(GameActions actions, ILogger<ShoutTask> logger)
    {
        _actions = actions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "shout";

    /// <summary>Twice a second. The interval itself is in whole seconds.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <summary>Which message is next, for the window to show.</summary>
    public int Next => _next;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Settings;

        if (!settings.ShoutEnabled || settings.ShoutMessages.Count == 0 || settings.ShoutIntervalSeconds == 0)
        {
            return;
        }

        if (!context.IsInWorld)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.ShoutIntervalSeconds);

        // Never said anything yet means say something now, so switching it on is visible.
        if (_lastSaid is { } last && _clock.Elapsed - last < interval)
        {
            return;
        }

        // Taken modulo the count each time rather than kept in range, because the player
        // can edit the list between one message and the next.
        var index = _next % settings.ShoutMessages.Count;
        var message = settings.ShoutMessages[index];

        _logger.LogInformation(
            "Saying {Index} of {Count}: {Message}", index + 1, settings.ShoutMessages.Count, message);

        _actions.Say(context.Process, ChatChannel.Normal, message);

        _next = (index + 1) % settings.ShoutMessages.Count;
        _lastSaid = _clock.Elapsed;
    }
}
