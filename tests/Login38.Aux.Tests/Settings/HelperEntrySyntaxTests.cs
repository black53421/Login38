using Login38.Aux.Settings;
using Shouldly;

namespace Login38.Aux.Tests.Settings;

/// <summary>
/// Covers the one-line form operators write their helper lists in.
/// </summary>
/// <remarks>
/// <para>
/// Two spellings, a middle field that means two different things, and a set of suffixes
/// that grew over years — so most of what matters here is that every line already in
/// somebody's file keeps meaning what it meant. The cases come from the reference's own
/// parser suite, which is the closest thing to a specification the format has.
/// </para>
/// <para>
/// The other half is that nothing throws. A line nobody can make sense of costs that line,
/// not the file.
/// </para>
/// </remarks>
public sealed class HelperEntrySyntaxTests
{
    [Fact]
    public void ReadsAPlainItem()
    {
        var entry = HelperEntrySyntax.Parse("肉_-1_I");

        entry.Name.ShouldBe("肉");
        entry.StateId.ShouldBe(-1);
        entry.Kind.ShouldBe(EntryKind.Item);
        entry.Cast.Kind.ShouldBe(CastKind.Item);
    }

    [Theory]
    [InlineData("保護罩_-1_M", CastKind.NoSpec)]
    [InlineData("生命之泉_-1_MME", CastKind.OnSelf)]
    [InlineData("寒冰術_-1_MT", CastKind.HoverTarget)]
    [InlineData("淨化_-1_MIA", CastKind.OnInUseItem)]
    [InlineData("祝福_-1_MIW", CastKind.OnWieldedItem)]
    public void ReadsTheSpellSuffixes(string line, CastKind expected)
    {
        var entry = HelperEntrySyntax.Parse(line);

        entry.Kind.ShouldBe(EntryKind.Skill);
        entry.Cast.Kind.ShouldBe(expected);
        entry.Cast.Target.ShouldBeNull();
    }

    // The middle field's second job. Without this, an entry that acts on something named
    // would have nowhere to say what.
    [Fact]
    public void ReadsATargetOutOfTheMiddleField()
    {
        var entry = HelperEntrySyntax.Parse("提煉魔石_紅魔石_MI");

        entry.Name.ShouldBe("提煉魔石");
        entry.Kind.ShouldBe(EntryKind.Skill);
        entry.Cast.Kind.ShouldBe(CastKind.OnNamedItem);
        entry.Cast.Target.ShouldBe("紅魔石");
    }

    // A number there is a status flag, so the entry acts on the first thing that matches.
    [Fact]
    public void ReadsANumberInTheMiddleFieldAsAFlagRatherThanAName()
    {
        var entry = HelperEntrySyntax.Parse("淨化卷軸_3_IIA");

        entry.StateId.ShouldBe(3);
        entry.Cast.Kind.ShouldBe(CastKind.OnInUseItem);
        entry.Cast.Target.ShouldBeNull();
    }

    // The suffix an operator types into the window carries a slash. This used to fall
    // through to "use the item", which looked for a spell in the bag and found nothing.
    [Theory]
    [InlineData("暗影之牙/MIA", CastKind.OnInUseItem)]
    [InlineData("暗影之牙/MIW", CastKind.OnWieldedItem)]
    public void ReadsTheSpellSuffixesWithASlashToo(string line, CastKind expected)
    {
        var entry = HelperEntrySyntax.Parse(line);

        entry.Name.ShouldBe("暗影之牙");
        entry.Kind.ShouldBe(EntryKind.Skill);
        entry.Cast.Kind.ShouldBe(expected);
    }

    [Fact]
    public void ReadsATargetAfterAnEqualsSign()
    {
        var entry = HelperEntrySyntax.Parse("提煉魔石/MI=紅魔石");

        entry.Kind.ShouldBe(EntryKind.Skill);
        entry.Cast.Kind.ShouldBe(CastKind.OnNamedItem);
        entry.Cast.Target.ShouldBe("紅魔石");
    }

    [Fact]
    public void ReadsDestroy() =>
        HelperEntrySyntax.Parse("廢物_-1_ID").Cast.Kind.ShouldBe(CastKind.DropItem);

    [Fact]
    public void ReadsTheDebugSuffix() =>
        HelperEntrySyntax.Parse("DEBUG_-1_INFO").Cast.Kind.ShouldBe(CastKind.Info);

    [Fact]
    public void ReadsAFunctionKeyMacro()
    {
        var entry = HelperEntrySyntax.Parse("召喚術_-1_KEY=F1");

        entry.Kind.ShouldBe(EntryKind.Key);
        entry.Cast.Kind.ShouldBe(CastKind.Key);
        entry.Cast.FunctionKey.ShouldBe((byte)1);

        var delayed = HelperEntrySyntax.Parse("龍息術_-1_DKEY=F12");

        delayed.Cast.Kind.ShouldBe(CastKind.DelayKey);
        delayed.Cast.FunctionKey.ShouldBe((byte)12);
    }

    // Suffixes that were written down and never built. Plainly using the item is the one
    // outcome that cannot send the server a packet it did not expect.
    [Theory]
    [InlineData("法術書_-1_IBM")]
    [InlineData("祝福卷軸_某玩家_IP")]
    [InlineData("未知_-1_XYZ")]
    public void FallsBackToUsingTheItemForASuffixItDoesNotKnow(string line) =>
        HelperEntrySyntax.Parse(line).Cast.Kind.ShouldBe(CastKind.Item);

    [Theory]
    [InlineData("0_強力加速術/M", CastKind.NoSpec)]
    [InlineData("0_加速術/ME", CastKind.OnSelf)]
    public void ReadsTheOlderSpelling(string line, CastKind expected) =>
        HelperEntrySyntax.Parse(line).Cast.Kind.ShouldBe(expected);

    // Some sections of an operator's file have never carried a flag at all.
    [Fact]
    public void ReadsALineWithNoStatusFlag()
    {
        var entry = HelperEntrySyntax.Parse("解毒術/ME");

        entry.Name.ShouldBe("解毒術");
        entry.StateId.ShouldBe(-1);
        entry.Kind.ShouldBe(EntryKind.Skill);
        entry.Cast.Kind.ShouldBe(CastKind.OnSelf);
    }

    [Fact]
    public void ReadsALineThatIsNothingButAName()
    {
        var entry = HelperEntrySyntax.Parse("解毒藥水");

        entry.Name.ShouldBe("解毒藥水");
        entry.StateId.ShouldBe(-1);
        entry.Kind.ShouldBe(EntryKind.Item);
        entry.Cast.Kind.ShouldBe(CastKind.Item);
    }

    // Two fields with a number first is the older spelling, and with a name first is the
    // newer one missing its suffix. Nothing in the line says which but the number.
    [Fact]
    public void TellsTheTwoTwoFieldSpellingsApart()
    {
        var older = HelperEntrySyntax.Parse("0_自我加速藥水");

        older.StateId.ShouldBe(0);
        older.Name.ShouldBe("自我加速藥水");

        var newer = HelperEntrySyntax.Parse("肉_-1");

        newer.StateId.ShouldBe(-1);
        newer.Name.ShouldBe("肉");
        newer.Cast.Kind.ShouldBe(CastKind.Item);
    }

    [Fact]
    public void ReadsTheOlderExtendedSuffixAsATargetedCast()
    {
        var entry = HelperEntrySyntax.Parse("5_寒冰術/M=紅魔石");

        entry.Cast.Kind.ShouldBe(CastKind.OnNamedItem);
        entry.Cast.Target.ShouldBe("紅魔石");
    }

    [Theory]
    [InlineData("0_魔法卷軸 (初級治癒術)/IME", 0, "魔法卷軸 (初級治癒術)")]
    [InlineData("魔法卷軸 (高級治癒術)_-1_IME", -1, "魔法卷軸 (高級治癒術)")]
    public void ReadsSelfCastEitherWay(string line, int stateId, string name)
    {
        var entry = HelperEntrySyntax.Parse(line);

        entry.StateId.ShouldBe(stateId);
        entry.Name.ShouldBe(name);
        entry.Kind.ShouldBe(EntryKind.Item);
        entry.Cast.Kind.ShouldBe(CastKind.OnSelfItem);
    }

    [Theory]
    [InlineData("3_淨化卷軸/IA", CastKind.OnInUseItem, null)]
    [InlineData("3_淨化卷軸/IA=胸甲", CastKind.OnInUseItem, "胸甲")]
    [InlineData("0_祝福卷軸/IW", CastKind.OnWieldedItem, null)]
    [InlineData("0_祝福卷軸/IW=神聖戰鎚", CastKind.OnWieldedItem, "神聖戰鎚")]
    [InlineData("0_變身卷軸/I=狼人", CastKind.OnNamedItem, "狼人")]
    public void ReadsTheItemSuffixes(string line, CastKind expected, string? target)
    {
        var entry = HelperEntrySyntax.Parse(line);

        entry.Kind.ShouldBe(EntryKind.Item);
        entry.Cast.Kind.ShouldBe(expected);
        entry.Cast.Target.ShouldBe(target);
    }

    // The same suffix means two different things depending on whether it names anything:
    // with a name the entity is found by scanning, without one the client picks.
    [Fact]
    public void TellsAutomaticAndManualTargetingApart()
    {
        HelperEntrySyntax.Parse("0_復活卷軸/IT").Cast.Kind.ShouldBe(CastKind.HoverTarget);

        var named = HelperEntrySyntax.Parse("0_治癒卷軸/IT=阿狗");

        named.Cast.Kind.ShouldBe(CastKind.OnNamedEntity);
        named.Cast.Target.ShouldBe("阿狗");

        var newer = HelperEntrySyntax.Parse("治癒卷軸_召喚物A_IT");

        newer.Cast.Kind.ShouldBe(CastKind.OnNamedEntity);
        newer.Cast.Target.ShouldBe("召喚物A");
    }

    // A bare /MI is from a version that read the suffix as something else, and carries no
    // name to act on. Casting untargeted is the closest thing left.
    [Fact]
    public void ReadsANameLessOlderTargetedCastAsUntargeted()
    {
        var entry = HelperEntrySyntax.Parse("0_寒冰術/MI");

        entry.Kind.ShouldBe(EntryKind.Skill);
        entry.Cast.Kind.ShouldBe(CastKind.NoSpec);
    }

    // Written back in the spelling that is already in operators' files, except for the
    // suffixes that spelling has no words for.
    [Theory]
    [InlineData("-1_肉", "-1_肉")]
    [InlineData("-1_保護罩/M", "-1_保護罩/M")]
    [InlineData("-1_生命之泉/ME", "-1_生命之泉/ME")]
    [InlineData("-1_召喚術/KEY=F1", "-1_召喚術/KEY=F1")]
    [InlineData("-1_龍息術/DKEY=F12", "-1_龍息術/DKEY=F12")]
    [InlineData("肉_-1_I", "-1_肉")]
    [InlineData("保護罩_-1_M", "-1_保護罩/M")]
    [InlineData("生命之泉_-1_MME", "-1_生命之泉/ME")]
    [InlineData("寒冰術_-1_MT", "寒冰術_-1_MT")]
    [InlineData("淨化_-1_MIA", "淨化_-1_MIA")]
    [InlineData("祝福_-1_MIW", "祝福_-1_MIW")]
    [InlineData("提煉魔石_紅魔石_MI", "提煉魔石_紅魔石_MI")]
    [InlineData("廢物_-1_ID", "廢物_-1_ID")]
    [InlineData("DEBUG_-1_INFO", "DEBUG_-1_INFO")]
    [InlineData("淨化卷軸_-1_IIA", "-1_淨化卷軸/IA")]
    [InlineData("祝福卷軸_-1_IIW", "-1_祝福卷軸/IW")]
    [InlineData("變身卷軸_狼人_II", "-1_變身卷軸/I=狼人")]
    [InlineData("淨化卷軸_胸甲_IIA", "-1_淨化卷軸/IA=胸甲")]
    [InlineData("祝福卷軸_神聖戰鎚_IIW", "-1_祝福卷軸/IW=神聖戰鎚")]
    [InlineData("-1_淨化卷軸/IA", "-1_淨化卷軸/IA")]
    [InlineData("-1_祝福卷軸/IW", "-1_祝福卷軸/IW")]
    [InlineData("0_變身卷軸/I=狼人", "0_變身卷軸/I=狼人")]
    [InlineData("3_淨化卷軸/IA=胸甲", "3_淨化卷軸/IA=胸甲")]
    [InlineData("0_祝福卷軸/IW=神聖戰鎚", "0_祝福卷軸/IW=神聖戰鎚")]
    [InlineData("0_復活卷軸/IT", "0_復活卷軸/IT")]
    [InlineData("復活卷軸_-1_IT", "-1_復活卷軸/IT")]
    [InlineData("0_魔法卷軸 (初級治癒術)/IME", "0_魔法卷軸 (初級治癒術)/IME")]
    [InlineData("魔法卷軸 (初級治癒術)_-1_IME", "-1_魔法卷軸 (初級治癒術)/IME")]
    public void WritesBackTheOneSpellingItChose(string line, string expected) =>
        HelperEntrySyntax.ToSettingText(HelperEntrySyntax.Parse(line)).ShouldBe(expected);

    // Whatever it was written as, reading what was written gives the same entry.
    [Theory]
    [InlineData("肉_-1_I")]
    [InlineData("提煉魔石_紅魔石_MI")]
    [InlineData("0_祝福卷軸/IW=神聖戰鎚")]
    [InlineData("0_治癒卷軸/IT=阿狗")]
    [InlineData("龍息術_-1_DKEY=F12")]
    public void ReadsBackWhatItWrote(string line)
    {
        var entry = HelperEntrySyntax.Parse(line);

        HelperEntrySyntax.Parse(HelperEntrySyntax.ToSettingText(entry)).ShouldBe(entry);
    }

    // A command box has no status flag to check, so the form it takes leaves it out.
    [Theory]
    [InlineData("肉_-1_I", "肉")]
    [InlineData("0_加速術/ME", "加速術/ME")]
    [InlineData("提煉魔石_紅魔石_MI", "提煉魔石/MI=紅魔石")]
    [InlineData("0_祝福卷軸/IW=神聖戰鎚", "祝福卷軸/IW=神聖戰鎚")]
    [InlineData("召喚術_-1_KEY=F1", "召喚術/KEY=F1")]
    [InlineData("淨化_-1_MIA", "淨化/MIA")]
    public void WritesTheFormACommandBoxTakes(string line, string expected) =>
        HelperEntrySyntax.ToCommandText(HelperEntrySyntax.Parse(line)).ShouldBe(expected);

    [Theory]
    [InlineData("F1", 1)]
    [InlineData("f12", 12)]
    [InlineData(" F5 ", 5)]
    public void ReadsAFunctionKey(string text, byte expected) =>
        HelperEntrySyntax.ParseFunctionKey(text).ShouldBe(expected);

    [Theory]
    [InlineData("F0")]
    [InlineData("F13")]
    [InlineData("G1")]
    [InlineData("F")]
    [InlineData("")]
    [InlineData("Fx")]
    public void RefusesAKeyTheClientDoesNotHave(string text) =>
        HelperEntrySyntax.ParseFunctionKey(text).ShouldBeNull();

    // A key that does not exist makes the macro meaningless, so the entry falls back to
    // the safe default rather than pressing something arbitrary.
    [Fact]
    public void FallsBackWhenAMacroNamesAKeyThatIsNotThere()
    {
        var entry = HelperEntrySyntax.Parse("召喚術_-1_KEY=F99");

        entry.Kind.ShouldBe(EntryKind.Item);
        entry.Cast.Kind.ShouldBe(CastKind.Item);
    }

    // Read at launch, from a file somebody edited by hand.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("_")]
    [InlineData("__")]
    [InlineData("/")]
    [InlineData("_/_")]
    [InlineData("0_")]
    [InlineData("肉_-1_I_extra")]
    public void ReadsAnythingWithoutThrowing(string line) =>
        Should.NotThrow(() => HelperEntrySyntax.Parse(line));
}
