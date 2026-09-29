namespace OpenCsms.Suite.Support;

using System.Net;
using System.Net.Sockets;

/// <summary>Reserves a loopback port for a fake the worker must address before it starts.</summary>
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
