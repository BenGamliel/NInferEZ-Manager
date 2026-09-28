using System.Net;
using System.Net.Sockets;

namespace NInferManager.Backend;

public static class PortService
{
    public static bool IsAvailable(int port)
    {
        if (port is < 1 or > 65535) return false;
        TcpListener? listener = null;
        try { listener = new(IPAddress.Loopback, port); listener.Start(); return true; }
        catch (SocketException) { return false; }
        finally { listener?.Stop(); }
    }
    public static int FindAvailable(params int[] excluded)
    {
        var blocked = excluded.ToHashSet();
        for (var port = 49152; port <= 65535; port++) if (!blocked.Contains(port) && IsAvailable(port)) return port;
        throw new InvalidOperationException("No free local API port was found.");
    }
}
