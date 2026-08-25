using Login38.Aux.Game;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers turning a name into something a packet can be aimed at.
/// </summary>
/// <remarks>
/// The client keeps no index from names to the things standing in the world, so the only
/// way is to walk its heap for records beginning with the player class's vtable and read
/// the names out. Getting the choice between several wrong sends a scroll at a stranger.
/// </remarks>
public sealed class EntityScanTests : IDisposable
{
    private const string Me = "銀劍客";
    private const string Somebody = "Qwqqq456456";

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly List<RemoteBuffer> _pages = [];

    public void Dispose()
    {
        foreach (var page in _pages)
        {
            page.Dispose();
        }

        _process.Dispose();
    }

    [Fact]
    public void PicksTheOnePersonCalledThat() =>
        EntityScan.Match([Candidate(0x100, Somebody)], Somebody)?.Id.ShouldBe(0x100u);

    [Fact]
    public void HasNothingToSayAboutANameNobodyHas() =>
        EntityScan.Match([Candidate(0x100, Somebody)], "某人").ShouldBeNull();

    // Three fields can hold a name and which is filled depends on what the thing is, so a
    // match on any of them counts.
    [Fact]
    public void LooksInEveryFieldThatCanHoldAName() =>
        EntityScan.Match([Candidate(0x100, "位置", $"{Somebody}的", Somebody)], Somebody)?.Name
            .ShouldBe(Somebody);

    // The client draws "某人的 魔熊" from the owner and the species but only stores the
    // first piece, so a player copying what they can see types more than the heap holds.
    [Fact]
    public void MatchesWhenThePlayerTypedMoreThanTheClientStores() =>
        EntityScan.Match([Candidate(0x100, $"{Somebody}的")], $"{Somebody}的 魔熊")?.Id
            .ShouldBe(0x100u);

    // The important ordering. A prefix match is a guess; somebody actually called that is
    // not, and must win however far down the heap they are.
    [Fact]
    public void PrefersSomebodyActuallyCalledThatOverAGuess()
    {
        Entity? found = EntityScan.Match(
            [Candidate(0x100, "銀"), Candidate(0x200, "銀劍客")], "銀劍客");

        found?.Id.ShouldBe(0x200u);
    }

    // One character is a prefix of half the names in the game.
    [Fact]
    public void WillNotGuessFromASingleCharacter() =>
        EntityScan.Match([Candidate(0x100, "銀")], "銀劍客").ShouldBeNull();

    [Fact]
    public void ReportsWhereTheRecordWasSoItCanBeCheckedAgainstTheLog() =>
        EntityScan.Match([Candidate(0x100, Somebody)], Somebody)?.Address
            .ShouldBe(new GameAddress(0xABCD0000));

    // The player's own name, which is a case of its own: a summon carries its owner's name,
    // so without this "/IT=my own name" would find the pet rather than the person.
    [Fact]
    public void RecognisesThePlayersOwnName() =>
        EntityScan.Yourself(Candidate(0x00BF, Me), 0x4001, Me)?.Id.ShouldBe(0x4001u);

    // The whole point of the special case. The id in the player's own record is a number
    // the client made up for itself; a packet aimed at it comes back refused. What goes out
    // is the id the client uses when it aims at the player, which is the server's.
    [Fact]
    public void SendsTheServersIdForThePlayerRatherThanTheClientsOwn() =>
        EntityScan.Yourself(Candidate(0x00BF, Me), 0x4001, Me)?.Id.ShouldNotBe(0x00BFu);

    // On the way into the world the record exists before the server has said what it is
    // called. Aiming at nothing is worse than not firing.
    [Fact]
    public void WaitsUntilTheClientKnowsWhatTheServerCallsIt() =>
        EntityScan.Yourself(Candidate(0x00BF, Me), 0, Me).ShouldBeNull();

    [Fact]
    public void IsNotThePlayerWhenTheNameIsSomebodyElses() =>
        EntityScan.Yourself(Candidate(0x00BF, Me), 0x4001, Somebody).ShouldBeNull();

    [Fact]
    public void IsNotThePlayerBeforeTheirRecordCanBeRead() =>
        EntityScan.Yourself(null, 0x4001, Me).ShouldBeNull();

    // From here on the real thing, against a record written into this process: the vtable
    // scan, the field offsets and the name pointers, all as they are read from a client.
    [Fact]
    public void FindsARecordLaidOutTheWayTheClientLaysThemOut()
    {
        var page = Record(0x1234, Somebody);

        var found = Scan().All(_process, page.Address, page.Address + page.Size);

        found.Count.ShouldBe(1);
        found[0].Id.ShouldBe(0x1234u);
        found[0].Names.ShouldContain(Somebody);
    }

    [Fact]
    public void ReadsAllThreeNameFields()
    {
        var page = Record(0x1234, "位置", $"{Somebody}的", Somebody);

        var found = Scan().All(_process, page.Address, page.Address + page.Size);

        found[0].Names.ShouldBe(["位置", $"{Somebody}的", Somebody]);
    }

    // A record with no id is a slot the client has finished with. The reference keeps them,
    // and will return one, which sends a packet aimed at nothing.
    [Fact]
    public void PassesOverASlotTheClientHasFinishedWith()
    {
        var page = Record(0, Somebody);

        Scan().All(_process, page.Address, page.Address + page.Size).ShouldBeEmpty();
    }

    [Fact]
    public void PassesOverAnythingThatIsNotACharacter()
    {
        var page = Page(0x1000);
        page.Write(new byte[0x200]);

        Scan().All(_process, page.Address, page.Address + page.Size).ShouldBeEmpty();
    }

    // Nothing is aimed at, but nothing throws either: the whole address space of a process
    // that has never run the game holds no characters, and that has to read as "nobody".
    [Fact]
    public void SaysNobodyRatherThanFailingAgainstAProcessThatIsNotTheGame() =>
        Scan().Find(_process, "沒有這個人").ShouldBeNull();

    private static EntityScan Scan() => new(LegacyTextCodec.Auto, NullLogger<EntityScan>.Instance);

    private static EntityScan.Candidate Candidate(uint id, params string[] names) =>
        new(new GameAddress(0xABCD0000), id, names);

    /// <summary>A record laid out the way the client lays them out, in this process.</summary>
    private RemoteBuffer Record(uint id, params string[] names)
    {
        // The names go after the record, in the same page, so one write does both.
        var page = Page(0x1000);
        var bytes = new byte[0x1000];

        BitConverter.TryWriteBytes(bytes, EntityScan.Vtable.Value);
        BitConverter.TryWriteBytes(bytes.AsSpan((int)EntityScan.RecordId), id);

        var text = EntityScan.RecordLength;

        for (var i = 0; i < names.Length; i++)
        {
            var encoded = LegacyTextCodec.Auto.Encode(names[i], LegacyEncoding.Big5);

            BitConverter.TryWriteBytes(
                bytes.AsSpan((int)EntityScan.NameFields[i]), page.Address.Value + (uint)text);

            encoded.CopyTo(bytes.AsSpan(text));
            text += encoded.Length + 1;
        }

        page.Write(bytes);

        return page;
    }

    private RemoteBuffer Page(int size)
    {
        var page = _process.AllocateScratch(size);

        _pages.Add(page);

        return page;
    }
}
