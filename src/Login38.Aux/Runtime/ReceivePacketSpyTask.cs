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
    private bool _enabled;
    private bool _reportedUnavailable;
    private bool _analysisDumped;

    public ReceivePacketSpyTask(ILogger<ReceivePacketSpyTask> logger) => _logger = logger;

    public string Name => "recv-packet-log";

    public TimeSpan Interval => TimeSpan.FromMilliseconds(200);

    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requested = context.Settings.Misc.LogReceivedPackets;
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
                _logger.LogInformation("{Task}: recording what the server sends", Name);
            }

            if (!_analysisDumped)
            {
                DumpAnalysisAnchors(context.Process);
                _analysisDumped = true;
            }

            Drain(context.Process);
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

    private void Drain(RemoteProcess process)
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

            _logger.LogInformation("{Task}: {Packet}", Name, packet);
            _read++;
        }
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
