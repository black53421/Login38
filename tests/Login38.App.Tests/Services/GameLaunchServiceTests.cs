using System.IO;
using Login38.App.Services;
using Login38.Core.Configuration;
using Login38.Core.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.Services;

/// <summary>
/// The refusals the launcher makes before any client process exists.
/// </summary>
/// <remarks>
/// Only the paths that stop short of starting a game are exercised here. Everything past
/// that point needs a real client, and a test that started one would leave it running.
/// </remarks>
public sealed class GameLaunchServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"l38-launch-{Guid.NewGuid():N}");

    public GameLaunchServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary directory that outlives the run is not a failed test.
        }
    }

    /// <summary>
    /// A launcher whose instance slots belong to this test alone.
    /// </summary>
    /// <remarks>
    /// The real names are machine-wide, so a run on a machine with the game already open
    /// found every slot taken, refused the launch, and reported a missing refusal for a
    /// completely different reason.
    /// </remarks>
    private static GameLaunchService Build() => new(
        new Login38.Patching.PatchPipeline([], NullLogger<Login38.Patching.PatchPipeline>.Instance),
        new LineageConfigFile(NullLogger<LineageConfigFile>.Instance),
        new ServiceCollection().AddLogging().AddAuxFeatures().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLoggerFactory.Instance,
        $@"Local\L38TestSlot_{Guid.NewGuid():N}_");

    private GameLaunchRequest Request(AuxConfig aux, ServerInfo server) =>
        new(_directory, server, aux, Windowed: true, WindowMode.Size800x600);

    [Fact]
    public void SaysSoWhenTheClientIsNotThere()
    {
        var thrown = Should.Throw<FileNotFoundException>(() => Build().Launch(
            Request(new AuxConfig(), new ServerInfo { Name = "test", IpAddress = "127.0.0.1" })));

        thrown.Message.ShouldContain(_directory);
    }

    // A server with packet encryption on and no key pair drops every packet the client
    // sends, so the launch is refused rather than leaving a player at a login screen that
    // will never answer. The type matters as much as the refusal: the reference returned
    // this as a value the window had to handle, and turning it into an exception here made
    // it something the window could miss. It is caught, so it has to stay a kind the window
    // catches.
    [Fact]
    public void RefusesToLaunchAgainstAKeylessServerWhenEncryptionIsOn()
    {
        File.WriteAllBytes(Path.Combine(_directory, GameLaunchService.GameExecutable), [0x4D, 0x5A]);

        var thrown = Should.Throw<InvalidDataException>(() => Build().Launch(Request(
            new AuxConfig { PacketEncrypt = true },
            new ServerInfo { Name = "keyless", IpAddress = "127.0.0.1" })));

        thrown.Message.ShouldContain("keyless");
        thrown.Message.ShouldContain("沒有金鑰");
    }
}
