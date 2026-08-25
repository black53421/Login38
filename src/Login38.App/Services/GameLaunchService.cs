using System.IO;
using Login38.Core.Compatibility;
using Login38.Core.Configuration;
using Login38.Core.Net;
using Login38.Core.Servers;
using Login38.Interop;
using Login38.Patching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Login38.App.Services;

/// <summary>
/// Starts the game and arranges for it to be patched.
/// </summary>
/// <remarks>
/// <para>
/// The order here is not arbitrary. The display mode is written into the client's own
/// configuration file before it starts, because it reads that once at startup; the
/// compatibility flag likewise takes effect at the next launch. The instance slot is
/// claimed before either, so a refused launch changes nothing.
/// </para>
/// <para>
/// The client is created running rather than suspended. The reference had a suspended
/// path for installing a hook before the first instruction, and then disabled the hook —
/// leaving a suspended start that bought nothing and cost a resume.
/// </para>
/// </remarks>
public sealed class GameLaunchService
{
    /// <summary>The client executable, which the operator's package always ships as this.</summary>
    public const string GameExecutable = "TW13081901.bin";

    private readonly PatchPipeline _pipeline;
    private readonly LineageConfigFile _clientConfiguration;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameLaunchService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _slotPrefix;

    /// <param name="scopes">
    /// Where each game's helper features come from. One scope per game, because all of them
    /// hold state belonging to that client and the launcher can drive several at once.
    /// </param>
    /// <param name="slotPrefix">
    /// What the instance slots are named. Machine-wide by default, which is the point of
    /// them — but it also means anything else asking the same question shares the answer,
    /// so a test that wants its own count of running games says so rather than failing on
    /// a machine where somebody happens to have the game open.
    /// </param>
    public GameLaunchService(
        PatchPipeline pipeline,
        LineageConfigFile clientConfiguration,
        IServiceScopeFactory scopes,
        ILoggerFactory loggerFactory,
        string? slotPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _pipeline = pipeline;
        _clientConfiguration = clientConfiguration;
        _scopes = scopes;
        _loggerFactory = loggerFactory;
        _slotPrefix = slotPrefix ?? InstanceLimit.DefaultPrefix;
        _logger = loggerFactory.CreateLogger<GameLaunchService>();
    }

    /// <summary>
    /// Starts the game, or reports why it did not.
    /// </summary>
    /// <remarks>
    /// Returns as soon as the process exists. Patching runs on
    /// <see cref="GameSession.Patching"/> and takes seconds, because it has to wait for
    /// the client to unpack itself first.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The client is not in the given directory.</exception>
    public LaunchResult Launch(GameLaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = Path.Combine(request.GameDirectory, GameExecutable);

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException($"{request.GameDirectory} 裡找不到遊戲主程式。", executable);
        }

        var limit = InstanceLimit.EffectiveLimit(request.Aux.MultiInstance, request.Aux.MultiInstanceLimit);
        var slot = InstanceLimit.TryAcquire(limit, _slotPrefix);

        if (slot is null)
        {
            _logger.LogWarning("Refusing to launch: all {Limit} instances are already running", limit);
            return LaunchResult.Refused(limit);
        }

        PacketEncryptProxy? relay = null;
        AuxSession? aux = null;

        try
        {
            ApplyDisplayMode(request);

            // Started before the client, so the port it will be redirected to already
            // exists by the time the redirect is written.
            relay = StartPacketEncryptRelay(request, cancellationToken);

            var game = GameProcessLauncher.Launch(executable, request.GameDirectory);
            _logger.LogInformation("Game started as process {ProcessId}", game.Id);

            var context = new GamePatchContext(game.Process, request.GameDirectory, request.Aux, request.Server)
            {
                ConnectTarget = relay?.Endpoint,
            };

            aux = new AuxSession(_scopes);

            var session = new GameSession(
                game, slot, context, _pipeline, aux, _loggerFactory.CreateLogger<GameSession>(),
                relay, cancellationToken);

            return new LaunchResult(LaunchOutcome.Started, session);
        }
        catch
        {
            // The slot stands for a running game, and the relay for a game to relay for.
            // Nothing is running, so hand both back rather than making the player restart
            // the launcher to reclaim them.
            slot.Dispose();
            aux?.Dispose();

            if (relay is not null)
            {
                relay.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            throw;
        }
    }

    /// <summary>
    /// Starts the loopback relay, when the server wants the client's traffic encrypted.
    /// </summary>
    /// <remarks>
    /// A server with the setting on but no key pair cannot be connected to at all — the
    /// server would reject every packet the client sent in the clear — so this refuses the
    /// launch rather than starting a client that will be dropped at login.
    /// </remarks>
    /// <exception cref="InvalidDataException">The server has no key to encrypt with.</exception>
    private PacketEncryptProxy? StartPacketEncryptRelay(
        GameLaunchRequest request, CancellationToken cancellationToken)
    {
        if (!request.Aux.PacketEncrypt)
        {
            return null;
        }

        var server = request.Server;

        if (server.RsaD == 0 || server.RsaN == 0)
        {
            throw new InvalidDataException(
                $"已開啟封包加密，但「{server.Name}」沒有金鑰。" +
                "請在編碼器裡產生一組，再重新寫出伺服器列表。");
        }

        return PacketEncryptProxy.Start(
            ServerEndpoint.Resolve(server), server.RsaD, server.RsaN,
            _loggerFactory.CreateLogger<PacketEncryptProxy>(), cancellationToken);
    }

    /// <summary>
    /// Writes the display mode into the client's own configuration, and asks Windows to
    /// stop interfering with a fullscreen one.
    /// </summary>
    /// <remarks>
    /// Failures here are logged and ignored on purpose: a client that starts in the wrong
    /// window size is a worse experience than the player asked for, and a client that does
    /// not start at all is a worse one still.
    /// </remarks>
    private void ApplyDisplayMode(GameLaunchRequest request)
    {
        try
        {
            _clientConfiguration.ApplyDisplaySettings(
                request.GameDirectory, fullScreen: !request.Windowed, request.WindowMode);
        }
        catch (IOException e)
        {
            _logger.LogWarning(e, "Could not write the client's display configuration");
        }
        catch (UnauthorizedAccessException e)
        {
            _logger.LogWarning(e, "Could not write the client's display configuration");
        }

        if (request.Windowed)
        {
            // The flag only affects fullscreen presentation, so setting it for a windowed
            // launch would leave a compatibility entry behind that does nothing.
            return;
        }

        try
        {
            var executable = Path.Combine(request.GameDirectory, GameExecutable);

            if (AppCompatibilityFlags.Ensure(executable, AppCompatibilityFlags.DisableFullscreenOptimizations))
            {
                _logger.LogInformation("Disabled fullscreen optimisations for {Executable}", executable);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(e, "Could not set the compatibility flag");
        }
    }
}
