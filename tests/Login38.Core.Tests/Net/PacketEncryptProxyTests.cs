using System.Net;
using System.Net.Sockets;
using Login38.Core.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Core.Tests.Net;

/// <summary>
/// Covers the key derivation and the relay itself, over real loopback sockets.
/// </summary>
/// <remarks>
/// The relay is a socket program, so it is tested as one: a stand-in server on a loopback
/// port, a stand-in client, and assertions on the bytes that arrive. What a mocked stream
/// would not catch is the half-close handshake, which is the part that decides whether a
/// player's client hangs on logout.
/// </remarks>
public sealed class PacketEncryptProxyTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // The formula the client itself uses, so the two have to agree exactly or every packet
    // after the first is rejected.
    [Theory]
    [InlineData(42u, 1u, 1000u, 43)]
    [InlineData(254u, 1u, 1000u, 255)]
    [InlineData(255u, 1u, 1000u, 1)]
    public void DerivesTheKeyTheServerExpects(
        uint challenge, uint privateExponent, uint modulus, byte expected) =>
        PacketEncryptKey.FromChallenge(challenge, privateExponent, modulus).ShouldBe(expected);

    // A zero key would leave the traffic in the clear while both ends believed otherwise.
    [Theory]
    [InlineData(0u)]
    [InlineData(255u)]
    [InlineData(510u)]
    [InlineData(uint.MaxValue)]
    public void NeverDerivesAZeroKey(uint challenge) =>
        PacketEncryptKey.FromChallenge(challenge, 1, 0xFFFF_FFFF).ShouldNotBe((byte)0);

    [Theory]
    [InlineData(2u, 10u, 1000u, 24u)]     // 1024 mod 1000
    [InlineData(1234u, 0u, 5u, 1u)]       // anything to the zeroth
    [InlineData(0u, 5u, 7u, 0u)]
    public void RaisesToAPowerModulo(uint value, uint exponent, uint modulus, uint expected) =>
        PacketEncryptKey.ModPow(value, exponent, modulus).ShouldBe(expected);

    // The largest values that fit the field, to pin that the intermediate products stay
    // inside 64 bits and do not need big-integer arithmetic.
    [Fact]
    public void SurvivesTheLargestKeyTheFormatCanHold() =>
        Should.NotThrow(() => PacketEncryptKey.ModPow(uint.MaxValue - 1, uint.MaxValue, uint.MaxValue));

    // A server list with the setting on and no key in it. Zero would make every packet XOR
    // against 1, which the server would reject — better to say so.
    [Theory]
    [InlineData(0u, 1000u)]
    [InlineData(17u, 0u)]
    public void RefusesToStartWithoutAKey(uint privateExponent, uint modulus) =>
        Should.Throw<ArgumentOutOfRangeException>(() => PacketEncryptProxy.Start(
            new IPEndPoint(IPAddress.Loopback, 1), privateExponent, modulus, NullLogger.Instance));

    [Fact]
    public async Task ListensOnLoopbackOnly()
    {
        using var server = new StandInServer();
        await using var relay = Start(server);

        relay.Endpoint.Address.ShouldBe(IPAddress.Loopback);
        relay.Endpoint.Port.ShouldBeGreaterThan(0);
    }

    // The whole point: what leaves the client reaches the server XORed against the key the
    // server's own challenge implied.
    [Fact]
    public async Task EncryptsWhatTheClientSends()
    {
        using var server = new StandInServer();
        await using var relay = Start(server);

        using var client = await ConnectAsync(relay);
        await client.GetStream().WriteAsync(new byte[] { 1, 2, 3 });

        (await server.ReadAsync(3)).ShouldBe([1 ^ 43, 2 ^ 43, 3 ^ 43]);
    }

    // One-way. The client decrypts nothing, so anything the relay altered on the way back
    // would arrive as noise.
    [Fact]
    public async Task LeavesWhatTheServerSendsAlone()
    {
        using var server = new StandInServer();
        await using var relay = Start(server);

        using var client = await ConnectAsync(relay);
        await server.SendAsync([0x11, 0x22, 0x33]);

        (await ReadAsync(client, 3)).ShouldBe([0x11, 0x22, 0x33]);
    }

    // The challenge is the relay's business. The client's protocol starts after it, and
    // forwarding it would put four bytes of noise at the front of every session.
    [Fact]
    public async Task KeepsTheChallengeToItself()
    {
        using var server = new StandInServer();
        await using var relay = Start(server);

        using var client = await ConnectAsync(relay);
        await server.SendAsync([0xAA]);

        (await ReadAsync(client, 1)).ShouldBe([0xAA]);
    }

    // A client that closes its end has to reach the server as a closed end, or the server
    // holds the session open until it times out the player.
    [Fact]
    public async Task PassesOnTheClientClosingItsEnd()
    {
        using var server = new StandInServer();
        await using var relay = Start(server);

        using var client = await ConnectAsync(relay);
        client.Client.Shutdown(SocketShutdown.Send);

        (await server.ReadToEndAsync()).ShouldBeEmpty();
    }

    // And the other way, which is what a player sees as the client hanging on logout.
    [Fact]
    public async Task PassesOnTheServerClosingItsEnd()
    {
        using var server = new StandInServer();
        await using var relay = Start(server);

        using var client = await ConnectAsync(relay);
        await server.CloseSendAsync();

        (await ReadToEndAsync(client)).ShouldBeEmpty();
    }

    // More than one connection at a time: the client opens a second before closing the
    // first when it moves between the login and the game server.
    [Fact]
    public async Task RelaysMoreThanOneConnection()
    {
        using var first = new StandInServer();
        await using var relay = Start(first);

        using var clientOne = await ConnectAsync(relay);
        await clientOne.GetStream().WriteAsync(new byte[] { 1 });
        (await first.ReadAsync(1)).ShouldBe([1 ^ 43]);

        using var clientTwo = await ConnectAsync(relay);
        await clientTwo.GetStream().WriteAsync(new byte[] { 2 });
        (await first.ReadAsync(1, connection: 1)).ShouldBe([2 ^ 43]);
    }

    // The launcher outlives the game, so a relay that kept its port would accumulate one
    // per launch.
    [Fact]
    public async Task StopsListeningOnceDisposed()
    {
        using var server = new StandInServer();
        var relay = Start(server);
        var endpoint = relay.Endpoint;

        await relay.DisposeAsync();

        using var client = new TcpClient();
        await Should.ThrowAsync<SocketException>(() => client.ConnectAsync(endpoint));
    }

    // The game exiting stops it, and so does the session being disposed. Which happens
    // first depends on how the player closed things.
    [Fact]
    public async Task ToleratesBeingDisposedTwice()
    {
        using var server = new StandInServer();
        var relay = Start(server);

        await relay.DisposeAsync();
        await Should.NotThrowAsync(async () => await relay.DisposeAsync());
    }

    private static PacketEncryptProxy Start(StandInServer server) =>
        PacketEncryptProxy.Start(server.Endpoint, privateExponent: 1, modulus: 1000, NullLogger.Instance);

    private static async Task<TcpClient> ConnectAsync(PacketEncryptProxy relay)
    {
        var client = new TcpClient();
        await client.ConnectAsync(relay.Endpoint);
        return client;
    }

    private static async Task<byte[]> ReadAsync(TcpClient client, int count)
    {
        var buffer = new byte[count];
        using var timeout = new CancellationTokenSource(Patience);

        await client.GetStream().ReadExactlyAsync(buffer, timeout.Token);
        return buffer;
    }

    private static async Task<byte[]> ReadToEndAsync(TcpClient client)
    {
        using var timeout = new CancellationTokenSource(Patience);
        using var received = new MemoryStream();

        await client.GetStream().CopyToAsync(received, timeout.Token);
        return received.ToArray();
    }

    /// <summary>
    /// A server that sends the challenge and then does whatever the test asks.
    /// </summary>
    /// <remarks>
    /// The challenge is 42 and the key is derived with an exponent of one, so every test
    /// here expects a key of 43.
    /// </remarks>
    private sealed class StandInServer : IDisposable
    {
        private const uint Challenge = 42;

        private readonly TcpListener _listener;
        private readonly List<TcpClient> _accepted = [];
        private readonly SemaphoreSlim _hasConnection = new(0);
        private readonly Task _accepting;

        public StandInServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Endpoint = (IPEndPoint)_listener.LocalEndpoint;
            _accepting = Task.Run(AcceptAsync);
        }

        public IPEndPoint Endpoint { get; }

        public async Task<byte[]> ReadAsync(int count, int connection = 0)
        {
            var stream = (await ConnectionAsync(connection)).GetStream();
            var buffer = new byte[count];
            using var timeout = new CancellationTokenSource(Patience);

            await stream.ReadExactlyAsync(buffer, timeout.Token);
            return buffer;
        }

        public async Task<byte[]> ReadToEndAsync(int connection = 0)
        {
            using var timeout = new CancellationTokenSource(Patience);
            using var received = new MemoryStream();

            await (await ConnectionAsync(connection)).GetStream().CopyToAsync(received, timeout.Token);
            return received.ToArray();
        }

        public async Task SendAsync(byte[] payload, int connection = 0) =>
            await (await ConnectionAsync(connection)).GetStream().WriteAsync(payload);

        public async Task CloseSendAsync(int connection = 0) =>
            (await ConnectionAsync(connection)).Client.Shutdown(SocketShutdown.Send);

        private async Task<TcpClient> ConnectionAsync(int index)
        {
            while (true)
            {
                lock (_accepted)
                {
                    if (index < _accepted.Count)
                    {
                        return _accepted[index];
                    }
                }

                if (!await _hasConnection.WaitAsync(Patience))
                {
                    throw new TimeoutException($"The relay never opened connection {index}.");
                }
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();

                    // Sent unprompted, the way a server with encryption on opens.
                    await client.GetStream().WriteAsync(BitConverter.GetBytes(Challenge));

                    lock (_accepted)
                    {
                        _accepted.Add(client);
                    }

                    _hasConnection.Release();
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or IOException)
            {
                // Shut down.
            }
        }

        public void Dispose()
        {
            _listener.Stop();

            lock (_accepted)
            {
                _accepted.ForEach(client => client.Dispose());
            }

            _accepting.Wait(Patience);
            _listener.Dispose();
            _hasConnection.Dispose();
        }
    }
}
