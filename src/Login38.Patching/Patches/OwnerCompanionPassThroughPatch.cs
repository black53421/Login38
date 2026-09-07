using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Applies movement-only pass-through rules for the local player's own companions and
/// companions owned by other players.
/// </summary>
/// <remarks>
/// <para>
/// The two switches come from the encoder's <c>[aux]</c> settings:
/// <c>owner_companion_pass_through</c> and <c>other_companion_pass_through</c>.
/// This patch only changes the dynamic movement blocker inside <c>0x004F5A60</c>; it does
/// not change combat classification, targeting, pet control, or summon control.
/// </para>
/// <para>
/// Probe v3.7 established ownership-sensitive classes on this client build:
/// local-player companions arrive as class <c>6</c>, while another player's companions
/// arrive as class <c>5</c>. Class alone is not trusted: both paths also require a
/// non-empty owner name and validate whether that owner name matches the local player.
/// Everything else falls back to the original <c>0x005AF010</c> blocker classifier.
/// </para>
/// </remarks>
public sealed class OwnerCompanionPassThroughPatch : IGamePatch
{
    private static readonly GameAddress HookSite = new(0x004F_5CD5);
    private static readonly GameAddress OriginalReturn = new(0x004F_5CDC);
    private static readonly GameAddress ContinueEntityLoop = new(0x004F_5CE7);
    private static readonly GameAddress EntityCombatTypeClass = new(0x005A_EBE0);
    private static readonly GameAddress DynamicBlockerClassifier = new(0x005A_F010);
    private static readonly GameAddress ClientStringCompare = new(0x0058_6FD0);
    private static readonly GameAddress LocalPlayer = new(0x00C2_D2B8);

    private static readonly byte[] OriginalBytes =
    [
        0x8B, 0x08,
        0xE8, 0x34, 0x93, 0x0B, 0x00,
    ];

    private const int CaveSize = 0x200;
    private const byte OwnerCompanionClass = 0x06;
    private const byte OtherCompanionClass = 0x05;

    private readonly ILogger<OwnerCompanionPassThroughPatch> _logger;

    public OwnerCompanionPassThroughPatch(ILogger<OwnerCompanionPassThroughPatch> logger) => _logger = logger;

    public string Name => "owner-companion-pass-through";

    public PatchPhase Phase => PatchPhase.InWorld;

    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.OwnerCompanionPassThrough || context.Aux.OtherCompanionPassThrough;
    }

    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var allowOwner = context.Aux.OwnerCompanionPassThrough;
        var allowOther = context.Aux.OtherCompanionPassThrough;
        if (!allowOwner && !allowOther)
        {
            _logger.LogDebug("No companion pass-through setting is enabled; skipping installation");
            return;
        }

        var process = context.Process;
        if (InlineHook.IsInstalledAt(process, HookSite))
        {
            _logger.LogDebug("Companion pass-through is already installed at {Address}", HookSite);
            return;
        }

        var current = process.ReadBytes(HookSite, OriginalBytes.Length);
        if (!current.AsSpan().SequenceEqual(OriginalBytes))
        {
            throw new GameProcessException(
                $"Expected movement blocker call at {HookSite} to be {BytePattern.Format(OriginalBytes)} " +
                $"but found {BytePattern.Format(current)}.");
        }

        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, allowOwner, allowOther);
        if (shellcode.Length > CaveSize)
        {
            throw new GameProcessException(
                $"Companion pass-through shellcode needs {shellcode.Length} bytes but only {CaveSize} were reserved.");
        }

        process.WriteCode(cave, shellcode);

        var jump = InlineHook.BuildJump(HookSite, cave, OriginalBytes.Length);
        using (process.SuspendThreads())
        {
            process.WriteCode(HookSite, jump);
        }

        _logger.LogInformation(
            "Companion pass-through installed at {Address} through cave {Cave} (owner={Owner}, other={Other})",
            HookSite,
            cave,
            allowOwner,
            allowOther);
    }

    internal static byte[] BuildShellcode(GameAddress cave, bool allowOwner, bool allowOther)
    {
        if (!allowOwner && !allowOther)
        {
            throw new ArgumentException("At least one companion pass-through setting must be enabled.");
        }

        var code = new ShellcodeBuilder(cave);
        var fallbackLabels = new List<ShellcodeBuilder.NearLabel>();

        // Replay displaced mov ecx,[eax]. Preserve the entity because the shared class
        // function can clobber ECX and the original blocker expects ECX=entity.
        code.MovEcxFromEax()
            .PushEcx()
            .CallTo(EntityCombatTypeClass)
            .Bytes([0x3C, OwnerCompanionClass]); // cmp al, 6

        var notOwnerClass = code.ShortJumpIfNotEqual();

        if (allowOwner)
        {
            // class 6 is allowed only when ownerName is present and equals localPlayerName.
            code.PopEcx()
                .MovEaxFromEcx(0x6C)
                .TestEaxEax();
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.CmpBytePtrEax(0x00, 0x00);
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.Bytes([0x8B, 0x15]).Dword(LocalPlayer.Value) // mov edx,[LocalPlayer]
                .TestEdxEdx();
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.Bytes([0x8B, 0x52, 0x60]) // mov edx,[edx+60h]
                .TestEdxEdx();
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.PushEcx()
                .Byte(0x52) // push edx
                .PushEax()
                .CallTo(ClientStringCompare)
                .TestEaxEax()
                .PopEcx();
            fallbackLabels.Add(code.NearJump(0x85)); // jnz: owner != local

            code.JumpTo(ContinueEntityLoop);
        }
        else
        {
            code.PopEcx();
            fallbackLabels.Add(code.NearJumpAlways());
        }

        // A non-class-6 entity still has the saved entity on the stack.
        code.MarkLabel(notOwnerClass)
            .Bytes([0x3C, OtherCompanionClass]); // cmp al, 5

        var notOtherClass = code.ShortJumpIfNotEqual();

        if (allowOther)
        {
            // class 5 is allowed only when ownerName is present and differs from local name.
            code.PopEcx()
                .MovEaxFromEcx(0x6C)
                .TestEaxEax();
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.CmpBytePtrEax(0x00, 0x00);
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.Bytes([0x8B, 0x15]).Dword(LocalPlayer.Value) // mov edx,[LocalPlayer]
                .TestEdxEdx();
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.Bytes([0x8B, 0x52, 0x60]) // mov edx,[edx+60h]
                .TestEdxEdx();
            fallbackLabels.Add(code.NearJump(0x84)); // jz fallback

            code.PushEcx()
                .Byte(0x52) // push edx
                .PushEax()
                .CallTo(ClientStringCompare)
                .TestEaxEax()
                .PopEcx();
            fallbackLabels.Add(code.NearJump(0x84)); // jz: owner == local

            code.JumpTo(ContinueEntityLoop);
        }
        else
        {
            code.PopEcx();
            fallbackLabels.Add(code.NearJumpAlways());
        }

        // Any class other than 5/6 reaches here with the saved entity still on the stack.
        code.MarkLabel(notOtherClass)
            .PopEcx();

        foreach (var label in fallbackLabels)
        {
            code.MarkLabel(label);
        }

        code.CallTo(DynamicBlockerClassifier)
            .JumpTo(OriginalReturn);

        return code.Build();
    }
}
