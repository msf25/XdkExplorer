using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public enum ConsoleStatus
{
    Checking,
    Online,
    Offline
}

public sealed partial class ConsoleViewModel : ObservableObject
{
    // XBDM is back about 4 s after a reboot on xemu, real kits need longer for a cold boot
    private static readonly TimeSpan RebootTimeout = TimeSpan.FromSeconds(60);

    // DmSetTitle is only accepted while XBDM waits after a DMBOOT_WAIT reboot (15 s once it is back up)
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);

    private readonly XbdmWorker _worker;
    private readonly SavedConsole _saved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(StatusText), nameof(IsOnline), nameof(IsOffline), nameof(IsChecking))]
    public partial ConsoleStatus Status { get; set; } = ConsoleStatus.Checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(StatusText))]
    public partial bool IsRebooting { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsDefault { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Drives are shown below the console in the sidebar.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ConsoleViewModel(XbdmWorker worker, SavedConsole saved)
    {
        _worker = worker;
        _saved = saved;
    }

    public ObservableCollection<DriveViewModel> Drives { get; } = new();

    public SavedConsole Saved => _saved;

    /// <summary>Name or IP handed to XBDM.</summary>
    public string Target => _saved.Target;

    public string DisplayName => _saved.Name ?? _saved.Target;

    public string? Address => _saved.Address;

    public bool IsOnline => Status == ConsoleStatus.Online;

    public bool IsOffline => Status == ConsoleStatus.Offline;

    public bool IsChecking => Status == ConsoleStatus.Checking;

    public string Subtitle
    {
        get
        {
            string state = Status == ConsoleStatus.Online && IsDefault && !IsRebooting ? "Default" : StatusText;

            return string.IsNullOrEmpty(Address) ? state : $"{Address} · {state}";
        }
    }

    public string StatusText
    {
        get
        {
            if (IsRebooting)
            {
                return "Rebooting …";
            }

            return Status switch
            {
                ConsoleStatus.Online => "Online",
                ConsoleStatus.Offline => "Offline",
                _ => "Checking …"
            };
        }
    }

    /// <summary>True if the name or IP refers to this console: entered target, reported name or last known address.</summary>
    public bool Matches(string? nameOrAddress)
    {
        return !string.IsNullOrEmpty(nameOrAddress)
            && (string.Equals(Target, nameOrAddress, StringComparison.OrdinalIgnoreCase)
                || string.Equals(DisplayName, nameOrAddress, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Address, nameOrAddress, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Queries name, address and drives through the worker. Returns false if the console is unreachable.</summary>
    public async Task<bool> RefreshAsync()
    {
        if (Status != ConsoleStatus.Online)
        {
            Status = ConsoleStatus.Checking;
        }

        try
        {
            var info = await _worker.RunAsync(Target, () =>
            {
                var address = XbdmCommands.ResolveAddress();
                var name = XbdmCommands.GetConsoleName();
                var drives = XbdmCommands.GetDriveDetails();

                return (Address: address, Name: name, Drives: drives);
            });

            Apply(info.Address.ToString(), info.Name, info.Drives);

            return true;
        }
        catch (XbdmException)
        {
            Status = ConsoleStatus.Offline;

            return false;
        }
    }

    /// <summary>Cheap periodic check. Only consoles with a known IP are probed, the rest wait for a manual refresh.</summary>
    public async Task ProbeAsync()
    {
        // A rebooting console is briefly unreachable on purpose and is watched by the reboot itself
        if (IsRebooting || !IPAddress.TryParse(Address, out var ip))
        {
            return;
        }

        bool isReachable = await ConsoleProbe.IsReachableAsync(ip);

        if (isReachable && Status != ConsoleStatus.Online)
        {
            await RefreshAsync();
        }
        else if (!isReachable)
        {
            Status = ConsoleStatus.Offline;
        }
    }

    /// <summary>Reboots and waits until XBDM answers again. Returns false if the console did not come back in time.</summary>
    public async Task<bool> RebootAsync(bool warm)
    {
        return await RunRebootAsync(async () =>
        {
            await _worker.RunAsync(Target, () => XbdmCommands.Reboot(warm, false));

            return await WaitUntilBackAsync();
        });
    }

    /// <summary>Reboots into the given .xbe the way VS 2003 does: reboot with DMBOOT_WAIT, then DmSetTitle and DmGo.</summary>
    public async Task LaunchAsync(string xbePath, bool warm)
    {
        await RunRebootAsync(async () =>
        {
            await _worker.RunAsync(Target, () => XbdmCommands.Reboot(warm, true));

            var deadline = DateTime.UtcNow + LaunchTimeout;

            while (true)
            {
                await Task.Delay(1000);

                try
                {
                    await _worker.RunAsync(Target, () => XbdmCommands.SetTitleAndGo(xbePath));

                    return true;
                }
                catch (XbdmException) when (DateTime.UtcNow < deadline)
                {
                    // XBDM is not back up yet
                }
            }
        });
    }

    /// <summary>Takes over the result of a successful query and marks the console as online.</summary>
    public void Apply(string address, string? name, IEnumerable<XboxDrive> drives)
    {
        _saved.Address = address;
        _saved.Name = string.IsNullOrEmpty(name) ? _saved.Name : name;

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Subtitle));

        UpdateDrives(drives);

        Status = ConsoleStatus.Online;
    }

    private async Task<bool> RunRebootAsync(Func<Task<bool>> reboot)
    {
        IsRebooting = true;
        Status = ConsoleStatus.Checking;

        bool isBack;

        try
        {
            isBack = await reboot();
        }
        catch (XbdmException)
        {
            IsRebooting = false;
            await RefreshAsync();

            throw;
        }

        IsRebooting = false;

        if (!isBack)
        {
            Status = ConsoleStatus.Offline;

            return false;
        }

        return await RefreshAsync();
    }

    private async Task<bool> WaitUntilBackAsync()
    {
        // XBDM goes down within a second after DmReboot, polling earlier could still reach the old instance
        await Task.Delay(1500);

        var deadline = DateTime.UtcNow + RebootTimeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await _worker.RunAsync(Target, XbdmCommands.GetConsoleName);

                return true;
            }
            catch (XbdmException)
            {
                await Task.Delay(1000);
            }
        }

        return false;
    }

    private void UpdateDrives(IEnumerable<XboxDrive> drives)
    {
        char? selected = Drives.FirstOrDefault(d => d.IsSelected)?.Letter;

        Drives.Clear();

        foreach (var drive in drives.Select(d => new DriveViewModel(this, d)).OrderBy(d => d.SortOrder))
        {
            drive.IsSelected = drive.Letter == selected;
            Drives.Add(drive);
        }
    }
}
