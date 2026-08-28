namespace Login38.Aux.Game;

/// <summary>
/// Why the server would not cast, in its own words.
/// </summary>
/// <remarks>
/// <para>
/// Read out of the client's own chat window rather than guessed at. The server answers a
/// refused cast with a numbered message and the client looks the number up in a table it
/// keeps in memory, so the reason is there to be read — see <see cref="CastWatch"/>.
/// </para>
/// <para>
/// The distinction that matters is not what went wrong but what would put it right, because
/// a rotation standing down has to know when to stand up again. Three of these can be
/// watched directly; the rest have nothing to watch and are answered by trying again.
/// </para>
/// </remarks>
public enum CastRefusal
{
    /// <summary>Carrying too much. 「你攜帶太多物品，因此無法使用法術。」</summary>
    Weight,

    /// <summary>Not enough mana. 「因魔力不足而無法使用魔法。」</summary>
    Mana,

    /// <summary>Not enough health. 「因體力不足而無法使用魔法。」</summary>
    Health,

    /// <summary>Nothing in the way of it. 「施咒失敗。」</summary>
    Blocked,

    /// <summary>Interrupted part way. 「施咒取消。」</summary>
    Cancelled,

    /// <summary>Some state the character is in forbids it. 「在此狀態下無法使用魔法。」</summary>
    State,

    /// <summary>Out of whatever it is cast with. 「施放魔法所需材料不足。」</summary>
    Reagent,

    /// <summary>Cast at the wrong attribute. 「若要使用這個法術，屬性必須成為 %0。」</summary>
    Attribute,

    /// <summary>Invisible, which forbids some of them. 「透明狀態無法使用的魔法。」</summary>
    Unseen,
}
