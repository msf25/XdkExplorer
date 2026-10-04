using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace XdkExplorer.Services;

public sealed class DiscoveredConsole
{
    public DiscoveredConsole(string name, string address)
    {
        Name = name;
        Address = address;
    }

    public string Name { get; }

    public string Address { get; }
}

/// <summary>
/// Finds consoles with the XBDM name answering protocol: a UDP broadcast of [0x03, 0x00] to port 731,
/// every console answers with [0x02, length, name]. Works without xboxdbg.dll and outside the worker.
/// </summary>
public static class ConsoleDiscovery
{
    private const int NapPort = 731;
    private const byte NapWildcardRequest = 0x03;
    private const byte NapNameReply = 0x02;

    public static async Task<List<DiscoveredConsole>> DiscoverAsync(TimeSpan wait)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.Supports(NetworkInterfaceComponent.IPv4))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .ToList();

        // One broadcast per interface, otherwise Windows only sends it out on one of them
        var perInterface = await Task.WhenAll(interfaces.Select(a => Task.Run(() => DiscoverOn(a, wait))));

        return perInterface
            .SelectMany(found => found)
            .GroupBy(c => c.Address)
            .Select(g => g.First())
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DiscoveredConsole> DiscoverOn(IPAddress localAddress, TimeSpan wait)
    {
        var found = new List<DiscoveredConsole>();
        var buffer = new byte[512];

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            socket.EnableBroadcast = true;
            socket.ReceiveTimeout = 200;
            socket.Bind(new IPEndPoint(localAddress, 0));
            socket.SendTo([NapWildcardRequest, 0x00], new IPEndPoint(IPAddress.Broadcast, NapPort));

            var elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < wait)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int received;

                try
                {
                    received = socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException)
                {
                    // Receive timeout, keep waiting until the deadline
                    continue;
                }

                if (received < 2 || buffer[0] != NapNameReply || buffer[1] + 2 != received)
                {
                    continue;
                }

                int nameLength = buffer[1];

                if (nameLength > 0)
                {
                    found.Add(new DiscoveredConsole(Encoding.ASCII.GetString(buffer, 2, nameLength), ((IPEndPoint)remote).Address.ToString()));
                }
            }
        }
        catch (SocketException)
        {
            // Interface without broadcast support or already gone
        }

        return found;
    }
}
