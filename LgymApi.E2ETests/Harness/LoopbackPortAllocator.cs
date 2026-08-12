using System.Net;
using System.Net.Sockets;

namespace LgymApi.E2ETests.Harness;

internal interface ILoopbackPortAllocator
{
    int Allocate();
}

internal sealed class LoopbackPortAllocator : ILoopbackPortAllocator
{
    public int Allocate()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
