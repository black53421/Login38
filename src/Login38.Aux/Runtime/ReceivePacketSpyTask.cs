using Login38.Aux.Game;
using Login38.Interop;
using Login38.Patching.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Writes Server-to-Client packets to the log before the client dispatches them.
/// </summary>
/// <remarks>
/// This is a diagnostic companion to <see cref="PacketSpyTask"/>. The dispatcher entry is
/// already owned by the long-item-status router, so this task only toggles and drains the
/// recorder embedded there instead of installing a second detour on the same instructions.
/// </remarks>
public sealed class ReceivePacketSpyTask : IAuxTask, IAuxTaskShutdown
{
    private static readonly GameAddress ScanStart = new(0x0040_0000);
    private static readonly GameAddress ScanEnd = new(0x0060_0000);
    private static readonly GameAddress Dispatcher = new(0x0054_4A20);
    private static readonly GameAddress RangeCheck = new(0x0053_9360);

    private readonly ILogger<ReceivePacketSpyTask> _logger;

    private RemoteProcess? _game;
    private uint _read;
    private uint _buffApplyRead;
    private bool _enabled;
    private bool _reportedUnavailable;
    private bool _analysisDumped;
    private bool _buffApplyInstalled;
    private bool _buffApplyEnabled;
    private bool _buffApplyInstallFailed;
    private readonly HashSet<uint> _buffApplyCallSitesDumped = [];

    public ReceivePacketSpyTask(ILogger<ReceivePacketSpyTask> logger) => _logger = logger;

    public string Name => "recv-packet-log";

    public TimeSpan Interval => TimeSpan.FromMilliseconds(200);

    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var logAllPackets = context.Settings.Misc.LogReceivedPackets;
        var elfBuffDiagnostics = context.Settings.Misc.ElfBuffStateDiagnostics;
        var requested = logAllPackets || elfBuffDiagnostics;
        _game = context.Process;

        if (!requested)
        {
            Disable(context.Process);
            return;
        }

        try
        {
            if (!ReceivePacketProbe.TrySetEnabled(context.Process, true))
            {
                if (!_reportedUnavailable)
                {
                    _reportedUnavailable = true;
                    _logger.LogWarning(
                        "{Task}: receive probe is not ready yet; waiting for the packet dispatcher patch",
                        Name);
                }

                return;
            }

            if (!_enabled)
            {
                _enabled = true;
                _reportedUnavailable = false;
                _logger.LogInformation(
                    "{Task}: receive diagnostics enabled (allPackets={AllPackets}, elfBuffStates={ElfBuffStates})",
                    Name,
                    logAllPackets,
                    elfBuffDiagnostics);
            }

            if (elfBuffDiagnostics)
            {
                EnableBuffApplyProbe(context.Process);
            }
            else
            {
                DisableBuffApplyProbe(context.Process);
            }

            if (logAllPackets && !_analysisDumped)
            {
                DumpAnalysisAnchors(context.Process);
                _analysisDumped = true;
            }

            Drain(context.Process, logAllPackets, elfBuffDiagnostics);

            if (elfBuffDiagnostics && _buffApplyEnabled)
            {
                DrainBuffApply(context.Process);
            }
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "{Task} could not be applied", Name);
        }
    }

    public void Stopping()
    {
        if (_game is { IsRunning: true } game)
        {
            try
            {
                Disable(game);
            }
            catch (GameProcessException e)
            {
                _logger.LogWarning(e, "{Task} could not be disabled", Name);
            }
        }

        _game = null;
    }

    private void Disable(RemoteProcess process)
    {
        DisableBuffApplyProbe(process);

        if (!_enabled)
        {
            return;
        }

        if (ReceivePacketProbe.TrySetEnabled(process, false))
        {
            _logger.LogInformation("{Task}: no longer recording", Name);
        }

        _enabled = false;
        _read = 0;
        _analysisDumped = false;
    }

    private void EnableBuffApplyProbe(RemoteProcess process)
    {
        if (_buffApplyInstallFailed)
        {
            return;
        }

        if (!_buffApplyInstalled)
        {
            if (!BuffApplyProbe.TryInstall(process, out var info, out var error))
            {
                _buffApplyInstallFailed = true;
                _logger.LogWarning(
                    "[ELF_BUFF_DIAG2] apply_buff probe unavailable: {Reason}",
                    error ?? "unknown structural mismatch");
                return;
            }

            _buffApplyInstalled = true;
            _logger.LogInformation(
                "[ELF_BUFF_DIAG2] apply_buff probe installed target={Target} cave={Cave} directCallSites={CallSites}",
                info.ApplyBuff,
                info.Cave,
                info.PatchedCallSites);
        }

        if (_buffApplyEnabled)
        {
            return;
        }

        if (BuffApplyProbe.TryReadWriteIndex(process, out var written))
        {
            _buffApplyRead = written;
        }

        if (BuffApplyProbe.TrySetEnabled(process, true))
        {
            _buffApplyEnabled = true;
            _logger.LogInformation(
                "[ELF_BUFF_DIAG2] apply_buff tracing enabled for states 6,7,8,18..23");
        }
    }

    private void DisableBuffApplyProbe(RemoteProcess process)
    {
        if (!_buffApplyEnabled)
        {
            return;
        }

        BuffApplyProbe.TrySetEnabled(process, false);
        _buffApplyEnabled = false;

        if (BuffApplyProbe.TryReadWriteIndex(process, out var written))
        {
            _buffApplyRead = written;
        }

        _logger.LogInformation("[ELF_BUFF_DIAG2] apply_buff tracing disabled");
    }

    private void Drain(RemoteProcess process, bool logAllPackets, bool elfBuffDiagnostics)
    {
        if (!ReceivePacketProbe.TryReadWriteIndex(process, out var written) || written == _read)
        {
            return;
        }

        var pending = written - _read;
        if (pending > ReceivePacketProbe.RingLength)
        {
            _logger.LogWarning(
                "{Task}: {Count} packets were overwritten before they could be read",
                Name,
                pending - ReceivePacketProbe.RingLength);

            _read = written - ReceivePacketProbe.RingLength;
        }

        while (_read != written)
        {
            if (!ReceivePacketProbe.TryRead(process, _read, out var packet))
            {
                // The writer claims a slot before filling it. Do not skip an entry merely
                // because this polling pass raced the final sequence write.
                break;
            }

            if (logAllPackets)
            {
                _logger.LogInformation("{Task}: {Packet}", Name, packet);
            }

            if (elfBuffDiagnostics && ElfBuffDiagnostic.TryParse(packet.Bytes.Span, out var iconPacket))
            {
                LogElfBuffState(process, packet, iconPacket, written - (_read + 1));
            }

            _read++;
        }
    }

    private void DrainBuffApply(RemoteProcess process)
    {
        if (!BuffApplyProbe.TryReadWriteIndex(process, out var written) || written == _buffApplyRead)
        {
            return;
        }

        var pending = written - _buffApplyRead;
        if (pending > BuffApplyProbe.RingLength)
        {
            _logger.LogWarning(
                "[ELF_BUFF_DIAG2] {Count} apply_buff trace events were overwritten before they could be read",
                pending - BuffApplyProbe.RingLength);
            _buffApplyRead = written - BuffApplyProbe.RingLength;
        }

        while (_buffApplyRead != written)
        {
            if (!BuffApplyProbe.TryRead(process, _buffApplyRead, out var record))
            {
                break;
            }

            var receiveSequence = record.ReceiveWriteIndex == 0
                ? "n/a"
                : (record.ReceiveWriteIndex - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

            _logger.LogInformation(
                "[ELF_BUFF_CALL] seq={Sequence} recvSeq={ReceiveSequence} phase={Phase} " +
                "caller={Caller} callSite={CallSite} state={State} skill={Skill} value={Value} " +
                "class={Class} raw={Primary:X2}/{Secondary:X2}",
                record.Sequence,
                receiveSequence,
                record.PhaseName,
                record.Caller,
                record.CallSite,
                record.StateId,
                ElfBuffDiagnostic.StateName(record.StateId),
                record.Value,
                record.CharacterClass,
                record.Primary,
                record.Secondary);

            if (_buffApplyCallSitesDumped.Add(record.CallSite.Value))
            {
                DumpBuffCallSite(process, record);
            }

            _buffApplyRead++;
        }
    }


    private void DumpBuffCallSite(RemoteProcess process, BuffApplyRecord record)
    {
        const int before = 24;
        const int length = 56;

        var start = record.CallSite - before;
        var bytes = new byte[length];
        if (!process.TryReadBytes(start, bytes))
        {
            _logger.LogWarning(
                "[ELF_BUFF_CALL_CODE] could not read callSite={CallSite} state={State}",
                record.CallSite,
                record.StateId);
            return;
        }

        _logger.LogInformation(
            "[ELF_BUFF_CALL_CODE] callSite={CallSite} state={State} skill={Skill} start={Start} bytes={Bytes}",
            record.CallSite,
            record.StateId,
            ElfBuffDiagnostic.StateName(record.StateId),
            start,
            BytePattern.Format(bytes));
    }

    private void LogElfBuffState(
        RemoteProcess process,
        ReceivePacketRecord packet,
        ElfBuffIconPacket iconPacket,
        uint queuedAfter)
    {
        var state = BuffState.Read(process);
        if (state is null)
        {
            _logger.LogWarning(
                "[ELF_BUFF_DIAG] seq={Sequence} caller={Caller} kind={Kind} wireId={WireId} skill={Skill} " +
                "duration={Duration} targetState={TargetState} queuedAfter={QueuedAfter} states=unavailable",
                packet.Sequence,
                packet.DispatcherCaller,
                iconPacket.Icon.Kind,
                iconPacket.Icon.WireId,
                iconPacket.Icon.Name,
                iconPacket.Duration,
                iconPacket.Icon.StateId,
                queuedAfter);
            return;
        }

        _logger.LogInformation(
            "[ELF_BUFF_DIAG] seq={Sequence} caller={Caller} kind={Kind} wireId={WireId} skill={Skill} " +
            "duration={Duration} targetState={TargetState} class={Class} queuedAfter={QueuedAfter} states={States}",
            packet.Sequence,
            packet.DispatcherCaller,
            iconPacket.Icon.Kind,
            iconPacket.Icon.WireId,
            iconPacket.Icon.Name,
            iconPacket.Duration,
            iconPacket.Icon.StateId,
            state.Class,
            queuedAfter,
            ElfBuffDiagnostic.FormatStates(state));
    }

    /// <summary>
    /// Emits the fixed dispatcher bytes and xrefs to the expected NPC-shape descriptor.
    /// </summary>
    /// <remarks>
    /// These lines are intentionally one-shot. They are enough to reconstruct the relevant
    /// switch and parser references from a user log without turning every packet into a
    /// memory dump.
    /// </remarks>
    private void DumpAnalysisAnchors(RemoteProcess process)
    {
        DumpBytes(process, "dispatcher", Dispatcher, 0x80);
        DumpBytes(process, "range-check", RangeCheck, 0x80);

        var snapshot = MemorySnapshot.Capture(process, ScanStart, ScanEnd);
        var formats = snapshot.FindAll(BytePattern.Exact("ddhhh\0"u8));

        if (formats.Count == 0)
        {
            _logger.LogInformation("{Task}: descriptor ddhhh was not found in 0x00400000-0x00600000", Name);
            return;
        }

        foreach (var format in formats)
        {
            var encoded = BitConverter.GetBytes(format.Value);
            var xrefs = snapshot.FindAll(BytePattern.Exact(encoded));

            _logger.LogInformation(
                "{Task}: descriptor ddhhh at {Format}, absolute-xrefs={Count}",
                Name,
                format,
                xrefs.Count);

            foreach (var xref in xrefs.Take(16))
            {
                var from = xref - 16;
                var bytes = new byte[40];
                if (snapshot.TryRead(from, bytes))
                {
                    _logger.LogInformation(
                        "{Task}: ddhhh-xref {Xref} context={Bytes}",
                        Name,
                        xref,
                        BytePattern.Format(bytes));
                }
            }
        }
    }

    private void DumpBytes(RemoteProcess process, string label, GameAddress address, int count)
    {
        var bytes = new byte[count];
        if (process.TryReadBytes(address, bytes))
        {
            _logger.LogInformation(
                "{Task}: {Label} {Address} bytes={Bytes}",
                Name,
                label,
                address,
                BytePattern.Format(bytes));
        }
        else
        {
            _logger.LogWarning("{Task}: could not read {Label} at {Address}", Name, label, address);
        }
    }
}
