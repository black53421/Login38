using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Login38.Core;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Traces the movement-specific dynamic blocker classifier used after destination occupancy is detected.
/// </summary>
/// <remarks>
/// Enable with <c>LOGIN38_COMPANION_COLLISION_PROBE=1</c>. Probe v3.7 intentionally
/// keeps the environment-variable activation path and does not include the command-line
/// switch experiment.
/// </remarks>
public sealed class CompanionCollisionProbePatch : IGamePatch
{
    public const string EnableVariable = "LOGIN38_COMPANION_COLLISION_PROBE";

    private static readonly GameAddress LocalPlayer = new(0x00C2_D2B8);
    private static readonly GameAddress MovementCandidate = new(0x00C2_D2B4);
    private static readonly GameAddress CurrentObject = new(0x00AB_F440);
    private static readonly GameAddress CurrentObjectAux = new(0x00AB_F444);
    private static readonly GameAddress SpriteTypeTablePointer = new(0x009A_8F18);
    private static readonly GameAddress SecondarySpriteTablePointer = new(0x009A_8F08);

    private static readonly GameAddress WalkEngineTick = new(0x005A_51A0);
    private static readonly GameAddress ComputeStepHeading = new(0x005A_4D60);
    private static readonly GameAddress TileBlocked = new(0x004F_5910);
    private static readonly GameAddress SmoothRunHookSite = new(0x0044_9776);

    private const int EntityRecordLength = 0x140;
    private const int SpriteTableDumpLength = 0x4000;
    private const int CopyChunkSize = 0x10000;
    private const int TraceCaveSize = 0x200;
    private const int TraceStateSize = 0x80;
    private const int ReadyTimeoutSeconds = 30;
    private const int PollDelayMilliseconds = 10;
    private const int MaxTraceEvents = 100_000;

    private const int SeqOffset = 0x00;
    private const int SiteOffset = 0x04;
    private const int KindOffset = 0x08;
    private const int EaxOffset = 0x0C;
    private const int EcxOffset = 0x10;
    private const int EdxOffset = 0x14;
    private const int EntityOffset = 0x18;
    private const int ClassOffset = 0x1C;
    private const int RelativeResultOffset = 0x20;
    private const int CurrentObjectOffset = 0x24;
    private const int CurrentObjectAuxOffset = 0x28;
    private const int GfxOffset = 0x2C;
    private const int OwnerNameOffset = 0x30;
    private const int LocalPlayerOffset = 0x34;
    private const int LocalNameOffset = 0x38;
    private const int EntityXOffset = 0x3C;
    private const int EntityYOffset = 0x40;
    private const int LocalXOffset = 0x44;
    private const int LocalYOffset = 0x48;
    private const int CallerOffset = 0x4C;
    private const int ArgXOffset = 0x50;
    private const int ArgYOffset = 0x54;
    private const int HeadingOffset = 0x58;
    private const int ResultOffset = 0x5C;

    private const uint UnknownByte = 0xFFFF_FFFF;

    private const uint KindSanity = 1;
    private const uint KindMovementBlocker = 2;
    private const uint KindBlockerClassifier = 3;

    private static readonly TraceSite[] TraceSites =
    [
        new("walk-sanity", 1, KindSanity, new GameAddress(0x005A_51A0), new GameAddress(0x005A_51A5),
            [0x55, 0x8B, 0xEC, 0x6A, 0xFF], TraceReplay.Raw),
        new("movement-blocker", 2, KindMovementBlocker, new GameAddress(0x004F_5CD5), new GameAddress(0x004F_5CDC),
            [0x8B, 0x08, 0xE8, 0x34, 0x93, 0x0B, 0x00], TraceReplay.MovementBlockerCall),
        new("blocker-classifier", 3, KindBlockerClassifier, new GameAddress(0x005A_F021), new GameAddress(0x005A_F028),
            [0x88, 0x45, 0xFF, 0x0F, 0xB6, 0x45, 0xFF], TraceReplay.Raw),
    ];

    private static readonly (string Name, GameAddress Address)[] CodeAnchors =
    [
        ("WalkEngineTick", WalkEngineTick),
        ("ComputeStepHeading", ComputeStepHeading),
        ("TileBlocked", TileBlocked),
        ("MovementCanPass", new GameAddress(0x004F_5A60)),
        ("DynamicBlockerClassifier", new GameAddress(0x005A_F010)),
        ("EntityCombatTypeClass", new GameAddress(0x005A_EBE0)),
        ("SmoothRunHookSite", SmoothRunHookSite),
    ];

    private static readonly ushort[] ComparisonSprites = [96, 5919, 6096, 6100];

    private readonly ILogger<CompanionCollisionProbePatch> _logger;

    public CompanionCollisionProbePatch(ILogger<CompanionCollisionProbePatch> logger) => _logger = logger;

    public string Name => "companion-collision-probe";

    public PatchPhase Phase => PatchPhase.InWorld;

    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return EnvironmentSwitch.IsEnabled(EnableVariable) &&
            !context.Aux.OwnerCompanionPassThrough &&
            !context.Aux.OtherCompanionPassThrough;
    }

    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        _ = Task.Run(
            () => RunProbeAsync(context, CancellationToken.None),
            CancellationToken.None);
    }

    private async Task RunProbeAsync(GamePatchContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!await WaitUntilReadyAsync(context.Process, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning(
                    "Companion collision probe v3.7 did not see a ready client within {Seconds} seconds",
                    ReadyTimeoutSeconds);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);

            var directory = CreateOutputDirectory(context.GameDirectory, context.Process.Id);
            var manifest = new StringBuilder();

            manifest.AppendLine(Invariant($"probe_version=3.7"));
            manifest.AppendLine(Invariant($"pid={context.Process.Id}"));
            manifest.AppendLine(Invariant($"scan_start={context.ScanStart}"));
            manifest.AppendLine(Invariant($"scan_end={context.ScanEnd}"));
            manifest.AppendLine(Invariant($"captured_utc={DateTimeOffset.UtcNow:O}"));

            DumpImage(
                context.Process,
                context.ScanStart,
                context.ScanEnd,
                directory,
                manifest,
                cancellationToken);
            DumpState(context.Process, directory, manifest);
            AppendPreHookCorrelation(context.Process, directory, manifest);

            var installed = InstallTraceHooks(context.Process, manifest);
            File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString(), Encoding.UTF8);

            _logger.LogInformation(
                "Companion collision probe v3.7 installed {Count} trace sites; output {Directory}",
                installed.Count,
                directory);

            await TraceAsync(context.Process, installed, directory, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The launcher/game session is closing.
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "Companion collision probe v3.7 stopped after a game-memory failure");
        }
        catch (IOException e)
        {
            _logger.LogWarning(e, "Companion collision probe v3.7 stopped after a diagnostic-file failure");
        }
    }

    private static async Task<bool> WaitUntilReadyAsync(
        RemoteProcess process,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(ReadyTimeoutSeconds);

        while (process.IsRunning && DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReadPointer(process, LocalPlayer) >= 0x0001_0000 &&
                HasExpectedTraceSites(process))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool HasExpectedTraceSites(RemoteProcess process)
    {
        foreach (var site in TraceSites)
        {
            var current = new byte[site.Original.Length];
            if (!process.TryReadBytes(site.HookSite, current) ||
                !current.AsSpan().SequenceEqual(site.Original))
            {
                return false;
            }
        }

        return true;
    }

    private static List<InstalledTraceSite> InstallTraceHooks(
        RemoteProcess process,
        StringBuilder manifest)
    {
        var installed = new List<InstalledTraceSite>(TraceSites.Length);

        foreach (var site in TraceSites)
        {
            var current = process.ReadBytes(site.HookSite, site.Original.Length);
            if (!current.AsSpan().SequenceEqual(site.Original))
            {
                throw new GameProcessException(
                    $"Expected trace site {site.Name} at {site.HookSite} to be " +
                    $"{BytePattern.Format(site.Original)} but found {BytePattern.Format(current)}.");
            }

            var state = process.AllocateData(TraceStateSize);
            var cave = process.AllocateExecutable(TraceCaveSize);
            process.WriteBytes(state, new byte[TraceStateSize]);

            var shellcode = BuildTraceShellcode(cave, state, site);
            if (shellcode.Length > TraceCaveSize)
            {
                throw new GameProcessException(
                    $"Trace site {site.Name} needs {shellcode.Length} cave bytes but only {TraceCaveSize} were reserved.");
            }

            process.WriteCode(cave, shellcode);
            installed.Add(new InstalledTraceSite(site, state, cave));

            manifest.AppendLine(Invariant($"trace_{site.Name}_hook={site.HookSite}"));
            manifest.AppendLine(Invariant($"trace_{site.Name}_return={site.ReturnSite}"));
            manifest.AppendLine(Invariant($"trace_{site.Name}_state={state}"));
            manifest.AppendLine(Invariant($"trace_{site.Name}_cave={cave}"));
        }

        using var suspended = process.SuspendThreads();
        foreach (var item in installed)
        {
            process.WriteCode(
                item.Site.HookSite,
                InlineHook.BuildJump(item.Site.HookSite, item.Cave, item.Site.Original.Length));
        }

        foreach (var item in installed)
        {
            var patched = process.ReadBytes(item.Site.HookSite, item.Site.Original.Length);
            var expected = InlineHook.BuildJump(item.Site.HookSite, item.Cave, item.Site.Original.Length);
            var verified = patched.AsSpan().SequenceEqual(expected);
            manifest.AppendLine(Invariant($"trace_{item.Site.Name}_installed={verified}"));
            manifest.AppendLine(Invariant($"trace_{item.Site.Name}_patched={BytePattern.Format(patched)}"));
        }

        return installed;
    }

    internal static byte[] BuildTraceShellcode(
        GameAddress cave,
        GameAddress state,
        TraceSite site)
    {
        var code = new ShellcodeBuilder(cave);

        if (site.Replay == TraceReplay.MovementBlockerCall)
        {
            BuildMovementBlockerShellcode(code, state, site);
            return code.Build();
        }

        code.PushFd().PushAd();

        code.MovDwordPtr(state + SiteOffset, site.SiteId)
            .MovDwordPtr(state + KindOffset, site.Kind)
            .MovDwordPtr(state + ResultOffset, UnknownByte)
            .MovDwordPtr(state + ClassOffset, UnknownByte);

        if (site.Kind == KindBlockerClassifier)
        {
            CaptureResultAl(code, state + ClassOffset);
            CaptureEbpDword(code, state + CallerOffset, 0x04);
            CaptureEbpDword(code, state + EntityOffset, -0x08);
            CaptureEntityFields(code, state);
            CaptureLocalFields(code, state);
        }

        PublishSequence(code, state);
        ReplayAndReturn(code, site);
        return code.Build();
    }

    private static void BuildMovementBlockerShellcode(
        ShellcodeBuilder code,
        GameAddress state,
        TraceSite site)
    {
        // At 0x004F5CD5 EAX points at the entity-list slot. The displaced code is
        //   mov ecx,[eax]
        //   call 0x005AF010
        // Capture the entity before the call, replay the exact call, then capture AL.
        code.PushFd().PushAd()
            .MovDwordPtr(state + SiteOffset, site.SiteId)
            .MovDwordPtr(state + KindOffset, site.Kind)
            .MovDwordPtr(state + ClassOffset, UnknownByte)
            .MovDwordPtr(state + ResultOffset, UnknownByte)
            .Bytes([0x8B, 0x00])
            .MovEaxTo(state + EntityOffset);

        CaptureEntityFields(code, state);
        CaptureLocalFields(code, state);
        CaptureEbpDword(code, state + ArgXOffset, -0x08);
        CaptureEbpDword(code, state + ArgYOffset, -0x04);
        CaptureEbpDword(code, state + HeadingOffset, 0x08);
        code.MovDwordPtr(state + CallerOffset, 0x004F_5CDC);

        code.PopAd().PopFd()
            .MovEcxFromEax()
            .CallTo(new GameAddress(0x005A_F010))
            .PushFd().PushAd();

        CaptureResultAl(code, state + ResultOffset);
        PublishSequence(code, state);

        code.PopAd().PopFd()
            .JumpTo(site.ReturnSite);
    }

    private static void CaptureResultAl(ShellcodeBuilder code, GameAddress destination)
    {
        code.Bytes([0x0F, 0xB6, 0xC0])
            .MovEaxTo(destination);
    }

    private static void CaptureEbpDword(
        ShellcodeBuilder code,
        GameAddress destination,
        sbyte displacement)
    {
        code.Bytes([0x8B, 0x45, unchecked((byte)displacement)])
            .MovEaxTo(destination);
    }

    private static void CaptureEntityFields(ShellcodeBuilder code, GameAddress state)
    {
        code.MovEaxFrom(state + EntityOffset).TestEaxEax();
        var noEntity = code.ShortJumpIfZero();
        code.Bytes([0x0F, 0xBF, 0x50, 0x18])
            .Bytes([0x89, 0x15]).Dword((state + GfxOffset).Value)
            .Bytes([0x8B, 0x50, 0x6C])
            .Bytes([0x89, 0x15]).Dword((state + OwnerNameOffset).Value)
            .Bytes([0x8B, 0x50, 0x34])
            .Bytes([0x89, 0x15]).Dword((state + EntityXOffset).Value)
            .Bytes([0x8B, 0x50, 0x38])
            .Bytes([0x89, 0x15]).Dword((state + EntityYOffset).Value);
        code.MarkLabel(noEntity);
    }

    private static void CaptureLocalFields(ShellcodeBuilder code, GameAddress state)
    {
        code.MovEaxFrom(LocalPlayer)
            .MovEaxTo(state + LocalPlayerOffset)
            .TestEaxEax();
        var noLocal = code.ShortJumpIfZero();
        code.Bytes([0x8B, 0x50, 0x60])
            .Bytes([0x89, 0x15]).Dword((state + LocalNameOffset).Value)
            .Bytes([0x8B, 0x50, 0x34])
            .Bytes([0x89, 0x15]).Dword((state + LocalXOffset).Value)
            .Bytes([0x8B, 0x50, 0x38])
            .Bytes([0x89, 0x15]).Dword((state + LocalYOffset).Value);
        code.MarkLabel(noLocal);
    }

    private static void PublishSequence(ShellcodeBuilder code, GameAddress state)
    {
        // Publish sequence last so the launcher only consumes a complete snapshot.
        code.MovEaxFrom(state + SeqOffset)
            .AddEax(1)
            .MovEaxTo(state + SeqOffset);
    }

    private static void ReplayAndReturn(ShellcodeBuilder code, TraceSite site)
    {
        code.PopAd().PopFd();

        code.Bytes(site.Original).JumpTo(site.ReturnSite);
    }

    private static async Task TraceAsync(
        RemoteProcess process,
        IReadOnlyList<InstalledTraceSite> installed,
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "dynamic-blocker-trace-v3.7.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        writer.WriteLine("utc,site,seq,seq_delta,caller,entity,class,result,gfx,owner_name_hex,local_name_hex,entity_x,entity_y,dest_x,dest_y,heading,local_x,local_y");

        var heartbeatPath = Path.Combine(directory, "probe-heartbeat-v3.7.csv");
        using var heartbeat = new StreamWriter(heartbeatPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        heartbeat.WriteLine("utc,site,state_readable,seq");

        var lastSequences = new uint[installed.Count];
        var events = 0;
        var nextHeartbeat = DateTimeOffset.UtcNow;

        while (process.IsRunning && events < MaxTraceEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var index = 0; index < installed.Count; index++)
            {
                var item = installed[index];
                if (!TryReadStableTrace(process, item.State, out var sample) ||
                    sample.Sequence == 0 ||
                    sample.Sequence == lastSequences[index])
                {
                    continue;
                }

                var previous = lastSequences[index];
                var delta = previous == 0 ? sample.Sequence : unchecked(sample.Sequence - previous);
                lastSequences[index] = sample.Sequence;
                events++;
                var classValue = FormatProbeByte(sample.Classification);
                var resultValue = FormatProbeByte(sample.Result);
                writer.WriteLine(Invariant(
                    $"{DateTimeOffset.UtcNow:O},{item.Site.Name},{sample.Sequence},{delta},0x{sample.Caller:X8},0x{sample.Entity:X8},{classValue},{resultValue},0x{sample.Gfx:X},{ReadCStringHex(process, sample.OwnerName)},{ReadCStringHex(process, sample.LocalName)},{sample.EntityX},{sample.EntityY},{sample.ArgX},{sample.ArgY},{sample.Heading},{sample.LocalX},{sample.LocalY}"));
            }

            if (DateTimeOffset.UtcNow >= nextHeartbeat)
            {
                foreach (var item in installed)
                {
                    var readable = process.TryRead<uint>(item.State + SeqOffset, out var sequence);
                    heartbeat.WriteLine(Invariant(
                        $"{DateTimeOffset.UtcNow:O},{item.Site.Name},{readable},{sequence}"));
                }

                nextHeartbeat = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
            }

            await Task.Delay(PollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatProbeByte(uint value) =>
        value == UnknownByte ? "unknown" : value.ToString(CultureInfo.InvariantCulture);

    private static bool TryReadStableTrace(
        RemoteProcess process,
        GameAddress state,
        out TraceSample sample)
    {
        sample = default;

        if (!process.TryRead<uint>(state + SeqOffset, out var before) || before == 0)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[ResultOffset + sizeof(uint)];
        if (!process.TryReadBytes(state, payload) ||
            !process.TryRead<uint>(state + SeqOffset, out var after) ||
            before != after)
        {
            return false;
        }

        sample = new TraceSample(
            before,
            BinaryPrimitives.ReadUInt32LittleEndian(payload[SiteOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[KindOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[EaxOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[EcxOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[EdxOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[EntityOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[ClassOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[RelativeResultOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[CurrentObjectOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[CurrentObjectAuxOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[GfxOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[OwnerNameOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[LocalPlayerOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[LocalNameOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[EntityXOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[EntityYOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[LocalXOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[LocalYOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[CallerOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[ArgXOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[ArgYOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[HeadingOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[ResultOffset..]));
        return true;
    }

    private static EntitySnapshot ReadEntitySnapshot(RemoteProcess process, uint address)
    {
        if (address < 0x0001_0000)
        {
            return default;
        }

        if (!process.TryRead<ushort>(new GameAddress(address + 0x18), out var gfx) ||
            !process.TryRead<uint>(new GameAddress(address + 0x34), out var x) ||
            !process.TryRead<uint>(new GameAddress(address + 0x38), out var y))
        {
            return default;
        }

        return new EntitySnapshot("0x" + gfx.ToString("X", CultureInfo.InvariantCulture), x, y);
    }

    private static string ReadCStringHex(RemoteProcess process, uint address)
    {
        if (address < 0x0001_0000)
        {
            return string.Empty;
        }

        Span<byte> bytes = stackalloc byte[32];
        if (!process.TryReadBytes(new GameAddress(address), bytes))
        {
            return "unreadable";
        }

        var nul = bytes.IndexOf((byte)0);
        var length = nul >= 0 ? nul : bytes.Length;
        return Convert.ToHexString(bytes[..length]);
    }

    private static void AppendPreHookCorrelation(
        RemoteProcess process,
        string directory,
        StringBuilder manifest)
    {
        var walkBytes = process.ReadBytes(WalkEngineTick, 64);
        var smoothBytes = process.ReadBytes(SmoothRunHookSite, 16);

        File.WriteAllBytes(Path.Combine(directory, "walk-engine-prehook.bin"), walkBytes);
        File.WriteAllBytes(Path.Combine(directory, "smooth-run-prehook.bin"), smoothBytes);

        manifest.AppendLine(Invariant($"prehook_walk_bytes={BytePattern.Format(walkBytes)}"));
        manifest.AppendLine(Invariant($"prehook_smoothrun_bytes={BytePattern.Format(smoothBytes)}"));
        manifest.AppendLine(Invariant($"prehook_smoothrun_is_jump={smoothBytes.Length >= 5 && smoothBytes[0] == 0xE9}"));

        if (smoothBytes.Length >= 5 && smoothBytes[0] == 0xE9)
        {
            var displacement = BinaryPrimitives.ReadInt32LittleEndian(smoothBytes.AsSpan(1, 4));
            var target = unchecked((uint)(SmoothRunHookSite.Value + 5 + displacement));
            manifest.AppendLine(Invariant($"prehook_smoothrun_jump_target=0x{target:X8}"));
        }
    }

    private static string CreateOutputDirectory(string gameDirectory, uint processId)
    {
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var root = Path.Combine(gameDirectory, "Login38-Diagnostics", "companion-collision-v3.7");
        var directory = Path.Combine(root, $"{stamp}-pid{processId}");

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DumpImage(
        RemoteProcess process,
        GameAddress start,
        GameAddress end,
        string directory,
        StringBuilder manifest,
        CancellationToken cancellationToken)
    {
        var imagePath = Path.Combine(directory, $"client-unpacked-{start.Value:X8}-{end.Value:X8}.bin");
        var regionPath = Path.Combine(directory, "image-regions.csv");
        var imageLength = (long)end.Value - start.Value;
        var buffer = new byte[CopyChunkSize];

        using var image = new FileStream(imagePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var regions = new StreamWriter(regionPath, false, new UTF8Encoding(false));

        image.SetLength(imageLength);
        regions.WriteLine("start,end,size,kind");

        foreach (var region in process.Regions(start, end))
        {
            cancellationToken.ThrowIfCancellationRequested();

            regions.WriteLine(Invariant(
                $"0x{region.Start.Value:X8},0x{region.End.Value:X8},0x{region.Size:X},{region.Kind}"));

            for (var offset = 0u; offset < region.Size;)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var take = (int)Math.Min((uint)buffer.Length, region.Size - offset);
                var window = buffer.AsSpan(0, take);

                if (!process.TryReadBytes(region.Start + offset, window))
                {
                    break;
                }

                image.Position = (long)(region.Start.Value + offset) - start.Value;
                image.Write(window);
                offset += (uint)take;
            }
        }

        manifest.AppendLine(Invariant($"image_file={Path.GetFileName(imagePath)}"));
        manifest.AppendLine(Invariant($"image_base={start}"));
    }

    private static void DumpState(RemoteProcess process, string directory, StringBuilder manifest)
    {
        using var state = new StreamWriter(
            Path.Combine(directory, "collision-state.txt"), false, new UTF8Encoding(false));

        state.WriteLine("# Code anchors (virtual addresses in the unpacked image)");
        foreach (var (name, address) in CodeAnchors)
        {
            state.WriteLine(Invariant($"{name}=0x{address.Value:X8}"));
        }

        state.WriteLine();
        state.WriteLine("# Live pointers");

        var local = ReadPointer(process, LocalPlayer);
        var candidate = ReadPointer(process, MovementCandidate);
        var current = ReadPointer(process, CurrentObject);
        var currentAux = ReadPointer(process, CurrentObjectAux);
        var tableA = ResolveTable(process, SpriteTypeTablePointer);
        var tableB = ResolveTable(process, SecondarySpriteTablePointer);

        state.WriteLine(Invariant($"local_player_global={LocalPlayer}"));
        state.WriteLine(Invariant($"local_player=0x{local:X8}"));
        state.WriteLine(Invariant($"movement_candidate_global={MovementCandidate}"));
        state.WriteLine(Invariant($"movement_candidate=0x{candidate:X8}"));
        state.WriteLine(Invariant($"current_object_global={CurrentObject}"));
        state.WriteLine(Invariant($"current_object=0x{current:X8}"));
        state.WriteLine(Invariant($"current_object_aux_global={CurrentObjectAux}"));
        state.WriteLine(Invariant($"current_object_aux=0x{currentAux:X8}"));
        state.WriteLine(Invariant($"sprite_table_a_global={SpriteTypeTablePointer}"));
        state.WriteLine(Invariant($"sprite_table_a=0x{tableA:X8}"));
        state.WriteLine(Invariant($"sprite_table_b_global={SecondarySpriteTablePointer}"));
        state.WriteLine(Invariant($"sprite_table_b=0x{tableB:X8}"));

        if (local >= 0x0001_0000)
        {
            DumpExact(process, new GameAddress(local), EntityRecordLength, Path.Combine(directory, "local-player.bin"));
        }

        if (candidate >= 0x0001_0000)
        {
            DumpExact(process, new GameAddress(candidate), EntityRecordLength, Path.Combine(directory, "movement-candidate.bin"));
        }

        if (current >= 0x0001_0000)
        {
            DumpExact(process, new GameAddress(current), EntityRecordLength, Path.Combine(directory, "current-object.bin"));
        }

        DumpTable(process, tableA, Path.Combine(directory, "sprite-table-a.bin"));
        DumpTable(process, tableB, Path.Combine(directory, "sprite-table-b.bin"));

        state.WriteLine();
        state.WriteLine("# Sprite table comparison");
        state.WriteLine("sprite,table_a,table_b");

        foreach (var sprite in ComparisonSprites)
        {
            state.WriteLine(Invariant(
                $"{sprite},{ReadTableByte(process, tableA, sprite)},{ReadTableByte(process, tableB, sprite)}"));
        }

        manifest.AppendLine(Invariant($"local_player=0x{local:X8}"));
        manifest.AppendLine(Invariant($"movement_candidate=0x{candidate:X8}"));
        manifest.AppendLine(Invariant($"current_object=0x{current:X8}"));
        manifest.AppendLine(Invariant($"current_object_aux=0x{currentAux:X8}"));
        manifest.AppendLine(Invariant($"sprite_table_a=0x{tableA:X8}"));
        manifest.AppendLine(Invariant($"sprite_table_b=0x{tableB:X8}"));
    }

    private static uint ReadPointer(RemoteProcess process, GameAddress global) =>
        process.TryRead<uint>(global, out var value) ? value : 0;

    private static uint ResolveTable(RemoteProcess process, GameAddress global)
    {
        var pointer = ReadPointer(process, global);
        return pointer >= 0x0001_0000 ? pointer : global.Value;
    }

    private static string ReadTableByte(RemoteProcess process, uint table, ushort sprite)
    {
        if (table < 0x0001_0000 ||
            !process.TryRead<byte>(new GameAddress(table + sprite), out var value))
        {
            return "unreadable";
        }

        return "0x" + value.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static void DumpTable(RemoteProcess process, uint table, string path)
    {
        if (table < 0x0001_0000)
        {
            return;
        }

        DumpExact(process, new GameAddress(table), SpriteTableDumpLength, path);
    }

    private static void DumpExact(RemoteProcess process, GameAddress address, int length, string path)
    {
        var bytes = new byte[length];

        if (process.TryReadBytes(address, bytes))
        {
            File.WriteAllBytes(path, bytes);
        }
    }

    private static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);

    internal enum TraceReplay
    {
        Raw,
        MovementBlockerCall,
    }

    internal readonly record struct TraceSite(
        string Name,
        uint SiteId,
        uint Kind,
        GameAddress HookSite,
        GameAddress ReturnSite,
        byte[] Original,
        TraceReplay Replay);

    private readonly record struct InstalledTraceSite(
        TraceSite Site,
        GameAddress State,
        GameAddress Cave);

    private readonly record struct TraceSample(
        uint Sequence,
        uint Site,
        uint Kind,
        uint Eax,
        uint Ecx,
        uint Edx,
        uint Entity,
        uint Classification,
        uint RelativeResult,
        uint CurrentObject,
        uint CurrentObjectAux,
        uint Gfx,
        uint OwnerName,
        uint LocalPlayer,
        uint LocalName,
        uint EntityX,
        uint EntityY,
        uint LocalX,
        uint LocalY,
        uint Caller,
        uint ArgX,
        uint ArgY,
        uint Heading,
        uint Result);

    private readonly record struct EntitySnapshot(string Gfx, uint X, uint Y);
}
