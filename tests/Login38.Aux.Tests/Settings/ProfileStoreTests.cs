using Login38.Aux.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Settings;

/// <summary>
/// Covers how a character's settings get to disk and back.
/// </summary>
/// <remarks>
/// This runs while somebody is playing, so the interesting cases are the ones where it
/// cannot do what it was asked: a name that is not a legal file name, a file somebody
/// edited into nonsense, a directory that is not there yet.
/// </remarks>
public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-profiles-" + Guid.NewGuid().ToString("N"));

    private ProfileStore Store() => new(NullLogger<ProfileStore>.Instance, _directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ReadsBackWhatItSaved()
    {
        var store = Store();
        var settings = new AuxSettings { HelperEnabled = true, ShoutIntervalSeconds = 45 };
        settings.Potions[2].Item = "綠色藥水";
        settings.HelperEntries.Add(HelperEntrySyntax.Parse("0_加速術/ME"));

        store.Save("Sqw887979", settings).ShouldBeTrue();

        var loaded = store.Load("Sqw887979");

        loaded.Profile.ShouldBe("Sqw887979");
        loaded.HelperEnabled.ShouldBeTrue();
        loaded.ShoutIntervalSeconds.ShouldBe(45u);
        loaded.Potions[2].Item.ShouldBe("綠色藥水");
        loaded.HelperEntries.ShouldBe(settings.HelperEntries);
    }

    // Settings belong to a character, not to an installation.
    [Fact]
    public void KeepsCharactersApart()
    {
        var store = Store();

        store.Save("騎士", new AuxSettings { ShoutIntervalSeconds = 10 });
        store.Save("法師", new AuxSettings { ShoutIntervalSeconds = 20 });

        store.Load("騎士").ShoutIntervalSeconds.ShouldBe(10u);
        store.Load("法師").ShoutIntervalSeconds.ShouldBe(20u);
    }

    [Fact]
    public void StartsFromTheDefaultsForACharacterItHasNotSeen()
    {
        var settings = Store().Load("新角色");

        settings.Profile.ShouldBe("新角色");
        settings.Potions.Length.ShouldBe(AuxSettings.PotionRows);
        settings.HelperEntries.ShouldBeEmpty();
    }

    [Fact]
    public void CreatesItsDirectoryOnFirstSave()
    {
        Directory.Exists(_directory).ShouldBeFalse();

        Store().Save("A", new AuxSettings()).ShouldBeTrue();

        Directory.Exists(_directory).ShouldBeTrue();
    }

    // A game is being played. A file that will not parse costs this session's settings,
    // and nothing else.
    [Fact]
    public void FallsBackToDefaultsForAFileItCannotRead()
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PathFor("壞掉"), "{ this is not json");

        var settings = store.Load("壞掉");

        settings.Profile.ShouldBe("壞掉");
        settings.Potions.Length.ShouldBe(AuxSettings.PotionRows);
    }

    // And it is not replaced. Whatever is wrong with it, the player's own file is more
    // likely to be fixable by hand than worth throwing away.
    [Fact]
    public void LeavesAFileItCouldNotReadWhereItIs()
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        var path = store.PathFor("壞掉");
        File.WriteAllText(path, "{ this is not json");

        store.Load("壞掉");

        File.ReadAllText(path).ShouldBe("{ this is not json");
    }

    // Killed mid-write, the previous settings should still be there rather than half of
    // the new ones.
    [Fact]
    public void LeavesNoPartialFileBehind()
    {
        var store = Store();

        store.Save("A", new AuxSettings { ShoutIntervalSeconds = 1 });
        store.Save("A", new AuxSettings { ShoutIntervalSeconds = 2 });

        Directory.GetFiles(_directory).ShouldHaveSingleItem();
        store.Load("A").ShoutIntervalSeconds.ShouldBe(2u);
    }

    // A character name can hold anything the server allowed.
    [Theory]
    [InlineData("Sqw887979", "Sqw887979")]
    [InlineData("玩家A", "玩家A")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("a:b?c*", "a_b_c_")]
    [InlineData("a<b>c|d\"", "a_b_c_d_")]
    public void ReplacesCharactersAFileNameCannotHold(string character, string expected) =>
        ProfileStore.SafeFileName(character).ShouldBe(expected);

    // Windows keeps these for devices whatever the extension is. Writing to `CON.json`
    // opens the console: the write appears to work and nothing is ever stored.
    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("AUX")]
    public void MovesADeviceNameOutOfTheWay(string character)
    {
        var name = ProfileStore.SafeFileName(character);

        name.ShouldNotBe(character);
        name.ShouldEndWith(character);
    }

    // Windows drops these silently, which would point two characters at one file.
    [Theory]
    [InlineData("玩家.", "玩家")]
    [InlineData("玩家 ", "玩家")]
    [InlineData("玩家. . ", "玩家")]
    public void RemovesWhatWindowsWouldDropAnyway(string character, string expected) =>
        ProfileStore.SafeFileName(character).ShouldBe(expected);

    [Fact]
    public void NamesAFileForACharacterWhoseNameIsAllIllegal() =>
        ProfileStore.SafeFileName("///").ShouldNotBeEmpty();

    // Two characters whose names differ only in something a file name cannot hold end up
    // in one file. Nothing can be done about that without renaming them, but it must not
    // be a crash — the second one just loads the first one's settings.
    [Fact]
    public void SurvivesTwoCharactersMappingToOneName()
    {
        var store = Store();

        store.Save("a/b", new AuxSettings { ShoutIntervalSeconds = 1 }).ShouldBeTrue();
        store.Save("a\\b", new AuxSettings { ShoutIntervalSeconds = 2 }).ShouldBeTrue();

        store.Load("a/b").ShoutIntervalSeconds.ShouldBe(2u);
    }

    [Fact]
    public void RefusesACharacterWithNoName() =>
        Should.Throw<ArgumentException>(() => Store().Load("   "));
}
