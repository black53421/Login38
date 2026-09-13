using Login38.Interop;
using Login38.Patching;
using Login38.Aux.Actions;

namespace Login38.Aux.Toggles;

public sealed class RangeSkillDamageProtocolPatch : IGamePatch
{
    internal const uint CapabilityMarker = 0x4438334C;
    private const uint ServerVersionOpcode = 0x0E;
    private const uint ServerVersionReturnAddress = 0x004E0EE1;
    private const byte ClientVersionStackOffset = 0x1C;
    private const int MarkerCaveSize = 0x100;

    private readonly RangeSkillDamageProtocolState _state;

    public RangeSkillDamageProtocolPatch(RangeSkillDamageProtocolState state) => _state = state;

    public string Name => "range-skill-damage-protocol";

    public PatchPhase Phase => PatchPhase.Startup;

    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.RangeSkillDamageExtension && !context.Aux.PacketSpyStartupEnabled;
    }

    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;
        Expect(process, DamageCave.Area);
        Expect(process, PacketSpyCave.Site);

        var damageCave = process.AllocateExecutable(DamageCave.CaveSize);
        var (damageCode, entries) = DamageCave.Build(damageCave, TickCount(process));
        if (damageCode.Length > DamageCave.CaveSize)
        {
            throw new GameProcessException("range-skill damage cave does not fit its allocation");
        }

        process.WriteCode(damageCave, damageCode);
        var areaEnabled = DamageCave.AreaEnabledAddress(damageCave, entries);
        uint disabled = 0;
        process.Write(areaEnabled, disabled);

        var markerCave = process.AllocateExecutable(MarkerCaveSize);
        var markerCode = BuildMarkerCave(markerCave);
        if (markerCode.Length > MarkerCaveSize)
        {
            throw new GameProcessException("range-skill capability marker cave does not fit its allocation");
        }

        process.WriteCode(markerCave, markerCode);

        var areaJump = DamageCave.Area.JumpTo(damageCave + entries.Area);
        var markerJump = PacketSpyCave.Site.JumpTo(markerCave);

        process.WriteCode(DamageCave.Area.Address, areaJump);
        try
        {
            process.WriteCode(PacketSpyCave.Site.Address, markerJump);
        }
        catch
        {
            process.WriteCode(DamageCave.Area.Address, DamageCave.Area.Stock);
            throw;
        }

        _state.Set(process.Id, new RangeSkillDamageProtocolState.Session(
            damageCave, damageCave + entries.Single, areaEnabled));
    }

    internal static byte[] BuildMarkerCave(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave);

        // PacketSpy confirms the 3.80C server-version call as:
        // SendPacketData("chdcddc", 0x0E, ..., clientVersion, 1).
        // At the function entry, clientVersion is the sixth variadic argument at [esp+0x1C].
        // Guard the exact call site as well as the opcode so another opcode 0x0E packet cannot
        // accidentally receive the capability marker.
        code.Bytes([0x81, 0x3C, 0x24]).Dword(ServerVersionReturnAddress); // cmp dword ptr [esp], return address
        var wrongCaller = code.NearJump(0x85);
        code.Bytes([0x81, 0x7C, 0x24, 0x08]).Dword(ServerVersionOpcode);  // cmp dword ptr [esp+8], 0x0E
        var wrongOpcode = code.NearJump(0x85);
        code.Bytes([0xC7, 0x44, 0x24, ClientVersionStackOffset]).Dword(CapabilityMarker);
        code.MarkLabel(wrongCaller)
            .MarkLabel(wrongOpcode)
            .Bytes(PacketSpyCave.Site.Stock)
            .JumpTo(PacketSpyCave.Site.Resume);

        return code.Build();
    }

    private static void Expect(RemoteProcess process, HookSite site)
    {
        if (site.Read(process, null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{site.Address} does not hold what this client is expected to have there.");
        }
    }


    private static GameAddress TickCount(RemoteProcess process)
    {
        var kernel = process.FindModule("kernel32.dll")
            ?? throw new GameProcessException("kernel32.dll is not loaded in the game.");

        return process.FindExport(kernel, "GetTickCount")
            ?? throw new GameProcessException($"kernel32.dll at {kernel} does not export GetTickCount.");
    }
}
