using System.Runtime.InteropServices;
using Login38.Interop;
using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Exercises the Win32 layer against a real, running process — this one.
/// </summary>
/// <remarks>
/// <para>
/// Unit tests cannot catch a wrong <c>StructLayout</c>, a mismarshalled
/// <c>SafeHandle</c> or a bad <c>[LibraryImport]</c> signature: those compile fine and
/// fail at runtime. Opening the current process needs no elevation, so these can run
/// anywhere while still proving the interop signatures are correct.
/// </para>
/// <para>
/// The launcher ships as x86 and <see cref="GameAddress"/> is 32-bit accordingly. When
/// the test host happens to be 64-bit, addresses would not fit, so the address-shaped
/// assertions are skipped rather than made to lie.
/// </para>
/// </remarks>
public sealed partial class LiveProcessTests
{
    private static bool Is32Bit => IntPtr.Size == 4;

    [Fact]
    public void OpensTheCurrentProcess()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        process.Id.ShouldBe((uint)Environment.ProcessId);
        process.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void OpeningAProcessThatDoesNotExistThrows() =>
        Should.Throw<GameProcessException>(() => RemoteProcess.Open(0xFFFF_FFF0));

    [Fact]
    public void AllocatesWritesAndReadsBack()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var buffer = process.AllocateScratch(256);

        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04];
        buffer.Write(payload);

        process.ReadBytes(buffer.Address, payload.Length).ShouldBe(payload);
    }

    [Fact]
    public void ReadsATypedValueBackFromRemoteMemory()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var buffer = process.AllocateScratch(64);

        process.Write(buffer.Address, 0x1234_5678u);

        process.Read<uint>(buffer.Address).ShouldBe(0x1234_5678u);
    }

    [Fact]
    public void ReadingUnmappedMemoryFailsSoftlyWhenAsked()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        // Address zero is never mapped; a polling loop must be able to survive this.
        process.TryRead<uint>(GameAddress.Zero, out _).ShouldBeFalse();
    }

    [Fact]
    public void ReadingUnmappedMemoryThrowsOnTheStrictPath()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        Should.Throw<GameProcessException>(() => process.ReadBytes(GameAddress.Zero, 4));
    }

    [Fact]
    public void FindsALoadedModule()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        var found = process.FindModule("kernel32.dll");

        found.ShouldNotBeNull();
        found.Value.Value.ShouldBe((uint)GetModuleHandle("kernel32.dll"));
    }

    [Fact]
    public void DoesNotFindAModuleThatIsNotLoaded()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        process.FindModule("definitely-not-loaded-38.dll").ShouldBeNull();
    }

    /// <summary>
    /// The important one: walking the PE export table in the target must produce the
    /// same answer the loader would. This is what <see cref="DllInjector"/> depends on.
    /// </summary>
    [Fact]
    public void ResolvesAnExportByWalkingThePeHeadersInTheTarget()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        var kernel32 = process.FindModule("kernel32.dll").ShouldNotBeNull();

        var resolved = process.FindExport(kernel32, "LoadLibraryW").ShouldNotBeNull();

        resolved.Value.ShouldBe((uint)GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW"));
    }

    [Fact]
    public void ReturnsNullForAnExportThatDoesNotExist()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        var kernel32 = process.FindModule("kernel32.dll").ShouldNotBeNull();

        process.FindExport(kernel32, "ThisExportDoesNotExist38").ShouldBeNull();
    }

    [Fact]
    public void ScanFindsAPatternWrittenIntoRemoteMemory()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var buffer = process.AllocateScratch(0x1000);

        var payload = new byte[0x1000];
        payload[0x800] = 0xDE;
        payload[0x801] = 0xAD;
        payload[0x802] = 0x99;
        payload[0x803] = 0xBE;
        payload[0x804] = 0xEF;
        buffer.Write(payload);

        var hit = process.Scan(
            BytePattern.Parse("DE AD ?? BE EF"), buffer.Address, buffer.Address + 0x1000);

        hit.ShouldBe(buffer.Address + 0x800);
    }

    [Fact]
    public void ScanSkipsUnreadableRegionsInsteadOfFailing()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        // The first 64 KB of every process is a permanently unmapped guard region.
        var hit = process.Scan(
            BytePattern.Parse("DE AD BE EF"), GameAddress.Zero, new GameAddress(0x1_0000));

        hit.ShouldBeNull();
    }

    /// <summary>
    /// Launches a throwaway process suspended and suspends it again through the scope.
    /// </summary>
    /// <remarks>
    /// Never point <see cref="RemoteProcess.SuspendThreads"/> at the current process:
    /// it suspends the calling thread too, and the resume never runs. A child created
    /// with <c>CREATE_SUSPENDED</c> is the safe subject — it has exactly one thread and
    /// has not executed an instruction.
    /// </remarks>
    [Fact]
    public void LaunchesSuspendedAndSuspendsItsThreads()
    {
        var target = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(target))
        {
            return;
        }

        using var launched = GameProcessLauncher.Launch(
            target, Environment.SystemDirectory, arguments: null, suspended: true);

        try
        {
            launched.IsSuspended.ShouldBeTrue();
            launched.Id.ShouldBeGreaterThan(0u);
            launched.Process.IsRunning.ShouldBeTrue();

            using (var scope = launched.Process.SuspendThreads())
            {
                scope.Count.ShouldBeGreaterThan(0);
            }
        }
        finally
        {
            using var handle = System.Diagnostics.Process.GetProcessById((int)launched.Id);
            handle.Kill();
        }
    }

    [Fact]
    public void LaunchingAMissingExecutableThrows() =>
        Should.Throw<GameProcessException>(() => GameProcessLauncher.Launch(
            Path.Combine(Path.GetTempPath(), "definitely-missing-38.exe"), Path.GetTempPath()));

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string moduleName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true,
        StringMarshalling = StringMarshalling.Custom,
        StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    private static partial nint GetProcAddress(nint module, string procName);
}
