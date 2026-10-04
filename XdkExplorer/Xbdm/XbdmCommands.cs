using System.Net;
using System.Text;
using static XdkExplorer.Xbdm.XboxDbgNative;

namespace XdkExplorer.Xbdm;

/// <summary>
/// Typed wrappers around the xboxdbg.dll calls. They act on the console currently selected
/// in the DLL, so only call them through <see cref="XbdmWorker"/>.
/// </summary>
public static unsafe class XbdmCommands
{
    /// <summary>The default console stored in the registry, shared with VS .NET 2003 and the XDK tools.</summary>
    public static string GetDefaultXboxName()
    {
        var sb = new StringBuilder(256);
        uint size = (uint)sb.Capacity;

        ThrowIfFailed(DmGetXboxName(sb, ref size), "DmGetXboxName");

        return sb.ToString();
    }

    public static IPAddress ResolveAddress()
    {
        ThrowIfFailed(DmResolveXboxName(out var address), "DmResolveXboxName");

        return ToIPAddress(address);
    }

    /// <summary>
    /// The name the console reports for itself, e.g. when it was added by IP.
    /// Uses the dbgname command over TCP instead of DmGetNameOfXbox, which asks via UDP and gets no answer
    /// from xemu when connected through 127.0.0.1.
    /// </summary>
    public static string GetConsoleName()
    {
        return SendCommand("dbgname") ?? "";
    }

    /// <summary>XBDM version such as 1.00.5849.1, or null if the console is older than 4831 and has no dmversion command.</summary>
    public static string? GetDebugMonitorVersion()
    {
        return SendCommand("dmversion");
    }

    /// <summary>Everything the overview page shows. Queries the console cannot answer come back as null.</summary>
    public static ConsoleDetails GetConsoleDetails()
    {
        return new ConsoleDetails
        {
            TitleAddress = GetTitleAddress(),
            RunningTitle = GetRunningTitle(),
            DebugMonitorVersion = GetDebugMonitorVersion(),
            SystemTime = GetSystemTime(),
            IsLocked = IsSecurityEnabled()
        };
    }

    public static IPAddress? GetTitleAddress()
    {
        if (!IsAnswered(DmGetAltAddress(out var address), "DmGetAltAddress"))
        {
            return null;
        }

        return ToIPAddress(address);
    }

    /// <summary>Path of the running .xbe, or null while the dashboard runs.</summary>
    public static string? GetRunningTitle()
    {
        // An empty name asks for the running title. The dashboard does not count and gives XBDM_NOSUCHFILE.
        if (!IsAnswered(DmGetXbeInfo("", out var info), "DmGetXbeInfo"))
        {
            return null;
        }

        return string.IsNullOrEmpty(info.LaunchPath) ? null : info.LaunchPath;
    }

    /// <summary>Console clock in UTC, or null if it is not set.</summary>
    public static DateTime? GetSystemTime()
    {
        if (!IsAnswered(DmGetSystemTime(out var time), "DmGetSystemTime"))
        {
            return null;
        }

        return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, time.Milliseconds, DateTimeKind.Utc);
    }

    /// <summary>The DLL has no wrapper for this, so the raw setsystime command is sent. It takes a FILETIME in UTC.</summary>
    public static void SetSystemTime(DateTime utc)
    {
        long fileTime = utc.ToFileTimeUtc();

        if (SendCommand($"setsystime clockhi=0x{(uint)(fileTime >> 32):x8} clocklo=0x{(uint)fileTime:x8}") == null)
        {
            throw new XbdmException(XBDM_INVALIDCMD, "setsystime");
        }
    }

    public static bool? IsSecurityEnabled()
    {
        if (!IsAnswered(DmIsSecurityEnabled(out var isEnabled), "DmIsSecurityEnabled"))
        {
            return null;
        }

        return isEnabled;
    }

    /// <summary>Writes the current screen of the console as .bmp to a local file.</summary>
    public static void SaveScreenshot(string bmpPath)
    {
        ThrowIfFailed(DmScreenShot(bmpPath), "DmScreenShot");
    }

    public static List<char> GetDrives()
    {
        var buffer = stackalloc byte[64];
        uint count = 64;

        ThrowIfFailed(DmGetDriveList(buffer, ref count), "DmGetDriveList");

        var drives = new List<char>();

        for (int i = 0; i < count; i++)
        {
            drives.Add((char)buffer[i]);
        }

        return drives;
    }

    public static (ulong Free, ulong Total) GetDiskSpace(char drive)
    {
        ThrowIfFailed(DmGetDiskFreeSpace($@"{drive}:\", out var free, out var total, out _), "DmGetDiskFreeSpace");

        return (free, total);
    }

    /// <summary>All drives with name and free space. Drives whose space cannot be read are reported with 0 bytes.</summary>
    public static List<XboxDrive> GetDriveDetails()
    {
        // getutildrvinfo only exists from XBDM 5558 on
        DM_UTILITY_DRIVE_INFO? utility = Succeeded(DmGetUtilityDriveInfo(out var info)) ? info : null;

        var drives = new List<XboxDrive>();

        foreach (var letter in GetDrives())
        {
            ulong free = 0;
            ulong total = 0;

            if (Succeeded(DmGetDiskFreeSpace($@"{letter}:\", out var freeToCaller, out var totalBytes, out _)))
            {
                free = freeToCaller;
                total = totalBytes;
            }

            drives.Add(new XboxDrive(letter, XboxDrive.GetName(letter, utility), free, total));
        }

        return drives;
    }

    public static List<XboxFileEntry> ListDirectory(string path)
    {
        var entries = new List<XboxFileEntry>();
        var walk = IntPtr.Zero;

        try
        {
            while (true)
            {
                var hr = DmWalkDir(ref walk, path, out var attributes);

                if (hr == XBDM_ENDOFLIST)
                {
                    break;
                }

                ThrowIfFailed(hr, $"DmWalkDir {path}");

                entries.Add(XboxFileEntry.FromNative(attributes));
            }
        }
        finally
        {
            if (walk != IntPtr.Zero)
            {
                DmCloseDir(walk);
            }
        }

        return entries;
    }

    /// <summary>Attributes of a file or directory, or null if it does not exist.</summary>
    public static XboxFileEntry? GetAttributes(string path)
    {
        var hr = DmGetFileAttributes(path, out var attributes);

        if (hr == XBDM_NOSUCHFILE)
        {
            return null;
        }

        ThrowIfFailed(hr, $"DmGetFileAttributes {path}");

        var entry = XboxFileEntry.FromNative(attributes);

        return new XboxFileEntry(XboxPath.GetName(path), entry.Size, entry.Attributes, entry.Created, entry.Modified);
    }

    public static void CreateDirectory(string path)
    {
        var hr = DmMkdir(path);

        if (hr != XBDM_ALREADYEXISTS)
        {
            ThrowIfFailed(hr, $"DmMkdir {path}");
        }
    }

    /// <summary>Directories must be empty. A path that no longer exists counts as deleted.</summary>
    public static void Delete(string path, bool isDirectory)
    {
        int hr = DmDeleteFile(path, isDirectory);

        if (hr != XBDM_NOSUCHFILE)
        {
            ThrowIfFailed(hr, $"DmDeleteFile {path}");
        }
    }

    public static void Rename(string oldPath, string newPath)
    {
        ThrowIfFailed(DmRenameFile(oldPath, newPath), $"DmRenameFile {oldPath}");
    }

    public static void Reboot(bool warm, bool waitForTitle)
    {
        uint flags = (warm ? DMBOOT_WARM : 0) | (waitForTitle ? DMBOOT_WAIT : 0);

        ThrowIfFailed(DmReboot(flags), "DmReboot");
    }

    /// <summary>Only has an effect in the 15 second window after a reboot with DMBOOT_WAIT.</summary>
    public static void SetTitleAndGo(string xbePath)
    {
        var directory = XboxPath.GetParent(xbePath) ?? xbePath;

        ThrowIfFailed(DmSetTitle(directory, XboxPath.GetName(xbePath), null), "DmSetTitle");
        ThrowIfFailed(DmGo(), "DmGo");
    }

    /// <summary>XBDM only removes empty directories, so the content goes first.</summary>
    public static void DeleteRecursive(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            List<XboxFileEntry> entries;

            try
            {
                entries = ListDirectory(path);
            }
            catch (XbdmException ex) when (ex.HResult == XBDM_NOSUCHFILE)
            {
                return;
            }

            foreach (var entry in entries)
            {
                DeleteRecursive(XboxPath.Combine(path, entry.Name), entry.IsDirectory);
            }
        }

        Delete(path, isDirectory);
    }

    /// <summary>All files below a directory as (full path, entry), plus every subdirectory path in creation order.</summary>
    public static (List<(string Path, XboxFileEntry Entry)> Files, List<string> Directories) ListRecursive(string directory)
    {
        var files = new List<(string, XboxFileEntry)>();
        var directories = new List<string>();
        var pending = new Queue<string>();

        pending.Enqueue(directory);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            directories.Add(current);

            foreach (var entry in ListDirectory(current))
            {
                var path = XboxPath.Combine(current, entry.Name);

                if (entry.IsDirectory)
                {
                    pending.Enqueue(path);
                }
                else
                {
                    files.Add((path, entry));
                }
            }
        }

        return (files, directories);
    }

    /// <summary>
    /// Sends a one-line command on its own connection and returns the text after the status code,
    /// e.g. "Helmo" for "200- Helmo". Returns null if this XBDM version does not know the command.
    /// </summary>
    private static string? SendCommand(string command)
    {
        ThrowIfFailed(DmOpenConnection(out var connection), "DmOpenConnection");

        try
        {
            int hr = XbdmProtocol.SendCommand(connection, command, out var response);

            if (hr == XBDM_INVALIDCMD)
            {
                return null;
            }

            ThrowIfFailed(hr, command.Split(' ')[0]);

            int separator = response.IndexOf("- ", StringComparison.Ordinal);

            return separator < 0 ? response.Trim() : response[(separator + 2)..].Trim();
        }
        finally
        {
            DmCloseConnection(connection);
        }
    }

    /// <summary>True if the query succeeded, false if the console has no answer for it. Lost connections still throw.</summary>
    private static bool IsAnswered(int hr, string operation)
    {
        if (Succeeded(hr))
        {
            return true;
        }

        if (hr == XBDM_CANNOTCONNECT || hr == XBDM_CONNECTIONLOST)
        {
            throw new XbdmException(hr, operation);
        }

        return false;
    }

    // The DLL returns addresses in host byte order
    private static IPAddress ToIPAddress(uint address)
    {
        return new IPAddress([(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]);
    }
}
