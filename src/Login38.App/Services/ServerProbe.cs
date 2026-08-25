using System.Net.Sockets;
using Login38.Core.Servers;

namespace Login38.App.Services;

/// <summary>Checks whether a server is accepting connections.</summary>
public interface IServerProbe
{
    /// <summary>Whether the server answered.</summary>
    Task<bool> IsReachableAsync(ServerInfo server, CancellationToken cancellationToken = default);
}

/// <summary>
/// Checks a server by opening a TCP connection to it.
/// </summary>
/// <remarks>
/// <para>
/// A completed handshake is the whole test. The login protocol would tell us more, but
/// speaking it means sending credentials nobody has typed yet — and the question the
/// player is asking is only "is it up".
/// </para>
/// <para>
/// The connection is closed immediately. Servers log connections, and a launcher that
/// held one open per server per refresh would show up as a client that never logs in.
/// </para>
/// </remarks>
public sealed class TcpServerProbe : IServerProbe
{
    /// <summary>
    /// Short on purpose: this runs behind a list the player is already looking at, and a
    /// server that takes longer than this to answer is not one they want.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _timeout;

    public TcpServerProbe()
        : this(DefaultTimeout)
    {
    }

    public TcpServerProbe(TimeSpan timeout) => _timeout = timeout;

    /// <inheritdoc/>
    public async Task<bool> IsReachableAsync(ServerInfo server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var client = new TcpClient();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(server.IpAddress, server.Port, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            // Refused, unreachable, or a name that does not resolve. All of them mean the
            // same thing to the player.
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout, not the caller. A server too slow to answer counts as down.
            return false;
        }
    }
}
