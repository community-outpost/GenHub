using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using System;
using System.Diagnostics;

namespace GenHub.Core.Helpers;

/// <summary>
/// Throttles high-frequency download progress reports so per-file extraction and hashing
/// updates cannot flood the UI thread. The first report, every phase change, and completion
/// always pass through; steady-state reports pass at most once per throttle interval.
/// </summary>
public sealed class DownloadProgressThrottle
{
    private readonly long _minIntervalTicks;
    private long _lastForwardTimestamp;
    private ContentAcquisitionPhase? _lastPhase;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadProgressThrottle"/> class.
    /// </summary>
    /// <param name="minIntervalMs">Minimum milliseconds between steady-state forwards.</param>
    public DownloadProgressThrottle(int minIntervalMs = ManifestConstants.DownloadProgressBroadcastThrottleMs)
    {
        _minIntervalTicks = (long)(Math.Max(0, minIntervalMs) / 1000.0 * Stopwatch.Frequency);
    }

    /// <summary>
    /// Determines whether a progress report should be forwarded to UI-bound sinks.
    /// </summary>
    /// <param name="phase">The acquisition phase of the report.</param>
    /// <param name="percentage">The clamped progress percentage of the report.</param>
    /// <returns>True when the report should be forwarded; otherwise false.</returns>
    public bool ShouldForward(ContentAcquisitionPhase phase, double percentage)
    {
        return ShouldForward(phase, percentage, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Determines whether a progress report should be forwarded, using an explicit timestamp.
    /// </summary>
    /// <param name="phase">The acquisition phase of the report.</param>
    /// <param name="percentage">The clamped progress percentage of the report.</param>
    /// <param name="timestamp">The current <see cref="Stopwatch"/> timestamp.</param>
    /// <returns>True when the report should be forwarded; otherwise false.</returns>
    internal bool ShouldForward(ContentAcquisitionPhase phase, double percentage, long timestamp)
    {
        if (_lastForwardTimestamp == 0 || phase != _lastPhase || percentage >= 100)
        {
            _lastForwardTimestamp = timestamp;
            _lastPhase = phase;
            return true;
        }

        if (timestamp - _lastForwardTimestamp >= _minIntervalTicks)
        {
            _lastForwardTimestamp = timestamp;
            _lastPhase = phase;
            return true;
        }

        return false;
    }
}
