using Login38.Aux.Game;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers deciding which things in the world get a colour, and which one.
/// </summary>
public sealed class MonsterScanTests
{
    private static readonly GameAddress Somewhere = new(0x0200_0000);

    // ---- reading a record ------------------------------------------------------------

    [Fact]
    public void ReadsEveryFieldOutOfTheRecordItIsGiven()
    {
        var entity = Parse(record =>
        {
            Put(record, 0x0C, 0x0123_4567u);
            record[0x14] = 0x00;
            Put(record, 0x18, (ushort)0x02BC);
            Put(record, 0x30, (ushort)0xF800);
            record[0x5A] = 63;
            Put(record, 0x60, 0x0055_0000u);
            Put(record, 0x80, 0x0000_0004u);
        });

        entity.ServerId.ShouldBe(0x0123_4567u);
        entity.Kind.ShouldBe((byte)0);
        entity.Sprite.ShouldBe((ushort)0x02BC);
        entity.Colour.ShouldBe((ushort)0xF800);
        entity.LevelByte.ShouldBe((byte)63);
        entity.NamePointer.ShouldBe(0x0055_0000u);
        entity.Map.ShouldBe(4u);
        entity.Address.ShouldBe(Somewhere);
    }

    [Fact]
    public void RefusesARecordShorterThanTheFieldsItReads() =>
        MonsterScan.Snapshot.Parse(Somewhere, new byte[MonsterScan.RecordLength - 1]).ShouldBeNull();

    // The client keeps freed entity records and reuses them, so having the vtable is not by
    // itself being something on screen.
    [Theory]
    [InlineData(0, 4, 0x550000u, false)]
    [InlineData(700, 0, 0x550000u, false)]
    [InlineData(700, 4, 0u, false)]
    [InlineData(700, 4, 0x550000u, true)]
    public void IsOnlyInTheWorldWithASpriteAMapAndAName(
        int sprite, uint map, uint name, bool visible) =>
        Parse(record =>
        {
            Put(record, 0x18, (ushort)sprite);
            Put(record, 0x80, map);
            Put(record, 0x60, name);
        }).IsVisible.ShouldBe(visible);

    // The byte is a level on a monster and something else on everything else, so a bound is
    // the cheapest way to throw out most of the nonsense before the colour rule sees it.
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, 1u)]
    [InlineData(120, 120u)]
    [InlineData(121, null)]
    [InlineData(255, null)]
    public void BelievesALevelByteOnlyInsideTheRangeALevelCanBe(int stored, uint? level) =>
        Parse(record => record[0x5A] = (byte)stored).Level.ShouldBe(level);

    // ---- deciding whether to colour it -----------------------------------------------

    // With the sprite table read, what the sprite draws is the answer and the object id is
    // not consulted at all.
    [Fact]
    public void ColoursSomethingTheSpriteTableCallsAMonster() =>
        MonsterScan.Colourable(Monster(id: 1), SpriteTypes.Monster, alreadyOurs: false).ShouldBeTrue();

    [Fact]
    public void LeavesAloneSomethingTheSpriteTableCallsAnythingElse() =>
        MonsterScan.Colourable(Monster(), spriteType: 3, alreadyOurs: false).ShouldBeFalse();

    // Until the table has been found, everything spawned into a running world has a high
    // object id and everything the map placed does not.
    [Fact]
    public void FallsBackToTheObjectIdWhileTheTableIsUnknown()
    {
        MonsterScan.Colourable(Monster(id: 0x0100_0000), null, false).ShouldBeTrue();
        MonsterScan.Colourable(Monster(id: 0x00FF_FFFF), null, false).ShouldBeFalse();
    }

    [Fact]
    public void LeavesAloneSomethingThatIsNotAWorldMonster() =>
        MonsterScan.Colourable(Monster(kind: 1), SpriteTypes.Monster, false).ShouldBeFalse();

    // A monster in combat carries a different kind byte from one standing still. Losing its
    // colour every time it is hit is worse than trusting a record already decided about.
    [Fact]
    public void KeepsColouringOneItHasAlreadyColoured() =>
        MonsterScan.Colourable(Monster(kind: 1), SpriteTypes.Monster, alreadyOurs: true).ShouldBeTrue();

    // But not to the point of overriding the sprite table: a door this feature somehow
    // coloured once does not stay coloured for the session.
    [Fact]
    public void DoesNotKeepColouringSomethingTheTableSaysIsNotAMonster() =>
        MonsterScan.Colourable(Monster(), spriteType: 3, alreadyOurs: true).ShouldBeFalse();

    // ---- telling the player from everything else -------------------------------------

    [Fact]
    public void KnowsThePlayerByTheAddressTheClientPointsAt() =>
        new MonsterScan.Player(Somewhere.Value, 0, 0, []).Is(Monster()).ShouldBeTrue();

    [Fact]
    public void KnowsThemByTheIdOnTheirOwnRecord() =>
        new MonsterScan.Player(0, 77, 0, []).Is(Monster(id: 77)).ShouldBeTrue();

    [Fact]
    public void KnowsThemByTheIdTheClientAimsAtThemWith() =>
        new MonsterScan.Player(0, 0, 77, []).Is(Monster(id: 77)).ShouldBeTrue();

    // Zero is "the client has not told us yet", not "the entity whose id is zero".
    [Fact]
    public void DoesNotMatchEverythingBeforeTheClientHasSaidWhoTheyAre() =>
        new MonsterScan.Player(0, 0, 0, []).Is(Monster(id: 0)).ShouldBeFalse();

    [Fact]
    public void KnowsThemByName() =>
        new MonsterScan.Player(0, 0, 0, ["某人"]).Is(["某人"]).ShouldBeTrue();

    [Fact]
    public void DoesNotTakeAMonsterForThePlayerOnAnEmptyNameList() =>
        new MonsterScan.Player(0, 0, 0, []).Is(["魔熊"]).ShouldBeFalse();

    [Fact]
    public void IsNotConfusedByAnotherCharacterWithAnotherName() =>
        new MonsterScan.Player(0, 0, 0, ["某人"]).Is(["別人"]).ShouldBeFalse();

    // ---- the pass itself -------------------------------------------------------------

    [Fact]
    public void HasNothingToPutBackBeforeItHasWrittenAnything()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        new MonsterScan(LegacyTextCodec.Auto, NullLogger<MonsterScan>.Instance)
            .RestoreAll(process).ShouldBe(0);
    }

    // A process that is not the game has no player status object and no experience
    // reading, so the level falls all the way through to what an empty character is.
    [Fact]
    public void SaysNothingRatherThanFailingWhereThereIsNoPlayer()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        Should.NotThrow(() => MonsterScan.PlayerLevel(process));
    }

    private static MonsterScan.Snapshot Monster(uint id = 0x0100_0000, byte kind = 0) =>
        new(Somewhere, id, kind, Sprite: 700, Map: 4, NamePointer: 0x0055_0000, Colour: 0xFFDF, LevelByte: 30);

    private static MonsterScan.Snapshot Parse(Action<byte[]> fill)
    {
        var record = new byte[MonsterScan.RecordLength];

        fill(record);

        return MonsterScan.Snapshot.Parse(Somewhere, record)!.Value;
    }

    private static void Put(byte[] record, int at, uint value) =>
        BitConverter.TryWriteBytes(record.AsSpan(at), value);

    private static void Put(byte[] record, int at, ushort value) =>
        BitConverter.TryWriteBytes(record.AsSpan(at), value);
}
