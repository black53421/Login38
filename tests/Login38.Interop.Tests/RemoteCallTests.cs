using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Covers running a short piece of code inside a process and waiting for it.
/// </summary>
/// <remarks>
/// Against this process, which is the only 32-bit process available to a test. The code
/// run here touches nothing but its own registers.
/// </remarks>
public sealed class RemoteCallTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    // Whatever the code leaves in eax is the thread's exit code, which is how a remote
    // call gets an answer back.
    [Fact]
    public void GivesBackWhatTheCodeReturned()
    {
        // mov eax, 0x00C0FFEE; ret
        byte[] code = [0xB8, 0xEE, 0xFF, 0xC0, 0x00, 0xC3];

        RemoteCall.Run(_process, code, TimeSpan.FromSeconds(5)).ShouldBe(0x00C0FFEEu);
    }

    [Fact]
    public void RunsCodeThatReturnsNothing()
    {
        // xor eax, eax; ret
        byte[] code = [0x31, 0xC0, 0xC3];

        RemoteCall.Run(_process, code, TimeSpan.FromSeconds(5)).ShouldBe(0u);
    }

    /// <summary>
    /// Code that takes noticeably longer than any timeout a test would set, and then stops
    /// on its own.
    /// </summary>
    /// <remarks>
    /// <c>loop</c> is microcoded and slow — several hundred million of them is a fraction
    /// of a second on any machine that can run this, and far more than the timeout below.
    /// It has to finish by itself: the page it is running from is deliberately left mapped,
    /// so a thread that never returned would spin for as long as the test host lives.
    /// </remarks>
    private static byte[] SlowCode => [0xB9, 0x00, 0x00, 0x00, 0x20, 0xE2, 0xFE, 0x31, 0xC0, 0xC3];

    [Fact]
    public void SaysSoWhenTheCodeDoesNotReturnInTime()
    {
        var e = Should.Throw<GameProcessException>(
            () => RemoteCall.Run(_process, SlowCode, TimeSpan.FromMilliseconds(50)));

        e.Message.ShouldContain("did not return");
    }

    // Freeing the page while a thread is still executing from it unmaps the code out from
    // under it, which is a crash of the game rather than a failure of the launcher. The
    // page is deliberately kept, and the message says so.
    [Fact]
    public void KeepsThePageMappedWhenTheCodeIsStillRunning()
    {
        var e = Should.Throw<GameProcessException>(
            () => RemoteCall.Run(_process, SlowCode, TimeSpan.FromMilliseconds(50)));

        e.Message.ShouldContain("left mapped");
    }

    [Fact]
    public void RefusesToRunNothing() =>
        Should.Throw<ArgumentException>(() => RemoteCall.Run(_process, [], TimeSpan.FromSeconds(1)));

    [Fact]
    public void RefusesAProcessThatIsNotThere() =>
        Should.Throw<ArgumentNullException>(() => RemoteCall.Run(null!, [0xC3], TimeSpan.FromSeconds(1)));
}
