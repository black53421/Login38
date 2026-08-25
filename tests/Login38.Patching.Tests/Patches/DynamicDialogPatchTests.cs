using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins both halves of the server-sent dialog hook.
/// </summary>
/// <remarks>
/// The expected sequences come from transcribing the reference's
/// <c>build_loader_wrapper_shellcode</c> and <c>build_parse_hook_shellcode</c> and running
/// them for a cave at <c>0x0A000000</c> with the reference's own layout. Both hooks sit on
/// paths every dialog goes through, so what matters most is that a dialog which is not one
/// of ours comes out of them unchanged.
/// </remarks>
public sealed class DynamicDialogPatchTests
{
    private static readonly GameAddress Cave = new(0x0A00_0000);

    private static readonly GameAddress Wrapper = Cave;

    private static readonly GameAddress ParseHook = Cave + 0x0100;

    private static readonly GameAddress BodyPointer = Cave + 0x0200;

    private static readonly GameAddress Body = Cave + 0x0300;

    private const string ExpectedWrapper =
        "8B 44 24 04 85 C0 74 17 80 38 40 75 12 A1 00 02 00 0A 85 C0 74 09 89 44 24 04 E9 C1 44 49 F6 E9 " +
        "8C 43 49 F6";

    private const string ExpectedParseHook =
        "8B 75 08 80 7E 04 40 74 0F 8D 4D EC 51 8B 15 B8 8E 9A 00 E9 4A 79 52 F6 C7 05 00 02 00 0A 00 03 " +
        "00 0A C6 05 00 03 00 0A 00 8D 4D EC 51 68 00 03 00 0A 68 00 70 00 00 8D 85 DC FE FF FF 50 68 00 " +
        "01 00 00 8D 4D E4 51 68 70 4A 8D 00 8B 55 08 52 E8 BB 1F 52 F6 83 C4 20 89 45 08 E9 32 79 52 F6";

    [Fact]
    public void WrapperMatchesTheReferenceByteForByte() =>
        DynamicDialogPatch.BuildWrapper(Wrapper, BodyPointer).ShouldBe(Parse(ExpectedWrapper));

    [Fact]
    public void ParseHookMatchesTheReferenceByteForByte() =>
        DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body).ShouldBe(Parse(ExpectedParseHook));

    // Both hooks fit the slots the cave layout gives them, which is what keeps the body
    // buffer where the other half expects to find it.
    [Fact]
    public void BothHooksFitTheirPartOfTheCave()
    {
        DynamicDialogPatch.BuildWrapper(Wrapper, BodyPointer).Length.ShouldBeLessThanOrEqualTo(0x100);
        DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body).Length.ShouldBeLessThanOrEqualTo(0x100);
    }

    // One convention, in one byte, checked before anything else happens. Everything the
    // server has always sent goes down the client's own path.
    [Fact]
    public void RecognisesADialogByItsFirstCharacter()
    {
        BytePattern.Format(DynamicDialogPatch.BuildWrapper(Wrapper, BodyPointer))
            .ShouldContain("80 38 40");                       // cmp byte [eax], '@'

        BytePattern.Format(DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body))
            .ShouldContain("80 7E 04 40");                    // cmp byte [esi+4], '@' — past the id
    }

    // The loader takes a name and the parser takes the text, in the same argument slot. So
    // the whole substitution is one store, and the stack is what the callee expects either way.
    [Fact]
    public void HandsTheParserTheBodyInTheSlotTheNameWasIn()
    {
        var code = DynamicDialogPatch.BuildWrapper(Wrapper, BodyPointer);

        BytePattern.Format(code).ShouldContain("A1 00 02 00 0A");    // mov eax, [body pointer]
        BytePattern.Format(code).ShouldContain("89 44 24 04");       // mov [esp+4], eax

        // The tail is the two jumps: to the memory parser, then to the file loader.
        code[^10].ShouldBe((byte)0xE9);
        (Wrapper + (code.Length - 10 + 5 + BitConverter.ToInt32(code, code.Length - 9)))
            .ShouldBe(new GameAddress(0x0049_44E0));
    }

    // Three ways of not being one of ours — no name, a name that is not ours, and no body
    // captured — and every one of them reaches the loader the wrapper replaced, with the
    // stack untouched.
    [Fact]
    public void SendsEveryOtherDialogToTheLoaderItStandsInFrontOf()
    {
        var code = DynamicDialogPatch.BuildWrapper(Wrapper, BodyPointer);
        var loader = code.Length - 5;

        code[loader].ShouldBe((byte)0xE9);
        (Wrapper + (loader + 5 + BitConverter.ToInt32(code, loader + 1)))
            .ShouldBe(new GameAddress(0x0049_43B0));

        foreach (var branch in ShortBranches(code))
        {
            (branch + 1 + (sbyte)code[branch]).ShouldBe(loader);
        }
    }

    // Ten bytes displaced, ten bytes replayed. Five would leave the tail of an instruction
    // behind, which decodes as whatever those bytes happen to be.
    [Fact]
    public void ReplaysEverythingTheJumpDisplaced()
    {
        var code = DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body);

        code[9..19].ShouldBe([0x8D, 0x4D, 0xEC, 0x51, 0x8B, 0x15, 0xB8, 0x8E, 0x9A, 0x00]);
        code[19].ShouldBe((byte)0xE9);
        (ParseHook + (19 + 5 + BitConverter.ToInt32(code, 20))).ShouldBe(new GameAddress(0x0052_7A62));
    }

    // The body is captured by the client's own reader with a longer format string, not by
    // anything written here — so a field this does not understand is still parsed correctly.
    [Fact]
    public void CapturesTheBodyWithTheClientsOwnReader()
    {
        var code = BytePattern.Format(DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body));

        code.ShouldContain("68 70 4A 8D 00");                 // push "dssh"
        code.ShouldContain("8B 55 08 52");                    // mov edx, [ebp+8]; push edx
        code.ShouldContain("83 C4 20");                       // add esp, 0x20 — eight arguments
    }

    // Every string field the reader writes has to be given a limit, or a long dialog runs
    // past the end of whatever it is written into.
    [Fact]
    public void GivesEveryFieldItsOwnLimit()
    {
        var code = BytePattern.Format(DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body));

        code.ShouldContain("68 00 70 00 00");                 // push 0x7000 — the body
        code.ShouldContain("68 00 01 00 00");                 // push 0x100 — the name
    }

    // The pointer is what the two halves share, and it is set only once the buffer has been
    // made safe to read: terminated first, then pointed at.
    [Fact]
    public void PointsAtABodyThatIsAlreadyReadable()
    {
        var code = BytePattern.Format(DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body));

        code.ShouldContain("C7 05 00 02 00 0A 00 03 00 0A");  // mov [pointer], body
        code.ShouldContain("C6 05 00 03 00 0A 00");           // mov byte [body], 0
    }

    // The client resumes past its own parse with the result where it would have put it, so
    // everything downstream is its own code.
    [Fact]
    public void RejoinsTheClientWithTheResultWhereItExpectsIt()
    {
        var code = DynamicDialogPatch.BuildParseHook(ParseHook, BodyPointer, Body);

        code[^8..^5].ShouldBe([0x89, 0x45, 0x08]);            // mov [ebp+8], eax
        code[^5].ShouldBe((byte)0xE9);
        (ParseHook + (code.Length - 5 + 5 + BitConverter.ToInt32(code, code.Length - 4)))
            .ShouldBe(new GameAddress(0x0052_7A92));
    }

    /// <summary>Offsets of the displacement byte of every <c>Jcc rel8</c> in the block.</summary>
    private static IEnumerable<int> ShortBranches(byte[] code)
    {
        for (var at = 0; at + 2 <= code.Length; at++)
        {
            if (code[at] is 0x74 or 0x75)
            {
                yield return at + 1;
            }
        }
    }

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
