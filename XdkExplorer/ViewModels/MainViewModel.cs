using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

/// <summary>Builds the parts of the main window, connects them and opens window-wide dialogs.</summary>
public sealed partial class MainViewModel
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;

    public MainViewModel(XbdmWorker worker, AppSettings settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        var pcXdk = XboxDbgNative.LoadedDllPath is { } dll ? XdkLocator.FromDll(dll) : null;

        InfoBar = new InfoBarViewModel();
        Transfers = new TransfersViewModel();
        Consoles = new ConsoleListViewModel(worker, settings, dialogs, InfoBar);
        Browser = new FileBrowserViewModel(worker, settings, dialogs, InfoBar, Transfers);
        Overview = new ConsoleOverviewViewModel(worker, dialogs, pcXdk);

        Consoles.ConsoleSelected += async (_, console) => await Browser.OpenConsoleAsync(console);
        Consoles.DriveSelected += async (_, drive) => await Browser.OpenDriveAsync(drive);
        Consoles.OnlineStateChanged += async (_, console) =>
        {
            await Browser.HandleOnlineStateChangedAsync(console);
            await Overview.HandleOnlineStateChangedAsync(console);
        };
        Consoles.ConsoleRemoved += (_, console) =>
        {
            Browser.CloseConsole(console);
            Overview.Close(console);
        };

        Browser.OverviewRequested += async (_, console) => await Overview.ShowAsync(console);
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileBrowserViewModel.IsOverview))
            {
                Overview.IsActive = Browser.IsOverview;
            }
        };

        // Same path as a click on the drive in the sidebar, so the sidebar selection follows
        Overview.DriveSelected += (_, drive) => Consoles.SelectDriveCommand.Execute(drive);

        Transfers.RemoteContentChanged += async (_, change) => await Browser.HandleRemoteChangeAsync(change);
    }

    public ConsoleListViewModel Consoles { get; }

    public FileBrowserViewModel Browser { get; }

    public ConsoleOverviewViewModel Overview { get; }

    public TransfersViewModel Transfers { get; }

    public InfoBarViewModel InfoBar { get; }

    public async Task InitializeAsync()
    {
        var initial = Consoles.Load();

        if (initial != null)
        {
            await Browser.OpenConsoleAsync(initial);
        }

        await Consoles.StartMonitoringAsync(initial);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _dialogs.ShowSettings(new SettingsViewModel(_settings, _dialogs, XboxDbgNative.LoadedDllPath));
    }
}
