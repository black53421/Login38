using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Makes the chat input box capture its background from the screen, so it stops drawing
/// black when the game is windowed.
/// </summary>
/// <remarks>
/// <para>
/// Opening the box builds an offscreen bitmap of its own size and blits whatever is behind
/// it into that bitmap; painting then stretches the bitmap over the box and draws the text
/// on top. The blit's source is a device context taken from the DirectDraw screen surface,
/// at coordinates derived from the viewport offset the client keeps for full screen.
/// </para>
/// <para>
/// Full screen, that device context holds the rendered frame and the box looks right.
/// Windowed on Windows 11, where DirectDraw runs over Direct3D 9, the same call hands back
/// an empty or mis-offset buffer — so the box shows black, or shows a piece of the map
/// from somewhere else entirely.
/// </para>
/// <para>
/// The composited frame is on the real screen whatever DirectDraw thinks, so this replaces
/// that one blit with a screen capture: ask the window where it actually is, take a device
/// context for the desktop, and copy from there. Runs once each time the box opens.
/// </para>
/// <para>
/// Not used when the present hook is running, which composes the box from the swapchain
/// instead. Two captures into the same bitmap is one too many.
/// </para>
/// </remarks>
public sealed class InputBoxBackgroundPatch : IGamePatch
{
    /// <summary>
    /// The capture, as <c>cmp dword ptr [gate], 3; jne short; push SRCCOPY</c>.
    /// </summary>
    /// <remarks>
    /// Located by signature rather than by address: the client in the field is built a
    /// little later than the one these addresses were read from and this whole region sits
    /// <c>0x390</c> higher. The paint routine has a matching gate, but reaches it through
    /// <c>movzx</c>/<c>test</c>, so it cannot be confused with this one.
    /// </remarks>
    private const string CaptureSignature = "83 3D ?? ?? ?? ?? 03 75 ?? 68 20 00 CC 00";

    /// <summary>Where that signature is looked for.</summary>
    /// <remarks>
    /// Narrow on purpose. The match has to be unique to be trusted, and uniqueness across
    /// a hundred-kilobyte window containing the widget code is a much stronger statement
    /// than uniqueness across the whole image.
    /// </remarks>
    private static readonly GameAddress SearchStart = new(0x0059_0000);

    /// <inheritdoc cref="SearchStart"/>
    private static readonly GameAddress SearchEnd = new(0x005B_0000);

    /// <summary>The <c>cmp</c> the detour replaces.</summary>
    private const int CaptureInstructionLength = 7;

    /// <summary>Offset of the <c>jne</c>'s displacement within the signature.</summary>
    private const int SkipDisplacementOffset = 8;

    private const uint SrcCopy = 0x00CC_0020;

    // Fields of the edit control, from its own frame.
    private const int This = -0x10;
    private const int MemoryDc = -0x04;
    private const int WindowHandle = 0x2D8;
    private const int Left = 0x8C;
    private const int Right = 0x94;
    private const int Top = 0x88;
    private const int Bottom = 0x90;

    private const int CaveSize = 256;

    /// <summary>Scratch inside the cave, past anything the code will reach.</summary>
    private const int RectOffset = 0xC0;

    /// <inheritdoc cref="RectOffset"/>
    private const int ScreenDcOffset = 0xD0;

    private const int RectTopOffset = 4;

    private readonly ILogger<InputBoxBackgroundPatch> _logger;

    public InputBoxBackgroundPatch(ILogger<InputBoxBackgroundPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "input-box-background";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.WindowVisible;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !context.PresentHookInstalled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;

        // Captured fresh rather than taken from the shared image: this region is decrypted
        // late, and the shared copy was taken before the window existed.
        var region = MemorySnapshot.Capture(process, SearchStart, SearchEnd);

        var site = region.FindOnly(BytePattern.Parse(CaptureSignature), "the input box background capture")
            ?? throw new GameProcessException(
                $"No input box background capture between {SearchStart} and {SearchEnd}.");

        if (InlineHook.IsInstalledAt(process, site))
        {
            _logger.LogDebug("The input box background capture at {Address} is already diverted", site);
            return;
        }

        // Where the client goes when the gate says it is not in the world — which is also
        // where this should end up, since the capture is all that is being replaced.
        var skip = (sbyte)process.Read<byte>(site + SkipDisplacementOffset);
        var rejoin = site + (SkipDisplacementOffset + 1 + skip);

        var api = ScreenCaptureApi.Resolve(process, cancellationToken);
        _logger.LogDebug("Screen capture through {Api}", api);

        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, rejoin, api);

        if (shellcode.Length > RectOffset)
        {
            throw new GameProcessException(
                $"The input box shellcode is {shellcode.Length} bytes and would overrun its own scratch at {RectOffset:X}.");
        }

        process.WriteCode(cave, shellcode);

        using (process.SuspendThreads())
        {
            process.WriteCode(site, InlineHook.BuildJump(site, cave, CaptureInstructionLength));
        }

        _logger.LogInformation(
            "The input box at {Site} now captures its background from the screen, through a cave at {Cave}",
            site, cave);
    }

    /// <summary>
    /// Assembles the replacement capture.
    /// </summary>
    /// <remarks>
    /// Entered by a jump, so <c>ebp</c> is still the client's frame and the control and its
    /// memory device context are read straight out of it. <c>pushad</c> spans the whole
    /// body, which makes the registers used here free regardless of what the client had
    /// live across the instruction being replaced.
    /// </remarks>
    internal static byte[] BuildShellcode(GameAddress cave, GameAddress rejoin, ScreenCaptureApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var rect = cave + RectOffset;
        var screenDc = cave + ScreenDcOffset;

        return new ShellcodeBuilder(cave)
            .PushAd()

            .Bytes([0x8B, 0x75, unchecked((byte)(sbyte)This)])       // mov esi, [ebp-0x10] — the control
            .Bytes([0x8B, 0x9E]).Dword(Right)                        // mov ebx, [esi+0x94]
            .Bytes([0x2B, 0x9E]).Dword(Left)                         // sub ebx, [esi+0x8C] — width
            .Bytes([0x8B, 0xBE]).Dword(Bottom)                       // mov edi, [esi+0x90]
            .Bytes([0x2B, 0xBE]).Dword(Top)                          // sub edi, [esi+0x88] — height

            // GetWindowRect(control->hwnd, &rect). Asking the window where it is avoids the
            // viewport offset the client maintains, which is only meaningful full screen.
            .PushImm32(rect)
            .Bytes([0xFF, 0xB6]).Dword(WindowHandle)                 // push [esi+0x2D8]
            .MovEax(api.GetWindowRect.Value).CallEax()

            // GetDC(NULL) — the desktop, which is where the composited frame is.
            .PushImm8(0)
            .MovEax(api.GetDc.Value).CallEax()
            .MovEaxTo(screenDc)

            // BitBlt(memoryDc, 0, 0, width, height, screenDc, rect.left, rect.top, SRCCOPY)
            .PushImm32(SrcCopy)
            .PushPtr(rect + RectTopOffset)
            .PushPtr(rect)
            .PushPtr(screenDc)
            .Byte(0x57)                                              // push edi — height
            .Byte(0x53)                                              // push ebx — width
            .PushImm8(0).PushImm8(0)
            .PushLocal(MemoryDc)
            .MovEax(api.BitBlt.Value).CallEax()

            .PushPtr(screenDc)
            .PushImm8(0)
            .MovEax(api.ReleaseDc.Value).CallEax()

            .PopAd()
            .JumpTo(rejoin)
            .Build();
    }
}

/// <summary>
/// Where the screen-capture entry points live in the game process.
/// </summary>
/// <param name="GetWindowRect"><c>user32!GetWindowRect</c>.</param>
/// <param name="GetDc"><c>user32!GetDC</c>.</param>
/// <param name="BitBlt"><c>gdi32!BitBlt</c>.</param>
/// <param name="ReleaseDc"><c>user32!ReleaseDC</c>.</param>
/// <remarks>
/// Read out of the game's own module list. System DLLs do share a base across processes
/// within a boot, which is what let the reference resolve these in the launcher and use
/// them in the game — but that is a property of how Windows happens to relocate them, not
/// a guarantee, and reading the right process costs one module lookup.
/// </remarks>
public sealed record ScreenCaptureApi(
    GameAddress GetWindowRect, GameAddress GetDc, GameAddress BitBlt, GameAddress ReleaseDc)
{
    private const string UserModule = "user32.dll";

    private const string GdiModule = "gdi32.dll";

    /// <summary>Both are loaded before the client's first instruction runs.</summary>
    private static readonly TimeSpan ModuleWaitTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ModuleWaitPoll = TimeSpan.FromMilliseconds(100);

    internal static ScreenCaptureApi Resolve(RemoteProcess process, CancellationToken cancellationToken)
    {
        var user = Module(process, UserModule, cancellationToken);
        var gdi = Module(process, GdiModule, cancellationToken);

        return new ScreenCaptureApi(
            Export(process, user, UserModule, "GetWindowRect"),
            Export(process, user, UserModule, "GetDC"),
            Export(process, gdi, GdiModule, "BitBlt"),
            Export(process, user, UserModule, "ReleaseDC"));
    }

    private static GameAddress Module(RemoteProcess process, string name, CancellationToken cancellationToken) =>
        process.WaitForModuleAsync(name, ModuleWaitTimeout, ModuleWaitPoll, cancellationToken)
            .GetAwaiter().GetResult()
        ?? throw new GameProcessException($"{name} was not loaded within {ModuleWaitTimeout.TotalSeconds:0}s.");

    private static GameAddress Export(RemoteProcess process, GameAddress module, string moduleName, string export) =>
        process.FindExport(module, export)
        ?? throw new GameProcessException($"{moduleName} at {module} does not export {export}.");
}
