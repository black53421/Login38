using System.Diagnostics;
using Login38.Interop;
using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Covers the hook that lets a key pressed in the game reach the launcher.
/// </summary>
/// <remarks>
/// <para>
/// Against this process, watching keys nothing sends: a low-level keyboard hook sees every
/// keystroke on the machine, so a test that watched for something ordinary would swallow it
/// out from under whoever is at the keyboard.
/// </para>
/// <para>
/// It cannot swallow anything here anyway — a key only counts while the watched process has
/// the window in front, and a test host has no windows at all.
/// </para>
/// </remarks>
public sealed class KeyboardHookTests
{
    /// <summary>F13 to F16, which are on almost no keyboard.</summary>
    private static readonly int[] Unlikely = [0x7C, 0x7D, 0x7E, 0x7F];

    [Fact]
    public void InstallsAndComesBack()
    {
        using var hook = KeyboardHook.Install(Unlikely, (uint)Environment.ProcessId);

        hook.ShouldNotBeNull();
    }

    [Fact]
    public void HasSeenNothingToBeginWith()
    {
        using var hook = KeyboardHook.Install(Unlikely, (uint)Environment.ProcessId);

        hook.Waiting.ShouldBe(0);
        hook.TryTake(out _).ShouldBeFalse();
    }

    // The thread sits in GetMessage, which returns when something is posted to it. Waiting
    // on that thread without posting first would wait for ever, so this is the one property
    // worth timing.
    [Fact]
    public void ComesOffPromptlyWhenAskedTo()
    {
        var hook = KeyboardHook.Install(Unlikely, (uint)Environment.ProcessId);
        var clock = Stopwatch.StartNew();

        hook.Dispose();

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    // The helper is scoped to one game, and a player running two clients gets two of these.
    [Fact]
    public void CanBeInstalledMoreThanOnce()
    {
        using var first = KeyboardHook.Install(Unlikely, (uint)Environment.ProcessId);
        using var second = KeyboardHook.Install(Unlikely, 0);

        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void DoesNotMindBeingDisposedTwice()
    {
        var hook = KeyboardHook.Install(Unlikely, (uint)Environment.ProcessId);

        hook.Dispose();

        Should.NotThrow(hook.Dispose);
    }
}
