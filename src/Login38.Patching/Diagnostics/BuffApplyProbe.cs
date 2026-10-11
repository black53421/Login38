using System.Collections.Concurrent;
using Login38.Interop;

namespace Login38.Patching.Diagnostics;

/// <summary>
/// Diagnostic-only recorder for the client's common apply_buff routine.
/// </summary>
/// <remarks>
/// The 3.81C client calls <c>0x004EE2D0</c> with <c>(stateId, enabled)</c>. Rather than
/// detouring the function prologue, which would require stealing an instruction-length-safe
/// prefix, this probe rewrites the direct <c>call rel32</c> xrefs to a pass-through wrapper.
/// The wrapper records only the elf states currently under study and then calls the original
/// function with the same arguments. When disabled it tail-jumps directly to apply_buff.
/// </remarks>
public static class BuffApplyProbe
{
    public const int RingLength = 128;
    public const int EntrySize = 32;

    public const uint EnabledOffset = 0x00;
    public const uint IndexOffset = 0x04;
    public const uint RingOffset = 0x100;

    public const int CallerOffset = 0x00;
    public const int StateOffset = 0x04;
    public const int ValueOffset = 0x08;
    public const int PhaseOffset = 0x0C;
    public const int PrimaryOffset = 0x0D;
    public const int SecondaryOffset = 0x0E;
    public const int ClassOffset = 0x0F;
    public const int ReceiveWriteIndexOffset = 0x10;
    public const int SequenceOffset = 0x14;

    public const byte PhaseBefore = 0;
    public const byte PhaseAfter = 1;

    public const int DataSize = checked((int)RingOffset + (RingLength * EntrySize));

    private static readonly GameAddress ApplyBuff = new(0x004E_E2D0);
    private static readonly GameAddress KnownCallSite = new(0x005A_5B4A);
    // Owned by the optional elf-buff coexistence filter. The diagnostic tracer must
    // leave this call instruction untouched so both features can be enabled together.
    private static readonly GameAddress ElfPeerClearCallSite = new(0x004E_E48C);
    private static readonly GameAddress ScanStart = new(0x0040_1000);
    private static readonly GameAddress ScanEnd = new(0x00A0_0000);
    private static readonly GameAddress EffectTable = new(0x00AB_F4C8);
    private static readonly GameAddress CharacterClass = new(0x00C3_1544);

    private const int EffectHalf = 0x1F0;
    private const int CaveSize = 0x300;
    // Runtime scanning counts only direct E8 rel32 xrefs that still target apply_buff.
    // The current 3.81C image exposes 135 such sites after Login38 startup; this is not
    // the same metric as older static-analysis notes that counted a broader caller set.
    // Keep a conservative range and retain the known-anchor check below.
    private const int MinimumExpectedCallSites = 120;
    private const int MaximumExpectedCallSites = 160;

    private static readonly byte[] TargetStates = [6, 7, 8, 18, 19, 20, 21, 22, 23];

    private static readonly ConcurrentDictionary<uint, Session> Sessions = new();

    /// <summary>Installs the pass-through apply_buff recorder once for a process.</summary>
    public static bool TryInstall(
        RemoteProcess process,
        out BuffApplyProbeInfo info,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (Sessions.TryGetValue(process.Id, out var installed))
        {
            info = installed.Info;
            error = null;
            return true;
        }

        if (!TryResolveDirectCall(process, KnownCallSite, out var anchorTarget) || anchorTarget != ApplyBuff)
        {
            info = default;
            error = $"known apply_buff call {KnownCallSite} no longer targets {ApplyBuff}";
            return false;
        }

        var image = MemorySnapshot.Capture(process, ScanStart, ScanEnd);
        var callSites = FindDirectCalls(image, ApplyBuff)
            .Where(site => site != ElfPeerClearCallSite)
            .ToList();

        if (callSites.Count < MinimumExpectedCallSites || callSites.Count > MaximumExpectedCallSites)
        {
            info = default;
            error = $"apply_buff direct-call count {callSites.Count} is outside " +
                $"{MinimumExpectedCallSites}..{MaximumExpectedCallSites}";
            return false;
        }

        var data = process.AllocateData(DataSize);
        var cave = process.AllocateExecutable(CaveSize);
        var receiveData = ReceivePacketProbe.TryGetBlock(process, out var receiveBlock)
            ? receiveBlock
            : GameAddress.Zero;

        process.Write(data + EnabledOffset, 0u);
        process.Write(data + IndexOffset, 0u);

        var wrapper = BuildWrapper(cave, data, receiveData);
        if (wrapper.Length > CaveSize)
        {
            throw new InvalidOperationException(
                $"apply_buff diagnostic wrapper is {wrapper.Length} bytes but the cave is {CaveSize}");
        }

        process.WriteCode(cave, wrapper);

        using (process.SuspendThreads())
        {
            // Validate the complete set before writing anything. A structural mismatch must
            // never leave a partially rewritten client even though the wrapper is pass-through.
            foreach (var site in callSites)
            {
                if (!TryResolveDirectCall(process, site, out var target) || target != ApplyBuff)
                {
                    info = default;
                    error = $"apply_buff call site {site} changed before installation";
                    return false;
                }
            }

            foreach (var site in callSites)
            {
                process.WriteCode(site, BuildCall(site, cave));
            }
        }

        info = new BuffApplyProbeInfo(ApplyBuff, cave, callSites.Count);
        Sessions[process.Id] = new Session(data, info);
        error = null;
        return true;
    }

    /// <summary>Enables or disables event recording without rewriting code.</summary>
    public static bool TrySetEnabled(RemoteProcess process, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!Sessions.TryGetValue(process.Id, out var session))
        {
            return false;
        }

        process.Write(session.Data + EnabledOffset, enabled ? 1u : 0u);
        return true;
    }

    /// <summary>Reads the number of event slots claimed by the in-process recorder.</summary>
    public static bool TryReadWriteIndex(RemoteProcess process, out uint written)
    {
        ArgumentNullException.ThrowIfNull(process);
        written = 0;

        return Sessions.TryGetValue(process.Id, out var session)
            && process.TryRead(session.Data + IndexOffset, out written);
    }

    /// <summary>Reads one committed apply_buff trace entry.</summary>
    public static bool TryRead(RemoteProcess process, uint sequence, out BuffApplyRecord record)
    {
        ArgumentNullException.ThrowIfNull(process);
        record = default;

        if (!Sessions.TryGetValue(process.Id, out var session))
        {
            return false;
        }

        Span<byte> entry = stackalloc byte[EntrySize];
        var slot = session.Data + RingOffset + ((sequence % RingLength) * EntrySize);

        if (!process.TryReadBytes(slot, entry))
        {
            return false;
        }

        var committed = BitConverter.ToUInt32(entry[SequenceOffset..]);
        if (committed != unchecked(sequence + 1))
        {
            return false;
        }

        record = new BuffApplyRecord(
            sequence,
            new GameAddress(BitConverter.ToUInt32(entry[CallerOffset..])),
            BitConverter.ToInt32(entry[StateOffset..]),
            BitConverter.ToInt32(entry[ValueOffset..]),
            entry[PhaseOffset],
            entry[PrimaryOffset],
            entry[SecondaryOffset],
            entry[ClassOffset],
            BitConverter.ToUInt32(entry[ReceiveWriteIndexOffset..]));
        return true;
    }

    internal static IReadOnlyList<GameAddress> FindDirectCalls(MemorySnapshot image, GameAddress target)
    {
        ArgumentNullException.ThrowIfNull(image);

        var hits = image.FindAll(BytePattern.Exact(new byte[] { 0xE8 }));
        var result = new List<GameAddress>();
        Span<byte> instruction = stackalloc byte[5];

        foreach (var hit in hits)
        {
            if (!image.TryRead(hit, instruction))
            {
                continue;
            }

            var resolved = hit + (5 + BitConverter.ToInt32(instruction[1..]));
            if (resolved == target)
            {
                result.Add(hit);
            }
        }

        return result;
    }

    internal static byte[] BuildWrapper(GameAddress cave, GameAddress data, GameAddress receiveData)
    {
        var code = new ShellcodeBuilder(cave);

        code.Bytes([0x83, 0x3D]).Dword((data + EnabledOffset).Value).Byte(0x00); // cmp dword [enabled], 0
        var disabled = code.NearJump(0x84);                                      // je passthrough

        code.Bytes([0x8B, 0x44, 0x24, 0x04]);                                   // mov eax, [esp+4]
        var targetBranches = new List<ShellcodeBuilder.Label>();
        foreach (var state in TargetStates)
        {
            code.Bytes([0x83, 0xF8, state]);                                     // cmp eax, state
            targetBranches.Add(code.ShortJumpIfZero());                          // je trace
        }

        code.MarkLabel(disabled)
            .JumpTo(ApplyBuff);

        foreach (var branch in targetBranches)
        {
            code.MarkLabel(branch);
        }

        EmitRecord(code, data, receiveData, PhaseBefore);

        // Preserve the original caller's arguments. The two pushes use the same [esp+8]
        // displacement because the first push moves the stack by one dword.
        code.PushStackSlot(0x08)
            .PushStackSlot(0x08)
            .CallTo(ApplyBuff)
            .AddEsp(0x08);

        EmitRecord(code, data, receiveData, PhaseAfter);
        return code.Ret().Build();
    }

    private static void EmitRecord(
        ShellcodeBuilder code,
        GameAddress data,
        GameAddress receiveData,
        byte phase)
    {
        code.PushFd()
            .PushAd()
            .MovEax(1)
            .Bytes([0xF0, 0x0F, 0xC1, 0x05]).Dword((data + IndexOffset).Value)    // lock xadd [index], eax
            .Bytes([0x89, 0xC3])                                                 // mov ebx, eax
            .Byte(0x25).Dword((uint)(RingLength - 1))                                   // and eax, RingLength-1
            .Bytes([0xC1, 0xE0, 0x05])                                          // shl eax, 5
            .AddEax((data + RingOffset).Value)
            .Bytes([0x89, 0xC7])                                                 // mov edi, eax
            .Bytes([0x8B, 0x4C, 0x24, 0x24])                                    // mov ecx, [esp+0x24] caller
            .Bytes([0x89, 0x0F])                                                 // mov [edi], ecx
            .Bytes([0x8B, 0x4C, 0x24, 0x28])                                    // mov ecx, [esp+0x28] state
            .Bytes([0x89, 0x4F, StateOffset])                                    // mov [edi+4], ecx
            .Bytes([0x8B, 0x54, 0x24, 0x2C])                                    // mov edx, [esp+0x2C] value
            .Bytes([0x89, 0x57, ValueOffset])                                    // mov [edi+8], edx
            .Bytes([0xC6, 0x47, PhaseOffset, phase])                             // mov byte [edi+phase], phase
            .Bytes([0x89, 0xC8])                                                 // mov eax, ecx
            .Bytes([0x8A, 0x90]).Dword(EffectTable.Value)                        // mov dl, [eax+table]
            .Bytes([0x88, 0x57, PrimaryOffset])                                  // mov [edi+primary], dl
            .Bytes([0x8A, 0x90]).Dword((EffectTable + EffectHalf).Value)         // mov dl, [eax+table+half]
            .Bytes([0x88, 0x57, SecondaryOffset])                                // mov [edi+secondary], dl
            .MovAlFrom(CharacterClass)
            .Bytes([0x88, 0x47, ClassOffset]);                                   // mov [edi+class], al

        if (!receiveData.IsNull)
        {
            code.MovEaxFrom(receiveData + ReceivePacketProbe.IndexOffset)
                .Bytes([0x89, 0x47, ReceiveWriteIndexOffset]);                   // mov [edi+recvIndex], eax
        }
        else
        {
            code.Bytes([0xC7, 0x47, ReceiveWriteIndexOffset]).Dword(0);
        }

        code.IncEbx()
            .Bytes([0x89, 0x5F, SequenceOffset])                                 // commit sequence+1 last
            .PopAd()
            .PopFd();
    }

    private static bool TryResolveDirectCall(
        RemoteProcess process,
        GameAddress site,
        out GameAddress target)
    {
        Span<byte> instruction = stackalloc byte[5];
        target = default;

        if (!process.TryReadBytes(site, instruction) || instruction[0] != 0xE8)
        {
            return false;
        }

        target = site + (5 + BitConverter.ToInt32(instruction[1..]));
        return true;
    }

    private static byte[] BuildCall(GameAddress from, GameAddress to) =>
        [0xE8, .. BitConverter.GetBytes(to - (from + 5))];

    private sealed record Session(GameAddress Data, BuffApplyProbeInfo Info);
}

/// <summary>Addresses and xref count for an installed apply_buff diagnostic wrapper.</summary>
public readonly record struct BuffApplyProbeInfo(
    GameAddress ApplyBuff,
    GameAddress Cave,
    int PatchedCallSites);

/// <summary>One before/after observation around a call to the client's apply_buff routine.</summary>
public readonly record struct BuffApplyRecord(
    uint Sequence,
    GameAddress Caller,
    int StateId,
    int Value,
    byte Phase,
    byte Primary,
    byte Secondary,
    byte CharacterClass,
    uint ReceiveWriteIndex)
{
    public GameAddress CallSite => Caller - 5;

    public string PhaseName => Phase == BuffApplyProbe.PhaseBefore ? "before" : "after";
}
