using System.Globalization;
using System.Net;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class ConnectRedirectPatchTests
{
    private static readonly GameAddress Cave = new(0x2000_0000);

    /// <summary>A plausible winsock export address and its prologue.</summary>
    private static readonly GameAddress ConnectExport = new(0x71AB_4210);

    private static readonly byte[] Prologue = [0x8B, 0xFF, 0x55, 0x8B, 0xEC];

    /// <summary>
    /// Derived by transcribing the reference's <c>build_shellcode</c> and running it
    /// for the same inputs. The reference took a hover-counter address it always
    /// received as zero, so that branch never appeared in the emitted code and is not
    /// carried over.
    /// </summary>
    private const string ExpectedTrampoline =
        "60 8B 44 24 28 66 83 38 02 75 0D 66 C7 40 02 07 D0 C7 40 04 C0 A8 01 32 61 8B FF 55 " +
        "8B EC E9 F2 41 AB 51";

    [Fact]
    public void TrampolineMatchesTheReferenceByteForByte()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Parse("192.168.1.50"), 2000));

        code.ShouldBe(Parse(ExpectedTrampoline));
    }

    // The struct holds both fields in network byte order already, so they are written
    // without further swapping. Writing the port little-endian connects to port 53255.
    [Fact]
    public void PortIsWrittenInNetworkByteOrder()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Loopback, 2000));

        var index = IndexOfSequence(code, [0x66, 0xC7, 0x40, 0x02]);

        index.ShouldBeGreaterThanOrEqualTo(0);
        code[index + 4].ShouldBe((byte)0x07);
        code[index + 5].ShouldBe((byte)0xD0);
    }

    [Fact]
    public void AddressIsWrittenInNetworkByteOrder()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Parse("127.0.0.1"), 2000));

        var index = IndexOfSequence(code, [0xC7, 0x40, 0x04]);

        index.ShouldBeGreaterThanOrEqualTo(0);
        code.AsSpan(index + 3, 4).ToArray().ShouldBe([127, 0, 0, 1]);
    }

    // Rewriting a non-IPv4 sockaddr would corrupt the client's local IPC and name
    // resolution, which use the same entry point.
    [Fact]
    public void NonInetAddressesSkipTheRewrite()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Loopback, 2000));

        var compareIndex = IndexOfSequence(code, [0x66, 0x83, 0x38, 0x02]);
        compareIndex.ShouldBeGreaterThanOrEqualTo(0);

        code[compareIndex + 4].ShouldBe((byte)0x75, "the comparison should be followed by jne");

        // The branch must land on popad, past both writes.
        var jneOperandIndex = compareIndex + 5;
        var landing = jneOperandIndex + 1 + code[jneOperandIndex];
        code[landing].ShouldBe((byte)0x61);
    }

    [Fact]
    public void ReplaysTheStolenBytesBeforeContinuing()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Loopback, 2000));

        // popad, then the displaced prologue, then the jump back.
        var popAdIndex = Array.LastIndexOf(code, (byte)0x61);
        code.AsSpan(popAdIndex + 1, Prologue.Length).ToArray().ShouldBe(Prologue);
    }

    [Fact]
    public void ContinuesPastTheBytesItReplaced()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Loopback, 2000));

        code[^5].ShouldBe((byte)0xE9);
        var displacement = BitConverter.ToInt32(code, code.Length - 4);
        var resumeAt = unchecked((uint)(Cave.Value + code.Length + displacement));

        resumeAt.ShouldBe(ConnectExport.Value + (uint)Prologue.Length);
    }

    [Fact]
    public void FitsTheCodeCave()
    {
        var code = ConnectRedirectPatch.BuildTrampoline(
            Cave, ConnectExport, Prologue, new IPEndPoint(IPAddress.Loopback, 2000));

        code.Length.ShouldBeLessThanOrEqualTo(96);
    }

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
