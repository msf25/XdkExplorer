using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XdkExplorer.ViewModels;

/// <summary>Queue of transfer jobs. At most two run at the same time, the worker interleaves their chunks.</summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    private const int MaxConcurrentJobs = 2;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; } = true;

    /// <summary>A job created, finished or removed something on a console. Also raised after the job left the list.</summary>
    public event EventHandler<RemoteChange>? RemoteContentChanged;

    /// <summary>A job completed, failed for good or was cancelled.</summary>
    public event EventHandler<TransferJobViewModel>? JobFinished;

    public ObservableCollection<TransferJobViewModel> Jobs { get; } = new();

    public bool HasJobs => Jobs.Count > 0;

    public bool HasFinished => Jobs.Any(j => j.IsFinished);

    public bool HasActive => ActiveJob != null;

    /// <summary>The job shown in the status bar and the collapsed panel.</summary>
    public TransferJobViewModel? ActiveJob => Jobs.FirstOrDefault(j => j.State == TransferState.Running);

    public string Summary
    {
        get
        {
            int running = Jobs.Count(j => j.State is TransferState.Running or TransferState.Preparing or TransferState.Error);
            int waiting = Jobs.Count(j => j.State == TransferState.Waiting);

            if (running == 0 && waiting == 0)
            {
                return Jobs.Count == 0 ? "None" : "All done";
            }

            return waiting == 0 ? $"{running} active" : $"{running} active · {waiting} waiting";
        }
    }

    public void Enqueue(TransferJobViewModel job)
    {
        job.PropertyChanged += OnJobChanged;
        job.Finished += (_, _) =>
        {
            StartWaitingJobs();
            JobFinished?.Invoke(this, job);
        };
        job.DismissRequested += (_, _) => Remove(job);
        job.RemoteContentChanged += (_, change) => RemoteContentChanged?.Invoke(this, change);

        Jobs.Add(job);
        IsOpen = true;

        RaiseSummary();
        StartWaitingJobs();
    }

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList())
        {
            Remove(job);
        }
    }

    private void Remove(TransferJobViewModel job)
    {
        job.PropertyChanged -= OnJobChanged;
        Jobs.Remove(job);

        RaiseSummary();
    }

    private void StartWaitingJobs()
    {
        int running = Jobs.Count(j => (j.IsActive && !j.IsWaiting) || j.IsError);

        foreach (var job in Jobs.Where(j => j.IsWaiting).ToList())
        {
            if (running >= MaxConcurrentJobs)
            {
                break;
            }

            running++;

            _ = job.RunAsync();
        }
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TransferJobViewModel.State) or nameof(TransferJobViewModel.Progress))
        {
            RaiseSummary();
        }
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(ActiveJob));
    }
}
