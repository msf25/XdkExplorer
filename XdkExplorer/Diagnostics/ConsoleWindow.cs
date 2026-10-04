using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XdkExplorer.Diagnostics;

/// <summary>
/// Text output for a window application. XDK Explorer is built as a window application, so Windows gives it no console
/// and Console.WriteLine goes nowhere. This connects it to the terminal it was started from, or opens a console window.
/// </summary>
internal static class ConsoleWindow
{
    private const int StdOutputHandle = -11;
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;

    // AttachConsole: the console of the process that started this one
    private const int AttachParentProcess = -1;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int stdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr file);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation,
        uint flags, IntPtr template);

    /// <summary>True if Connect opened an own console window, which closes as soon as the process ends.</summary>
    public static bool IsOwnWindow { get; private set; }

    public static void Connect()
    {
        // Output redirected to a file or pipe (e.g. "> log.txt" or "| more") already works as it is
        var output = GetStdHandle(StdOutputHandle);

        if (output != IntPtr.Zero && output != new IntPtr(-1) && GetFileType(output) is FileTypeDisk or FileTypePipe)
        {
            return;
        }

        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
            IsOwnWindow = true;
        }

        // A window application does not get the standard handles of the console it attaches to,
        // so the console is opened by name. Without a BOM, which the console would print as garbage.
        var encoding = Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ? new UTF8Encoding(false) : Console.OutputEncoding;

        var conout = CreateFile("CONOUT$", GenericRead | GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        Console.SetOut(new StreamWriter(new FileStream(conout, FileAccess.Write), encoding) { AutoFlush = true });

        var conin = CreateFile("CONIN$", GenericRead | GenericWrite, FileShareRead, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        Console.SetIn(new StreamReader(new FileStream(conin, FileAccess.Read), encoding));
    }

    /// <summary>Keeps an own console window open until the user has read it.</summary>
    public static void WaitIfOwnWindow()
    {
        if (!IsOwnWindow)
        {
            return;
        }

        Console.WriteLine("\nPress Enter to close this window.");
        Console.ReadLine();
    }
}
