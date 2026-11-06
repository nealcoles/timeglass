using System.Net;
using System.Net.Sockets;

namespace Timeglass.Api;

/// <summary>Picks the port the API listens on.</summary>
public static class LoopbackPort
{
    /// <summary>
    /// <paramref name="preferred"/> if nothing listens on it on 127.0.0.1, otherwise a free port
    /// the OS hands out. Another process can still take the port before the server binds it;
    /// that is rare enough to be a startup error.
    /// </summary>
    public static int Choose(int preferred)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(preferred);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(preferred, IPEndPoint.MaxPort);
        return TryReserve(preferred) ?? TryReserve(0) ?? throw new IOException("No free port on 127.0.0.1.");
    }

    private static int? TryReserve(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);

        // Without this, Windows lets a second socket bind a port another one already listens on.
        listener.ExclusiveAddressUse = true;
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch (SocketException)
        {
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }
}
