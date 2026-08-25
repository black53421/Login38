using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the code that stands in front of the client's message loop.
/// </summary>
/// <remarks>
/// This one runs on the game's own message thread, thousands of times a second, so a wrong
/// byte in it is not a feature that does not work — it is a client that stops responding.
/// The whole cave is pinned.
/// </remarks>
public sealed class LowCpuDetourTests
{
    private static readonly GameAddress Cave = new(0x10000000);
    private static readonly GameAddress Peek = new(0x75100000);
    private static readonly nint Window = unchecked((nint)0xABCDEF01);

    /// <summary>What <c>PeekMessageA</c> actually starts with on this build of Windows.</summary>
    /// <remarks><c>mov edi, edi; push ebp; mov ebp, esp</c> — the hot-patch prologue.</remarks>
    private static readonly byte[] Prologue = [0x8B, 0xFF, 0x55, 0x8B, 0xEC];

    private static readonly LowCpuApi Api = new(
        Peek, new GameAddress(0x75ABCDEF), new GameAddress(0x75EEFF00), new GameAddress(0x75DDEEFF));

    /// <summary>
    /// The reference's own detour, transcribed instruction by instruction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out from the Rust source rather than produced by anything the port shares,
    /// so agreeing with it means the port encodes the same instructions and resolves the
    /// same branches.
    /// </para>
    /// <para>
    /// One difference, and it is the layout rather than the code: the reference padded the
    /// detour with ten <c>nop</c>s to put the trampoline at a hand-chosen <c>0x60</c>, and
    /// checked the detour still fitted with a <c>debug_assert!</c> that release builds
    /// drop. Here the trampoline follows the detour wherever it ends, so it cannot be
    /// outgrown. Everything before that point, and the order of the four slots after it,
    /// is the reference's.
    /// </para>
    /// </remarks>
    private static ReadOnlySpan<byte> Reference =>
    [
        0xFF, 0x74, 0x24, 0x14, 0xFF, 0x74, 0x24, 0x14, 0xFF, 0x74, 0x24, 0x14,
        0xFF, 0x74, 0x24, 0x14, 0xFF, 0x74, 0x24, 0x14, 0xE8, 0x3D, 0x00, 0x00,
        0x00, 0x85, 0xC0, 0x75, 0x36, 0xFF, 0x15, 0x60, 0x00, 0x00, 0x10, 0x3B,
        0x05, 0x68, 0x00, 0x00, 0x10, 0x74, 0x26, 0x53, 0x31, 0xDB, 0x43, 0x53,
        0xFF, 0x15, 0x6C, 0x00, 0x00, 0x10, 0x66, 0xA9, 0x00, 0x80, 0x75, 0x14,
        0x43, 0x81, 0xFB, 0xFF, 0x00, 0x00, 0x00, 0x7C, 0xEA, 0x5B, 0x6A, 0x32,
        0xFF, 0x15, 0x64, 0x00, 0x00, 0x10, 0xEB, 0x01, 0x5B, 0x33, 0xC0, 0xC2,
        0x14, 0x00, 0x8B, 0xFF, 0x55, 0x8B, 0xEC, 0xE9, 0xA5, 0xFF, 0x0F, 0x65,
        0xEF, 0xCD, 0xAB, 0x75, 0xFF, 0xEE, 0xDD, 0x75, 0x01, 0xEF, 0xCD, 0xAB,
        0x00, 0xFF, 0xEE, 0x75,
    ];

    private static byte[] Build() => LowCpuDetour.Build(Cave, Api, Window, Prologue);

    private static LowCpuLayout Layout() => LowCpuDetour.LayoutFor(Cave, Prologue.Length);

    [Fact]
    public void BuildsTheSameCodeTheReferenceDid() => Build().ShouldBe(Reference.ToArray());

    [Fact]
    public void AllocatesExactlyWhatItWrites() => Layout().Size.ShouldBe(Build().Length);

    // Each push moves esp, so reading the same displacement five times walks backwards
    // through the caller's arguments and leaves them in the order the call wants. Using
    // five different displacements — the obvious way to write it — would push arg5 five
    // times.
    [Fact]
    public void HandsTheCallersFiveArgumentsStraightThrough()
    {
        var code = Build();

        for (var i = 0; i < 5; i++)
        {
            code[(i * 4)..((i * 4) + 4)].ShouldBe(new byte[] { 0xFF, 0x74, 0x24, 0x14 });
        }
    }

    [Fact]
    public void CallsTheTrampolineRatherThanTheFunctionItReplaced()
    {
        var code = Build();

        code[20].ShouldBe((byte)0xE8);
        Target(code, at: 21).ShouldBe(Layout().Trampoline.Value);
    }

    // The trampoline is the front of PeekMessageA moved here, then a jump past the five
    // bytes the detour took. Calling it is calling the real function.
    [Fact]
    public void RunsTheStolenBytesAndGoesBackToTheRest()
    {
        var code = Build();
        var at = (int)(Layout().Trampoline.Value - Cave.Value);

        code[at..(at + Prologue.Length)].ShouldBe(Prologue);
        code[at + Prologue.Length].ShouldBe((byte)0xE9);
        Target(code, at + Prologue.Length + 1).ShouldBe(Peek.Value + LowCpuDetour.StolenLength);
    }

    // A call that found a message is a busy client; sleeping there would let input pile up
    // behind the delay. So that path skips the sleep and returns what the real call did.
    [Fact]
    public void ReturnsStraightAwayWhenThereIsAMessage()
    {
        var code = Build();

        code[25..27].ShouldBe(new byte[] { 0x85, 0xC0 });
        code[27].ShouldBe((byte)0x75);

        var landing = 29 + (sbyte)code[28];
        code[landing..(landing + 3)].ShouldBe(new byte[] { 0xC2, 0x14, 0x00 });
    }

    // Nothing to clean up on this path, so it goes to the same `xor eax, eax` the sleeping
    // path ends at rather than to its own exit.
    [Fact]
    public void SkipsTheSleepWhenTheGameIsInFront()
    {
        var code = Build();

        code[41].ShouldBe((byte)0x74);

        var landing = 43 + (sbyte)code[42];
        code[landing..(landing + 2)].ShouldBe(new byte[] { 0x33, 0xC0 });
    }

    // The helper's own macros drive the client while it is in the background, so a held
    // key is a reason not to throttle even when the window is behind everything else.
    [Fact]
    public void ChecksEveryVirtualKeyBeforeSleeping()
    {
        var code = Build();

        // xor ebx, ebx; inc ebx — the first key asked about.
        code[44..47].ShouldBe(new byte[] { 0x31, 0xDB, 0x43 });
        LowCpuDetour.FirstVirtualKey.ShouldBe((byte)1);

        // cmp ebx, 0xFF; jl — one past the last, because 0xFF is not a key.
        code[61..67].ShouldBe(new byte[] { 0x81, 0xFB, 0xFF, 0x00, 0x00, 0x00 });
        code[67].ShouldBe((byte)0x7C);
        (69 + (sbyte)code[68]).ShouldBe(47);
    }

    // ebx is the caller's to keep, and this runs on the game's message thread. Every way
    // out of the scan has to put it back.
    [Fact]
    public void GivesBackTheRegisterItBorrowedOnBothPaths()
    {
        var code = Build();

        code[43].ShouldBe((byte)0x53);

        // Found a key down: pop, then straight to the shared exit.
        code[58].ShouldBe((byte)0x75);
        (60 + (sbyte)code[59]).ShouldBe(80);
        code[80].ShouldBe((byte)0x5B);

        // Ran out of keys: pop before the sleep.
        code[69].ShouldBe((byte)0x5B);
    }

    [Fact]
    public void SleepsForFiftyMilliseconds()
    {
        var code = Build();

        code[70..72].ShouldBe(new byte[] { 0x6A, LowCpuDetour.SleepMilliseconds });
        LowCpuDetour.SleepMilliseconds.ShouldBe((byte)50);
    }

    // PeekMessageA is __stdcall with five arguments, so the detour standing in for it has
    // to take them off the stack itself.
    [Fact]
    public void CleansUpTheCallersArguments()
    {
        var code = Build();

        code[83..86].ShouldBe(new byte[] { 0xC2, 0x14, 0x00 });
    }

    [Fact]
    public void PutsTheFourAddressesInTheSlotsThatCallThem()
    {
        var code = Build();
        var layout = Layout();

        Slot(code, layout.ForegroundWindowSlot).ShouldBe(Api.ForegroundWindow.Value);
        Slot(code, layout.SleepSlot).ShouldBe(Api.Sleep.Value);
        Slot(code, layout.WindowSlot).ShouldBe(unchecked((uint)Window));
        Slot(code, layout.AsyncKeyStateSlot).ShouldBe(Api.AsyncKeyState.Value);
    }

    // Somebody else's detour. Taking the bytes for our own would send their calls into the
    // middle of it, and putting ours over the top would take out their trampoline.
    [Theory]
    [InlineData((byte)0xE9)]
    [InlineData((byte)0xEB)]
    public void SeesWhenSomethingElseIsAlreadyHookedThere(byte opcode) =>
        LowCpuToggle.AlreadyHooked([opcode, 0x00, 0x00, 0x00, 0x00]).ShouldBeTrue();

    // The hot-patch prologue every user32 function starts with, and an ordinary one.
    [Theory]
    [InlineData(new byte[] { 0x8B, 0xFF, 0x55, 0x8B, 0xEC })]
    [InlineData(new byte[] { 0x55, 0x8B, 0xEC, 0x83, 0xEC })]
    public void TakesAFunctionNobodyElseIsInFrontOf(byte[] prologue) =>
        LowCpuToggle.AlreadyHooked(prologue).ShouldBeFalse();

    [Fact]
    public void FollowsTheLowCpuSwitch()
    {
        var toggle = new LowCpuToggle(NullLogger<LowCpuToggle>.Instance);

        toggle.WantedBy(new AuxSettings { Misc = new MiscToggles { LowCpu = true } }).ShouldBeTrue();
        toggle.WantedBy(new AuxSettings { Misc = new MiscToggles { LowCpu = false } }).ShouldBeFalse();
    }

    [Fact]
    public void HasAName() => new LowCpuToggle(NullLogger<LowCpuToggle>.Instance).Name.ShouldBe("low-cpu");

    // Nothing to take out, so nothing to do — and nothing allocated.
    [Fact]
    public void DoesNothingWhenItIsOffAndWasNeverOn()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        new LowCpuToggle(NullLogger<LowCpuToggle>.Instance)
            .Apply(process, wanted: false).ShouldBeTrue();
    }

    private static uint Target(byte[] code, int at) =>
        (uint)(Cave.Value + at + 4 + BitConverter.ToInt32(code, at));

    private static uint Slot(byte[] code, GameAddress slot) =>
        BitConverter.ToUInt32(code, (int)(slot.Value - Cave.Value));
}
