using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace XdkExplorer.Xbdm;

/// <summary>
/// P/Invoke surface of xboxdbg.dll (32-bit, stdcall, ANSI). Signatures follow XboxDbg.h from XDK 5849.
/// The DLL keeps the target console in global state, so all calls must go through <see cref="XbdmWorker"/>.
/// </summary>
internal static unsafe class XboxDbgNative
{
    public const string DllName = "xboxdbg.dll";

    // HRESULTs, FACILITY_XBDM = 0x2db
    public const int XBDM_NOERR = 0x02DB0000;
    public const int XBDM_CONNECTED = 0x02DB0001;
    public const int XBDM_MULTIRESPONSE = 0x02DB0002;
    public const int XBDM_BINRESPONSE = 0x02DB0003;
    public const int XBDM_READYFORBIN = 0x02DB0004;
    public const int XBDM_NOSUCHFILE = unchecked((int)0x82DB0002);
    public const int XBDM_CLOCKNOTSET = unchecked((int)0x82DB0006);
    public const int XBDM_INVALIDCMD = unchecked((int)0x82DB0007);
    public const int XBDM_ALREADYEXISTS = unchecked((int)0x82DB000A);
    public const int XBDM_DIRNOTEMPTY = unchecked((int)0x82DB000B);
    public const int XBDM_CANNOTCONNECT = unchecked((int)0x82DB0100);
    public const int XBDM_CONNECTIONLOST = unchecked((int)0x82DB0101);
    public const int XBDM_ENDOFLIST = unchecked((int)0x82DB0104);

    // DmReboot flags
    public const uint DMBOOT_WAIT = 1;
    public const uint DMBOOT_WARM = 2;
    public const uint DMBOOT_NODEBUG = 4;
    public const uint DMBOOT_STOP = 8;

    public static bool Succeeded(int hr) => hr >= 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DM_FILE_ATTRIBUTES
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Name;
        public long CreationTime;
        public long ChangeTime;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Attributes;
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct DM_UTILITY_DRIVE_INFO
    {
        public uint Flags;
        public uint TitleId0;
        public uint TitleId1;
        public uint TitleId2;
    }

    // DM_UTILITY_DRIVE_INFO flags, one bit per utility partition
    public const uint DM_UTILITY_DRIVE_0_NEVER_USED = 0x01;
    public const uint DM_UTILITY_DRIVE_0_NOT_USED = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DM_XBE
    {
        // MAX_PATH + 1
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 261)]
        public string LaunchPath;
        public uint TimeStamp;
        public uint CheckSum;
        public uint StackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    // Naming and addressing
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmGetXboxName(StringBuilder name, ref uint size);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmSetXboxName(string name);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmSetXboxNameNoRegister(string name);
    [DllImport(DllName)] public static extern int DmResolveXboxName(out uint address);
    [DllImport(DllName)] public static extern int DmGetAltAddress(out uint address);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmGetNameOfXbox(StringBuilder name, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool resolvable);
    [DllImport(DllName)] public static extern int DmSetConnectionTimeout(uint connectTimeout, uint conversationTimeout);
    [DllImport(DllName)] public static extern int DmUseSharedConnection([MarshalAs(UnmanagedType.Bool)] bool share);


    // Socket level
    [DllImport(DllName)] public static extern int DmOpenConnection(out IntPtr connection);
    [DllImport(DllName)] public static extern int DmCloseConnection(IntPtr connection);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmSendCommand(IntPtr connection, string command, byte* response, ref uint responseSize);
    [DllImport(DllName)] public static extern int DmReceiveStatusResponse(IntPtr connection, byte* response, ref uint responseSize);
    [DllImport(DllName)] public static extern int DmSendBinary(IntPtr connection, byte* data, uint count);
    [DllImport(DllName)] public static extern int DmReceiveBinary(IntPtr connection, byte* data, uint count, out uint received);

    // File system
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmSendFileA(string localName, string remoteName);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmReceiveFileA(string localName, string remoteName);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmGetFileAttributes(string fileName, out DM_FILE_ATTRIBUTES attributes);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmMkdir(string directoryName);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmRenameFile(string oldName, string newName);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmDeleteFile(string fileName, [MarshalAs(UnmanagedType.Bool)] bool isDirectory);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmWalkDir(ref IntPtr walk, string directory, out DM_FILE_ATTRIBUTES attributes);
    [DllImport(DllName)] public static extern int DmCloseDir(IntPtr walk);
    [DllImport(DllName)] public static extern int DmGetDriveList(byte* drives, ref uint count);
    [DllImport(DllName)] public static extern int DmGetUtilityDriveInfo(out DM_UTILITY_DRIVE_INFO info);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmGetDiskFreeSpace(string drive, out ulong freeToCaller, out ulong total, out ulong totalFree);


    // Title control
    [DllImport(DllName)] public static extern int DmReboot(uint flags);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmSetTitle(string directory, string title, string? commandLine);
    [DllImport(DllName)] public static extern int DmGo();
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmGetXbeInfo(string name, out DM_XBE info);

    // Console state
    [DllImport(DllName)] public static extern int DmGetSystemTime(out SYSTEMTIME time);
    [DllImport(DllName)] public static extern int DmIsSecurityEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmScreenShot(string fileName);

    // Misc
    [DllImport(DllName, CharSet = CharSet.Ansi)] public static extern int DmTranslateErrorA(int hr, StringBuilder buffer, int bufferMax);

    private static bool _resolverInstalled;

    /// <summary>The DLL bound by UseDll. It cannot be swapped while the process runs.</summary>
    public static string? LoadedDllPath { get; private set; }

    /// <summary>xboxdbg.dll location from the XDK installer's registry entry, or null.</summary>
    public static string? FindInstalledDll()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\XboxSDK");

        if (key?.GetValue("InstallPath") is not string installPath)
        {
            return null;
        }

        var dll = Path.Combine(installPath, "xbox", "bin", DllName);

        return File.Exists(dll) ? dll : null;
    }

    /// <summary>Binds all imports of xboxdbg.dll to the given file. Must run before the first Dm* call.</summary>
    public static void UseDll(string dllPath)
    {
        if (_resolverInstalled)
        {
            return;
        }

        var handle = NativeLibrary.Load(dllPath);

        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(),
            (name, _, _) => name == DllName ? handle : IntPtr.Zero);

        _resolverInstalled = true;
        LoadedDllPath = dllPath;
    }

    public static string TranslateError(int hr)
    {
        var sb = new StringBuilder(512);

        if (Succeeded(DmTranslateErrorA(hr, sb, sb.Capacity)) && sb.Length > 0)
        {
            return sb.ToString().Trim();
        }

        return $"0x{hr:X8}";
    }

    public static void ThrowIfFailed(int hr, string operation)
    {
        if (!Succeeded(hr))
        {
            throw new XbdmException(hr, operation);
        }
    }
}
