using System.Diagnostics;

namespace XdkExplorer.Services;

/// <summary>
/// Transfer speed over a sliding window. A window instead of the average since start, because the first
/// chunks of an upload only land in the socket buffer and would make the early rate look far too high.
/// </summary>
public sealed class RateMeter
{
    private readonly double _windowSeconds;
    private readonly Queue<(double Seconds, long Bytes)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public RateMeter(double windowSeconds = 3)
    {
        _windowSeconds = windowSeconds;
    }

    /// <summary>Records the running total and returns bytes per second, or null while there is too little data.</summary>
    public double? Sample(long totalBytes)
    {
        double now = _clock.Elapsed.TotalSeconds;

        _samples.Enqueue((now, totalBytes));

        while (_samples.Count > 2 && now - _samples.Peek().Seconds > _windowSeconds)
        {
            _samples.Dequeue();
        }

        var (oldestTime, oldestBytes) = _samples.Peek();
        double elapsed = now - oldestTime;

        if (elapsed < 0.5)
        {
            return null;
        }

        double rate = (totalBytes - oldestBytes) / elapsed;

        return rate > 0 ? rate : null;
    }
}
