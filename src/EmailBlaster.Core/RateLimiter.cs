using System.Diagnostics;

namespace EmailBlaster.Core;

/// <summary>
/// Paces work to a target rate expressed in operations per second. A rate of 0 or less means
/// unlimited (the limiter never delays). Thread-affinity is not required, but the limiter is intended
/// to be driven by a single send loop.
/// </summary>
public sealed class RateLimiter
{
    private readonly double _minIntervalMs;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _nextSlotMs;

    /// <param name="ratePerSecond">Target operations per second; 0 or negative for unlimited.</param>
    public RateLimiter(double ratePerSecond)
    {
        _minIntervalMs = ratePerSecond <= 0 ? 0 : 1000.0 / ratePerSecond;
        _nextSlotMs = _stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>True when no pacing is applied.</summary>
    public bool IsUnlimited => _minIntervalMs <= 0;

    /// <summary>
    /// Waits until the next slot is due, then reserves the following slot. Call this immediately
    /// before each operation.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        if (IsUnlimited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var now = _stopwatch.Elapsed.TotalMilliseconds;
        var waitMs = _nextSlotMs - now;

        if (waitMs > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(waitMs), cancellationToken).ConfigureAwait(false);
        else
            cancellationToken.ThrowIfCancellationRequested();

        // Schedule the next slot relative to the slot we just consumed. If we fell behind (waitMs
        // very negative), anchor to "now" so we don't try to catch up in a burst.
        var basis = waitMs > 0 ? _nextSlotMs : now;
        _nextSlotMs = basis + _minIntervalMs;
    }
}
