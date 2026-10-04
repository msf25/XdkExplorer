using System.Diagnostics;
using static XdkExplorer.Xbdm.XboxDbgNative;

namespace XdkExplorer.Xbdm;

/// <summary>
/// Owns the only thread that calls into xboxdbg.dll. The DLL keeps the target console
/// in process-wide state, so every call is serialized here and tagged with its console.
///
/// Commands always run before the next transfer chunk. Active transfers are stepped
/// round-robin, so browsing stays responsive while uploads are running.
/// </summary>
public sealed class XbdmWorker : IDisposable
{
    private const uint ConnectTimeoutMs = 5000;

    // Must cover the wait for the sendfile status after the last chunk. On xemu, which receives
    // at about 0.13 MB/s, up to 1 MB can still sit in the socket buffer at that point.
    private const uint ConversationTimeoutMs = 15000;

    private readonly Thread _thread;
    private readonly object _lock = new();
    private readonly Queue<Action> _commands = new();
    private readonly List<ActiveTransfer> _transfers = new();
    private readonly AutoResetEvent _signal = new(false);
    private string? _selectedConsole;
    private int _nextTransfer;
    private bool _disposed;

    public XbdmWorker()
    {
        _thread = new Thread(Run)
        {
            Name = "XBDM worker",
            IsBackground = true
        };

        _thread.Start();

        // Without a conversation timeout a console that stops answering mid-transfer blocks the worker forever
        Enqueue(() => DmSetConnectionTimeout(ConnectTimeoutMs, ConversationTimeoutMs));
    }

    /// <param name="console">Name or IP of the target console, or null for calls that do not talk to a console.</param>
    public Task<T> RunAsync<T>(string? console, Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Enqueue(() =>
        {
            try
            {
                SelectConsole(console);
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    public Task RunAsync(string? console, Action action)
    {
        return RunAsync(console, () =>
        {
            action();

            return true;
        });
    }

    /// <param name="progress">Invoked on the worker thread after every chunk with (transferred, total).</param>
    public Task RunTransferAsync(string console, IChunkedTransfer transfer,
        Action<long, long>? progress = null, CancellationToken cancellationToken = default)
    {
        var active = new ActiveTransfer(console, transfer, progress, cancellationToken);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _transfers.Add(active);
        }

        // Wake the worker so a cancelled transfer is cleaned up even while nothing else is queued
        active.CancellationRegistration = cancellationToken.Register(() => _signal.Set());

        _signal.Set();

        return active.Completion.Task;
    }

    /// <summary>
    /// Removes what an aborted transfer left behind. Retries as separate commands with pauses in between,
    /// so a console that keeps the file locked for a while does not block other commands.
    /// Returns false if the target still exists after the timeout.
    /// </summary>
    public async Task<bool> RemovePartialTargetAsync(string console, IChunkedTransfer transfer, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                // Nothing here needs the caller's thread, and blocking callers must not deadlock
                if (await RunAsync(console, transfer.TryRemovePartialTarget).ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (XbdmException)
            {
                // Console unreachable for the moment, try again until the timeout
            }

            if (elapsed.Elapsed > timeout)
            {
                return false;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _signal.Set();
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void Enqueue(Action command)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _commands.Enqueue(command);
        }

        _signal.Set();
    }

    private void Run()
    {
        while (true)
        {
            Action? command = null;
            ActiveTransfer? transfer = null;

            lock (_lock)
            {
                if (_disposed)
                {
                    break;
                }

                if (_commands.Count > 0)
                {
                    command = _commands.Dequeue();
                }
                else if (_transfers.Count > 0)
                {
                    _nextTransfer %= _transfers.Count;
                    transfer = _transfers[_nextTransfer];
                    _nextTransfer++;
                }
            }

            if (command != null)
            {
                command();
            }
            else if (transfer != null)
            {
                StepTransfer(transfer);
            }
            else
            {
                _signal.WaitOne();
            }
        }

        AbortAllTransfers();
    }

    private void StepTransfer(ActiveTransfer active)
    {
        if (active.CancellationToken.IsCancellationRequested)
        {
            Abort(active);
            active.Completion.TrySetCanceled(active.CancellationToken);

            return;
        }

        try
        {
            if (!active.IsStarted)
            {
                SelectConsole(active.Console);

                active.IsStarted = true;
                active.Transfer.Begin();
            }

            bool isDone = active.Transfer.Step();

            active.Progress?.Invoke(active.Transfer.TransferredBytes, active.Transfer.TotalBytes);

            if (isDone)
            {
                Remove(active);
                active.Completion.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            Abort(active);
            active.Completion.TrySetException(ex);
        }
    }

    private void Abort(ActiveTransfer active)
    {
        Remove(active);

        if (!active.IsStarted)
        {
            return;
        }

        try
        {
            active.Transfer.Abort();
        }
        catch
        {
            // Closing is best effort, the original error is what matters
        }
    }

    private void AbortAllTransfers()
    {
        List<ActiveTransfer> remaining;

        lock (_lock)
        {
            remaining = new List<ActiveTransfer>(_transfers);
        }

        foreach (var active in remaining)
        {
            Abort(active);
            active.Completion.TrySetCanceled();
        }
    }

    private void Remove(ActiveTransfer active)
    {
        lock (_lock)
        {
            _transfers.Remove(active);
        }

        active.CancellationRegistration.Dispose();
    }

    private void SelectConsole(string? console)
    {
        if (console == null || string.Equals(console, _selectedConsole, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ThrowIfFailed(DmSetXboxNameNoRegister(console), "DmSetXboxNameNoRegister");

        _selectedConsole = console;
    }

    private sealed class ActiveTransfer
    {
        public ActiveTransfer(string console, IChunkedTransfer transfer, Action<long, long>? progress, CancellationToken cancellationToken)
        {
            Console = console;
            Transfer = transfer;
            Progress = progress;
            CancellationToken = cancellationToken;
        }

        public string Console { get; }

        public IChunkedTransfer Transfer { get; }

        public Action<long, long>? Progress { get; }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsStarted { get; set; }

        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }
}
