using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// The four small things the status page does.
/// </summary>
/// <remarks>
/// <para>
/// Eating when hungry, taking an antidote when poisoned, sharpening a weapon that is
/// wearing out, and staying transformed. Together rather than separately because they share
/// the same guard and the same reading of the bag, and because none of them is more than a
/// condition and an action.
/// </para>
/// <para>
/// Each keeps its own interval. Eating and sharpening are quick and want to be caught up
/// with; an antidote and a transformation are answered by the server with an animation, so
/// sending a second before the first has landed wastes it.
/// </para>
/// </remarks>
public sealed class StatusTask : IAuxTask
{
    /// <summary>What the client calls the food this eats.</summary>
    internal const string Meat = "肉";

    /// <summary>And the stone that repairs a weapon.</summary>
    internal const string Whetstone = "磨刀石";

    /// <summary>
    /// What a weapon's description says when it can be sharpened.
    /// </summary>
    /// <remarks>
    /// There is no field for wear. The client writes it into the text it shows when the
    /// item is pointed at, and this phrase appearing there is the whole signal.
    /// </remarks>
    internal const string Wearing = "損壞度";

    /// <summary>Quick, because a character who is hungry stops healing.</summary>
    internal static readonly TimeSpan EatingInterval = TimeSpan.FromSeconds(1);

    /// <summary>Likewise — the sharpening goes out roughly once a second.</summary>
    internal static readonly TimeSpan SharpeningInterval = TimeSpan.FromSeconds(1);

    /// <summary>Long enough for the server to answer and the animation to run.</summary>
    internal static readonly TimeSpan PoisonInterval = TimeSpan.FromSeconds(5);

    /// <inheritdoc cref="PoisonInterval"/>
    internal static readonly TimeSpan TransformInterval = TimeSpan.FromSeconds(5);

    /// <summary>How much of a description is read.</summary>
    private const int DescriptionLength = 256;

    private readonly GameActions _actions;
    private readonly Spells _spells;
    private readonly ILegacyTextCodec _codec;
    private readonly ILogger<StatusTask> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// What has already been complained about, so it is complained about once.
    /// </summary>
    /// <remarks>
    /// The alternative is to start the cooldown on a failed attempt, which keeps the log
    /// quiet by not trying again — and a rule that is not tried again is a rule that does
    /// nothing for five seconds after the moment it became possible.
    /// </remarks>
    private readonly HashSet<string> _said = [];

    private TimeSpan? _ate;
    private TimeSpan? _cured;
    private TimeSpan? _sharpened;
    private TimeSpan? _transformed;

    public StatusTask(
        GameActions actions, Spells spells, ILegacyTextCodec codec, ILogger<StatusTask> logger)
    {
        _actions = actions;
        _spells = spells;
        _codec = codec;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "status";

    /// <summary>Twice a second, which is twice the fastest of the four intervals.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Settings;

        if (!settings.EatWhenHungry && !settings.AntidoteEnabled
            && !settings.KeepWeaponSharp && !settings.TransformEnabled)
        {
            return;
        }

        if (!context.IsInWorld)
        {
            return;
        }

        var now = _clock.Elapsed;

        Eat(context, now);
        Cure(context, now);
        Sharpen(context, now);
        Transform(context, now);
    }

    /// <summary>Whether something on its own interval is due.</summary>
    /// <remarks>Never having happened counts as due, so switching a feature on does it.</remarks>
    internal static bool Due(TimeSpan? last, TimeSpan interval, TimeSpan now) =>
        last is not { } previous || now - previous >= interval;

    /// <summary>
    /// Eats when the character is anything short of full.
    /// </summary>
    /// <remarks>
    /// No threshold to set: a character who is not full heals more slowly, and the food is
    /// cheap. The reading is a byte out of 225 rather than a percentage.
    /// </remarks>
    private void Eat(AuxContext context, TimeSpan now)
    {
        if (!context.Settings.EatWhenHungry || !Due(_ate, EatingInterval, now))
        {
            return;
        }

        if (!context.Process.TryRead<byte>(GameStructures.FoodLevel, out var full)
            || full >= GameStructures.FoodLevelFull)
        {
            return;
        }

        if (InventoryReader.FindByName(context.Bag, Meat) is not { } meat)
        {
            if (_said.Add(Meat))
            {
                _logger.LogInformation("Hungry at {Full}/{Of}, but there is no 肉 in the bag",
                    full, GameStructures.FoodLevelFull);
            }

            return;
        }

        _said.Remove(Meat);

        // Started when something was actually sent, not when it was decided that something
        // should be. Marked before the bag was searched, "the meat has not been read in
        // yet" became a second of standing there hungry — and for the two five-second
        // rules below, five.
        _ate = now;

        _logger.LogInformation(
            "Eating {Meat} at {Full}/{Of}", meat.Name, full, GameStructures.FoodLevelFull);

        _actions.UseItem(context.Process, meat.Entry);
    }

    /// <summary>Takes whatever the player nominated when poison starts doing damage.</summary>
    private void Cure(AuxContext context, TimeSpan now)
    {
        var settings = context.Settings;

        if (!settings.AntidoteEnabled || string.IsNullOrWhiteSpace(settings.AntidoteItem))
        {
            return;
        }

        if (!Due(_cured, PoisonInterval, now) || !Poison.IsDamaging(context.Process))
        {
            return;
        }

        if (Fire(context, settings.AntidoteItem, "poisoned"))
        {
            _cured = now;
        }
    }

    /// <summary>
    /// Uses the nominated item or casts the nominated skill.
    /// </summary>
    /// <remarks>
    /// A skill goes at the player whatever suffix they wrote, and an item is simply used.
    /// This is not the general dispatcher: there is exactly one sensible target for curing
    /// yourself, and honouring a suffix here would only let a mistake in a settings box aim
    /// an antidote at a weapon.
    /// </remarks>
    /// <returns>Whether anything was actually sent.</returns>
    private bool Fire(AuxContext context, string command, string because)
    {
        var entry = HelperEntrySyntax.Parse(command);

        if (entry.Kind == EntryKind.Skill)
        {
            if (_spells.Find(context.Process, entry.Name) is not { } packed)
            {
                if (_said.Add(entry.Name))
                {
                    _logger.LogInformation("{Name} has not been learned", entry.Name);
                }

                return false;
            }

            _said.Remove(entry.Name);

            _logger.LogInformation("{Because}: casting {Name}", because, entry.Name);

            _actions.Cast(context.Process, packed, SkillTarget.Self);

            return true;
        }

        if (InventoryReader.FindByName(context.Bag, entry.Name) is not { } item)
        {
            if (_said.Add(entry.Name))
            {
                _logger.LogInformation("{Because}, but there is no {Name} in the bag", because, entry.Name);
            }

            return false;
        }

        _said.Remove(entry.Name);

        _logger.LogInformation("{Because}: using {Name}", because, item.Name);

        _actions.UseItem(context.Process, item.Entry);

        return true;
    }

    /// <summary>
    /// Sharpens the weapon being swung once it starts to wear.
    /// </summary>
    /// <remarks>
    /// As a packet rather than through the client's own repair routine: that routine reads
    /// state from the object it is called on, which is not set up on a thread the launcher
    /// started, and the half-built packet it sends gets the account disconnected.
    /// </remarks>
    private void Sharpen(AuxContext context, TimeSpan now)
    {
        if (!context.Settings.KeepWeaponSharp || !Due(_sharpened, SharpeningInterval, now))
        {
            return;
        }

        if (InventoryReader.Find(context.Bag, item => item.IsWielded) is not { } weapon)
        {
            return;
        }

        if (!Worn(context, weapon))
        {
            return;
        }

        if (InventoryReader.FindByName(context.Bag, Whetstone) is not { } stone)
        {
            if (_said.Add(Whetstone))
            {
                _logger.LogInformation(
                    "{Weapon} is wearing out, but there is no 磨刀石 in the bag", weapon.Name);
            }

            return;
        }

        _said.Remove(Whetstone);
        _sharpened = now;

        _logger.LogInformation("Sharpening {Weapon} with {Stone}", weapon.Name, stone.Name);

        _actions.UseOn(context.Process, stone.Param, weapon.Param);
    }

    /// <summary>
    /// Whether a weapon's own description says it can be sharpened.
    /// </summary>
    /// <remarks>
    /// Decoded before it is searched rather than matched as bytes. The reference looks for
    /// the six Big5 bytes outright, which finds nothing at all on a client running in the
    /// other code page this launcher supports.
    /// </remarks>
    private bool Worn(AuxContext context, InventoryItem weapon)
    {
        if (!context.Process.TryRead<uint>(weapon.Entry + GameStructures.Item.Description, out var text)
            || text < GameStructures.LowestValidPointer.Value)
        {
            // Empty for a moment after something is equipped.
            return false;
        }

        var buffer = new byte[DescriptionLength];

        if (!context.Process.TryReadBytes(new GameAddress(text), buffer))
        {
            return false;
        }

        return _codec.DecodeNullTerminated(buffer).Contains(Wearing, StringComparison.Ordinal);
    }

    /// <summary>
    /// Uses whatever the player nominated while they are not transformed.
    /// </summary>
    /// <remarks>
    /// Two shapes, told apart by whether the player filled in the second box. Empty means
    /// an ordinary potion, which is simply used; anything else is a scroll that asks which
    /// form, and the answer goes in the packet with it.
    /// </remarks>
    private void Transform(AuxContext context, TimeSpan now)
    {
        var settings = context.Settings;

        if (!settings.TransformEnabled || string.IsNullOrWhiteSpace(settings.TransformItem))
        {
            return;
        }

        if (!Due(_transformed, TransformInterval, now))
        {
            return;
        }

        var entry = HelperEntrySyntax.Parse(settings.TransformItem);

        if (entry.Kind != EntryKind.Item)
        {
            // Transforming by skill is a different thing entirely and the box is not for it.
            return;
        }

        if (BuffState.Read(context.Process) is not { } state || state.At(BuffState.Transformed))
        {
            return;
        }

        if (InventoryReader.FindByName(context.Bag, entry.Name) is not { } item)
        {
            if (_said.Add(entry.Name))
            {
                _logger.LogInformation("{Name} is not in the bag", entry.Name);
            }

            return;
        }

        _said.Remove(entry.Name);
        _transformed = now;

        var choice = Option(settings.TransformCondition);

        if (choice.Length == 0)
        {
            _logger.LogInformation("Transforming with {Name}", item.Name);

            _actions.UseItem(context.Process, item.Entry);

            return;
        }

        _logger.LogInformation("Transforming with {Name} into {Choice}", item.Name, choice);

        _actions.UseWithOption(context.Process, item.Param, choice);
    }

    /// <summary>
    /// Pulls the form out of a line the settings window wrote.
    /// </summary>
    /// <remarks>
    /// The window writes <c>名字_option_編號</c> and the packet wants the middle part. A
    /// player who typed the form in by hand gets what they typed, which is why the number
    /// on the end has to be a number for the line to be read the long way.
    /// </remarks>
    internal static string Option(string? condition)
    {
        var text = condition?.Trim() ?? string.Empty;
        var parts = text.Split('_', 3);

        return parts.Length == 3 && uint.TryParse(parts[2].Trim(), out _) ? parts[1].Trim() : text;
    }
}
