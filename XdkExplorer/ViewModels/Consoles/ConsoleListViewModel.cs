using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

/// <summary>
/// The sidebar: consoles, their drives, default console and the periodic online check.
/// The list and the default console live in the registry only, see ConsoleRegistry.
/// </summary>
public sealed partial class ConsoleListViewModel : ObservableObject
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(15);

    private readonly XbdmWorker _worker;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly InfoBarViewModel _infoBar;
    private readonly DispatcherTimer _probeTimer;

    [ObservableProperty]
    public partial ConsoleViewModel? SelectedConsole { get; private set; }

    public ConsoleListViewModel(XbdmWorker worker, AppSettings settings, IDialogService dialogs, InfoBarViewModel infoBar)
    {
        _worker = worker;
        _settings = settings;
        _dialogs = dialogs;
        _infoBar = infoBar;

        _probeTimer = new DispatcherTimer { Interval = ProbeInterval };
        _probeTimer.Tick += async (_, _) => await ProbeAllAsync();
    }

    /// <summary>A console was picked in the sidebar. The browser shows its default drive.</summary>
    public event EventHandler<ConsoleViewModel>? ConsoleSelected;

    /// <summary>A drive was picked in the sidebar.</summary>
    public event EventHandler<DriveViewModel>? DriveSelected;

    /// <summary>The periodic check found a console that went offline or came back.</summary>
    public event EventHandler<ConsoleViewModel>? OnlineStateChanged;

    /// <summary>A console left the list, removed here or in the Xbox Neighborhood.</summary>
    public event EventHandler<ConsoleViewModel>? ConsoleRemoved;

    public ObservableCollection<ConsoleViewModel> Consoles { get; } = new();

    public string CountText => Consoles.Count.ToString();

    /// <summary>Reads the list, selects the default console and returns it, or the first console if there is no default.</summary>
    public ConsoleViewModel? Load()
    {
        Sync();

        var initial = Consoles.FirstOrDefault(c => c.IsDefault) ?? Consoles.FirstOrDefault();

        if (initial != null)
        {
            MarkSelected(initial);
        }

        return initial;
    }

    /// <summary>
    /// Takes over changes made by the Xbox Neighborhood or other tools. Only the difference is applied,
    /// so existing entries keep their state and the sidebar does not flicker.
    /// </summary>
    public void Sync()
    {
        var targets = ConsoleRegistry.ReadConsoles();
        var wanted = targets
            .Select(target => Find(target) ?? CreateConsole(target))
            .ToList();

        // Consoles removed in the Xbox Neighborhood or by other tools leave nothing behind in the cache
        if (_settings.KeepConsoleInfo(targets))
        {
            _settings.Save();
        }

        foreach (var removed in Consoles.Except(wanted).ToList())
        {
            removed.PropertyChanged -= OnConsoleChanged;
            Consoles.Remove(removed);

            if (SelectedConsole == removed)
            {
                SelectedConsole = null;
            }

            ConsoleRemoved?.Invoke(this, removed);
        }

        foreach (var added in wanted.Except(Consoles).ToList())
        {
            added.PropertyChanged += OnConsoleChanged;
            Consoles.Add(added);

            _ = RefreshAsync(added);
        }

        foreach (var console in Consoles)
        {
            console.IsDefault = ConsoleRegistry.IsDefault(console.Target);
        }

        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>Queries every console except the given one and starts the periodic online check.</summary>
    public async Task StartMonitoringAsync(ConsoleViewModel? alreadyChecked)
    {
        foreach (var console in Consoles.Where(c => c != alreadyChecked && !c.IsOnline).ToList())
        {
            await console.RefreshAsync();
        }

        _probeTimer.Start();
    }

    /// <summary>Registers a tested console in the shared list and returns it.</summary>
    public ConsoleViewModel Add(SavedConsole tested)
    {
        var info = _settings.GetConsoleInfo(tested.Target);

        info.Name = tested.Name;
        info.Address = tested.Address;

        ConsoleRegistry.Add(tested.Target);
        _settings.Save();

        Sync();

        return Find(tested.Target)!;
    }

    /// <summary>Shows a console without registering it, for design-time data.</summary>
    public ConsoleViewModel AddPreview(SavedConsole saved)
    {
        var console = new ConsoleViewModel(_worker, saved);

        Consoles.Add(console);
        OnPropertyChanged(nameof(CountText));

        return console;
    }

    /// <summary>Name of a known console matching the given name or IP, or null.</summary>
    public string? FindDuplicate(string nameOrAddress)
    {
        return Consoles.FirstOrDefault(c => c.Matches(nameOrAddress))?.DisplayName;
    }

    [RelayCommand]
    private void AddConsole()
    {
        var form = new ConnectConsoleViewModel(_worker, FindDuplicate);

        if (!_dialogs.ShowAddConsole(form))
        {
            return;
        }

        var console = Add(form.CreateSavedConsole());

        if (form.SetAsDefault)
        {
            SetDefault(console);
        }

        SelectConsole(console);
    }

    [RelayCommand]
    private void Scan()
    {
        var scan = new NetworkScanViewModel(_worker, FindDuplicate, saved =>
        {
            Add(saved);

            return Task.CompletedTask;
        });

        _dialogs.ShowNetworkScan(scan);

        if (scan.IsAddByIpRequested)
        {
            AddConsole();
        }
    }

    [RelayCommand]
    private void SelectConsole(ConsoleViewModel console)
    {
        MarkSelected(console);
        ConsoleSelected?.Invoke(this, console);
    }

    [RelayCommand]
    private void ToggleExpanded(ConsoleViewModel console)
    {
        console.IsExpanded = !console.IsExpanded;
    }

    [RelayCommand]
    private void SelectDrive(DriveViewModel drive)
    {
        MarkSelected(drive.Console);
        DriveSelected?.Invoke(this, drive);
    }

    /// <summary>The default is exactly what the console was added with, a name or an IP.</summary>
    [RelayCommand]
    private void SetDefault(ConsoleViewModel console)
    {
        ConsoleRegistry.Add(console.Target);
        ConsoleRegistry.SetDefault(console.Target);

        Sync();

        _infoBar.Show($"{console.DisplayName} is now the default console for Visual Studio and the XDK tools.");
    }

    /// <summary>Clears the default but keeps the console in the list.</summary>
    [RelayCommand]
    private void RemoveDefault(ConsoleViewModel console)
    {
        ConsoleRegistry.ClearDefault();

        Sync();

        _infoBar.Show($"{console.DisplayName} is no longer the default. Visual Studio and the XDK tools have no default console now.");
    }

    [RelayCommand]
    private async Task RefreshAsync(ConsoleViewModel console)
    {
        bool wasOnline = console.IsOnline;

        await console.RefreshAsync();

        if (wasOnline != console.IsOnline)
        {
            OnlineStateChanged?.Invoke(this, console);
        }
    }

    [RelayCommand]
    private async Task RebootAsync(ConsoleViewModel console)
    {
        _infoBar.Show($"Rebooting {console.DisplayName} …");

        try
        {
            bool isBack = await console.RebootAsync(true);

            _infoBar.Show(isBack
                ? $"{console.DisplayName} is back online."
                : $"{console.DisplayName} did not come back after the reboot.");
        }
        catch (XbdmException ex)
        {
            _infoBar.Show(ex.Message);
        }
    }

    [RelayCommand]
    private void Remove(ConsoleViewModel console)
    {
        bool isDefault = ConsoleRegistry.IsDefault(console.Target);

        var message = isDefault
            ? $"{console.DisplayName} is the default console. Visual Studio and the XDK tools have no default console " +
              "until another one is set. Remove it anyway?"
            : $"Remove {console.DisplayName} from the list?";

        if (!_dialogs.Confirm("Remove console", message, "Remove"))
        {
            return;
        }

        ConsoleRegistry.Remove(console.Target);

        if (isDefault)
        {
            ConsoleRegistry.ClearDefault();
        }

        // Also drops the cached details of the console
        Sync();
    }

    private ConsoleViewModel? Find(string target)
    {
        return Consoles.FirstOrDefault(c => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase));
    }

    private ConsoleViewModel CreateConsole(string target)
    {
        return new ConsoleViewModel(_worker, _settings.GetConsoleInfo(target));
    }

    private void MarkSelected(ConsoleViewModel console)
    {
        foreach (var other in Consoles)
        {
            other.IsSelected = other == console;
        }

        console.IsExpanded = true;
        SelectedConsole = console;
    }

    private async Task ProbeAllAsync()
    {
        // Also catches changes from the Xbox Neighborhood while this window stays inactive, e.g. on a second screen
        Sync();

        foreach (var console in Consoles.ToList())
        {
            bool wasOnline = console.IsOnline;

            await console.ProbeAsync();

            if (wasOnline != console.IsOnline)
            {
                OnlineStateChanged?.Invoke(this, console);
            }
        }
    }

    private void OnConsoleChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Name and address are learned from the console and kept for the next start
        if (e.PropertyName is nameof(ConsoleViewModel.Address) or nameof(ConsoleViewModel.DisplayName))
        {
            _settings.Save();
        }
    }
}
