using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Lets the server send an NPC dialog instead of naming one.
/// </summary>
/// <remarks>
/// <para>
/// A hypertext packet carries a file name, and the client loads
/// <c>lineage/html/&lt;name&gt;.html</c> from its own installation. So every dialog a server
/// wants to show has to already be on every player's disk, and changing one means shipping
/// a patch.
/// </para>
/// <para>
/// The packet has a second string field the stock path never reads. This adds one
/// convention on top of it: a name beginning with <c>@</c> means the dialog itself is in
/// that second field. The parse is redone with a format that reads both strings, the body
/// is kept, and the loader call is answered from memory rather than from disk.
/// </para>
/// <para>
/// Anything not beginning with <c>@</c> takes the client's own path, byte for byte —
/// including on a client where this failed to install, and on a server that has never heard
/// of the convention.
/// </para>
/// </remarks>
public sealed class DynamicDialogPatch : IGamePatch
{
    /// <summary>A dialog the server sent rather than named.</summary>
    private const byte DynamicPrefix = (byte)'@';

    /// <summary>Where the client parses a hypertext packet.</summary>
    private static readonly GameAddress ParseSite = new(0x0052_7A58);

    /// <summary>
    /// <c>lea ecx, [ebp-0x14]; push ecx; mov edx, [0x009A8EB8]</c>.
    /// </summary>
    /// <remarks>
    /// Ten bytes, replayed whole by the hook for an ordinary dialog. The jump only needs
    /// five, but leaving the other five would be the tail of an instruction that decodes as
    /// something arbitrary.
    /// </remarks>
    private static ReadOnlySpan<byte> ParseOriginal =>
        [0x8D, 0x4D, 0xEC, 0x51, 0x8B, 0x15, 0xB8, 0x8E, 0x9A, 0x00];

    /// <summary>Where an ordinary dialog rejoins the client.</summary>
    private static readonly GameAddress ParseResume = new(0x0052_7A62);

    /// <summary>Where a dialog rejoins once it has been parsed, whichever way.</summary>
    private static readonly GameAddress ParseAfter = new(0x0052_7A92);

    /// <summary>The client's own packet reader, driven by a format string.</summary>
    private static readonly GameAddress Deserialise = new(0x0052_2110);

    /// <summary>
    /// <c>dssh</c> — an id, two strings and a short.
    /// </summary>
    /// <remarks>
    /// The client's own string, already in the image. The stock parse uses a shorter one
    /// that stops before the second string, which is why that field is free to carry a body.
    /// </remarks>
    private static readonly GameAddress DialogFormat = new(0x008D_4A70);

    /// <summary>Loads a dialog from a file on disk.</summary>
    private static readonly GameAddress LoadFromFile = new(0x0049_43B0);

    /// <summary>Parses a dialog that is already in memory.</summary>
    /// <remarks>
    /// The client's own, used by its own code for dialogs it builds itself. Nothing about
    /// showing a dialog is reimplemented here — only where the text comes from.
    /// </remarks>
    private static readonly GameAddress ParseFromMemory = new(0x0049_44E0);

    /// <summary>Every call that loads a dialog from disk.</summary>
    private static readonly GameAddress[] LoaderCalls =
    [
        new(0x0049_E1F7), new(0x0049_E20D), new(0x0049_E418),
        new(0x005E_F97B), new(0x005E_FB78), new(0x0064_3A5F),
    ];

    // The cave holds both hooks, the pointer between them and the body itself.
    private const int WrapperOffset = 0x0000;
    private const int ParseHookOffset = 0x0100;
    private const int BodyPointerOffset = 0x0200;
    private const int BodyOffset = 0x0300;

    /// <summary>The longest dialog the server can send.</summary>
    /// <remarks>
    /// Passed to the client's own reader as the field's limit, so a longer one is truncated
    /// rather than written past the end.
    /// </remarks>
    private const uint BodySize = 0x7000;

    private const int CaveSize = 0x9000;

    private readonly ILogger<DynamicDialogPatch> _logger;

    public DynamicDialogPatch(ILogger<DynamicDialogPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "dynamic-dialog";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.DynamicDialogEnabled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;
        var site = process.ReadBytes(ParseSite, ParseOriginal.Length);

        if (site[0] == InlineHook.JumpOpcode)
        {
            _logger.LogInformation("Server-sent dialogs are already hooked");
            return;
        }

        if (!site.AsSpan().SequenceEqual(ParseOriginal))
        {
            throw new GameProcessException(
                $"The hypertext parse at {ParseSite} does not read the way it should; "
                + $"it is {BytePattern.Format(site)}.");
        }

        var cave = process.AllocateExecutable(CaveSize);
        var wrapper = cave + WrapperOffset;
        var parseHook = cave + ParseHookOffset;
        var bodyPointer = cave + BodyPointerOffset;
        var body = cave + BodyOffset;

        Write(process, wrapper, BuildWrapper(wrapper, bodyPointer), ParseHookOffset, "loader wrapper");
        Write(process, parseHook, BuildParseHook(parseHook, bodyPointer, body),
            BodyPointerOffset - ParseHookOffset, "parse hook");

        // Suspended, because these are calls the client can be executing right now.
        using var suspension = process.SuspendThreads();

        var rerouted = LoaderCalls.Count(call => Reroute(process, call, wrapper));

        if (rerouted == 0)
        {
            // The parse hook has not been written yet, so the client is exactly as it was.
            throw new GameProcessException(
                $"None of the {LoaderCalls.Length} dialog loader calls could be rerouted.");
        }

        // Last. Until this goes in, the body pointer stays null and the wrapper hands every
        // dialog to the loader it replaced — so a failure above leaves the stock behaviour
        // rather than dialogs that parse into nothing.
        process.WriteCode(ParseSite, InlineHook.BuildJump(ParseSite, parseHook, ParseOriginal.Length));

        _logger.LogInformation(
            "Server-sent dialogs enabled: {Rerouted} of {Total} loader calls rerouted to {Wrapper}",
            rerouted, LoaderCalls.Length, wrapper);
    }

    private static void Write(
        RemoteProcess process, GameAddress at, byte[] shellcode, int room, string what)
    {
        if (shellcode.Length > room)
        {
            throw new GameProcessException(
                $"The dialog {what} is {shellcode.Length} bytes but it has {room}.");
        }

        process.WriteCode(at, shellcode);
    }

    /// <summary>Points one loader call at the wrapper, if it is currently one.</summary>
    private bool Reroute(RemoteProcess process, GameAddress call, GameAddress wrapper)
    {
        Span<byte> instruction = stackalloc byte[5];
        process.ReadBytes(call, instruction);

        var target = DeferredSites.RelativeTarget(call, instruction);

        if (instruction[0] != 0xE8 || target != LoadFromFile)
        {
            // Named rather than counted: which one moved is the whole diagnosis when a
            // client build changes.
            _logger.LogWarning(
                "The dialog loader call at {Call} goes to {Target}, not {Loader}", call, target, LoadFromFile);

            return false;
        }

        process.WriteCode(call, DeferredSites.BuildCall(call, wrapper));
        return true;
    }

    /// <summary>
    /// Assembles the wrapper that stands in front of the file loader.
    /// </summary>
    /// <remarks>
    /// Entered by a call, with the dialog's name where the loader expects it. A name that
    /// is not one of ours, or one with no body captured for it, falls through to the loader
    /// with the stack untouched — so the client cannot tell the wrapper is there.
    /// </remarks>
    internal static byte[] BuildWrapper(GameAddress cave, GameAddress bodyPointer)
    {
        var code = new ShellcodeBuilder(cave);

        code.Bytes([0x8B, 0x44, 0x24, 0x04])          // mov eax, [esp+4] — the name
            .Bytes([0x85, 0xC0]);                     // test eax, eax
        var noName = code.ShortJumpIfZero();

        code.Bytes([0x80, 0x38, DynamicPrefix]);      // cmp byte [eax], '@'
        var notOurs = code.ShortJumpIfNotEqual();

        code.Byte(0xA1).Dword(bodyPointer.Value)      // mov eax, [body pointer]
            .Bytes([0x85, 0xC0]);                     // test eax, eax
        var noBody = code.ShortJumpIfZero();

        // The loader takes a name; the parser takes the text. Same argument slot, so the
        // substitution is the whole of it.
        code.Bytes([0x89, 0x44, 0x24, 0x04])          // mov [esp+4], eax
            .JumpTo(ParseFromMemory);

        return code.MarkLabel(noName).MarkLabel(notOurs).MarkLabel(noBody)
                   .JumpTo(LoadFromFile)
                   .Build();
    }

    /// <summary>
    /// Assembles the hook on the packet parse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is at a fixed offset in the packet, so whether this is one of ours can be
    /// decided before anything is parsed. If it is not, the ten bytes the jump displaced
    /// run here and the client carries on having lost nothing.
    /// </para>
    /// <para>
    /// If it is, the packet is read again with the format that includes the second string,
    /// into the launcher's own buffer, and the client rejoins past its own parse with the
    /// same result in the same place.
    /// </para>
    /// </remarks>
    internal static byte[] BuildParseHook(GameAddress cave, GameAddress bodyPointer, GameAddress body)
    {
        var code = new ShellcodeBuilder(cave);

        code.Bytes([0x8B, 0x75, 0x08])                        // mov esi, [ebp+8] — the packet
            .Bytes([0x80, 0x7E, 0x04, DynamicPrefix]);        // cmp byte [esi+4], '@' — past the id
        var ours = code.ShortJumpIfZero();

        code.Bytes(ParseOriginal).JumpTo(ParseResume);

        code.MarkLabel(ours);

        code.Bytes([0xC7, 0x05]).Dword(bodyPointer.Value).Dword(body.Value)   // mov [pointer], body
            .Bytes([0xC6, 0x05]).Dword(body.Value).Byte(0);                   // mov byte [body], 0

        // The client's own reader, right to left: the packet, the format, and a destination
        // and a limit for each field in it.
        code.Bytes([0x8D, 0x4D, 0xEC, 0x51])                  // lea ecx, [ebp-0x14]; push ecx
            .PushImm32(body)
            .PushImm32(BodySize)
            .Bytes([0x8D, 0x85, 0xDC, 0xFE, 0xFF, 0xFF, 0x50])   // lea eax, [ebp-0x124]; push eax
            .PushImm32(NameSize)
            .Bytes([0x8D, 0x4D, 0xE4, 0x51])                  // lea ecx, [ebp-0x1C]; push ecx
            .PushImm32(DialogFormat)
            .Bytes([0x8B, 0x55, 0x08, 0x52])                  // mov edx, [ebp+8]; push edx
            .CallTo(Deserialise)
            .AddEsp(0x20);

        return code.Bytes([0x89, 0x45, 0x08])                 // mov [ebp+8], eax — where it stopped
                   .JumpTo(ParseAfter)
                   .Build();
    }

    /// <summary>The client's own limit on a dialog name.</summary>
    private const uint NameSize = 0x100;
}
