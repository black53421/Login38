using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Hunt;

/// <summary>
/// Takes the player's rotation, one turn per blow, at whatever the hunt is fighting.
/// </summary>
/// <remarks>
/// <para>
/// Every question but "in what order" is answered by the client. The spell book carries the
/// range each skill reaches, its dispatch handler says whether it acts on a target at all,
/// and the cast itself goes through the client's own path — so the animation plays, the
/// client's cooldown applies, and the target global is put back afterwards. What is left is
/// the order, a per-row throttle and a mana floor, which are the things only a player knows.
/// </para>
/// <para>
/// A turn is a blow, not a pass. The hunt runs five times a second and a rotation stepped
/// once per pass would be round a six-row list in a little over a second, which is not a
/// rotation, it is all of them at once. The clock is the client's own attack cooldown: it
/// moves forward when the client starts a swing, so one move is one turn. A character
/// walking to something takes no turns, which is right — it is not attacking.
/// </para>
/// <para>
/// A turn that cannot be taken is spent rather than retried. A skill still on its cooldown,
/// short of mana, out of range or not learned is skipped and comes back on the next lap, and
/// the alternative — walking forward to whatever <em>is</em> ready — is the priority list
/// this replaced, in which the first row fires every time and the last one never does.
/// </para>
/// <para>
/// Weapon rows cast nothing. The client's attack chain swings on its own schedule and is
/// neither started nor stopped here; what a weapon row does is take up a turn, which is how
/// a player says "two swings between these two skills".
/// </para>
/// <para>
/// One cast per pass at most. The client queues a cast and fires it from the next tick of
/// its own walk engine; a second one written over the top of the first is a cast that never
/// happens, and the client's mode word remembers the attempt whether or not it worked.
/// </para>
/// </remarks>
public sealed class SkillVolley
{
    private readonly GameActions _actions;
    private readonly Spells _spells;
    private readonly ILogger<SkillVolley> _logger;

    /// <summary>When each row last went off, by the name the player wrote.</summary>
    private readonly Dictionary<string, TimeSpan> _fired = new(StringComparer.Ordinal);

    /// <summary>Names already complained about, so a typo costs one line and not five a second.</summary>
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);

    /// <summary>
    /// How near to be before casting anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five, and it is a floor rather than a guess. Of the twenty-six attack skills a
    /// character can actually learn, the shortest two that are not weapon skills reach three
    /// and the server allows two more than it says, so five is where every one of them still
    /// lands: 寒冷戰慄 and 烈炎術 at five, 吸血鬼之吻 at six, and everything else at eight or
    /// beyond. Six loses the first two, seven loses the third, nine loses six more.
    /// </para>
    /// <para>
    /// It has to be a floor because getting it wrong is invisible. The server drops an
    /// out-of-range cast with no miss and no message, so a rotation standing at six against a
    /// skill that reaches five spends that row on nothing, every lap, for the whole fight.
    /// </para>
    /// <para>
    /// The alternative was looking the range up, and there is nowhere to look. The client does
    /// not hold one: the spell book record is a vtable, an id and a name; the catalogue is
    /// names; the only range in the client is the flat fifteen <c>FUN_0073C260</c> tests a
    /// hover target against, and every write to that global is the same literal. The range
    /// lives on the server and is never told to the client, so one number that satisfies all
    /// of them is the honest answer.
    /// </para>
    /// <para>
    /// It costs distance. A bow that would happily stand at eight comes to five when a skill
    /// row is up and stays there, because the hunt closes but never retreats. That is the
    /// trade being made on purpose: a skill that always goes off beats a standoff that is
    /// sometimes right. A weapon skill in the list — 會心一擊 reaches three, 屠宰者 four —
    /// is the one case this is still too far for, and the receipt below finds that out and
    /// closes further after one wasted cast.
    /// </para>
    /// </remarks>
    private const int Certain = 5;

    /// <summary>How long to wait for a cast to show up as spent mana.</summary>
    /// <remarks>
    /// The client queues a cast and fires it from its own next tick, then the server answers.
    /// Generous, because being early here reads as a refusal and pulls the character in for
    /// nothing.
    /// </remarks>
    private static readonly TimeSpan Answered = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The nearest distance each skill has been refused at, by the name the player wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Learned rather than looked up, because there is nothing to look up. A skill's range
    /// lives on the server; the client keeps none of it — the spell book record is a vtable,
    /// an id and a name pointer, the catalogue is names, and the client's own check is the
    /// flat fifteen above. The bracketed numbers on a skill's name are its mana and health
    /// costs: 光箭 reads <c>(3/0)</c>.
    /// </para>
    /// <para>
    /// So the question is answered the only way it can be — by casting and watching. A cast
    /// the server took costs mana; one it dropped costs nothing and says nothing. Mana that
    /// has not moved by <see cref="Answered"/> is a cast that did not happen, and the
    /// distance it was tried from is too far.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, int> _refused = new(StringComparer.Ordinal);

    /// <summary>A cast that has gone out and is waiting to be paid for.</summary>
    private (string Name, int Distance, uint Mana, uint Health, TimeSpan At)? _pending;

    /// <summary>Whose turn it is, as an index into the rows the player wrote.</summary>
    private int _turn;

    /// <summary>
    /// How near the character has to get for the row that could not go off, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A skill out of range used to be a turn thrown away: the row was skipped, the rotation
    /// moved on, and the character carried on standing exactly where it could not cast from.
    /// A weapon does not behave that way — the client's own attack walks in first and swings
    /// on arrival — and there is no reason a skill should be the odd one out.
    /// </para>
    /// <para>
    /// So the range check reports what it wanted instead of only refusing, the hunt stands at
    /// that instead of at the player's own standoff, and the row goes off on its next turn.
    /// Held until a cast actually lands rather than cleared each turn: the walking takes
    /// several passes and the rotation goes round the other rows meanwhile, so a value that
    /// only lived for one turn would be forgotten before the character had moved.
    /// </para>
    /// <para>
    /// Only distance sets it. A row short of mana, still cooling down, or not learned is not a
    /// row that walking would help.
    /// </para>
    /// </remarks>
    public int? Approach { get; private set; }

    /// <summary>
    /// The client's attack cooldown as it stood when the last turn was taken.
    /// </summary>
    /// <remarks>
    /// The rotation's clock. The client pushes this forward as it starts a blow, so a change
    /// is a swing and a swing is a turn. Held rather than compared against the hunt's own
    /// clock because the weapon's speed is the thing a rotation should keep step with, and
    /// that is a number the character's equipment decides.
    /// </remarks>
    private uint _swung;

    /// <summary>Stands the rotation down while the server is refusing to cast.</summary>
    private readonly SkillGuard _guard;

    public SkillVolley(GameActions actions, Spells spells, ILogger<SkillVolley> logger)
    {
        _actions = actions;
        _spells = spells;
        _logger = logger;
        _guard = new SkillGuard(logger);
    }

    /// <summary>Whether the rotation is standing down and the weapon has to carry the fight.</summary>
    public bool Silenced => _guard.Silenced;

    /// <summary>Takes in what the server said about the last cast, and how things stand.</summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Fire"/> and called every pass rather than every turn, because
    /// the answer arrives on the server's schedule: a pass that returns early for a monster
    /// out of reach is still a pass on which the reason a cast failed came in, and dropping it
    /// would leave the rotation hammering away at a condition nobody had read.
    /// </para>
    /// <para>
    /// A refusal also cancels the outstanding mana receipt. A cast the server threw away for
    /// weight costs nothing, which is exactly what an out-of-range cast costs — so without
    /// this, being overweight would teach every skill in the list that it does not reach, and
    /// the character would spend the rest of the hunt walking into melee for skills that were
    /// never the problem.
    /// </para>
    /// </remarks>
    public void Refused(CastRefusal? why, CastConditions look, TimeSpan now)
    {
        _guard.Read(why, look, now);

        if (why is not null)
        {
            _pending = null;
        }
    }

    /// <summary>Casts at most one skill at the target.</summary>
    /// <param name="process">The running game.</param>
    /// <param name="settings">What the player has asked the hunt to do.</param>
    /// <param name="target">What the hunt is on, as read this pass.</param>
    /// <param name="player">Where the character is, in client coordinates.</param>
    /// <param name="mana">The character's mana, for the floor each row can set.</param>
    /// <param name="health">
    /// The character's health, which is the other purse a cast can be paid out of. Watched for
    /// the same reason as mana — see <see cref="Settle"/>.
    /// </param>
    /// <param name="casting">
    /// Whether the client is already holding a cast its own next tick will fire. Passed in
    /// rather than read here: the caller is reading the client's state anyway, and a policy
    /// that reaches into a process is a policy no test can pin down.
    /// </param>
    /// <param name="swing">
    /// The client's attack cooldown, which it pushes forward as it starts a blow. The
    /// rotation's clock: a change is a swing, and a swing is one turn. Passed in rather than
    /// read here for the same reason <paramref name="casting"/> is.
    /// </param>
    /// <param name="reaches">
    /// Whether the target can be reached from where the character stands, at a given range —
    /// the wall in the way counted, not only the distance. Asked rather than worked out here
    /// for the same reason <paramref name="casting"/> is passed in: the caller is already
    /// holding the collision grid, and a policy that reads a process is a policy no test can
    /// pin down.
    /// </param>
    /// <param name="now">The hunt's clock, so a test can say what time it is.</param>
    /// <returns>The skill that went off, or null.</returns>
    public string? Fire(
        RemoteProcess process,
        HuntSettings settings,
        HuntTarget target,
        (int X, int Y) player,
        Gauge mana,
        Gauge health,
        bool casting,
        uint swing,
        Func<int, bool> reaches,
        TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reaches);

        // Every pass, before the turn logic, because the answer to the last cast arrives on
        // its own schedule and not on the rotation's.
        Settle(mana, health, now);

        // The server has said it will not cast, and what it is waiting for has not happened
        // yet. Nothing to do but let the weapon fight — see SkillGuard, and note that this
        // leaves Approach alone so the hunt stands at the player's own distance meanwhile
        // rather than walking into melee for a skill it is not going to cast.
        if (_guard.Silenced && !_guard.Probing)
        {
            Approach = null;

            return null;
        }

        var rows = settings.Skills;

        if (rows is null || rows.Length == 0)
        {
            return null;
        }

        // Not this character's first sight of the cooldown. Taking a turn on the first pass
        // would spend one on whatever the number happened to be, which for a character who
        // has not swung yet is a rotation starting in the middle.
        if (_swung == 0)
        {
            _swung = swing;

            return null;
        }

        // Still mid-blow. The turn belongs to the swing that started it and has either been
        // taken already or been skipped; either way it is spent.
        if (swing == _swung)
        {
            return null;
        }

        _swung = swing;

        // Whose turn it was, before anything can go wrong with taking it. A turn is spent
        // whether or not it produces a cast — that is what stops the first row firing every
        // time and the last one never firing, which is the priority list this replaced.
        if (Taking(rows, _turn) is not { } turn)
        {
            return null;
        }

        _turn = (turn + 1) % rows.Length;

        var row = rows[turn];

        // A turn of the weapon, which the client is swinging anyway. Nothing to do but let it,
        // and that is the whole value of the row: a gap the rotation has to walk through.
        if (row.Step == HuntStep.Weapon || string.IsNullOrWhiteSpace(row.Name))
        {
            return null;
        }

        // The client is already holding a cast that its own next tick will fire. Writing a
        // second one over it loses both: the queued skill never goes out, and the mode word
        // is left saying a cast is in progress.
        if (casting || !Due(row, now) || !Cast(process, row, target, player, mana, health, reaches, now))
        {
            return null;
        }

        _fired[row.Name] = now;

        return row.Name;
    }

    /// <summary>
    /// How near the rotation needs the character to be, if nearer than it would otherwise go.
    /// </summary>
    /// <returns>The tightest range any turn of it asks for, or null when none does.</returns>
    /// <remarks>
    /// <para>
    /// The client will not walk a character into range of a skill. Its spell book path checks
    /// the range and, finding the target out of it, puts the cursor into "choose a target"
    /// mode and waits for a second click — there is no chase for a skill the way there is for
    /// a blow. Read out of <c>FUN_0073C260</c>, which is where every skill in the client's own
    /// dispatch ends up.
    /// </para>
    /// <para>
    /// So the walking has to come from the hunt, and the only thing the hunt understands about
    /// distance is where to stand. A bow's eight tiles with a three-tile skill in the rotation
    /// is a skill that never goes off: the character arrives at eight, the turn comes round,
    /// the range check refuses it, and it does that for as long as anyone watches.
    /// </para>
    /// <para>
    /// Only skills that could actually go off count. An unlearned name, a buff put in the
    /// rotation by mistake, a disabled row — none of them should drag the character into melee
    /// for something it is never going to cast.
    /// </para>
    /// </remarks>
    public int? Closest(RemoteProcess process, HuntSettings settings)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(settings);

        // Nothing is being cast, so nothing should be walked in for.
        if (_guard.Silenced)
        {
            return null;
        }

        int? closest = null;

        foreach (var row in settings.Skills ?? [])
        {
            if (row is null
                || !row.Enabled
                || row.Step == HuntStep.Weapon
                || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            if (_spells.Learn(process, row.Name) is not { IsAttack: true })
            {
                continue;
            }

            // Only a skill that has actually been refused somewhere. Until then it is worth
            // as much as the client will aim, which is further than the hunt ever stands off
            // and so has no opinion about where to stand.
            if (!_refused.TryGetValue(row.Name, out var refused))
            {
                continue;
            }

            var reach = Math.Max(1, refused - 1);

            closest = closest is { } near ? Math.Min(near, reach) : reach;
        }

        return closest;
    }

    /// <summary>
    /// The next row that is actually in the rotation, from a given place in the list.
    /// </summary>
    /// <returns>Its index, or null when the player has ticked nothing.</returns>
    /// <remarks>
    /// A row nobody ticked is not a turn. The first version of this spent one on every row in
    /// the list, so two skills among six rows cast twice and then stood there for four turns
    /// with nothing to do — which, on a character with no weapon setting the pace, is a
    /// four-second silence between every pair of casts. What a player ticks is the cycle; the
    /// rest of the list is not a slower cycle, it is not in one.
    /// </remarks>
    private static int? Taking(HuntSkill[] rows, int from)
    {
        for (var step = 0; step < rows.Length; step++)
        {
            var at = (from + step) % rows.Length;

            if (rows[at] is { Enabled: true })
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the rotation wants the weapon swung at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A weapon row used to be nothing but a gap, because the client's attack chain swings on
    /// its own and neither half of this started or stopped it — so a rotation of nothing but
    /// skills still had the character hitting things, which is not what unticking every weapon
    /// row looks like it should do.
    /// </para>
    /// <para>
    /// An empty rotation is a weapon rotation. Somebody who has never opened the panel has a
    /// character that fights, and reading "no rows" as "do not attack" would switch the hunt
    /// off for everyone who has not asked for anything.
    /// </para>
    /// </remarks>
    public static bool Swings(HuntSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var rotation = false;

        foreach (var row in settings.Skills ?? [])
        {
            if (row is null || !row.Enabled)
            {
                continue;
            }

            if (row.Step == HuntStep.Weapon)
            {
                return true;
            }

            rotation = true;
        }

        return !rotation;
    }

    /// <summary>
    /// Starts the rotation again from the top, for a new target.
    /// </summary>
    /// <remarks>
    /// The turn only. What each skill is throttled to is a property of the skill and not of
    /// what it was aimed at, and forgetting it here would let a skill on a minute's interval
    /// go off on every monster the hunt walks up to.
    /// </remarks>
    public void Restart()
    {
        _turn = 0;
        Approach = null;

        // Not what has been learned: that belongs to the character's skills and not to the
        // monster it happened to be fighting when it was found out.
        _pending = null;
    }

    /// <summary>Forgets every throttle, for a character who has just walked in.</summary>
    /// <remarks>
    /// The clock this is timed against belongs to the hunt and does not restart between
    /// characters, so without this the first pass of a new one would be told its skills
    /// went off a moment ago.
    /// </remarks>
    public void Reset()
    {
        _fired.Clear();
        _said.Clear();
        _turn = 0;
        _swung = 0;
        Approach = null;
        _pending = null;

        // A different character has different skills at different levels. What the last one
        // learned about its own is worth nothing here.
        _refused.Clear();

        _guard.Reset();
    }

    /// <summary>
    /// Works out what happened to the last cast, if anything has yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mana is the receipt. The server takes it when it accepts a cast and takes nothing when
    /// it drops one, and it drops an out-of-range cast <em>silently</em> — no miss, no
    /// message, nothing on the screen. So a skill that has gone out and cost nothing by the
    /// time <see cref="Answered"/> has passed did not go off, and the distance it was tried
    /// from is further than that skill reaches.
    /// </para>
    /// <para>
    /// Only ever tightened, never loosened. A cast can fail for reasons that have nothing to
    /// do with distance — a wall, a monster that has gone underground — and mistaking one of
    /// those for a range is a skill the character walks too far in for. Walking too near is
    /// the cheap mistake; the expensive one is standing where nothing lands.
    /// </para>
    /// </remarks>
    private void Settle(Gauge mana, Gauge health, TimeSpan now)
    {
        if (_pending is not { } cast)
        {
            return;
        }

        // Either purse. Most skills are paid for in mana, but the book shows plenty that are
        // not — 冥想術 (5/30), 魔力奪取 (1/50), 造屍術 (5/80/1) — and watching only the one
        // would have those learning that they never land and dragging the character to arm's
        // length for good. Health also falls for being hit, which reads as "paid" and is the
        // safe way round: this only ever tightens on silence.
        if (mana.Current < cast.Mana || health.Current < cast.Health)
        {
            // Paid for, so that distance works for that skill.
            _pending = null;

            // And whatever the server was refusing casts over is over, whether or not the
            // condition it was held against says so.
            _guard.Landed();

            return;
        }

        if (now - cast.At < Answered)
        {
            return;
        }

        _pending = null;

        // Already known to fail at least this near, so there is nothing new here.
        if (_refused.TryGetValue(cast.Name, out var was) && was <= cast.Distance)
        {
            return;
        }

        _refused[cast.Name] = cast.Distance;
        Approach = Math.Max(1, cast.Distance - 1);

        _logger.LogInformation(
            "{Name} cost nothing from {Distance} tiles, so it does not reach that far; the "
            + "hunt will close to {Reach} for it", cast.Name, cast.Distance, Approach);
    }

    /// <summary>How far a skill is worth trying from, as things stand.</summary>
    private int Reach(string name) =>
        _refused.TryGetValue(name, out var refused)
            ? Math.Min(Certain, Math.Max(1, refused - 1))
            : Certain;

    /// <summary>The distance between two points in tiles, the way the server counts it.</summary>
    /// <remarks>
    /// Two grid columns to a tile across and one row down, and the larger of the two — which
    /// is the same shape as the client's own box and as the server's own check.
    /// </remarks>
    private static int Tiles((int X, int Y) player, HuntTarget target) =>
        Math.Max(Math.Abs(player.X - target.X) / 2, Math.Abs(player.Y - target.Y));

    private bool Due(HuntSkill row, TimeSpan now) =>
        !_fired.TryGetValue(row.Name, out var last)
        || now - last >= TimeSpan.FromSeconds(Math.Max(row.IntervalSeconds, 0));

    private bool Cast(
        RemoteProcess process,
        HuntSkill row,
        HuntTarget target,
        (int X, int Y) player,
        Gauge mana,
        Gauge health,
        Func<int, bool> reaches,
        TimeSpan now)
    {
        if (mana.Maximum > 0 && mana.Percent < row.ManaAtLeast)
        {
            return false;
        }

        if (_spells.Learn(process, row.Name) is not { } spell)
        {
            if (_said.Add(row.Name))
            {
                _logger.LogInformation(
                    "this character has not learned {Name}, so the hunt will not cast it", row.Name);
            }

            return false;
        }

        // From the client's own dispatch table. A buff sent at a monster is a buff on the
        // monster, and there is no way to tell from the name which one a skill is.
        if (!spell.IsAttack)
        {
            if (_said.Add(row.Name))
            {
                _logger.LogInformation(
                    "{Name} does not act on a target; put it in the buff list instead", row.Name);
            }

            return false;
        }

        // As far as the client will aim, less whatever this skill has already been refused
        // at. Distance only, no line: the wall test belongs to walking, and applying it here
        // silenced whole fights depending on which way the monster lay.
        var reach = Reach(row.Name);

        if (!reaches(reach))
        {
            // What the walk is for. Kept rather than logged: the hunt reads it every pass and
            // stands at it until the row goes off.
            Approach = reach;

            return false;
        }

        _logger.LogDebug("casting {Name} at {Target}", row.Name, target.Name);
        _actions.Cast(
            process, spell.Packed, SkillTarget.Entity(target.Id), _spells.Icon(process, spell));

        // If this was the one cast a stood-down rotation is allowed, it has been spent.
        _guard.Cast(now);

        // Where it was cast from, so the answer can be attributed to a distance. The client
        // will send it whatever the server thinks; whether the server took it is the thing
        // being watched for.
        _pending = (row.Name, Tiles(player, target), mana.Current, health.Current, now);

        // In range as far as anything here knows, so whatever the character walked in for is
        // done with and the player's own standoff applies again.
        Approach = null;

        return true;
    }
}
