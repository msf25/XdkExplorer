using System.Net;

namespace XdkExplorer.Xbdm;

/// <summary>What the overview page shows about a console. Null means the console cannot tell.</summary>
public sealed class ConsoleDetails
{
    /// <summary>Second IP that titles use on the network, see DmGetAltAddress.</summary>
    public IPAddress? TitleAddress { get; init; }

    /// <summary>Path of the running .xbe, null while the dashboard (XDK Launcher) runs.</summary>
    public string? RunningTitle { get; init; }

    /// <summary>XBDM version such as 1.00.5849.1, null on XBDM older than 4831.</summary>
    public string? DebugMonitorVersion { get; init; }

    /// <summary>Console clock in UTC, null if it is not set.</summary>
    public DateTime? SystemTime { get; init; }

    public bool? IsLocked { get; init; }
}
