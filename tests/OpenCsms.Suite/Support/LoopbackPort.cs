namespace OpenCsms.Suite.Support;

using System.Net;
using System.Net.Sockets;

/// <summary>
/// Reserves a loopback port for a component the suite must address before it starts. The product's
/// notification worker reads its target addresses once, when its host starts, so the fakes serving
/// those targets cannot use a dynamic port; the run reserves one at setup time and hands the same
/// number to the fake and the worker's configuration. The reservation is released immediately, so
/// the fake binds the port on first use; a collision would fail loudly with the fake's own message.
/// </summary>
public static class LoopbackPort
{
    /// <summary>Reserves and releases a free loopback port, returning the number.</summary>
    public static int Reserve()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
