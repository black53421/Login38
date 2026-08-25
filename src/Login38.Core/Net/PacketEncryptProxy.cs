using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Login38.Core.Net;

/// <summary>
/// A loopback relay that encrypts the client's outgoing traffic.
/// </summary>
/// <remarks>
/// <para>
/// Servers with packet encryption on expect every byte from the client XORed against a key
/// they issue at connect time. The client knows nothing about that, and teaching it would
/// mean patching its socket code — so instead it is pointed at this, which sits between the
/// two and does the work.
/// </para>
/// <para>
/// The relay listens on loopback, on whatever port the operating system hands out, and the
/// winsock redirect sends the client there instead of to the server. Traffic in the other
/// direction is copied untouched: the encryption is one-way.
/// </para>
/// <para>
/// The first four bytes from the server are the challenge the key comes from. They are
/// consumed here and never forwarded — the client's protocol does not begin until after
/// them.
/// </para>
/// </remarks>
public sealed class PacketEncryptProxy : IAsyncDisposable
{
    private const int BufferSize = 8192;

    private readonly TcpListener _listener;
    private readonly IPEndPoint _upstream;
    private readonly uint _privateExponent;
    private readonly uint _modulus;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellation;

    private bool _disposed;

    private PacketEncryptProxy(
        TcpListener listener,
        IPEndPoint upstream,
        uint privateExponent,
        uint modulus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        _listener = listener;
        _upstream = upstream;
        _privateExponent = privateExponent;
        _modulus = modulus;
        _logger = logger;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Endpoint = (IPEndPoint)listener.LocalEndpoint;
        Completion = Task.Run(() => AcceptAsync(_cancellation.Token), CancellationToken.None);
    }

    /// <summary>Where the client should be told to connect.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>Completes when the relay has stopped accepting.</summary>
    public Task Completion { get; }

    /// <summary>Starts relaying to <paramref name="upstream"/>.</summary>
    /// <param name="upstream">The real server.</param>
    /// <param name="privateExponent">The server list's <c>rsa_d</c>.</param>
    /// <param name="modulus">The server list's <c>rsa_n</c>.</param>
    /// <param name="logger">Where connection outcomes are reported.</param>
    /// <param name="cancellationToken">Stops the relay when the launch ends.</param>
    /// <exception cref="ArgumentOutOfRangeException">The server list carries no key.</exception>
    public static PacketEncryptProxy Start(
        IPEndPoint upstream,
        uint privateExponent,
        uint modulus,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(logger);

        // Checked here rather than at the first connection: a server list built before the
        // operator generated a key pair would otherwise look fine until a player tried to
        // log in, and then fail somewhere with no obvious cause.
        ArgumentOutOfRangeException.ThrowIfZero(privateExponent);
        ArgumentOutOfRangeException.ThrowIfZero(modulus);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var proxy = new PacketEncryptProxy(
            listener, upstream, privateExponent, modulus, logger, cancellationToken);

        logger.LogInformation(
            "Packet encryption relay listening on {Endpoint}, forwarding to {Upstream}",
            proxy.Endpoint, upstream);

        return proxy;
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

                // Not awaited: the client can open a second connection while the first is
                // still running, and one that stalls must not stop the next being accepted.
                _ = Task.Run(() => RelayAsync(client, cancellationToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // The launch has ended.
        }
        catch (SocketException e)
        {
            _logger.LogWarning(e, "The packet encryption relay stopped accepting connections");
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task RelayAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var game = client;
        using var server = new TcpClient();

        try
        {
            await server.ConnectAsync(_upstream, cancellationToken).ConfigureAwait(false);

            await using var toGame = game.GetStream();
            await using var toServer = server.GetStream();

            var key = await ReadKeyAsync(toServer, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Relaying a connection to {Upstream} with key {Key:X2}", _upstream, key);

            // Both directions run at once and each closes its own half when it runs dry, so
            // the side that has nothing left to say does not hold the other open.
            await Task.WhenAll(
                Pump(toGame, toServer, key, server.Client, cancellationToken),
                Pump(toServer, toGame, key: 0, game.Client, cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The launch has ended.
        }
        catch (Exception e) when (e is IOException or SocketException or EndOfStreamException)
        {
            // The ordinary end of a session: the player quit, or the server dropped them.
            _logger.LogDebug(e, "A relayed connection to {Upstream} ended", _upstream);
        }
    }

    /// <summary>Reads the server's opening challenge and turns it into the stream key.</summary>
    private async Task<byte> ReadKeyAsync(NetworkStream server, CancellationToken cancellationToken)
    {
        var challenge = new byte[PacketEncryptKey.ChallengeLength];

        await server.ReadExactlyAsync(challenge, cancellationToken).ConfigureAwait(false);

        return PacketEncryptKey.FromChallenge(
            BitConverter.ToUInt32(challenge), _privateExponent, _modulus);
    }

    /// <summary>
    /// Copies one direction, XORing on the way when there is a key.
    /// </summary>
    /// <param name="key">The stream key, or zero to copy untouched.</param>
    /// <param name="destination">
    /// The socket the copy writes to, whose sending half is closed once there is nothing
    /// left to send through it.
    /// </param>
    private static async Task Pump(
        NetworkStream from, NetworkStream to, byte key, Socket destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];

        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                if (key != 0)
                {
                    for (var i = 0; i < read; i++)
                    {
                        buffer[i] ^= key;
                    }
                }

                await to.WriteAsync(buffer.AsMemory(..read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Half-close, so the far end sees the end of the stream rather than waiting for
            // a message that is never coming.
            try
            {
                destination.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException)
            {
                // Already gone.
            }
            catch (ObjectDisposedException)
            {
                // Already gone.
            }
        }
    }

    /// <remarks>
    /// Safe to call twice. The game exiting stops the relay, and so does the session being
    /// disposed; which happens first depends on how the player closed things.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _cancellation.CancelAsync().ConfigureAwait(false);

        // Guarded internally, so this never throws.
        await Completion.ConfigureAwait(false);

        _cancellation.Dispose();
        _listener.Dispose();
    }
}
