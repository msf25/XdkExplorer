using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public enum ScanResultState
{
    Available,
    Adding,
    Added,
    Known,
    Failed
}

public sealed partial class ScanResultViewModel : ObservableObject
{
    private readonly Func<ScanResultViewModel, Task> _add;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(IsAdding), nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial ScanResultState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial string? Error { get; set; }

    public ScanResultViewModel(DiscoveredConsole console, ScanResultState state, Func<ScanResultViewModel, Task> add)
    {
        Console = console;
        State = state;
        _add = add;
    }

    public DiscoveredConsole Console { get; }

    public bool CanAdd => State is ScanResultState.Available or ScanResultState.Failed;

    public bool IsAdding => State == ScanResultState.Adding;

    public string StateText
    {
        get
        {
            return State switch
            {
                ScanResultState.Added => "Added",
                ScanResultState.Known => "Already added",
                ScanResultState.Failed => Error ?? "Could not connect",
                _ => ""
            };
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        await _add(this);
    }
}

/// <summary>Searches the local network for consoles. Adding still requires a successful connection test.</summary>
public sealed partial class NetworkScanViewModel : ObservableObject
{
    private static readonly TimeSpan SearchTime = TimeSpan.FromSeconds(2);

    private readonly XbdmWorker _worker;
    private readonly Func<string, string?> _findDuplicate;
    private readonly Func<SavedConsole, Task> _addConsole;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(SummaryText), nameof(HasResults))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial bool IsSearching { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(SummaryText), nameof(HasResults))]
    public partial bool HasSearched { get; private set; }

    /// <param name="findDuplicate">Returns the name of a known console matching the given name or IP, or null.</param>
    /// <param name="addConsole">Adds a tested console to the list.</param>
    public NetworkScanViewModel(XbdmWorker worker, Func<string, string?> findDuplicate, Func<SavedConsole, Task> addConsole)
    {
        _worker = worker;
        _findDuplicate = findDuplicate;
        _addConsole = addConsole;
    }

    public ObservableCollection<ScanResultViewModel> Results { get; } = new();

    /// <summary>Set when the user chose to add a console by IP from the empty state.</summary>
    public bool IsAddByIpRequested { get; private set; }

    public bool HasResults => Results.Count > 0;

    public bool IsEmpty => HasSearched && !IsSearching && Results.Count == 0;

    public string SummaryText
    {
        get
        {
            if (IsSearching)
            {
                return "Searching …";
            }

            if (!HasSearched)
            {
                return "";
            }

            return Results.Count == 1 ? "Search finished · 1 console found" : $"Search finished · {Results.Count} consoles found";
        }
    }

    /// <summary>Shows results without searching, for design-time data.</summary>
    public void ShowResults(IEnumerable<ScanResultViewModel> results)
    {
        Results.Clear();

        foreach (var result in results)
        {
            Results.Add(result);
        }

        HasSearched = true;
    }

    public ScanResultViewModel CreateResult(DiscoveredConsole console)
    {
        bool isKnown = (_findDuplicate(console.Name) ?? _findDuplicate(console.Address)) != null;

        return new ScanResultViewModel(console, isKnown ? ScanResultState.Known : ScanResultState.Available, AddAsync);
    }

    public void RequestAddByIp()
    {
        IsAddByIpRequested = true;
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        Results.Clear();

        try
        {
            var found = await ConsoleDiscovery.DiscoverAsync(SearchTime);

            ShowResults(found.Select(CreateResult));
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !IsSearching;

    private async Task AddAsync(ScanResultViewModel result)
    {
        result.State = ScanResultState.Adding;
        result.Error = null;

        // The IP is used as target, it does not depend on name resolution by broadcast
        var test = await ConsoleConnectionTest.RunAsync(_worker, result.Console.Address);

        if (!test.IsSuccess)
        {
            result.Error = "Could not connect";
            result.State = ScanResultState.Failed;

            return;
        }

        await _addConsole(new SavedConsole { Target = test.Target, Name = test.Name, Address = test.Address });

        result.State = ScanResultState.Added;
    }
}
