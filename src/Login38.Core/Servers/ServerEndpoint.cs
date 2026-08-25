using System.Net;
using System.Net.Sockets;

namespace Login38.Core.Servers;

/// <summary>A server list entry that does not name a reachable address.</summary>
public sealed class ServerAddressException : Exception
{
    public ServerAddressException(string message) : base(message)
    {
    }

    public ServerAddressException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ServerAddressException()
    {
    }
}

/// <summary>Turns a server list entry into an address to connect to.</summary>
public static class ServerEndpoint
{
    /// <summary>
    /// Resolves a server's address and port.
    /// </summary>
    /// <remarks>
    /// A name is resolved here rather than left to whoever connects. The winsock redirect
    /// writes a literal address into the client's own code, so there is nowhere in that path
    /// for a lookup to happen — and doing one inside a suspended process is not an option.
    /// </remarks>
    /// <exception cref="ServerAddressException">
    /// The name does not resolve to IPv4, or the port is not a port.
    /// </exception>
    public static IPEndPoint Resolve(ServerInfo server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (server.Port is <= 0 or > ushort.MaxValue)
        {
            throw new ServerAddressException($"Port {server.Port} is out of range.");
        }

        if (IPAddress.TryParse(server.IpAddress, out var address))
        {
            return new IPEndPoint(address, server.Port);
        }

        IPAddress[] resolved;

        try
        {
            resolved = Dns.GetHostAddresses(server.IpAddress);
        }
        catch (SocketException e)
        {
            throw new ServerAddressException($"'{server.IpAddress}' could not be resolved.", e);
        }

        // The client only speaks IPv4, so an address family it cannot use is no answer.
        return Array.Find(resolved, a => a.AddressFamily == AddressFamily.InterNetwork) is { } ipv4
            ? new IPEndPoint(ipv4, server.Port)
            : throw new ServerAddressException(
                $"'{server.IpAddress}' has no IPv4 address; the client only speaks IPv4.");
    }
}
