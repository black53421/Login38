using System.Net;
using Login38.Core.Servers;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Redirects every outbound TCP connection the client makes to the chosen server.
/// </summary>
/// <remarks>
/// <para>
/// The client has its own idea of which address to connect to, baked in and reachable
/// only through the server list it ships with. Rather than patch that logic, this hooks
/// winsock itself: <c>connect</c> and <c>WSAConnect</c> are diverted to a code cave that
/// rewrites the <c>sockaddr_in</c> in place before the real call proceeds.
/// </para>
/// <para>
/// Both entry points are hooked because which one the client uses depends on its
/// winsock version path. Hooking either one alone works most of the time, which is
/// worse than not working at all — so this succeeds if at least one is hooked and says
/// which.
/// </para>
/// <para>
/// Only <c>AF_INET</c> addresses are rewritten. The client also makes local IPC and
/// name-resolution connections that would break if their addresses were overwritten.
/// </para>
/// </remarks>
public sealed class ConnectRedirectPatch : IGamePatch
{
    private const string WinsockModule = "ws2_32.dll";

    private static readonly string[] ConnectExports = ["connect", "WSAConnect"];

    /// <summary>
    /// Winsock is loaded lazily, so it is usually absent when the process is created.
    /// </summary>
    private static readonly TimeSpan ModuleWaitTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ModuleWaitPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>Enough for the largest trampoline this builds.</summary>
    private const int CaveSize = 96;

    /// <summary>
    /// Offset of the <c>sockaddr*</c> argument once the trampoline has run
    /// <c>pushad</c>: 32 bytes of saved registers, the return address, then the socket
    /// handle.
    /// </summary>
    private const byte SockAddrStackOffset = 0x28;

    private const byte AddressFamilyInet = 0x02;

    private readonly ILogger<ConnectRedirectPatch> _logger;

    public ConnectRedirectPatch(ILogger<ConnectRedirectPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "connect-redirect";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Networking;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.ConnectTarget ?? ServerEndpoint.Resolve(context.Server);
        var process = context.Process;

        _logger.LogInformation("Redirecting winsock connections to {Endpoint}", endpoint);

        var winsock = process.WaitForModuleAsync(WinsockModule, ModuleWaitTimeout, ModuleWaitPoll, cancellationToken)
            .GetAwaiter().GetResult()
            ?? throw new GameProcessException(
                $"{WinsockModule} was not loaded within {ModuleWaitTimeout.TotalSeconds:0}s.");

        _logger.LogDebug("{Module} base {Base}", WinsockModule, winsock);

        var hooked = new List<string>();

        // Both hooks go in under one suspension: with only one installed the client can
        // connect to the wrong address through the other entry point.
        using (process.SuspendThreads())
        {
            foreach (var export in ConnectExports)
            {
                if (TryHookExport(process, winsock, export, endpoint))
                {
                    hooked.Add(export);
                }
            }
        }

        if (hooked.Count == 0)
        {
            throw new GameProcessException(
                $"Neither connect nor WSAConnect could be hooked in {WinsockModule}.");
        }

        _logger.LogInformation("Winsock redirect installed via {Exports}", string.Join(", ", hooked));
    }

    private bool TryHookExport(
        RemoteProcess process,
        GameAddress winsockBase,
        string exportName,
        IPEndPoint endpoint)
    {
        if (process.FindExport(winsockBase, exportName) is not { } target)
        {
            _logger.LogDebug("{Export} is not exported by {Module}", exportName, WinsockModule);
            return false;
        }

        var stolen = process.ReadBytes(target, InlineHook.JumpSize);

        // Already diverted, by an earlier run against this same process. Re-hooking
        // would capture our own jump as the original bytes and loop forever.
        if (stolen[0] == 0xE9)
        {
            _logger.LogDebug("{Export} at {Address} is already hooked", exportName, target);
            return true;
        }

        var cave = process.AllocateExecutable(CaveSize);
        var trampoline = BuildTrampoline(cave, target, stolen, endpoint);

        if (trampoline.Length > CaveSize)
        {
            throw new InvalidOperationException(
                $"{exportName} trampoline is {trampoline.Length} bytes but the cave is {CaveSize}.");
        }

        process.WriteCode(cave, trampoline);
        process.WriteCode(target, InlineHook.BuildJump(target, cave));

        _logger.LogDebug("{Export} at {Address} redirected through {Cave}", exportName, target, cave);
        return true;
    }

    /// <summary>
    /// Builds the code cave: rewrite the address if it is IPv4, replay the bytes the
    /// hook displaced, then continue into the real function.
    /// </summary>
    internal static byte[] BuildTrampoline(
        GameAddress cave,
        GameAddress target,
        ReadOnlySpan<byte> stolenBytes,
        IPEndPoint endpoint)
    {
        var address = BitConverter.ToUInt32(endpoint.Address.GetAddressBytes());
        var port = (ushort)IPAddress.HostToNetworkOrder((short)endpoint.Port);

        var code = new ShellcodeBuilder(cave);

        code.PushAd()
            .Bytes([0x8B, 0x44, 0x24, SockAddrStackOffset])          // mov eax, [esp+0x28]
            .Bytes([0x66, 0x83, 0x38, AddressFamilyInet]);           // cmp word ptr [eax], AF_INET

        var notInet = code.ShortJumpIfNotEqual();

        // sin_port and sin_addr are already network byte order in the struct, so the
        // values are written as-is with no further swapping.
        code.Bytes([0x66, 0xC7, 0x40, 0x02]).Word(port)              // mov word ptr [eax+2], port
            .Bytes([0xC7, 0x40, 0x04]).Dword(address);               // mov dword ptr [eax+4], addr

        code.MarkLabel(notInet);

        code.PopAd()
            .Bytes(stolenBytes)
            .JumpTo(target + stolenBytes.Length);

        return code.Build();
    }
}
