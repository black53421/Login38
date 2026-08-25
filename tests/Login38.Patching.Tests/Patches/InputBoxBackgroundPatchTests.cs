using System.Globalization;
using Login38.Core.Servers;
using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the screen-capture shellcode against what the Rust build emits, byte for byte, and
/// checks that it stands aside for the present hook.
/// </summary>
/// <remarks>
/// The expected sequence comes from transcribing the reference's
/// <c>build_input_box_shellcode</c> and running it for a cave at <c>0x07000000</c>
/// rejoining at <c>0x0059ABCD</c>, with stand-in entry points. It runs inside the client's
/// own frame with the client's registers live, so a wrong displacement corrupts the caller
/// rather than failing where it can be seen.
/// </remarks>
public sealed class InputBoxBackgroundPatchTests
{
    private static readonly GameAddress Cave = new(0x0700_0000);

    private static readonly GameAddress Rejoin = new(0x0059_ABCD);

    private static readonly ScreenCaptureApi Api = new(
        GetWindowRect: new GameAddress(0x7700_1000),
        GetDc: new GameAddress(0x7700_2000),
        BitBlt: new GameAddress(0x7600_3000),
        ReleaseDc: new GameAddress(0x7700_4000));

    private const string Expected =
        "60 8B 75 F0 8B 9E 94 00 00 00 2B 9E 8C 00 00 00 8B BE 90 00 00 00 2B BE 88 00 00 00 " +
        "68 C0 00 00 07 FF B6 D8 02 00 00 B8 00 10 00 77 FF D0 6A 00 B8 00 20 00 77 FF D0 A3 " +
        "D0 00 00 07 68 20 00 CC 00 FF 35 C4 00 00 07 FF 35 C0 00 00 07 FF 35 D0 00 00 07 57 " +
        "53 6A 00 6A 00 FF 75 FC B8 00 30 00 76 FF D0 FF 35 D0 00 00 07 6A 00 B8 00 40 00 77 " +
        "FF D0 61 E9 55 AB 59 F9";

    [Fact]
    public void MatchesTheReferenceByteForByte() => Build().ShouldBe(Parse(Expected));

    // The client has its own values live in these registers across the instruction being
    // replaced, and this uses four of them. Without the pair the box works and something
    // else in the widget breaks minutes later.
    [Fact]
    public void SavesAndRestoresEveryRegister()
    {
        var code = Build();

        code[0].ShouldBe((byte)0x60);
        code[^6].ShouldBe((byte)0x61);
    }

    // rel32 is measured from the end of the jump, and the target is the client's own
    // cleanup path — the one the gate would have taken.
    [Fact]
    public void RejoinsTheClientAfterTheCapture()
    {
        var code = Build();

        code[^5].ShouldBe((byte)0xE9);
        (Cave + code.Length + BitConverter.ToInt32(code, code.Length - 4)).ShouldBe(Rejoin);
    }

    // Everything is written to and read from scratch past the end of the code. Overlap
    // would have the capture overwrite its own instructions on the way through.
    [Fact]
    public void KeepsItsScratchClearOfItsCode()
    {
        var code = Build();

        // The rectangle sits at cave+0xC0 and the device context at cave+0xD0.
        code.Length.ShouldBeLessThan(0xC0);
    }

    // The stack arguments are pushed right to left, so the order in the code is the
    // reverse of the signature. Getting it wrong blits the wrong rectangle silently.
    [Fact]
    public void PassesBitBltItsArgumentsInReverse()
    {
        var code = BytePattern.Format(Build());

        var expected = string.Join(" ",
            "68 20 00 CC 00",        // SRCCOPY
            "FF 35 C4 00 00 07",     // rect.top
            "FF 35 C0 00 00 07",     // rect.left
            "FF 35 D0 00 00 07",     // the screen device context
            "57",                    // height
            "53",                    // width
            "6A 00 6A 00",           // destination y, x
            "FF 75 FC");             // the control's memory device context

        code.ShouldContain(expected);
    }

    // The screen device context has to be handed back. Leaking one per box opening
    // exhausts the process's quota over a session.
    [Fact]
    public void ReleasesTheScreenDeviceContext()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("A3 D0 00 00 07");                       // stored after GetDC
        code.ShouldContain("FF 35 D0 00 00 07 6A 00 B8 00 40 00 77 FF D0");  // ReleaseDC(NULL, it)
    }

    // Width and height come from the control's own rectangle; the position comes from the
    // window. Mixing the two is what the reference's original code did wrong.
    [Fact]
    public void TakesTheSizeFromTheControlAndThePositionFromTheWindow()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("8B 9E 94 00 00 00 2B 9E 8C 00 00 00");  // right - left
        code.ShouldContain("8B BE 90 00 00 00 2B BE 88 00 00 00");  // bottom - top
        code.ShouldContain("FF B6 D8 02 00 00 B8 00 10 00 77");     // GetWindowRect(hwnd, ...)
    }

    // Both compose the box background, and running both would have the second capture
    // overwrite the first.
    [Fact]
    public void StandsAsideForThePresentHook() =>
        ShouldApplyWith(presentHookInstalled: true).ShouldBeFalse();

    // The gate is what actually happened, not what was asked for: an injection that failed
    // is exactly when this fallback is needed.
    [Fact]
    public void RunsWhenThePresentHookIsNotThere() =>
        ShouldApplyWith(presentHookInstalled: false).ShouldBeTrue();

    private static bool ShouldApplyWith(bool presentHookInstalled)
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        var context = new GamePatchContext(
            process, AppContext.BaseDirectory, new AuxConfig(), new ServerInfo("test", "127.0.0.1", 2000))
        {
            PresentHookInstalled = presentHookInstalled,
        };

        return new InputBoxBackgroundPatch(NullLogger<InputBoxBackgroundPatch>.Instance).ShouldApply(context);
    }

    private static byte[] Build() => InputBoxBackgroundPatch.BuildShellcode(Cave, Rejoin, Api);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
