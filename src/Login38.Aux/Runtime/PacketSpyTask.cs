using Login38.Aux.Actions;
using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Writes every packet the client sends to the log.
/// </summary>
/// <remarks>
/// <para>
/// A tool rather than a feature. Nothing else in the launcher reads it: it is how the
/// addresses and formats the rest of this port hard-codes were found, and how the next
/// client version's would be found again.
/// </para>
/// <para>
/// This one owns its detour rather than leaving it to <see cref="ToggleTask"/>, because
/// there is only one clock involved — putting the recorder in and reading what it recorded
/// are the same job on the same cadence, and splitting them would mean a second type whose
/// only purpose is to hold an address for this one.
/// </para>
/// <para>
/// The reference never removes it. Its <c>uninstall</c> carries <c>#[allow(dead_code)]</c>
/// and nothing calls its <c>install</c> either, so the whole module is a diagnostic that
/// was wired up by hand once and left.
/// </para>
/// </remarks>
public sealed class PacketSpyTask : IAuxTask, IAuxTaskShutdown
{
    private readonly ILogger<PacketSpyTask> _logger;

    private RemoteProcess? _game;
    private GameAddress? _cave;
    private byte[]? _jump;
    private uint _read;
    private bool _reported;

    public PacketSpyTask(ILogger<PacketSpyTask> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "packet-log";

    /// <summary>Five times a second, as the reference's own polling thread ran.</summary>
    /// <remarks>
    /// The ring holds sixty-four calls. A client sending faster than that between two passes
    /// loses the oldest, which is said in the log rather than passed over.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(200);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (!context.Settings.Misc.LogSentPackets)
            {
                Remove(context.Process);

                return;
            }

            _game = context.Process;

            if (Install(context.Process) is { } cave)
            {
                Drain(context.Process, cave);
            }

            _reported = false;
        }
        catch (GameProcessException e)
        {
            if (!_reported)
            {
                _reported = true;
                _logger.LogWarning(e, "{Task} could not be applied", Name);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Takes the detour out if the game is still there to take it out of. It writes into a
    /// cave this launcher allocated, so a recorder left behind in a client that outlives the
    /// launcher writes into memory nothing will ever read.
    /// </remarks>
    public void Stopping()
    {
        if (_game is { IsRunning: true } game)
        {
            try
            {
                Remove(game);
            }
            catch (GameProcessException e)
            {
                _logger.LogWarning(e, "{Task} could not be taken out of the game", Name);
            }
        }

        _game = null;
    }

    /// <summary>Puts the recorder in, if it is not in already.</summary>
    private GameAddress? Install(RemoteProcess process)
    {
        if (_cave is { } already && PacketSpyCave.Site.Read(process, _jump) == SiteState.Ours)
        {
            return already;
        }

        if (PacketSpyCave.Site.Read(process, null) != SiteState.Stock)
        {
            throw new GameProcessException(
                $"{PacketSpyCave.Site.Address} does not hold the prologue this recorder was written against.");
        }

        var cave = process.AllocateExecutable(PacketSpyCave.CaveSize);

        // The whole cave, so the ring starts empty rather than holding whatever the last
        // allocation at that address left — which would read as finished entries.
        process.WriteCode(cave, PacketSpyCave.Empty());
        process.WriteCode(cave + PacketSpyCave.CodeOffset, PacketSpyCave.Build(cave));

        var jump = PacketSpyCave.Site.JumpTo(cave + PacketSpyCave.CodeOffset);

        process.WriteCode(PacketSpyCave.Site.Address, jump);

        _cave = cave;
        _jump = jump;
        _read = 0;

        _logger.LogInformation("{Task}: recording what the client sends, ring at {Cave}", Name, cave);

        return cave;
    }

    /// <summary>Takes it out again.</summary>
    /// <remarks>The cave stays allocated: a thread may be inside it.</remarks>
    private void Remove(RemoteProcess process)
    {
        if (_cave is null)
        {
            return;
        }

        if (PacketSpyCave.Site.Read(process, _jump) == SiteState.Ours)
        {
            process.WriteCode(PacketSpyCave.Site.Address, PacketSpyCave.Site.Stock);
            _logger.LogInformation("{Task}: no longer recording", Name);
        }
        else
        {
            _logger.LogWarning(
                "{Task}: {Address} is not what this launcher wrote; leaving it",
                Name, PacketSpyCave.Site.Address);
        }

        _cave = null;
        _jump = null;
    }

    /// <summary>Reads whatever has been recorded since the last pass.</summary>
    private void Drain(RemoteProcess process, GameAddress cave)
    {
        if (!process.TryRead<uint>(cave + PacketSpyCave.IndexOffset, out var written) || written == _read)
        {
            return;
        }

        var missed = written - _read;

        if (missed > PacketSpyCave.RingLength)
        {
            _logger.LogWarning(
                "{Task}: {Count} calls were overwritten before they could be read", Name,
                missed - PacketSpyCave.RingLength);

            _read = written - PacketSpyCave.RingLength;
        }

        Span<byte> entry = stackalloc byte[PacketSpyCave.EntrySize];

        for (; _read != written; _read++)
        {
            var slot = cave + PacketSpyCave.BufferOffset + (_read % PacketSpyCave.RingLength * PacketSpyCave.EntrySize);

            if (process.TryReadBytes(slot, entry) && SpiedPacket.Read(entry, _read) is { } packet)
            {
                _logger.LogInformation("{Task}: {Packet}", Name, packet);
            }
        }
    }
}
