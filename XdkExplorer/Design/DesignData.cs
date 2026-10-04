using XdkExplorer.Services;
using XdkExplorer.ViewModels;
using XdkExplorer.Xbdm;

namespace XdkExplorer.Design;

/// <summary>
/// Sample data for the XAML designer, referenced via d:DataContext="{x:Static design:DesignData.X}".
/// Uses the real view models without a worker, so nothing here may run a command against a console.
/// </summary>
public static class DesignData
{
    private const ulong MB = 1024 * 1024;

    static DesignData()
    {
        Main = new MainViewModel(null!, new AppSettings { IsPersistent = false }, null!);

        Main.Consoles.AddPreview(new SavedConsole { Target = "Devkit-01" });
        Main.Consoles.AddPreview(new SavedConsole { Target = "Devkit-02" });
        Main.Consoles.AddPreview(new SavedConsole { Target = "Debug-Kit Lab", Address = "192.168.1.35" });
        Main.Consoles.AddPreview(new SavedConsole { Target = "192.168.1.47" });

        var consoles = Main.Consoles.Consoles;
        var devkit = consoles[0];

        devkit.Apply("192.168.1.20", "Devkit-01",
        [
            Drive('E', 1710, 4994),
            Drive('C', 188, 500),
            Drive('P', 13, 750),
            Drive('T', 1710, 4994),
            Drive('U', 1710, 4994)
        ]);
        devkit.IsDefault = true;
        devkit.IsSelected = true;
        devkit.IsExpanded = true;

        consoles[1].Apply("192.168.1.21", "Devkit-02", [Drive('E', 3000, 4994)]);
        consoles[2].Status = ConsoleStatus.Offline;

        Main.Browser.ShowListing(devkit, @"E:\DEVKIT\Ember", SampleEntries());
        Main.InfoBar.Show("Starting default.xbe on Devkit-01 …");

        RunningUpload = CreateJob(devkit, TransferKind.Upload, @"D:\Builds\Ember\Release", @"E:\DEVKIT\Ember");
        RunningUpload.State = TransferState.Running;
        RunningUpload.TotalFiles = 1208;
        RunningUpload.CompletedFiles = 412;
        RunningUpload.TotalBytes = (long)(512 * MB);
        RunningUpload.TransferredBytes = (long)(38 * MB);
        RunningUpload.CurrentFileName = "level03.map";
        RunningUpload.CurrentFileProgress = 0.6;
        RunningUpload.RateText = "0.17 MB/s · 46 min 12 s left";

        var failedDownload = CreateJob(devkit, TransferKind.Download, @"E:\UDATA", @"C:\Xbox\Backups");
        failedDownload.State = TransferState.Error;
        failedDownload.TotalFiles = 14;
        failedDownload.CompletedFiles = 5;
        failedDownload.TotalBytes = (long)(252 * MB);
        failedDownload.TransferredBytes = (long)(96 * MB);
        failedDownload.CurrentFileName = "save03.xsv";
        failedDownload.Message = "save03.xsv: Connection to the console was lost";

        var finishedUpload = CreateJob(devkit, TransferKind.Upload, @"D:\Assets\Audio\music.xwb", @"E:\DEVKIT\Ember\sound");
        finishedUpload.State = TransferState.Completed;

        // Enqueue only starts waiting jobs, none of these is waiting
        Main.Transfers.Enqueue(RunningUpload);
        Main.Transfers.Enqueue(failedDownload);
        Main.Transfers.Enqueue(finishedUpload);

        // Console on an older XDK than the PC, so the warning bar shows
        var pcXdk = new XdkInstallation(@"C:\Program Files (x86)\Microsoft Xbox SDK\xbox\bin\xboxdbg.dll", "1.00.5849.1");

        Overview = new ConsoleOverviewViewModel(null!, null!, pcXdk) { IsActive = true };
        Overview.ShowDetails(devkit, new ConsoleDetails
        {
            TitleAddress = System.Net.IPAddress.Parse("192.168.1.120"),
            RunningTitle = @"E:\DEVKIT\Ember\default.xbe",
            DebugMonitorVersion = "1.00.4627.1",
            SystemTime = DateTime.UtcNow.AddMinutes(-4),
            IsLocked = false
        });
        Overview.CapturePath = @"C:\Users\Dev\Pictures\Devkit-01-image1.bmp";

        ConnectSuccess = new ConnectConsoleViewModel(null!, _ => null) { Input = "192.168.1.21", SetAsDefault = true };
        ConnectSuccess.Result = ConsoleConnectionResult.Success("192.168.1.21", "Devkit-02", "192.168.1.21");

        ConnectFailed = new ConnectConsoleViewModel(null!, _ => null) { Input = "devkit-03" };
        ConnectFailed.Result = ConsoleConnectionResult.Failure("devkit-03",
            "No answer from devkit-03. Check the name or IP address. The console has to be switched on and in the same network.");
    }

    public static MainViewModel Main { get; }

    public static ConsoleListViewModel Consoles => Main.Consoles;

    public static FileBrowserViewModel Browser => Main.Browser;

    public static FileActionsViewModel Actions => Main.Browser.Actions;

    public static TransfersViewModel Transfers => Main.Transfers;

    public static InfoBarViewModel InfoBar => Main.InfoBar;

    /// <summary>A separate instance, Main.Overview stays empty like at runtime before a console is opened.</summary>
    public static ConsoleOverviewViewModel Overview { get; }

    public static TransferJobViewModel RunningUpload { get; }

    public static ConnectConsoleViewModel ConnectSuccess { get; }

    public static ConnectConsoleViewModel ConnectFailed { get; }

    public static NetworkScanViewModel NetworkScan
    {
        get
        {
            var scan = new NetworkScanViewModel(null!, name => name == "Devkit-02" ? name : null, _ => Task.CompletedTask);

            scan.ShowResults(
            [
                scan.CreateResult(new DiscoveredConsole("Devkit-02", "192.168.1.21")),
                scan.CreateResult(new DiscoveredConsole("XDK-Testkit", "192.168.1.52")),
                scan.CreateResult(new DiscoveredConsole("Helmo", "192.168.178.25"))
            ]);

            return scan;
        }
    }

    public static SetupWizardViewModel Wizard { get; } = new(new AppSettings { IsPersistent = false }, null!);

    public static SettingsViewModel Settings { get; } = new(new AppSettings { IsPersistent = false }, null!, null);

    private static TransferJobViewModel CreateJob(ConsoleViewModel console, TransferKind kind, string source, string destination)
    {
        bool isDirectory = !source.Contains('.');

        return new TransferJobViewModel(null!, null!, console, kind, [new TransferSource(source, isDirectory, 0)], destination, 0);
    }

    private static XboxDrive Drive(char letter, ulong freeMb, ulong totalMb)
    {
        var utility = new XboxDbgNative.DM_UTILITY_DRIVE_INFO { TitleId2 = 0x4D530004 };

        return new XboxDrive(letter, XboxDrive.GetName(letter, utility), freeMb * MB, totalMb * MB);
    }

    private static List<XboxFileEntry> SampleEntries()
    {
        var now = new DateTime(2026, 10, 2, 0, 44, 0);

        return
        [
            Folder("media", now.AddDays(-4)),
            Folder("maps", now.AddHours(-2)),
            Folder("saves", now.AddMinutes(-13)),
            Folder("shaders", now.AddDays(-2)),
            Folder("sound", now.AddDays(-4)),
            File("default.xbe", 4.82, now),
            File("ember_profile.xbe", 6.10, now.AddMinutes(-46)),
            File("ember_tools.xbe", 1.37, now.AddDays(-1)),
            File("build.log", 0.04, now),
            File("config.ini", 0.002, now.AddDays(-3)),
            File("strings_de.xpr", 0.31, now.AddDays(-1)),
            File("titleimage.xbx", 0.06, now.AddDays(-8))
        ];
    }

    private static XboxFileEntry Folder(string name, DateTime modified)
    {
        return new XboxFileEntry(name, 0, 0x10, modified, modified);
    }

    private static XboxFileEntry File(string name, double megabytes, DateTime modified)
    {
        return new XboxFileEntry(name, (ulong)(megabytes * MB), 0x80, modified, modified);
    }
}
