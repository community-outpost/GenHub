using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using System.Diagnostics;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="DownloadProgressThrottle"/>.
/// </summary>
public sealed class DownloadProgressThrottleTests
{
    /// <summary>
    /// Verifies that the first report always passes through.
    /// </summary>
    [Fact]
    public void ShouldForward_FirstReport_AlwaysForwards()
    {
        var throttle = new DownloadProgressThrottle(minIntervalMs: 60000);

        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Downloading, 10, Stopwatch.GetTimestamp()));
    }

    /// <summary>
    /// Verifies that rapid same-phase reports are dropped.
    /// </summary>
    [Fact]
    public void ShouldForward_RapidSamePhaseReports_Drops()
    {
        var throttle = new DownloadProgressThrottle(minIntervalMs: 60000);
        var now = Stopwatch.GetTimestamp();

        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Extracting, 50, now));
        Assert.False(throttle.ShouldForward(ContentAcquisitionPhase.Extracting, 51, now + (Stopwatch.Frequency / 1000)));
    }

    /// <summary>
    /// Verifies that a phase change always passes through.
    /// </summary>
    [Fact]
    public void ShouldForward_PhaseChange_AlwaysForwards()
    {
        var throttle = new DownloadProgressThrottle(minIntervalMs: 60000);
        var now = Stopwatch.GetTimestamp();

        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Downloading, 40, now));
        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Extracting, 41, now));
    }

    /// <summary>
    /// Verifies that completion always passes through.
    /// </summary>
    [Fact]
    public void ShouldForward_Completion_AlwaysForwards()
    {
        var throttle = new DownloadProgressThrottle(minIntervalMs: 60000);
        var now = Stopwatch.GetTimestamp();

        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Extracting, 85, now));
        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Extracting, 100, now));
    }

    /// <summary>
    /// Verifies that steady-state reports pass again after the interval elapses.
    /// </summary>
    [Fact]
    public void ShouldForward_AfterInterval_Forwards()
    {
        var throttle = new DownloadProgressThrottle(minIntervalMs: 150);
        var now = Stopwatch.GetTimestamp();

        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Downloading, 10, now));
        Assert.True(throttle.ShouldForward(ContentAcquisitionPhase.Downloading, 11, now + Stopwatch.Frequency));
    }
}
