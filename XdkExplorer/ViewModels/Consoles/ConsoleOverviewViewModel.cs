using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public enum OverviewState
{
    Checking,
    Loading,
    Ready,
    Offline
}

/// <summary>
/// The page of a console itself: details, drives, reboot, screen capture, clock and security.
/// FileBrowserViewModel decides when it is shown, this class loads it and acts on the console.
/// </summary>
public sealed partial class ConsoleOverviewViewModel : ObservableObject
{
    // XBDM before 4831 has no dmversion command, so older consoles cannot tell their version
    public const int FirstBuildWithVersionQuery = 4831;

    // Reading the clock takes a moment itself, smaller differences count as in sync
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(2);

    private readonly XbdmWorker _worker;
    private readonly IDialogService _dialogs;
    private readonly XdkInstallation? _pcXdk;
    private readonly DispatcherTimer _clockTimer;
    private readonly HashSet<string> _dismissedVersionBars = new(StringComparer.OrdinalIgnoreCase);
    private int _loadVersion;

    // Console clock minus PC clock, null if the console clock is not set
    private TimeSpan? _clockOffset;

    [ObservableProperty]
    public partial ConsoleViewModel? Console { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking), nameof(IsReady), nameof(IsOffline), nameof(ShowSkeleton), nameof(OfflineText))]
    [NotifyCanExecuteChangedFor(nameof(RebootCommand), nameof(CaptureCommand), nameof(SyncClockCommand))]
    public partial OverviewState State { get; private set; } = OverviewState.Checking;

    /// <summary>True while the page is visible. The clocks only tick then.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleAddressText), nameof(RunningTitleText), nameof(HasRunningTitle), nameof(ConsoleXdkText),
        nameof(IsConsoleXdkOlder), nameof(SecurityText), nameof(IsLocked))]
    public partial ConsoleDetails? Details { get; private set; }

    [ObservableProperty]
    public partial string? VersionMessage { get; private set; }

    [ObservableProperty]
    public partial bool IsVersionWarning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsColdReboot))]
    public partial bool IsWarmReboot { get; set; } = true;

    [ObservableProperty]
    public partial bool RestartTitle { get; set; }

    [ObservableProperty]
    public partial string? RebootMessage { get; private set; }

    [ObservableProperty]
    public partial bool IsRebootError { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand))]
    public partial bool IsCapturing { get; private set; }

    [ObservableProperty]
    public partial string? CapturePath { get; set; }

    [ObservableProperty]
    public partial string? CaptureError { get; private set; }

    [ObservableProperty]
    public partial string ConsoleTimeText { get; private set; } = "";

    [ObservableProperty]
    public partial string PcTimeText { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncClockCommand))]
    public partial bool IsClockInSync { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncClockCommand))]
    public partial bool IsSyncingClock { get; private set; }

    [ObservableProperty]
    public partial string? ClockError { get; private set; }

    public ConsoleOverviewViewModel(XbdmWorker worker, IDialogService dialogs, XdkInstallation? pcXdk)
    {
        _worker = worker;
        _dialogs = dialogs;
        _pcXdk = pcXdk;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
    }

    /// <summary>A drive tile was clicked.</summary>
    public event EventHandler<DriveViewModel>? DriveSelected;

    public bool IsChecking => State == OverviewState.Checking;

    public bool IsReady => State == OverviewState.Ready;

    public bool IsOffline => State == OverviewState.Offline;

    public bool ShowSkeleton => State is OverviewState.Checking or OverviewState.Loading;

    public bool IsColdReboot
    {
        get => !IsWarmReboot;
        set => IsWarmReboot = !value;
    }

    public bool IsRebooting => Console?.IsRebooting == true;

    // XBDM assigns the title address a few seconds after boot, until then the query fails
    public string TitleAddressText => Details?.TitleAddress?.ToString() ?? "Not available";

    public string RunningTitleText => Details?.RunningTitle ?? "Dashboard";

    public bool HasRunningTitle => Details?.RunningTitle != null;

    public string ConsoleXdkText => Details?.DebugMonitorVersion ?? $"Older than {FirstBuildWithVersionQuery}";

    public bool IsConsoleXdkOlder
    {
        get
        {
            int? pcBuild = ParseBuild(_pcXdk?.Version);

            if (Details == null || pcBuild == null)
            {
                return false;
            }

            int? consoleBuild = ParseBuild(Details.DebugMonitorVersion);

            return consoleBuild == null || consoleBuild < pcBuild;
        }
    }

    public string PcXdkText => _pcXdk?.Version ?? "Unknown";

    /// <summary>The SDK folder for the standard layout ...\xbox\bin\xboxdbg.dll, otherwise the folder of the DLL.</summary>
    public string PcXdkFolder
    {
        get
        {
            var folder = Path.GetDirectoryName(_pcXdk?.DllPath);

            if (folder == null)
            {
                return "";
            }

            if (folder.EndsWith(@"\xbox\bin", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(Path.GetDirectoryName(folder)) ?? folder;
            }

            return folder;
        }
    }

    public string OfflineText
    {
        get
        {
            if (Console == null)
            {
                return "";
            }

            var who = string.IsNullOrEmpty(Console.Address) ? Console.DisplayName : $"{Console.DisplayName} ({Console.Address})";

            return $"{who} is not responding. Check that the console is switched on and in the same network. " +
                   "For xemu, port 731 has to be forwarded.";
        }
    }

    public bool IsLocked => Details?.IsLocked == true;

    public string SecurityText
    {
        get
        {
            return Details?.IsLocked switch
            {
                true => "This console is locked.",
                false => "This console is not locked.",
                _ => "The console did not report its security state."
            };
        }
    }

    /// <summary>Shows the console and loads its details. Refreshing the same console keeps the cards while loading.</summary>
    public async Task ShowAsync(ConsoleViewModel console)
    {
        if (Console != console)
        {
            Console = console;
            ResetConsoleState();
        }

        int version = ++_loadVersion;

        if (Details == null)
        {
            State = console.IsOnline ? OverviewState.Loading : OverviewState.Checking;
        }

        // Also picks up drives and free space that changed since the last visit
        if (!await console.RefreshAsync())
        {
            if (version == _loadVersion)
            {
                ShowOffline();
            }

            return;
        }

        if (version != _loadVersion)
        {
            return;
        }

        ConsoleDetails details;

        try
        {
            details = await _worker.RunAsync(console.Target, XbdmCommands.GetConsoleDetails);
        }
        catch (XbdmException)
        {
            await console.RefreshAsync();

            if (version == _loadVersion && !console.IsOnline)
            {
                ShowOffline();
            }

            return;
        }

        if (version == _loadVersion)
        {
            ShowDetails(console, details);
        }
    }

    /// <summary>Fills the cards. Also used to show sample data in the designer.</summary>
    public void ShowDetails(ConsoleViewModel console, ConsoleDetails details)
    {
        Console = console;
        Details = details;
        _clockOffset = details.SystemTime - DateTime.UtcNow;

        if (!HasRunningTitle)
        {
            RestartTitle = false;
        }

        UpdateClock();
        UpdateVersionBar();

        State = OverviewState.Ready;
        UpdateClockTimer();
    }

    public async Task HandleOnlineStateChangedAsync(ConsoleViewModel console)
    {
        // A reboot started here watches the console itself
        if (console != Console || !IsActive || console.IsRebooting)
        {
            return;
        }

        if (!console.IsOnline)
        {
            ShowOffline();
        }
        else if (State == OverviewState.Offline)
        {
            await ShowAsync(console);
        }
    }

    public void Close(ConsoleViewModel console)
    {
        if (console != Console)
        {
            return;
        }

        _loadVersion++;

        Console = null;
        ResetConsoleState();
        State = OverviewState.Checking;
        UpdateClockTimer();
    }

    partial void OnIsActiveChanged(bool value)
    {
        UpdateClockTimer();
    }

    partial void OnConsoleChanged(ConsoleViewModel? oldValue, ConsoleViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnConsolePropertyChanged;
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnConsolePropertyChanged;
        }

        OnPropertyChanged(nameof(IsRebooting));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Console != null)
        {
            await ShowAsync(Console);
        }
    }

    [RelayCommand]
    private void OpenDrive(DriveViewModel drive)
    {
        DriveSelected?.Invoke(this, drive);
    }

    [RelayCommand]
    private void DismissVersionBar()
    {
        if (Console != null)
        {
            _dismissedVersionBars.Add(Console.Target);
        }

        VersionMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanUseConsole))]
    private async Task RebootAsync()
    {
        var console = Console!;
        var title = RestartTitle ? Details?.RunningTitle : null;

        RebootMessage = null;
        IsRebootError = false;

        try
        {
            bool isBack;

            if (title != null)
            {
                await console.LaunchAsync(title, IsWarmReboot);
                isBack = console.IsOnline;
            }
            else
            {
                isBack = await console.RebootAsync(IsWarmReboot);
            }

            if (console != Console)
            {
                return;
            }

            if (!isBack)
            {
                IsRebootError = true;
                RebootMessage = "The console did not come back after the reboot.";
                ShowOffline();

                return;
            }

            RebootMessage = "Console is back online.";

            // The running title and the clock may have changed
            await ShowAsync(console);
        }
        catch (XbdmException ex)
        {
            if (console == Console)
            {
                IsRebootError = true;
                RebootMessage = ex.Message;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task CaptureAsync()
    {
        var console = Console!;
        var tempPath = Path.Combine(Path.GetTempPath(), $"XdkExplorer-{Guid.NewGuid():N}.bmp");

        CapturePath = null;
        CaptureError = null;
        IsCapturing = true;

        try
        {
            // Taken before the dialog opens, so the picture shows the moment of the click
            await _worker.RunAsync(console.Target, () => XbdmCommands.SaveScreenshot(tempPath));

            IsCapturing = false;

            var folder = ScreenshotFile.DefaultFolder;
            var target = _dialogs.PickSaveFile("Save screenshot", folder, ScreenshotFile.NextFreeName(folder, console.DisplayName),
                ScreenshotFile.DialogFilter);

            if (target != null)
            {
                ScreenshotFile.SaveAs(tempPath, target);
                CapturePath = target;
            }
        }
        catch (Exception ex) when (ex is XbdmException or IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            CaptureError = ex.Message;
        }
        finally
        {
            IsCapturing = false;
            DeleteQuietly(tempPath);
        }
    }

    private bool CanCapture() => CanUseConsole() && !IsCapturing;

    [RelayCommand]
    private void OpenCaptureFolder()
    {
        if (CapturePath != null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{CapturePath}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand(CanExecute = nameof(CanSyncClock))]
    private async Task SyncClockAsync()
    {
        var console = Console!;

        ClockError = null;
        IsSyncingClock = true;

        try
        {
            var time = await _worker.RunAsync(console.Target, () =>
            {
                XbdmCommands.SetSystemTime(DateTime.UtcNow);

                return XbdmCommands.GetSystemTime();
            });

            if (console == Console)
            {
                _clockOffset = time - DateTime.UtcNow;
                UpdateClock();
            }
        }
        catch (XbdmException ex)
        {
            ClockError = ex.Message;
        }
        finally
        {
            IsSyncingClock = false;
        }
    }

    private bool CanSyncClock() => CanUseConsole() && !IsClockInSync && !IsSyncingClock;

    private bool CanUseConsole() => State == OverviewState.Ready && Console is { IsOnline: true, IsRebooting: false };

    private void OnConsolePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ConsoleViewModel.IsRebooting) or nameof(ConsoleViewModel.Status)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsRebooting));

        RebootCommand.NotifyCanExecuteChanged();
        CaptureCommand.NotifyCanExecuteChanged();
        SyncClockCommand.NotifyCanExecuteChanged();
    }

    private void ShowOffline()
    {
        State = OverviewState.Offline;
        UpdateClockTimer();
    }

    private void ResetConsoleState()
    {
        Details = null;
        _clockOffset = null;

        VersionMessage = null;
        IsWarmReboot = true;
        RestartTitle = false;
        RebootMessage = null;
        IsRebootError = false;
        CapturePath = null;
        CaptureError = null;
        ClockError = null;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;

        PcTimeText = now.ToString("G");

        if (_clockOffset is { } offset)
        {
            ConsoleTimeText = (now + offset).ToString("G");
            IsClockInSync = offset.Duration() < ClockTolerance;
        }
        else
        {
            ConsoleTimeText = "Not set";
            IsClockInSync = false;
        }
    }

    private void UpdateClockTimer()
    {
        _clockTimer.IsEnabled = IsActive && State == OverviewState.Ready;
    }

    private void UpdateVersionBar()
    {
        VersionMessage = null;

        int? pcBuild = ParseBuild(_pcXdk?.Version);

        if (Console == null || Details == null || pcBuild == null || _dismissedVersionBars.Contains(Console.Target))
        {
            return;
        }

        int? consoleBuild = ParseBuild(Details.DebugMonitorVersion);

        if (consoleBuild == null || consoleBuild < pcBuild)
        {
            var consoleXdk = consoleBuild == null ? $"an XDK older than {FirstBuildWithVersionQuery}" : $"XDK {consoleBuild}";

            IsVersionWarning = true;
            VersionMessage = $"This console runs {consoleXdk}, this PC uses XDK {pcBuild}. " +
                             "Titles built on this PC may not run on this console, and some features may not be available.";
        }
        else if (consoleBuild > pcBuild)
        {
            IsVersionWarning = false;
            VersionMessage = $"This console runs XDK {consoleBuild}, this PC uses XDK {pcBuild}. " +
                             "Some console features may not be available with the XDK on this PC.";
        }
    }

    /// <summary>The build number of a version such as 1.00.5849.1.</summary>
    private static int? ParseBuild(string? version)
    {
        var parts = version?.Split('.');

        if (parts == null || parts.Length < 3 || !int.TryParse(parts[2], out int build))
        {
            return null;
        }

        return build;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Only a temp file, Windows cleans it up eventually
        }
    }
}
