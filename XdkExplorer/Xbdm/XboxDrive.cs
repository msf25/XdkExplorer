namespace XdkExplorer.Xbdm;

public sealed class XboxDrive
{
    public XboxDrive(char letter, string name, ulong free, ulong total)
    {
        Letter = letter;
        Name = name;
        Free = free;
        Total = total;
    }

    public char Letter { get; }

    public string Name { get; }

    public ulong Free { get; }

    public ulong Total { get; }

    /// <summary>
    /// Volume names as shown by the XDK tools (strings from xbutilsui.dll and xbdir.exe).
    /// utility holds the title IDs of the three utility partitions, or null if XBDM is older than 5558.
    /// </summary>
    internal static string GetName(char letter, XboxDbgNative.DM_UTILITY_DRIVE_INFO? utility)
    {
        switch (letter)
        {
            case 'C': return "Main Volume";
            case 'E': return "Game Development Volume";
            case 'S': return "Persistent Data - All Titles";
            case 'T': return "Persistent Data - Active Title";
            case 'U': return "Saved Games - Active Title";
            case 'V': return "Saved Games - All Titles";
            case 'W': return "Persistent Data - Alternate Title";
            case 'X': return "Saved Games - Alternate Title";
            case 'Y': return "Xbox Dashboard Volume";
            case 'Z': return "Active Utility Drive";
            case 'P':
            case 'Q':
            case 'R': return GetUtilityName(letter, utility);
            default: return "Unknown Volume";
        }
    }

    // Partition 0, 1, 2 map to R, Q, P. Verified against xemu and the shell extension.
    private static string GetUtilityName(char letter, XboxDbgNative.DM_UTILITY_DRIVE_INFO? utility)
    {
        if (utility is not { } info)
        {
            return "Utility Drive";
        }

        int partition = 'R' - letter;
        uint titleId = partition switch
        {
            0 => info.TitleId0,
            1 => info.TitleId1,
            _ => info.TitleId2
        };

        bool isNeverUsed = (info.Flags & (XboxDbgNative.DM_UTILITY_DRIVE_0_NEVER_USED << partition)) != 0;

        return isNeverUsed ? "Utility Drive for Unknown Title" : $"Utility Drive for Title {titleId:X8}";
    }
}
