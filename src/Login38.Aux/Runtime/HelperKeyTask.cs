using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// The two keys the helper answers to, inside the game.
/// </summary>
/// <remarks>
/// <para>
/// <b>Insert</b> starts and stops the helper. Launching a client is not asking for one: a
/// player may be logging in to look at something, or playing a character they do not want
/// drinking potions on their behalf. Nothing acts on the game until this has been pressed.
/// </para>
/// <para>
/// <b>Home</b> shows the settings and hides them again. It is the only way to them — the
/// launcher does not offer them, because they belong to the character playing rather than
/// to the client. Hidden rather than closed, so the window comes back with whatever was
/// selected still selected.
/// </para>
/// <para>
/// The <b>first</b> Home of a game also starts the helper. Reaching for the settings before
/// anything is running is a player who wants it running, and making them find a second key
/// to say so is a step that answers a question they have already answered. Every Home after
/// that is only the window; the switch is Insert from then on.
/// </para>
/// <para>
/// Two keys rather than one, and this is the second attempt: one key doing both meant
/// stopping the helper also took the window away, and wanting the settings without wanting
/// the helper running was not expressible at all.
/// </para>
/// <para>
/// Neither is a keyboard hook, unlike the macro keys. A hook swallows the key, which is
/// right for F1 to F4 — those are the launcher's and the client should never see them —
/// and wrong here, because both of these are ordinary keys the client may have its own use
/// for. This reads the machine's key state instead and takes nothing away.
/// </para>
/// <para>
/// That state is global, so a launcher driving one client would otherwise answer a key
/// pressed in another. Hence the check that the game this task belongs to is the one in
/// front — the same check, and for the same reason, that the reference made.
/// </para>
/// <para>
/// Both only once the character is in the world, as the reference had it. Everything the
/// helper does is done to a character — the potions it drinks, the buffs it keeps up, the
/// line it writes in the chat window — and there is none at the login or selection screen.
/// A window brought up there would be showing settings for nobody.
/// </para>
/// </remarks>
public sealed class HelperKeyTask : IAuxTask
{
    private readonly HelperSwitch _switch;
    private readonly ILogger<HelperKeyTask> _logger;
    private readonly Func<int, bool> _keyDown;
    private readonly Func<uint> _foreground;

    private bool _switchDown;
    private bool _windowDown;

    public HelperKeyTask(HelperSwitch helperSwitch, ILogger<HelperKeyTask> logger)
        : this(helperSwitch, logger, KeyboardState.IsDown, static () => GameWindow.ForegroundProcessId)
    {
    }

    /// <param name="helperSwitch">The switch Insert moves.</param>
    /// <param name="logger">Where a press is recorded.</param>
    /// <param name="keyDown">Whether a given virtual key is held down at this moment.</param>
    /// <param name="foreground">Which process owns the window the player is using.</param>
    internal HelperKeyTask(
        HelperSwitch helperSwitch,
        ILogger<HelperKeyTask> logger,
        Func<int, bool> keyDown,
        Func<uint> foreground)
    {
        _switch = helperSwitch;
        _logger = logger;
        _keyDown = keyDown;
        _foreground = foreground;

        // Where the keys already are, so a launcher started from a keyboard shortcut does
        // not count what is still held down as a press the moment the loop begins.
        _switchDown = keyDown(KeyboardState.Insert);
        _windowDown = keyDown(KeyboardState.Home);
    }

    /// <summary>Raised on the helper's loop thread when Home asks for the settings.</summary>
    /// <remarks>
    /// Not the loop's thread to show a window on. Whoever subscribes owns getting this onto
    /// whichever thread its windows live on.
    /// </remarks>
    public event EventHandler? WindowRequested;

    /// <inheritdoc/>
    public string Name => "helper keys";

    /// <summary>
    /// Every pass.
    /// </summary>
    /// <remarks>
    /// Reading a key state is a call and a bit test, and a key that is only looked at twice
    /// a second is a key that has to be held down to work.
    /// </remarks>
    public TimeSpan Interval => AuxHost.Cadence;

    /// <summary>The one task that has to run before the helper has been started.</summary>
    public bool RunsWhileOff => true;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Both edges are taken whether or not they are acted on. A press made while the
        // player was in something else has happened, and holding a key down across an
        // alt-tab should not fire the moment the game comes forward.
        var startStop = Pressed(KeyboardState.Insert, ref _switchDown);
        var window = Pressed(KeyboardState.Home, ref _windowDown);

        if (!Ours(_foreground(), context.Process.Id) || !context.IsInWorld)
        {
            return;
        }

        if (startStop)
        {
            _logger.LogInformation("Insert: the helper was switched {State}", _switch.Toggle() ? "on" : "off");
        }

        if (window)
        {
            // The first time, this starts it as well. Ordered so that the window is asked
            // for after the switch has moved, which is what puts the settings on screen
            // already showing a helper that is running.
            if (!_switch.HasBeenOn)
            {
                _logger.LogInformation("Home: the helper was started along with its settings");

                _switch.Toggle();
            }
            else
            {
                _logger.LogInformation("Home: the settings were asked for");
            }

            WindowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Pressed(int key, ref bool held)
    {
        var down = _keyDown(key);
        var pressed = down && !held;

        held = down;

        return pressed;
    }

    /// <summary>Whether the window in front belongs to the game this task is watching.</summary>
    /// <remarks>
    /// Zero is "nothing is in front", which is not this game however tempting the
    /// comparison. It happens while a window is being switched, which is exactly when a
    /// key press is least likely to have been meant for the game.
    /// </remarks>
    internal static bool Ours(uint foreground, uint game) => foreground != 0 && foreground == game;
}
