using Login38.Aux.Actions;
using Login38.Interop;
using Login38.Patching;

namespace Login38.Aux.Toggles;

/// <summary>
/// Installs the packet recorder during startup so login-phase send calls are captured.
/// </summary>
public sealed class PacketSpyStartupPatch : IGamePatch
{
    private readonly PacketSpyStartupState _state;

    public PacketSpyStartupPatch(PacketSpyStartupState state) => _state = state;

    public string Name => "packet-log-startup";

    public PatchPhase Phase => PatchPhase.Startup;

    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.PacketSpyStartupEnabled;
    }

    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var process = context.Process;

        if (_state.TryGet(process.Id, out var existing)
            && PacketSpyCave.Site.Read(process, existing.Jump) == SiteState.Ours)
        {
            return;
        }

        if (PacketSpyCave.Site.Read(process, null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{PacketSpyCave.Site.Address} does not hold the SendPacketData prologue expected by the startup recorder.");
        }

        var cave = process.AllocateExecutable(PacketSpyCave.CaveSize);
        process.WriteCode(cave, PacketSpyCave.Empty());
        process.WriteCode(cave + PacketSpyCave.CodeOffset, PacketSpyCave.Build(cave));

        var jump = PacketSpyCave.Site.JumpTo(cave + PacketSpyCave.CodeOffset);
        process.WriteCode(PacketSpyCave.Site.Address, jump);

        _state.Set(process.Id, new PacketSpyStartupState.Session(cave, jump));
    }
}
