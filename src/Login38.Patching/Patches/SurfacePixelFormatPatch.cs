using Login38.Core;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>Which 16-bit layout the client's offscreen surfaces are created with.</summary>
public enum SurfaceColourLayout
{
    /// <summary>Follow the blitter's own choice at run time.</summary>
    Auto,

    /// <summary>5 bits each of red, green and blue.</summary>
    Rgb555,

    /// <summary>5 red, 6 green, 5 blue.</summary>
    Rgb565,
}

/// <summary>
/// Gives the client's offscreen surfaces an explicit pixel format, so text drawn into
/// them stops flickering and the IME candidate window appears.
/// </summary>
/// <remarks>
/// <para>
/// The client creates its offscreen surfaces without <c>DDSD_PIXELFORMAT</c>, leaving
/// DirectDraw to pick one. On real hardware it picked the display's format and everything
/// matched; on Windows 11, where DirectDraw is emulated over Direct3D, it can pick a
/// different 16-bit layout from the one the client's own blitter assumes. The result is
/// the typing box flickering between two colour interpretations, and an IME candidate
/// list that never composites at all.
/// </para>
/// <para>
/// The fix is to set the flag that says "I am supplying the format" and then supply one.
/// The flag is an immediate inside an instruction, so it can be written in place; the
/// format is seven fields that will not fit, so a cave writes them and the instruction
/// that used to follow the flag is diverted into it.
/// </para>
/// <para>
/// <see cref="SurfaceColourLayout.Auto"/> is the default, and reads the client's own
/// selector rather than deciding: whatever the blitter is about to assume is by definition
/// the layout that will not flicker. Forcing 555 needs the present hook running as well —
/// on unhooked Windows 11 the 16-bit-to-32-bit present fails and the screen goes black —
/// which is why it is not the default.
/// </para>
/// </remarks>
public sealed class SurfacePixelFormatPatch : IGamePatch
{
    /// <summary>Turns the patch off, leaving the client's surfaces as they were.</summary>
    public const string DisableVariable = "LOGIN38_DISABLE_SURFACE_PF";

    /// <summary>Overrides the layout: <c>auto</c>, <c>555</c> or <c>565</c>.</summary>
    public const string LayoutVariable = "LOGIN38_SURFACE_PF_FORMAT";

    /// <summary>Pins the blitter's selector: <c>555</c>, <c>565</c>, or <c>none</c>.</summary>
    public const string PinVariable = "LOGIN38_SURFACE_PF_PIN";

    /// <summary>
    /// The <c>dwFlags</c> immediate of the surface description the client fills in.
    /// </summary>
    /// <remarks>
    /// Inside <c>mov dword ptr [ebp-0x84], imm32</c> at <c>0x00448336</c>, whose ten bytes
    /// end exactly where <see cref="DetourSite"/> begins.
    /// </remarks>
    private static readonly GameAddress DescriptionFlags = new(0x0044_833C);

    /// <summary>DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH.</summary>
    private static ReadOnlySpan<byte> FlagsOriginal => [0x07, 0x00, 0x00, 0x00];

    /// <summary>The original three, plus DDSD_PIXELFORMAT.</summary>
    private static ReadOnlySpan<byte> FlagsPatched => [0x07, 0x10, 0x00, 0x00];

    /// <summary><c>mov dword ptr [ebp-0x20], 0x840</c> — the surface caps.</summary>
    private static readonly GameAddress DetourSite = new(0x0044_8340);

    private static ReadOnlySpan<byte> DetourOriginal => [0xC7, 0x45, 0xE0, 0x40, 0x08, 0x00, 0x00];

    /// <summary>Where the cave rejoins the client: the instruction after the caps store.</summary>
    private static readonly GameAddress DetourReturn = new(0x0044_8347);

    /// <summary>
    /// The blitter's layout selector: zero for 555, non-zero for 565.
    /// </summary>
    private static readonly GameAddress LayoutSelector = new(0x009A_235C);

    /// <summary>
    /// The immediates of the two <c>mov byte ptr [selector], imm8</c> that set it — one on
    /// the 565 path, one on the 32-bit fallback.
    /// </summary>
    private static readonly GameAddress[] SelectorWrites =
        [new(0x0044_8D35), new(0x0044_8D52)];

    /// <summary>Comfortably above the largest of the three shellcode variants.</summary>
    private const int CaveSize = 128;

    /// <summary>
    /// <c>[ebp-0x40]</c> — the <c>DDPIXELFORMAT</c> inside the surface description, which
    /// the client keeps at <c>[ebp-0x88]</c> with the format at offset <c>+0x48</c>.
    /// </summary>
    private const int PixelFormat = -0x40;

    // DDPIXELFORMAT member offsets.
    private const int Size = PixelFormat + 0x00;
    private const int Flags = PixelFormat + 0x04;
    private const int BitCount = PixelFormat + 0x0C;
    private const int RedMask = PixelFormat + 0x10;
    private const int GreenMask = PixelFormat + 0x14;
    private const int BlueMask = PixelFormat + 0x18;

    /// <summary><c>[ebp-0x20]</c> — the surface caps the detour overwrites.</summary>
    private const int SurfaceCaps = -0x20;

    private const uint DdpfRgb = 0x40;

    private readonly ILogger<SurfacePixelFormatPatch> _logger;

    public SurfacePixelFormatPatch(ILogger<SurfacePixelFormatPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "surface-pixel-format";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.WindowVisible;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => !EnvironmentSwitch.IsEnabled(DisableVariable);

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;
        var layout = ConfiguredLayout();

        if (IsAlreadyApplied(process))
        {
            _logger.LogDebug("Surface descriptions already carry a pixel format");
            return;
        }

        Expect(process, DescriptionFlags, FlagsOriginal, "the surface description flags");
        Expect(process, DetourSite, DetourOriginal, "the surface caps store");

        // The cave is written first so that the instant the detour lands there is
        // something valid to jump to.
        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, DetourReturn, layout);

        if (shellcode.Length > CaveSize)
        {
            throw new GameProcessException(
                $"The pixel format shellcode is {shellcode.Length} bytes but the cave is {CaveSize}.");
        }

        process.WriteCode(cave, shellcode);

        // Both writes together, with nothing running: separately, a thread can see the
        // flag saying a format is supplied before the cave that supplies it is reachable.
        using (process.SuspendThreads())
        {
            process.WriteCode(DetourSite, InlineHook.BuildJump(DetourSite, cave, DetourOriginal.Length));
            process.WriteCode(DescriptionFlags, FlagsPatched);
        }

        _logger.LogInformation(
            "Offscreen surfaces now specify {Layout} explicitly, through a cave at {Cave}", layout, cave);

        PinSelector(process);
    }

    /// <summary>Whether a previous run already patched this client.</summary>
    /// <remarks>
    /// Judged from the flag rather than the detour: the flag is the part the client reads,
    /// and it is only ever set by this patch.
    /// </remarks>
    private static bool IsAlreadyApplied(RemoteProcess process) =>
        process.ReadBytes(DescriptionFlags, FlagsPatched.Length).AsSpan().SequenceEqual(FlagsPatched);

    private static void Expect(
        RemoteProcess process, GameAddress address, ReadOnlySpan<byte> expected, string what)
    {
        var actual = process.ReadBytes(address, expected.Length);

        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new GameProcessException(
                $"Expected {what} at {address} to be {BytePattern.Format(expected)} " +
                $"but found {BytePattern.Format(actual)}.");
        }
    }

    /// <summary>
    /// Forces the blitter's own selector to a layout, for diagnosing a mismatch.
    /// </summary>
    /// <remarks>
    /// Off by default and best-effort by design: it is a lever for working out which
    /// layout a machine wants, not part of the fix. A site that does not match is reported
    /// and left alone rather than failing the patch that has already succeeded.
    /// </remarks>
    private void PinSelector(RemoteProcess process)
    {
        if (ConfiguredPin() is not { } pinned)
        {
            return;
        }

        foreach (var site in SelectorWrites)
        {
            var current = process.Read<byte>(site);

            if (current is not (0x00 or 0x01))
            {
                _logger.LogWarning(
                    "Not pinning the selector at {Address}: expected 00 or 01, found {Actual:X2}", site, current);
                continue;
            }

            process.WriteCode(site, [pinned]);
        }

        _logger.LogInformation("Pinned the blitter's layout selector to {Layout}",
            pinned == 0 ? SurfaceColourLayout.Rgb555 : SurfaceColourLayout.Rgb565);
    }

    /// <summary>The layout the operator asked for, defaulting to following the client.</summary>
    private static SurfaceColourLayout ConfiguredLayout() => ParseLayout(EnvironmentSwitch.Value(LayoutVariable));

    /// <inheritdoc cref="ConfiguredLayout"/>
    /// <remarks>
    /// Anything unrecognised means <see cref="SurfaceColourLayout.Auto"/> rather than an
    /// error. This is a diagnostic lever set by hand on a player's machine, and a typo in
    /// it should not be the reason the game will not start.
    /// </remarks>
    internal static SurfaceColourLayout ParseLayout(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "555" or "rgb555" => SurfaceColourLayout.Rgb555,
            "565" or "rgb565" => SurfaceColourLayout.Rgb565,
            _ => SurfaceColourLayout.Auto,
        };

    /// <summary>The value to pin the selector to, or null to leave it alone.</summary>
    private static byte? ConfiguredPin() => ParsePin(EnvironmentSwitch.Value(PinVariable));

    /// <inheritdoc cref="ConfiguredPin"/>
    internal static byte? ParsePin(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "555" or "0" => 0,
            "565" or "1" => 1,
            _ => null,
        };

    /// <summary>
    /// Assembles the cave: the caps store the detour displaced, then the pixel format.
    /// </summary>
    /// <remarks>
    /// Entered by a jump, so <c>ebp</c> is still the client's frame and every field is
    /// written as a local of it. No register is touched on the fixed paths;
    /// <see cref="SurfaceColourLayout.Auto"/> uses <c>eax</c>, which the displaced
    /// instruction does not hold anything in.
    /// </remarks>
    internal static byte[] BuildShellcode(GameAddress cave, GameAddress rejoin, SurfaceColourLayout layout)
    {
        var code = new ShellcodeBuilder(cave)
            .MovLocalDword(SurfaceCaps, 0x840)
            .MovLocalDword(Size, 0x20)
            .MovLocalDword(Flags, DdpfRgb)
            .MovLocalDword(BitCount, 16)

            // Blue occupies the bottom five bits either way, so only red and green differ.
            .MovLocalDword(BlueMask, 0x001F);

        switch (layout)
        {
            case SurfaceColourLayout.Rgb555:
                Emit555(code);
                break;

            case SurfaceColourLayout.Rgb565:
                Emit565(code);
                break;

            default:
                code.MovAlFrom(LayoutSelector).TestAlAl();
                var zeroMeans555 = code.ShortJumpIfZero();

                Emit565(code);
                var done = code.ShortJumpAlways();

                code.MarkLabel(zeroMeans555);
                Emit555(code);
                code.MarkLabel(done);
                break;
        }

        return code.JumpTo(rejoin).Build();

        static void Emit555(ShellcodeBuilder code) =>
            code.MovLocalDword(RedMask, 0x7C00).MovLocalDword(GreenMask, 0x03E0);

        static void Emit565(ShellcodeBuilder code) =>
            code.MovLocalDword(RedMask, 0xF800).MovLocalDword(GreenMask, 0x07E0);
    }
}
