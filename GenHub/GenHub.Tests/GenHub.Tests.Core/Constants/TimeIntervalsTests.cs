using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for TimeIntervals constants.
/// </summary>
public class TimeIntervalsTests
{
    /// <summary>
    /// Tests that UpdaterTimeout is 10 minutes.
    /// </summary>
    [Fact]
    public void UpdaterTimeout_ShouldBe10Minutes()
    {
        Assert.Equal(10, TimeIntervals.UpdaterTimeout.TotalMinutes);
    }

    /// <summary>
    /// Tests that DownloadTimeout is 30 minutes.
    /// </summary>
    [Fact]
    public void DownloadTimeout_ShouldBe30Minutes()
    {
        Assert.Equal(30, TimeIntervals.DownloadTimeout.TotalMinutes);
    }

    /// <summary>
    /// Tests that NotificationHideDelay is 3000 milliseconds.
    /// </summary>
    [Fact]
    public void NotificationHideDelay_ShouldBe3000Milliseconds()
    {
        Assert.Equal(3000, TimeIntervals.NotificationHideDelay.TotalMilliseconds);
    }
}
