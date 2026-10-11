using System.Collections.Concurrent;
using Login38.Interop;
using Login38.Patching;

namespace Login38.Aux.Toggles;

/// <summary>
/// Prevents the tested elf fire/earth peer-clear path from deleting an already-active
/// client buff state when another buff in the same stock group arrives.
/// </summary>
/// <remarks>
/// Server-side coexistence is a separate requirement. This patch only fixes the 3.81C
/// client's local bookkeeping so the icon/state table can represent effects the server is
/// already keeping active. It is an operator setting from Encoder/config.ini, not a
/// per-character helper toggle.
/// </remarks>
public sealed class ElfBuffCoexistencePatch : IGamePatch
{
    internal static readonly GameAddress PeerClearCallSite = new(0x004E_E48C);
    internal static readonly GameAddress ApplyBuff = new(0x004E_E2D0);

    // E8 rel32 from 0x004EE48C to 0x004EE2D0.
    internal static readonly byte[] StockCall = [0xE8, 0x3F, 0xFE, 0xFF, 0xFF];

    internal static readonly int[] ProtectedStates = [6, 7, 8, 18, 19, 20];

    private const int CaveSize = 0x80;

    // One patch object is shared by all launches. Remember each process independently so
    // an accidental second Apply call is idempotent without confusing two clients.
    private readonly ConcurrentDictionary<uint, byte[]> _installedCalls = new();

    public string Name => "elf-buff-coexistence";

    public PatchPhase Phase => PatchPhase.Startup;

    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.ElfBuffCoexistenceEnabled;
    }

    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var process = context.Process;

        if (_installedCalls.TryGetValue(process.Id, out var installedCall))
        {
            switch (Classify(process, installedCall))
            {
                case SiteState.Ours:
                    return;
                case SiteState.Stock:
                    process.WriteCode(PeerClearCallSite, installedCall);
                    return;
                default:
                    throw new GameProcessException(
                        $"{PeerClearCallSite} is not the stock call or this launcher's elf buff coexistence call.");
            }
        }

        if (Classify(process, ours: null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{PeerClearCallSite} does not hold the verified peer-clear call.");
        }

        var cave = process.AllocateExecutable(CaveSize);
        var code = BuildCave(cave);
        if (code.Length > CaveSize)
        {
            throw new GameProcessException(
                $"Elf buff coexistence cave is {code.Length} bytes but only {CaveSize} were reserved.");
        }

        process.WriteCode(cave, code);

        var call = DeferredSites.BuildCall(PeerClearCallSite, cave);
        process.WriteCode(PeerClearCallSite, call);
        _installedCalls[process.Id] = call;
    }

    private static SiteState Classify(RemoteProcess process, byte[]? ours)
    {
        Span<byte> current = stackalloc byte[5];
        if (!process.TryReadBytes(PeerClearCallSite, current))
        {
            return SiteState.Foreign;
        }

        return HookSite.Classify(current, StockCall, ours);
    }

    /// <summary>Builds the call wrapper used by the one verified peer-clear site.</summary>
    internal static byte[] BuildCave(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave);

        // Wrapper entry stack:
        //   [esp+0] = return address (0x004EE491)
        //   [esp+4] = stateId
        //   [esp+8] = enabled (the stock peer-clear site passes zero)
        //
        // Keep the enabled check even though this call site currently pushes zero. It
        // makes the wrapper fail open if a later client build reuses the site differently.
        code.Bytes([0x83, 0x7C, 0x24, 0x08, 0x00]); // cmp dword ptr [esp+8], 0
        var passNonClear = code.ShortJumpIfNotEqual();

        List<ShellcodeBuilder.Label> skip = [];
        foreach (var state in ProtectedStates)
        {
            code.Bytes([0x83, 0x7C, 0x24, 0x04, checked((byte)state)]); // cmp [esp+4], state
            skip.Add(code.ShortJumpIfZero());
        }

        // Not one of the verified states: preserve stock behaviour exactly. A tail jump is
        // intentional; apply_buff returns to the original caller's return address already
        // on the stack, and that caller still performs its own add esp,8.
        code.MarkLabel(passNonClear);
        code.JumpTo(ApplyBuff);

        // Verified peer clear: return without touching the arguments. The stock caller at
        // 0x004EE491 executes add esp,8 immediately after this return.
        foreach (var label in skip)
        {
            code.MarkLabel(label);
        }
        code.Ret();

        return code.Build();
    }
}
