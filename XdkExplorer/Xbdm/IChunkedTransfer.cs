namespace XdkExplorer.Xbdm;

/// <summary>
/// A transfer the worker drives one chunk at a time, so other commands can run in between.
/// All members are called on the worker thread.
/// </summary>
public interface IChunkedTransfer
{
    long TotalBytes { get; }

    long TransferredBytes { get; }

    void Begin();

    /// <summary>Moves one chunk. Returns true once the transfer is complete.</summary>
    bool Step();

    /// <summary>Releases the connection. Must not wait for the console, it blocks the worker.</summary>
    void Abort();

    /// <summary>One attempt to remove the partially written target after Abort. Returns true once it is gone.</summary>
    bool TryRemovePartialTarget();
}
