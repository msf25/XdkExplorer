using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public enum TransferKind
{
    Upload,
    Download
}

public enum TransferState
{
    Waiting,
    Preparing,
    Running,
    Error,
    Completed,
    Cancelled
}

public enum ConflictChoice
{
    Replace,
    Skip,
    Cancel
}

public interface IConflictResolver
{
    /// <summary>Asks what to do with an existing target. applyToAll is offered when more conflicts may follow.</summary>
    (ConflictChoice Choice, bool ApplyToAll) Resolve(TransferKind kind, string targetPath, string consoleName, int remainingFiles);
}

/// <summary>One upload or download of files and folders, run file by file through the worker.</summary>
public sealed partial class TransferJobViewModel : ObservableObject
{
    private const int ProgressIntervalMs = 100;

    private static readonly TimeSpan PartialCleanupTimeout = TimeSpan.FromSeconds(15);

    private readonly XbdmWorker _worker;
    private readonly IConflictResolver _conflicts;
    private readonly int _chunkSize;
    private readonly IReadOnlyList<TransferSource> _sources;
    private readonly string _destination;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private TaskCompletionSource<bool>? _errorDecision;
    private ConflictChoice? _conflictPolicy;
    private long _completedBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(IsActive), nameof(IsError), nameof(IsFinished), nameof(IsCompleted),
        nameof(IsWaiting), nameof(ShowProgress), nameof(DetailText))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(RetryCommand), nameof(SkipCommand))]
    public partial TransferState State { get; set; } = TransferState.Waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BytesText), nameof(Progress))]
    public partial long TransferredBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BytesText), nameof(Progress))]
    public partial long TotalBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int CompletedFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int TotalFiles { get; set; }

    [ObservableProperty]
    public partial string CurrentFileName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFileProgressText))]
    public partial double CurrentFileProgress { get; set; }

    [ObservableProperty]
    public partial string RateText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(CancelText))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsCancelling { get; private set; }

    public TransferJobViewModel(XbdmWorker worker, IConflictResolver conflicts, ConsoleViewModel console, TransferKind kind,
        IReadOnlyList<TransferSource> sources, string destination, int chunkSize)
    {
        _worker = worker;
        _conflicts = conflicts;
        Console = console;
        Kind = kind;
        _sources = sources;
        _destination = destination;
        _chunkSize = chunkSize;
    }

    public event EventHandler? Finished;

    public event EventHandler? DismissRequested;

    /// <summary>Raised for uploads whenever a directory on the console changed: created, file finished, partial file removed.</summary>
    public event EventHandler<RemoteChange>? RemoteContentChanged;

    public ConsoleViewModel Console { get; }

    public TransferKind Kind { get; }

    public bool IsUpload => Kind == TransferKind.Upload;

    public bool IsWaiting => State == TransferState.Waiting;

    public bool IsActive => State is TransferState.Waiting or TransferState.Preparing or TransferState.Running;

    public bool IsError => State == TransferState.Error;

    public bool IsCompleted => State == TransferState.Completed;

    public bool IsFinished => State is TransferState.Completed or TransferState.Cancelled;

    public bool ShowProgress => State is TransferState.Running or TransferState.Error;

    public double Progress => TotalBytes == 0 ? 0 : (double)TransferredBytes / TotalBytes;

    public string CurrentFileProgressText => $"{CurrentFileProgress:P0}";

    public string Label
    {
        get
        {
            if (IsCancelling && State != TransferState.Cancelled)
            {
                return "Cancelling …";
            }

            return State switch
            {
                TransferState.Waiting => "Waiting",
                TransferState.Completed => "Done",
                TransferState.Cancelled => "Cancelled",
                _ => IsUpload ? "Upload" : "Download"
            };
        }
    }

    public string RouteText
    {
        get
        {
            var source = _sources.Count == 1 ? _sources[0].Path : $"{_sources.Count} items";

            return IsUpload
                ? $"{source} → {Console.DisplayName} {_destination}"
                : $"{Console.DisplayName} {source} → {_destination}";
        }
    }

    public string CancelText => IsCancelling ? "Cancelling …" : "Cancel";

    public string CountText => TotalFiles <= 1 ? "" : $"{CompletedFiles:N0} of {TotalFiles:N0} files";

    public string BytesText => $"{Format.Size(TransferredBytes)} of {Format.Size(TotalBytes)}";

    public string DetailText
    {
        get
        {
            if (!string.IsNullOrEmpty(Message))
            {
                return Message;
            }

            return State == TransferState.Preparing ? "Collecting files …" : "";
        }
    }

    public async Task RunAsync()
    {
        try
        {
            State = TransferState.Preparing;

            var items = IsUpload
                ? await TransferPlanner.PlanUploadAsync(_worker, Console.Target, _sources, _destination)
                : await TransferPlanner.PlanDownloadAsync(_worker, Console.Target, _sources, _destination);

            // Planning an upload creates the folder structure on the console
            ReportRemoteChange(_destination);

            TotalFiles = items.Count;
            TotalBytes = items.Sum(i => i.Size);
            State = TransferState.Running;

            foreach (var item in items)
            {
                await TransferWithRetryAsync(item);

                _completedBytes += item.Size;
                CompletedFiles++;
                TransferredBytes = _completedBytes;
            }

            CurrentFileName = "";
            RateText = "";
            State = TransferState.Completed;
        }
        catch (OperationCanceledException)
        {
            RateText = "";
            IsCancelling = false;
            State = TransferState.Cancelled;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            State = TransferState.Cancelled;
        }
        finally
        {
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        // The worker only notices between two blocks, so the state is shown right away
        IsCancelling = true;
        RateText = "";

        _cts.Cancel();
        _errorDecision?.TrySetCanceled();
    }

    private bool CanCancel() => (IsActive || IsError) && !IsCancelling;

    [RelayCommand(CanExecute = nameof(IsError))]
    private void Retry()
    {
        _errorDecision?.TrySetResult(true);
    }

    [RelayCommand(CanExecute = nameof(IsError))]
    private void Skip()
    {
        _errorDecision?.TrySetResult(false);
    }

    [RelayCommand]
    private void Dismiss()
    {
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task TransferWithRetryAsync(TransferItem item)
    {
        while (true)
        {
            _cts.Token.ThrowIfCancellationRequested();

            CurrentFileName = Path.GetFileName(item.Target.TrimEnd('\\'));
            CurrentFileProgress = 0;

            try
            {
                if (!await CheckConflictAsync(item))
                {
                    return;
                }

                await TransferAsync(item);

                ReportRemoteChange(XboxPath.GetParent(item.Target));

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TransferredBytes = _completedBytes;
                Message = $"{CurrentFileName}: {ex.Message}";
                State = TransferState.Error;

                _errorDecision = new TaskCompletionSource<bool>();

                bool shouldRetry = await _errorDecision.Task;

                Message = "";
                State = TransferState.Running;

                if (!shouldRetry)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Returns false if the file should be skipped.</summary>
    private async Task<bool> CheckConflictAsync(TransferItem item)
    {
        bool exists = IsUpload
            ? await _worker.RunAsync(Console.Target, () => XbdmCommands.GetAttributes(item.Target)) != null
            : File.Exists(item.Target);

        if (!exists)
        {
            return true;
        }

        var choice = _conflictPolicy;

        if (choice == null)
        {
            var (picked, applyToAll) = _conflicts.Resolve(Kind, item.Target, Console.DisplayName, TotalFiles - CompletedFiles - 1);

            choice = picked;

            if (applyToAll)
            {
                _conflictPolicy = picked;
            }
        }

        if (choice == ConflictChoice.Cancel)
        {
            _cts.Cancel();
            _cts.Token.ThrowIfCancellationRequested();
        }

        return choice == ConflictChoice.Replace;
    }

    private async Task TransferAsync(TransferItem item)
    {
        if (IsUpload && !XboxPath.IsValidName(XboxPath.GetName(item.Target)))
        {
            throw new IOException($"Name is not valid on FATX (max. {XboxPath.MaxNameLength} characters, no special characters).");
        }

        IChunkedTransfer transfer = IsUpload
            ? new FileUpload(item.Source, item.Target, _chunkSize)
            : new FileDownload(item.Source, item.Target, _chunkSize);

        var dispatcher = Application.Current.Dispatcher;
        long lastUpdate = 0;

        try
        {
            await _worker.RunTransferAsync(Console.Target, transfer,
                (done, total) =>
                {
                    // With small blocks the worker reports far more often than the UI needs to redraw
                    long now = Environment.TickCount64;

                    if (done < total && now - lastUpdate < ProgressIntervalMs)
                    {
                        return;
                    }

                    lastUpdate = now;
                    dispatcher.BeginInvoke(() => OnFileProgress(done, total));
                },
                _cts.Token);
        }
        catch
        {
            // Not awaited, the job reports the cancel or error right away
            _ = RemovePartialTargetAsync(transfer, item);

            throw;
        }
    }

    private void ReportRemoteChange(string? directory)
    {
        if (IsUpload && directory != null)
        {
            RemoteContentChanged?.Invoke(this, new RemoteChange(Console, directory));
        }
    }

    private async Task RemovePartialTargetAsync(IChunkedTransfer transfer, TransferItem item)
    {
        bool isRemoved = await _worker.RemovePartialTargetAsync(Console.Target, transfer, PartialCleanupTimeout);

        ReportRemoteChange(XboxPath.GetParent(item.Target));

        if (!isRemoved && State == TransferState.Cancelled)
        {
            Message = $"The partial file {item.Target} could not be removed.";
        }
    }

    private void OnFileProgress(long done, long total)
    {
        CurrentFileProgress = total == 0 ? 1 : (double)done / total;
        TransferredBytes = _completedBytes + done;

        if (IsCancelling)
        {
            return;
        }

        if (_rate.Sample(TransferredBytes) is double rate)
        {
            var remaining = TimeSpan.FromSeconds((TotalBytes - TransferredBytes) / rate);

            RateText = $"{Format.Rate(rate)} · {Format.Duration(remaining)} left";
        }
    }
}
