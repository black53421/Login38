using System.IO.MemoryMappedFiles;
using Login38.App.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.Notifications;

/// <summary>
/// Covers the one byte that crosses from the launcher into the game.
/// </summary>
/// <remarks>
/// Every failure here is silent on the screen. A name that does not match what the DLL
/// publishes, an offset off by four, a magic never checked — all of them look exactly like
/// "the hunt mark is not showing", which is also what a client running without the present
/// hook looks like, which is a supported way to run. So the block is written by hand here and
/// read back at the offsets the DLL uses.
/// </remarks>
public sealed class AtsMarkTests
{
    /// <summary><c>L38A</c>, as <c>ats.cpp</c> writes it.</summary>
    private const uint Magic = 0x4C333841;

    private const int MagicAt = 0;
    private const int VersionAt = 4;
    private const int HuntingAt = 8;
    private const int Length = 12;

    [Fact]
    public void SaysNothingIsThereWhileTheGameHasPublishedNothing()
    {
        using var mark = Mark();

        mark.Set(770001, hunting: true);

        mark.IsLive.ShouldBeFalse();
    }

    [Fact]
    public void SwitchesTheMarkOnInTheBlockTheGameMade()
    {
        const uint pid = 770002;

        using var block = Published(pid);
        using var view = block.CreateViewAccessor(0, Length);
        using var mark = Mark();

        mark.Set(pid, hunting: true);

        mark.IsLive.ShouldBeTrue();
        view.ReadByte(HuntingAt).ShouldBe((byte)1);
    }

    [Fact]
    public void SwitchesItOffAgain()
    {
        const uint pid = 770003;

        using var block = Published(pid);
        using var view = block.CreateViewAccessor(0, Length);
        using var mark = Mark();

        mark.Set(pid, hunting: true);
        mark.Set(pid, hunting: false);

        view.ReadByte(HuntingAt).ShouldBe((byte)0);
    }

    // The launcher is usually closed with the client still running, and nothing else would
    // ever clear the byte.
    [Fact]
    public void PutsTheMarkOutWhenItStops()
    {
        const uint pid = 770004;

        using var block = Published(pid);
        using var view = block.CreateViewAccessor(0, Length);
        using var mark = Mark();

        mark.Set(pid, hunting: true);
        mark.Stop();

        view.ReadByte(HuntingAt).ShouldBe((byte)0);
        mark.IsLive.ShouldBeFalse();
    }

    // Somebody else's block under the same name. Writing into it would be writing into a
    // stranger's memory over a name anyone can take.
    [Fact]
    public void LeavesABlockItDoesNotRecogniseAlone()
    {
        const uint pid = 770005;

        using var block = MemoryMappedFile.CreateNew(AtsMark.NameFor(pid), Length);
        using var view = block.CreateViewAccessor(0, Length);

        view.Write(MagicAt, 0xDEADBEEF);
        view.Write(VersionAt, 1u);

        using var mark = Mark();

        mark.Set(pid, hunting: true);

        mark.IsLive.ShouldBeFalse();
        view.ReadByte(HuntingAt).ShouldBe((byte)0);
    }

    [Fact]
    public void NamesTheBlockAfterTheClientItBelongsTo() =>
        AtsMark.NameFor(4321).ShouldBe(@"Local\l38ats_4321");

    private static AtsMark Mark() => new(NullLogger.Instance);

    /// <summary>A block shaped the way the injected DLL makes one.</summary>
    private static MemoryMappedFile Published(uint pid)
    {
        var block = MemoryMappedFile.CreateNew(AtsMark.NameFor(pid), Length);

        using var view = block.CreateViewAccessor(0, Length);

        view.Write(MagicAt, Magic);
        view.Write(VersionAt, 1u);
        view.Write(HuntingAt, (byte)0);

        return block;
    }
}
