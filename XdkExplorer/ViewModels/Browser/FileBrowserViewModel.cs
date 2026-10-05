using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public enum BrowserState
{
    NoConsole,
    Overview,
    Checking,
    Offline,
    Loading,
    Items,
    Empty,
    Error
}

/// <summary>
/// Navigation, listing, filter, sorting and selection of the console that is currently open.
/// The location null is the console overview, which ConsoleOverviewViewModel fills.
/// </summary>
public sealed partial class FileBrowserViewModel : ObservableObject
{
    private static readonly TimeSpan MinReloadInterval = TimeSpan.FromSeconds(1);

    private readonly XbdmWorker _worker;
    private readonly InfoBarViewModel _infoBar;
    private readonly NavigationHistory _history = new();
    private int _navigationVersion;
    private bool _isReloadScheduled;
    private DateTime _lastReload;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText), nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial ConsoleViewModel? Console { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    public partial string? CurrentPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(ShowList), nameof(IsOffline), nameof(IsEmpty), nameof(IsChecking),
        nameof(IsError), nameof(HasNoConsole), nameof(ShowHeader), nameof(IsOverview), nameof(ShowFileTools), nameof(SelectionText))]
    public partial BrowserState State { get; private set; } = BrowserState.NoConsole;

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial string EmptyText { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(SizeSortGlyph), nameof(ModifiedSortGlyph), nameof(TypeSortGlyph))]
    public partial SortColumn SortColumn { get; private set; } = SortColumn.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(SizeSortGlyph), nameof(ModifiedSortGlyph), nameof(TypeSortGlyph))]
    public partial bool SortAscending { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    public partial IReadOnlyList<FileItemViewModel> SelectedItems { get; set; } = [];

    public FileBrowserViewModel(XbdmWorker worker, AppSettings settings, IDialogService dialogs, InfoBarViewModel infoBar,
        TransfersViewModel transfers)
    {
        _worker = worker;
        _infoBar = infoBar;

        Actions = new FileActionsViewModel(worker, settings, dialogs, infoBar, transfers, this);

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        ApplySort();
    }

    /// <summary>The overview of the console was opened and should load its content.</summary>
    public event EventHandler<ConsoleViewModel>? OverviewRequested;

    public FileActionsViewModel Actions { get; }

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = new();

    public ICollectionView ItemsView { get; }

    public bool IsLoading => State == BrowserState.Loading;

    public bool ShowList => State == BrowserState.Items;

    public bool ShowHeader => State is BrowserState.Items or BrowserState.Loading or BrowserState.Empty;

    public bool IsOffline => State == BrowserState.Offline;

    public bool IsEmpty => State == BrowserState.Empty;

    public bool IsChecking => State == BrowserState.Checking;

    public bool IsError => State == BrowserState.Error;

    public bool HasNoConsole => State == BrowserState.NoConsole;

    public bool IsOverview => State == BrowserState.Overview;

    /// <summary>Filter and file toolbar make no sense on the overview.</summary>
    public bool ShowFileTools => State != BrowserState.Overview;

    public bool IsConnected => Console?.IsOnline == true;

    public string FilterPlaceholder => CurrentPath == null ? "Filter" : $"Filter {XboxPath.GetName(CurrentPath)}";

    public string ConnectionText
    {
        get
        {
            if (Console == null)
            {
                return "No console";
            }

            if (!Console.IsOnline)
            {
                return $"{Console.DisplayName} not connected";
            }

            return $"Connected to {Console.DisplayName} ({Console.Address})";
        }
    }

    public string SelectionText
    {
        get
        {
            if (State == BrowserState.Overview)
            {
                return "";
            }

            var text = $"{Items.Count:N0} items";

            if (SelectedItems.Count == 0)
            {
                return text;
            }

            var bytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => (double)i.Size);

            return bytes > 0
                ? $"{text} · {SelectedItems.Count} selected ({Format.Size(bytes)})"
                : $"{text} · {SelectedItems.Count} selected";
        }
    }

    public string NameSortGlyph => SortGlyph(SortColumn.Name);

    public string SizeSortGlyph => SortGlyph(SortColumn.Size);

    public string ModifiedSortGlyph => SortGlyph(SortColumn.Modified);

    public string TypeSortGlyph => SortGlyph(SortColumn.Type);

    // The status bar follows the console no matter who refreshed it, e.g. the overview or a reboot
    partial void OnConsoleChanged(ConsoleViewModel? oldValue, ConsoleViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnConsolePropertyChanged;
            oldValue.Drives.CollectionChanged -= OnDrivesChanged;
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnConsolePropertyChanged;
            newValue.Drives.CollectionChanged += OnDrivesChanged;
        }
    }

    /// <summary>A title on the console released the drive that is open here, e.g. Z:. The overview shows what is left.</summary>
    private async void OnDrivesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems == null || CurrentPath == null || Console == null)
        {
            return;
        }

        var gone = e.OldItems.Cast<DriveViewModel>()
            .FirstOrDefault(d => CurrentPath.StartsWith(d.RootPath, StringComparison.OrdinalIgnoreCase));

        if (gone == null)
        {
            return;
        }

        _infoBar.Show($"{gone.Label} is no longer available on {Console.DisplayName}.");

        // Navigating to the overview does not throw, so this async void handler is safe
        await NavigateAsync(null, true);
    }

    private void OnConsolePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConsoleViewModel.Status) or nameof(ConsoleViewModel.Address) or nameof(ConsoleViewModel.DisplayName))
        {
            RaiseConnectionChanged();
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        ItemsView.Refresh();
    }

    // ---------------------------------------------------------------- called by the console list

    /// <summary>Shows the overview of the console. Another console starts with a fresh history.</summary>
    public async Task OpenConsoleAsync(ConsoleViewModel console)
    {
        SwitchConsole(console);

        await NavigateAsync(null, true);
    }

    public async Task OpenDriveAsync(DriveViewModel drive)
    {
        SwitchConsole(drive.Console);

        await NavigateAsync(drive.RootPath, true);
    }

    public async Task HandleOnlineStateChangedAsync(ConsoleViewModel console)
    {
        if (console != Console)
        {
            return;
        }

        RaiseConnectionChanged();

        // The overview follows the online state by itself
        if (State is BrowserState.Overview or BrowserState.NoConsole)
        {
            return;
        }

        if (!console.IsOnline)
        {
            ShowOffline();
        }
        else if (State == BrowserState.Offline)
        {
            await NavigateAsync(CurrentPath, false);
        }
    }

    public void CloseConsole(ConsoleViewModel console)
    {
        if (console != Console)
        {
            return;
        }

        Console = null;
        CurrentPath = null;

        ClearListing();
        Breadcrumbs.Clear();
        State = BrowserState.NoConsole;
    }

    /// <summary>Shows the given entries as content of path. Also used to fill the designer with sample data.</summary>
    public void ShowListing(ConsoleViewModel console, string path, IEnumerable<XboxFileEntry> entries)
    {
        Console = console;
        CurrentPath = path;

        SetBreadcrumbs(console, path);
        UpdateDriveSelection(console, path);

        Items.Clear();

        foreach (var entry in entries)
        {
            Items.Add(new FileItemViewModel(entry, path));
        }

        SelectedItems = [];
        OnPropertyChanged(nameof(SelectionText));

        if (Items.Count == 0)
        {
            EmptyText = "This folder is empty.";
            State = BrowserState.Empty;
        }
        else
        {
            State = BrowserState.Items;
        }
    }

    /// <summary>Reloads the current folder if a transfer changed it. Bursts of changes lead to at most one reload per second.</summary>
    public async Task HandleRemoteChangeAsync(RemoteChange change)
    {
        if (change.Console != Console || !string.Equals(change.Directory, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_isReloadScheduled)
        {
            return;
        }

        _isReloadScheduled = true;

        var wait = MinReloadInterval - (DateTime.UtcNow - _lastReload);

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait);
        }

        _isReloadScheduled = false;

        await ReloadAsync();
    }

    /// <summary>Lists the current folder again and merges the result, so selection and scroll position survive.</summary>
    public async Task ReloadAsync()
    {
        var console = Console;
        var path = CurrentPath;
        int version = _navigationVersion;

        if (console == null || path == null || State is not (BrowserState.Items or BrowserState.Empty))
        {
            return;
        }

        _lastReload = DateTime.UtcNow;

        List<XboxFileEntry> entries;

        try
        {
            entries = await _worker.RunAsync(console.Target, () => XbdmCommands.ListDirectory(path));
        }
        catch (XbdmException)
        {
            // A failing background reload is not worth an error state, the next navigation shows it
            return;
        }

        // The user navigated somewhere else in the meantime
        if (version != _navigationVersion)
        {
            return;
        }

        MergeListing(path, entries);
    }

    private void MergeListing(string path, List<XboxFileEntry> entries)
    {
        var fresh = entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items.Where(i => !fresh.ContainsKey(i.Name)).ToList())
        {
            Items.Remove(item);
        }

        foreach (var item in Items)
        {
            var entry = fresh[item.Name];

            if (entry.Size != item.Entry.Size || entry.Modified != item.Entry.Modified || entry.Attributes != item.Entry.Attributes)
            {
                item.Update(entry);
            }

            fresh.Remove(item.Name);
        }

        foreach (var entry in fresh.Values)
        {
            Items.Add(new FileItemViewModel(entry, path));
        }

        State = Items.Count == 0 ? BrowserState.Empty : BrowserState.Items;
        EmptyText = "This folder is empty.";

        OnPropertyChanged(nameof(SelectionText));
    }

    // ---------------------------------------------------------------- navigation

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task BackAsync()
    {
        await NavigateAsync(_history.GoBack(CurrentPath), false);
    }

    private bool CanGoBack() => _history.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task ForwardAsync()
    {
        await NavigateAsync(_history.GoForward(CurrentPath), false);
    }

    private bool CanGoForward() => _history.CanGoForward;

    /// <summary>The parent of a drive root is the overview.</summary>
    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task UpAsync()
    {
        await NavigateAsync(XboxPath.GetParent(CurrentPath!), true);
    }

    private bool CanGoUp() => CurrentPath != null;

    [RelayCommand(CanExecute = nameof(HasConsole))]
    private async Task RefreshAsync()
    {
        // The overview refreshes the whole console itself
        if (CurrentPath != null && Console != null)
        {
            await Console.RefreshDrivesAsync();
        }

        await NavigateAsync(CurrentPath, false);
    }

    private bool HasConsole() => Console != null;

    [RelayCommand]
    private async Task NavigateCrumbAsync(Breadcrumb crumb)
    {
        if (!crumb.IsLast)
        {
            await NavigateAsync(crumb.Path, true);
        }
    }

    [RelayCommand]
    private async Task RetryConnectionAsync()
    {
        var console = Console;

        if (console == null)
        {
            return;
        }

        State = BrowserState.Checking;

        if (await console.RefreshAsync())
        {
            await NavigateAsync(CurrentPath, false);
        }
        else if (Console == console)
        {
            ShowOffline();
        }
    }

    [RelayCommand]
    private void Sort(SortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }

        ApplySort();
    }

    /// <param name="path">A folder of the open console, or null for its overview.</param>
    public async Task NavigateAsync(string? path, bool recordHistory)
    {
        var console = Console;

        if (console == null)
        {
            return;
        }

        if (recordHistory)
        {
            _history.Visit(CurrentPath, path);
        }

        int version = ++_navigationVersion;

        CurrentPath = path;
        FilterText = "";
        SetBreadcrumbs(console, path);
        UpdateDriveSelection(console, path);
        UpdateHistoryCommands();

        if (path == null)
        {
            ClearListing();
            State = BrowserState.Overview;
            RaiseConnectionChanged();

            OverviewRequested?.Invoke(this, console);

            return;
        }

        State = BrowserState.Loading;

        try
        {
            var entries = await _worker.RunAsync(console.Target, () => XbdmCommands.ListDirectory(path));

            // A newer navigation started while this listing was queued
            if (version != _navigationVersion)
            {
                return;
            }

            ShowListing(console, path, entries);
        }
        catch (XbdmException ex)
        {
            // A newer navigation took over while this one waited for the console, it shows its own result.
            // Rethrowing would end in an async event handler and crash the app.
            if (version != _navigationVersion)
            {
                return;
            }

            ClearListing();

            if (ex.IsConnectionError)
            {
                await console.RefreshAsync();

                if (!console.IsOnline)
                {
                    ShowOffline();

                    return;
                }
            }

            EmptyText = ex.Message;
            State = BrowserState.Error;
        }
    }

    // ---------------------------------------------------------------- helpers

    private void ShowOffline()
    {
        ClearListing();
        State = BrowserState.Offline;

        RaiseConnectionChanged();
    }

    private void ClearListing()
    {
        Items.Clear();
        SelectedItems = [];

        OnPropertyChanged(nameof(SelectionText));
    }

    private void RaiseConnectionChanged()
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsConnected));
    }

    private void SetBreadcrumbs(ConsoleViewModel console, string? path)
    {
        Breadcrumbs.Clear();

        foreach (var crumb in Breadcrumb.Build(console.DisplayName, path))
        {
            Breadcrumbs.Add(crumb);
        }
    }

    private static void UpdateDriveSelection(ConsoleViewModel console, string? path)
    {
        foreach (var drive in console.Drives)
        {
            drive.IsSelected = path != null && path.StartsWith(drive.RootPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SwitchConsole(ConsoleViewModel console)
    {
        if (Console == console)
        {
            return;
        }

        Console = console;
        CurrentPath = null;

        // A null path counts as the overview, so the new console's history starts there
        _history.Clear();
    }

    private void UpdateHistoryCommands()
    {
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private bool FilterItem(object item)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return ((FileItemViewModel)item).Name.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySort()
    {
        if (ItemsView is ListCollectionView view)
        {
            view.CustomSort = new FileItemComparer(SortColumn, SortAscending);
        }
    }

    private string SortGlyph(SortColumn column)
    {
        if (column != SortColumn)
        {
            return "";
        }

        return SortAscending ? "" : "";
    }
}
