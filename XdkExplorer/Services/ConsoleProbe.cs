using System.Net;
using System.Net.Sockets;

namespace XdkExplorer.Services;

/// <summary>
/// Checks whether XBDM accepts connections, without going through xboxdbg.dll.
/// Runs on the thread pool so periodic checks never queue up behind the XBDM worker.
/// </summary>
public static class ConsoleProbe
{
    public const int XbdmPort = 731;

    public static async Task<bool> IsReachableAsync(IPAddress address, int timeoutMs = 1500)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            await client.ConnectAsync(address, XbdmPort, cts.Token);

            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
