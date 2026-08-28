using System.Runtime.InteropServices;
using Login38.Aux.Hunt;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// The shape of the client's own "who is acting on me" set.
/// </summary>
/// <remarks>
/// Read out of a running client rather than worked out: the set held two monster ids while two
/// of them were biting the character, and one when the first of them died, with the ids the
/// scan reports for those monsters. The layout is the one the client's own add and remove both
/// use — the array at four, how many are live at eight.
/// </remarks>
public sealed class AggressorsTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly List<GCHandle> _pinned = [];

    public void Dispose()
    {
        foreach (var handle in _pinned)
        {
            handle.Free();
        }

        _process.Dispose();
    }

    [Fact]
    public void ReadsWhatTheClientIsHoldingOnTo() =>
        Ids(Set(0x0BEBFDA9, 0x0BEBFD6A, 0x0BEBFD60))
            .ShouldBe([0x0BEBFDA9u, 0x0BEBFD6Au, 0x0BEBFD60u]);

    [Fact]
    public void AnswersNothingWhenNothingIsActingOnTheCharacter() =>
        Ids(Set()).ShouldBeEmpty();

    // The live one. The client removes by shuffling the tail down and dropping the count, so
    // the entries past it are whatever was there before — four stale ids sat behind a count of
    // two in the client this was read from, and reading them would have had the hunt turn on
    // something that stopped attacking it a minute ago.
    [Fact]
    public void ReadsOnlyAsFarAsTheCountSays()
    {
        var at = Set(0x0BEBFDA9, 0x0BEBFD6A, 0x0BEBFD60, 0x0BEBFD82);

        Count(at, 2);

        Ids(at).ShouldBe([0x0BEBFDA9u, 0x0BEBFD6Au]);
    }

    // A count read out of a world being torn down. The client bounds this nowhere, so the
    // bound is the launcher's, and the difference between having one and not is a quarter of
    // a gigabyte of reads.
    [Fact]
    public void RefusesACountNoSetCouldHave()
    {
        var at = Set(0x0BEBFDA9);

        Count(at, 0x8F3A2C00);

        Ids(at).ShouldBeEmpty();
    }

    [Fact]
    public void AnswersNothingWhenTheClientHasNoSetYet()
    {
        var header = Pin(new byte[12]);

        BitConverter.TryWriteBytes(header.AsSpan(8), 3);

        Aggressors.Ids(_process, Address(header)).ShouldBeEmpty();
    }

    private IReadOnlyList<uint> Ids(byte[] header) => Aggressors.Ids(_process, Address(header));

    /// <summary>A vector holding these ids, laid out the way the client lays one out.</summary>
    private byte[] Set(params uint[] ids)
    {
        var buffer = Pin(new byte[Math.Max(ids.Length, 1) * sizeof(uint)]);

        for (var i = 0; i < ids.Length; i++)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(i * sizeof(uint)), ids[i]);
        }

        var header = Pin(new byte[12]);

        BitConverter.TryWriteBytes(header.AsSpan(4), (uint)Address(buffer).Value);
        BitConverter.TryWriteBytes(header.AsSpan(8), ids.Length);

        return header;
    }

    private static void Count(byte[] header, uint count) =>
        BitConverter.TryWriteBytes(header.AsSpan(8), count);

    private byte[] Pin(byte[] bytes)
    {
        _pinned.Add(GCHandle.Alloc(bytes, GCHandleType.Pinned));

        return bytes;
    }

    private static GameAddress Address(byte[] pinned) =>
        new((uint)Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0));
}
