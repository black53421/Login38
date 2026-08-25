using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Makes colour codes work in item descriptions.
/// </summary>
/// <remarks>
/// <para>
/// The client has two ways of drawing a line of text. One parses <c>\f</c> followed by a
/// digit as a colour, strips the code and draws the rest in that colour. The other draws the
/// bytes it is given. Item descriptions go through the second, so an operator who writes a
/// colour code into one gets a literal <c>\f3</c> on screen, in white.
/// </para>
/// <para>
/// This routes every call that draws a description line through a cave, which copies the
/// line, normalises <c>\F</c> to <c>\f</c> in the copy, and hands it to the renderer that
/// understands codes. The original text is never touched — the normalisation is what makes
/// the two cases mean the same thing without changing the renderer.
/// </para>
/// <para>
/// The renderer has a defect of its own: with its last argument non-zero it strips the code
/// and then skips setting the colour, so the text comes out clean and white. The cave passes
/// zero, which takes the branch that works.
/// </para>
/// <para>
/// Most of these call sites are decrypted only when the player first opens the panel they
/// belong to, so they are patched as they appear rather than all at once.
/// </para>
/// </remarks>
public sealed class ItemDescriptionColourPatch : IGamePatch
{
    /// <summary>Draws a line of text as given, understanding nothing in it.</summary>
    private static readonly GameAddress PlainLineDraw = new(0x0046_FEC0);

    /// <summary>Draws a line of text, parsing <c>\f</c> colour codes.</summary>
    private static readonly GameAddress ColourLineDraw = new(0x0046_E0F0);

    /// <summary>
    /// Every call to the plain draw that belongs to an item description.
    /// </summary>
    /// <remarks>
    /// Found by scanning the client for calls to <see cref="PlainLineDraw"/>. Listed rather
    /// than rescanned, because most of them are encrypted at the moment a scan would run and
    /// the list is what says how many there should be.
    /// </remarks>
    private static readonly GameAddress[] CallSites =
    [
        new(0x0045_B52E), new(0x0045_E04B), new(0x0045_E1D9), new(0x0045_E3C4),
        new(0x0046_187E), new(0x0046_19E3), new(0x0046_1BFF), new(0x004A_9A21),
        new(0x004A_E25F), new(0x004C_0212), new(0x004C_03F1), new(0x004C_0616),
        new(0x004C_0663), new(0x004C_0F1D), new(0x004C_1015), new(0x004C_114C),
        new(0x004C_B302), new(0x0051_27CC), new(0x0057_FD1D), new(0x0059_2833),
        new(0x0059_5123), new(0x0059_63FA), new(0x0059_6524), new(0x0059_6693),
        new(0x005B_4F9F), new(0x005B_4FD1), new(0x005B_6928), new(0x005B_6A2A),
        new(0x005B_6B6B), new(0x005C_35F4), new(0x005C_45BA), new(0x005C_46D9),
        new(0x005C_4840), new(0x0073_B5D4), new(0x0079_7481), new(0x0079_75E2),
        new(0x0079_7777),
    ];

    private const int CaveSize = 0x100;

    /// <summary>
    /// Scratch for the line being drawn.
    /// </summary>
    /// <remarks>
    /// Comfortably larger than <see cref="LineClamp"/>, and one buffer for all of it: the
    /// client draws one line at a time on one thread, so there is nothing to share it with.
    /// </remarks>
    private const int ScratchSize = 0x200;

    /// <summary>The longest line the cave will copy.</summary>
    /// <remarks>
    /// Descriptions do not come near this. The clamp is what stops a length the client
    /// computed wrongly from running off the end of the scratch.
    /// </remarks>
    private const uint LineClamp = 0xFF;

    private readonly ILogger<ItemDescriptionColourPatch> _logger;

    public ItemDescriptionColourPatch(ILogger<ItemDescriptionColourPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "item-description-colour";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;
        var scratch = process.AllocateExecutable(ScratchSize);
        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, scratch);

        if (shellcode.Length > CaveSize)
        {
            throw new GameProcessException(
                $"The description colour shellcode is {shellcode.Length} bytes but the cave is {CaveSize}.");
        }

        process.WriteCode(cave, shellcode);

        _logger.LogInformation(
            "Item description lines now go through the colour renderer, via a cave at {Cave}", cave);

        // Deliberately not awaited. Most of these sites will not exist for minutes, and
        // holding up the rest of the launch for them would trade a working client for a
        // colourful one.
        _ = Task.Run(
            () => WatchAsync(process, cave, cancellationToken), CancellationToken.None);
    }

    private async Task WatchAsync(RemoteProcess process, GameAddress cave, CancellationToken cancellationToken)
    {
        try
        {
            await DeferredSites.WatchAsync(
                process, CallSites, site => Reroute(process, site, cave),
                "Item description colour", _logger,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The launcher is closing.
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "Stopped rerouting item description lines");
        }
    }

    /// <summary>
    /// Points one call at the cave, if it is currently a call to the plain draw.
    /// </summary>
    /// <remarks>
    /// The target is checked rather than the bytes: an encrypted site decodes as anything at
    /// all, and only a call that actually goes to the plain draw is one of these. That check
    /// is also what makes this safe to run repeatedly — a site already pointed at the cave
    /// is recognised as settled rather than rewritten.
    /// </remarks>
    private static SiteAttempt Reroute(RemoteProcess process, GameAddress site, GameAddress cave)
    {
        Span<byte> instruction = stackalloc byte[5];

        if (!process.TryReadBytes(site, instruction))
        {
            return SiteAttempt.NotReady;
        }

        var target = DeferredSites.RelativeTarget(site, instruction);

        if (target == cave)
        {
            return SiteAttempt.Settled;
        }

        if (target != PlainLineDraw)
        {
            return SiteAttempt.NotReady;
        }

        process.WriteCode(site, DeferredSites.BuildCall(site, cave));
        return SiteAttempt.Settled;
    }

    /// <summary>
    /// Assembles the cave: copy the line, normalise its codes, draw it in colour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entered by a call, so the arguments are on the stack and both functions are
    /// <c>cdecl</c> — the caller cleans the original six, and this cleans the six it pushes.
    /// </para>
    /// <para>
    /// The two functions take different arguments. The plain draw is given a length; the
    /// colour renderer reads until a terminator and takes a flag in its place. That is why
    /// the line is copied at all: the copy is where the terminator goes.
    /// </para>
    /// </remarks>
    internal static byte[] BuildShellcode(GameAddress cave, GameAddress scratch)
    {
        var code = new ShellcodeBuilder(cave);

        // After these two pushes: [esp+0xC] surface, +0x10 text, +0x14 length,
        // +0x18 x, +0x1C y, +0x20 colour.
        code.Bytes([0x56, 0x57]);                                   // push esi; push edi

        code.Bytes([0x8B, 0x74, 0x24, 0x10])                        // mov esi, [esp+0x10]
            .Bytes([0x8B, 0x4C, 0x24, 0x14]);                       // mov ecx, [esp+0x14]

        code.Bytes([0x81, 0xF9]).Dword(LineClamp);                  // cmp ecx, 0xFF
        var withinClamp = code.ShortJump(JumpIfBelowOrEqualShort);
        code.Byte(0xB9).Dword(LineClamp);                           // mov ecx, 0xFF
        code.MarkLabel(withinClamp);

        code.Bytes([0x8B, 0xD1])                                    // mov edx, ecx — the clamped length
            .Byte(0xBF).Dword(scratch.Value)                        // mov edi, scratch
            .RepMovsb()
            .Bytes([0xC6, 0x07, 0x00]);                             // mov byte [edi], 0

        EmitNormalise(code, scratch);

        // Zero, which is the branch of the renderer that actually sets the colour.
        code.PushImm8(0);

        // Colour, y and x, right to left. Each push moves esp, so the same displacement
        // reaches the next argument down each time.
        for (var i = 0; i < 3; i++)
        {
            code.Bytes([0x8B, 0x44, 0x24, 0x24, 0x50]);             // mov eax, [esp+0x24]; push eax
        }

        code.Byte(0xB8).Dword(scratch.Value).Byte(0x50);            // mov eax, scratch; push eax
        code.Bytes([0x8B, 0x44, 0x24, 0x20, 0x50]);                 // mov eax, [esp+0x20]; push eax

        return code.CallTo(ColourLineDraw)
                   .AddEsp(0x18)
                   .Bytes([0x5F, 0x5E, 0xC3])                       // pop edi; pop esi; ret
                   .Build();
    }

    /// <summary>
    /// Rewrites <c>\F</c> to <c>\f</c> in the copy.
    /// </summary>
    /// <remarks>
    /// The renderer only recognises the lowercase form. Operators write both, and which one
    /// they wrote is not something a player should be able to see — so the copy is made to
    /// agree rather than the renderer being taught a second spelling.
    /// </remarks>
    private static void EmitNormalise(ShellcodeBuilder code, GameAddress scratch)
    {
        code.Byte(0xB8).Dword(scratch.Value);                       // mov eax, scratch

        var loop = code.Here();

        code.Bytes([0x8A, 0x08])                                    // mov cl, [eax]
            .Bytes([0x84, 0xC9]);                                   // test cl, cl
        var done = code.ShortJumpIfZero();

        code.Bytes([0x80, 0xF9, Backslash]);                        // cmp cl, '\'
        var notACode = code.ShortJumpIfNotEqual();

        code.Bytes([0x80, 0x78, 0x01, UppercaseF]);                 // cmp byte [eax+1], 'F'
        var alreadyLower = code.ShortJumpIfNotEqual();

        code.Bytes([0xC6, 0x40, 0x01, LowercaseF]);                 // mov byte [eax+1], 'f'

        code.MarkLabel(notACode).MarkLabel(alreadyLower);
        code.Byte(0x40)                                             // inc eax
            .ShortJumpBack(JumpAlwaysShort, loop);

        code.MarkLabel(done);
    }

    private const byte Backslash = (byte)'\\';
    private const byte UppercaseF = (byte)'F';
    private const byte LowercaseF = (byte)'f';

    private const byte JumpIfBelowOrEqualShort = 0x76;
    private const byte JumpAlwaysShort = 0xEB;
}
