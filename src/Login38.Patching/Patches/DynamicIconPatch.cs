using System.Text;
using Login38.Core.Icons;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Animates chosen item icons.
/// </summary>
/// <remarks>
/// <para>
/// An item icon is not a surface the client draws through an interface — it is a run-length
/// image decoded straight into the frame buffer. So there is nothing to substitute at draw
/// time. What there is, is one function every path goes through to turn an item's graphic id
/// into the image for it: every panel, the cache, and the lazy load behind it.
/// </para>
/// <para>
/// The launcher decodes the operator's frames, converts them into the client's own image
/// format, writes them into the client's memory, and hooks that function. When the id asked
/// for is one with an animation and the clock is inside its cycle, the hook returns the
/// frame; otherwise the client resolves its own image and nothing has changed.
/// </para>
/// <para>
/// The hook reads the clock rather than being driven by one, so every copy of the same item
/// on screen is on the same frame with nothing keeping them together — no timer, no thread,
/// and no list of what is currently visible.
/// </para>
/// </remarks>
public sealed class DynamicIconPatch : IGamePatch
{
    /// <summary>Turns a graphic id into the image for it.</summary>
    /// <remarks>
    /// The one gate every icon path goes through. The reference first hooked the preload
    /// cache instead, which lazily-loaded icons — the high ids an operator adds — went
    /// around entirely.
    /// </remarks>
    private static readonly GameAddress IconResolver = new(0x0045_B270);

    /// <summary><c>push ebp; mov ebp, esp; sub esp, 0x4C</c>.</summary>
    private static ReadOnlySpan<byte> ResolverPrologue => [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x4C];

    /// <summary>Where the resolver carries on, once the hook has replayed its prologue.</summary>
    private static readonly GameAddress ResolverContinuation = new(0x0045_B276);

    /// <summary>
    /// Where the clock comes from.
    /// </summary>
    /// <remarks>
    /// Resolved in the game's own kernel32, not the launcher's. They are almost always the
    /// same address within a boot, but that is how Windows happens to relocate system DLLs
    /// rather than something to rely on — and the consequence of being wrong is a call into
    /// arbitrary memory on the client's render path.
    /// </remarks>
    private const string ClockModule = "kernel32.dll";

    /// <inheritdoc cref="ClockModule"/>
    private const string ClockExport = "GetTickCount";

    /// <summary>Room for the hook. It comes out around 110 bytes.</summary>
    private const int CaveSize = 0x100;

    private readonly ILogger<DynamicIconPatch> _logger;

    public DynamicIconPatch(ILogger<DynamicIconPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "dynamic-icon";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.DynamicIconEnabled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var package = Load(context.GameDirectory, context.Aux.DynamicIconPakName);

        // An operator who switched the feature on and shipped an empty manifest gets a
        // client with no hook in it, which is what they described.
        if (package.Animations.Count == 0)
        {
            _logger.LogInformation("The icon package names no animations; nothing was hooked");
            return;
        }

        var process = context.Process;
        var prologue = process.ReadBytes(IconResolver, ResolverPrologue.Length);

        if (prologue[0] == InlineHook.JumpOpcode)
        {
            _logger.LogInformation("Item icons are already hooked");
            return;
        }

        if (!prologue.AsSpan().SequenceEqual(ResolverPrologue))
        {
            throw new GameProcessException(
                $"The icon resolver at {IconResolver} does not start the way it should; "
                + $"it reads {BytePattern.Format(prologue)}.");
        }

        var table = process.AllocateExecutable(package.Animations.Count * IconAnimationTable.RecordSize);
        process.WriteCode(table, IconAnimationTable.Serialise(Inject(process, package)));

        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, table, package.Animations.Count, Clock(process));

        if (shellcode.Length > CaveSize)
        {
            throw new GameProcessException(
                $"The icon hook is {shellcode.Length} bytes but the cave is {CaveSize}.");
        }

        process.WriteCode(cave, shellcode);

        // Last, so a failure anywhere above leaves the client resolving its own icons.
        process.WriteCode(IconResolver, InlineHook.BuildJump(IconResolver, cave, ResolverPrologue.Length));

        _logger.LogInformation(
            "{Count} item icons now animate, from a table at {Table} and a hook at {Cave}",
            package.Animations.Count, table, cave);
    }

    /// <summary>An operator's icon package, and what it says.</summary>
    private readonly record struct IconPackage(
        byte[] Body, byte[] Index, IReadOnlyList<IconAnimation> Animations);

    /// <summary>Reads the operator's package. Frames are still resource ids at this point.</summary>
    private IconPackage Load(string gameDirectory, string packageName)
    {
        var bodyPath = Path.Combine(gameDirectory, $"{packageName}.pak");
        var indexPath = Path.Combine(gameDirectory, $"{packageName}.idx");

        if (!File.Exists(bodyPath) || !File.Exists(indexPath))
        {
            throw new DynamicIconException(
                $"Animated icons are switched on but {packageName}.pak and {packageName}.idx are not both in {gameDirectory}.");
        }

        var body = File.ReadAllBytes(bodyPath);
        var index = File.ReadAllBytes(indexPath);
        var manifest = SpritePackage.Read(body, index, DynamicIconManifest.FileName);
        var animations = DynamicIconManifest.Parse(Encoding.UTF8.GetString(manifest.Span));

        _logger.LogInformation("Read {Count} icon animations from {Package}", animations.Count, bodyPath);

        return new IconPackage(body, index, animations);
    }

    /// <summary>
    /// Converts every frame and writes them all into the game.
    /// </summary>
    /// <remarks>
    /// One allocation for the lot. The reference allocated per frame, and a remote
    /// allocation reserves a full 64K however few bytes it is asked for — so a dozen icons
    /// of twenty frames each reserved megabytes of the client's address space to hold a few
    /// hundred kilobytes of image.
    /// </remarks>
    /// <returns>The same animations, with each frame replaced by where its image now lives.</returns>
    private List<IconAnimation> Inject(RemoteProcess process, IconPackage package)
    {
        var images = new List<byte[]>();
        var offsets = new List<int>();
        var total = 0;

        foreach (var animation in package.Animations)
        {
            foreach (var frame in animation.Frames)
            {
                var image = Convert(package, frame);
                images.Add(image);
                offsets.Add(total);

                // Kept aligned. Nothing requires it of a byte-addressed image, but a table
                // full of odd addresses is harder to read back when something is wrong.
                total += (image.Length + 3) & ~3;
            }
        }

        var frames = process.AllocateExecutable(total);

        for (var i = 0; i < images.Count; i++)
        {
            process.WriteCode(frames + offsets[i], images[i]);
        }

        var placed = new List<IconAnimation>(package.Animations.Count);
        var at = 0;

        foreach (var animation in package.Animations)
        {
            var addresses = new uint[animation.Frames.Count];

            for (var frame = 0; frame < addresses.Length; frame++)
            {
                addresses[frame] = (frames + offsets[at++]).Value;
            }

            placed.Add(animation with { Frames = addresses });
        }

        _logger.LogInformation(
            "{Frames} icon frames converted into {Bytes} bytes at {Address}", images.Count, total, frames);

        return placed;
    }

    /// <summary>Turns one frame of the operator's package into the client's image format.</summary>
    private static byte[] Convert(IconPackage package, uint resource)
    {
        var name = $"{resource}.png";

        try
        {
            var image = PngImage.Decode(SpritePackage.Read(package.Body, package.Index, name).Span);
            return TbtRawIcon.Encode(image.Rgba, image.Width, image.Height);
        }
        catch (DynamicIconException e)
        {
            // The message says what is wrong with the image; this says which one it was.
            throw new DynamicIconException($"{name}: {e.Message}", e);
        }
    }

    private static GameAddress Clock(RemoteProcess process)
    {
        var module = process.FindModule(ClockModule)
            ?? throw new GameProcessException($"The game has no {ClockModule} loaded.");

        return process.FindExport(module, ClockExport)
            ?? throw new GameProcessException($"{ClockModule} at {module} does not export {ClockExport}.");
    }

    /// <summary>
    /// Assembles the hook.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entered in place of the resolver's prologue, so the graphic id is still on the stack
    /// and every register belongs to the caller. It saves them all, decides, and either
    /// returns a frame or restores everything and runs the prologue it displaced.
    /// </para>
    /// <para>
    /// The arithmetic is <see cref="IconAnimation.FrameAt"/> in machine code: the clock
    /// modulo the cycle, and that divided by the frame time. Nothing is remembered between
    /// calls, which is what makes every copy of an icon agree.
    /// </para>
    /// </remarks>
    internal static byte[] BuildShellcode(GameAddress cave, GameAddress table, int count, GameAddress clock)
    {
        var code = new ShellcodeBuilder(cave);
        var end = table + (count * IconAnimationTable.RecordSize);

        code.PushAd()
            .Bytes([0x8B, 0x44, 0x24, 0x24])                     // mov eax, [esp+0x24] — the graphic id
            .Byte(0xBA).Dword(table.Value);                      // mov edx, table

        var scan = code.Here();

        code.Bytes([0x81, 0xFA]).Dword(end.Value);               // cmp edx, end
        var past = code.NearJump(JumpIfAboveOrEqualNear);

        // Zero-extended, so an id above the field's width cannot match a record by its low
        // half — an icon nobody configured would animate.
        code.Bytes([0x0F, 0xB7, 0x0A])                           // movzx ecx, word [edx]
            .Bytes([0x3B, 0xC1]);                                // cmp eax, ecx
        var found = code.NearJump(JumpIfEqualNear);

        code.Bytes([0x81, 0xC2]).Dword(IconAnimationTable.RecordSize)   // add edx, 408
            .ShortJumpBack(JumpAlwaysShort, scan);

        code.MarkLabel(found);

        // The record moves to ebp, which the clock call has to leave alone.
        code.Bytes([0x8B, 0xEA])                                 // mov ebp, edx
            .Byte(0xB8).Dword(clock.Value)                       // mov eax, GetTickCount
            .CallEax();

        code.Bytes([0x0F, 0xB7, 0x4D, 0x02])                     // movzx ecx, word [ebp+2] — frame time
            .Bytes([0x8B, 0x5D, 0x08])                           // mov ebx, [ebp+8] — frame count
            .Bytes([0x0F, 0xAF, 0xD9])                           // imul ebx, ecx — the animation's length
            .Bytes([0x8B, 0x7D, 0x04])                           // mov edi, [ebp+4] — the rest
            .Bytes([0x03, 0xFB]);                                // add edi, ebx — the cycle

        // A cycle of zero divides by zero, which ends the client rather than the animation.
        // The manifest refuses one; this is what makes that a missing animation rather than
        // a crash for a table built any other way.
        code.Bytes([0x85, 0xFF]);                                // test edi, edi
        var noCycle = code.NearJump(JumpIfEqualNear);

        code.Bytes([0x31, 0xD2, 0xF7, 0xF7])                     // xor edx, edx; div edi
            .Bytes([0x3B, 0xD3]);                                // cmp edx, ebx
        var resting = code.NearJump(JumpIfAboveOrEqualNear);

        code.Bytes([0x8B, 0xC2])                                 // mov eax, edx — how far into the cycle
            .Bytes([0x31, 0xD2, 0xF7, 0xF1])                     // xor edx, edx; div ecx — the frame index
            .Bytes([0x8B, 0x44, 0x85, FramesOffset]);            // mov eax, [ebp+eax*4+12]

        // Into the saved eax, so the restore hands it back. The reference parked it in a
        // fixed slot in the cave, which two threads drawing at once would have shared.
        code.Bytes([0x89, 0x44, 0x24, SavedEax])                 // mov [esp+0x1C], eax
            .PopAd()
            .Bytes([0xC2, 0x04, 0x00]);                          // ret 4 — the resolver is __thiscall

        code.MarkLabel(past).MarkLabel(noCycle).MarkLabel(resting);

        return code.PopAd()
                   .Bytes(ResolverPrologue)
                   .JumpTo(ResolverContinuation)
                   .Build();
    }

    /// <inheritdoc cref="IconAnimationTable.FramesOffset"/>
    private const byte FramesOffset = (byte)IconAnimationTable.FramesOffset;

    /// <summary>Where <c>pushad</c> leaves <c>eax</c>: pushed first of eight, so deepest.</summary>
    private const byte SavedEax = 0x1C;

    private const byte JumpIfEqualNear = 0x84;
    private const byte JumpIfAboveOrEqualNear = 0x83;
    private const byte JumpAlwaysShort = 0xEB;
}
