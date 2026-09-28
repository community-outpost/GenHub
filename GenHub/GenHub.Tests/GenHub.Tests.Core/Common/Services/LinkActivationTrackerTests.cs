using GenHub.Common.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="LinkActivationTracker"/>.
/// </summary>
public class LinkActivationTrackerTests
{
    /// <summary>
    /// Verifies that recording a link cancels the link token so an open Getting Started dialog can close.
    /// </summary>
    [Fact]
    public void RecordLink_CancelsLinkReceivedToken()
    {
        using var tracker = new LinkActivationTracker();
        var token = tracker.LinkReceivedToken;

        tracker.RecordLink();

        Assert.True(tracker.HasReceivedLink);
        Assert.True(token.IsCancellationRequested);
    }

    /// <summary>
    /// Verifies that only macOS waits for the host to report that launching finished.
    /// </summary>
    [Fact]
    public void WaitForLaunchFinishedAsync_CompletesImmediatelyExceptOnMacOS()
    {
        using var tracker = new LinkActivationTracker();

        var waiting = tracker.WaitForLaunchFinishedAsync(CancellationToken.None);

        Assert.Equal(!OperatingSystem.IsMacOS(), waiting.IsCompleted);
    }

    /// <summary>
    /// Verifies that marking the launch finished releases waiters.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task MarkLaunchFinished_ReleasesWaitersAsync()
    {
        using var tracker = new LinkActivationTracker(launchFinished: false);
        var waiting = tracker.WaitForLaunchFinishedAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        tracker.MarkLaunchFinished();

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
