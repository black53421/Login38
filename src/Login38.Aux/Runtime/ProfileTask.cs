using Login38.Aux.Settings;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Loads a character's settings when they enter the world and saves them when they leave.
/// </summary>
/// <remarks>
/// <para>
/// Which character is playing is not known at launch: the player picks one several screens
/// in, and can come back out and pick a different one without restarting the game. So this
/// watches for the name to change rather than reading it once.
/// </para>
/// <para>
/// Leaving the world is the only reliable moment to save. The launcher can be closed at any
/// time, and the game can exit on its own — neither gives anything to hook, and both leave
/// the character screen behind first.
/// </para>
/// </remarks>
public sealed class ProfileTask : IAuxTask, IAuxTaskShutdown
{
    private readonly ProfileStore _store;
    private readonly AuxSettingsSource _settings;
    private readonly ILogger<ProfileTask> _logger;

    private string? _character;

    public ProfileTask(ProfileStore store, AuxSettingsSource settings, ILogger<ProfileTask> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "profile";

    /// <summary>Once a second. Nothing here is worth noticing faster than that.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <summary>Whose settings are loaded, or null before a character is in the world.</summary>
    public string? Character => _character;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var character = context.Character;

        if (character == _character)
        {
            return;
        }

        // Left the world, or swapped characters. Either way what is in memory belongs to
        // whoever was playing a moment ago.
        if (_character is not null)
        {
            _store.Save(_character, _settings.Current);
        }

        _character = character;

        if (character is null)
        {
            _logger.LogInformation("No character in the world; helper settings are idle");
            return;
        }

        _settings.Publish(_store.Load(character));
    }

    /// <summary>
    /// Saves whatever is loaded, for a game that has stopped being watched.
    /// </summary>
    /// <remarks>
    /// The loop stops when the game exits, which is after the character has already gone —
    /// so without this, quitting straight from the world would lose the session's changes.
    /// </remarks>
    public void Stopping()
    {
        if (_character is not null)
        {
            _store.Save(_character, _settings.Current);
        }
    }
}
