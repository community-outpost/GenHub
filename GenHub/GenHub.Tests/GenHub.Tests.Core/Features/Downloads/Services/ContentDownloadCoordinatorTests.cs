using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Services;

/// <summary>
/// Unit tests for <see cref="ContentDownloadCoordinator"/>.
/// </summary>
public sealed class ContentDownloadCoordinatorTests
{
    /// <summary>
    /// Verifies that concurrent calls to download the exact same content are deduplicated to a single acquisition task.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_ConcurrentCallsSameContent_DeduplicatesToSingleAcquisitionAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.185.moddb.mod.rotr"),
            Name = "Rise of the Reds",
        };

        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult1 = new ContentSearchResult
        {
            Id = "moddb_rotr_185",
            Name = "Rise of the Reds",
            ProviderName = "ModDB",
        };

        var searchResult2 = new ContentSearchResult
        {
            Id = "moddb_rotr_185",
            Name = "Rise of the Reds",
            ProviderName = "ModDB",
        };

        // Act: Start two downloads concurrently
        var task1 = coordinator.DownloadContentAsync(searchResult1);
        var task2 = coordinator.DownloadContentAsync(searchResult2);

        // Complete the single in-flight acquisition
        acquisitionTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));

        var result1 = await task1;
        var result2 = await task2;

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Same(result1.Data, result2.Data);

        // Verify AcquireContentAsync was only invoked once
        orchestratorMock.Verify(
            x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when a second caller cancels its own token, it unblocks immediately without cancelling the first caller's download.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_SecondCallerCancels_DoesNotBlockOrCancelFirstCallerAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.moddb.mod.test"),
            Name = "Test Mod",
        };

        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "test_item_1",
            Name = "Test Mod",
            ProviderName = "ModDB",
        };

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        // Caller 1 starts
        var task1 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts1.Token);

        // Caller 2 joins
        var task2 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts2.Token);

        // Caller 2 cancels its token
        cts2.Cancel();

        // Caller 2 should throw OperationCanceledException immediately
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task2);

        // Caller 1 is still in flight
        Assert.False(task1.IsCompleted);

        // Now complete download for caller 1
        acquisitionTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));
        var result1 = await task1;

        Assert.True(result1.Success);
        Assert.Same(testManifest, result1.Data);
    }

    /// <summary>
    /// Verifies that when the initiator caller cancels its token, it unblocks immediately without cancelling the second caller or the underlying download.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_FirstCallerCancels_DoesNotCancelSecondCallerAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.moddb.mod.testinitiator"),
            Name = "Test Mod Initiator",
        };

        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "test_item_initiator_cancel",
            Name = "Test Mod Initiator",
            ProviderName = "ModDB",
        };

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        // Caller 1 (initiator) starts
        var task1 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts1.Token);

        // Caller 2 (joiner) joins
        var task2 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts2.Token);

        // Caller 1 (initiator) cancels
        cts1.Cancel();

        // Caller 1 throws OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);

        // Caller 2 is still waiting
        Assert.False(task2.IsCompleted);

        // Underlying download completes successfully
        acquisitionTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));

        var result2 = await task2;
        Assert.True(result2.Success);
        Assert.Same(testManifest, result2.Data);
    }

    /// <summary>
    /// Verifies that when all callers cancel, the underlying acquisition is cancelled via its internal token.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_AllCallersCancel_CancelsUnderlyingAcquisitionAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        CancellationToken internalToken = default;
        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((_, _, ct) => internalToken = ct)
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "test_item_all_cancel",
            Name = "Test Mod All Cancel",
            ProviderName = "ModDB",
        };

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        var task1 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts1.Token);
        var task2 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts2.Token);

        cts1.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);

        Assert.False(internalToken.IsCancellationRequested);

        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task2);

        Assert.True(internalToken.IsCancellationRequested);
    }

    /// <summary>
    /// Verifies that progress is multiplexed to both the initial caller and subsequent concurrent callers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_ConcurrentCallers_MultiplexesProgressToBothAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.moddb.mod.test"),
            Name = "Test Mod",
        };

        IProgress<ContentAcquisitionProgress>? capturedProgress = null;
        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((_, p, _) => capturedProgress = p)
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "test_item_2",
            Name = "Test Mod 2",
            ProviderName = "ModDB",
        };

        var progressList1 = new List<double>();
        var progressList2 = new List<double>();

        var p1 = new Progress<ContentAcquisitionProgress>(p => progressList1.Add(p.ProgressPercentage));
        var p2 = new Progress<ContentAcquisitionProgress>(p => progressList2.Add(p.ProgressPercentage));

        var task1 = coordinator.DownloadContentAsync(searchResult, p1);
        var task2 = coordinator.DownloadContentAsync(searchResult, p2);

        Assert.NotNull(capturedProgress);
        capturedProgress.Report(new ContentAcquisitionProgress { ProgressPercentage = 42 });

        // Allow any async dispatcher/post if needed
        await Task.Delay(50);

        acquisitionTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));
        await Task.WhenAll(task1, task2);

        Assert.Contains(42, progressList1);
        Assert.Contains(42, progressList2);
    }

    /// <summary>
    /// Verifies that IsDownloading returns true while a download is running and false after completion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsDownloading_ReturnsTrueWhileRunning_AndFalseWhenCompletedAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.mod.test"),
            Name = "Test Active Item",
        };

        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();
        IProgress<ContentAcquisitionProgress>? capturedProgress = null;

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((_, p, _) => capturedProgress = p)
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "test_active_item_1",
            Name = "Test Active Item",
            ProviderName = "ModDB",
        };

        Assert.False(coordinator.IsDownloading(searchResult));

        // Act
        var downloadTask = coordinator.DownloadContentAsync(searchResult);

        // Assert while running
        Assert.True(coordinator.IsDownloading(searchResult));

        // Report progress
        capturedProgress?.Report(new ContentAcquisitionProgress { ProgressPercentage = 55, CurrentOperation = "Downloading files..." });
        await Task.Delay(50);

        var hasProgress = coordinator.TryGetDownloadProgress(searchResult, out var pct, out var msg);
        Assert.True(hasProgress);
        Assert.Equal(55, pct);

        // Complete download
        acquisitionTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));
        await downloadTask;

        // Assert after completion
        Assert.False(coordinator.IsDownloading(searchResult));
    }

    /// <summary>
    /// Verifies that WeakReferenceMessenger broadcasts started, progress, and completed messages.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_BroadcastsStartedProgressAndCompletedMessagesAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.broadcast.mod.test"),
            Name = "Broadcast Mod",
        };

        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();
        IProgress<ContentAcquisitionProgress>? capturedProgress = null;

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((_, p, _) => capturedProgress = p)
            .Returns(acquisitionTcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "broadcast_test_1",
            Name = "Broadcast Mod",
            ProviderName = "ModDB",
        };

        var startedCount = 0;
        var progressCount = 0;
        var completedCount = 0;

        WeakReferenceMessenger.Default.Register<ContentDownloadStartedMessage>(coordinator, (_, m) =>
        {
            if (m.Matches(searchResult)) startedCount++;
        });
        WeakReferenceMessenger.Default.Register<ContentDownloadProgressMessage>(coordinator, (_, m) =>
        {
            if (m.Matches(searchResult)) progressCount++;
        });
        WeakReferenceMessenger.Default.Register<ContentDownloadCompletedMessage>(coordinator, (_, m) =>
        {
            if (m.Matches(searchResult)) completedCount++;
        });

        try
        {
            // Act
            var downloadTask = coordinator.DownloadContentAsync(searchResult);

            await Task.Delay(50);
            Assert.Equal(1, startedCount);

            capturedProgress?.Report(new ContentAcquisitionProgress { ProgressPercentage = 30 });
            await Task.Delay(50);
            Assert.True(progressCount >= 1);

            acquisitionTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));
            await downloadTask;

            await Task.Delay(50);
            Assert.Equal(1, completedCount);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<ContentDownloadStartedMessage>(coordinator);
            WeakReferenceMessenger.Default.Unregister<ContentDownloadProgressMessage>(coordinator);
            WeakReferenceMessenger.Default.Unregister<ContentDownloadCompletedMessage>(coordinator);
        }
    }

    /// <summary>
    /// Verifies that when a download is cancelled by its waiter and then immediately retried,
    /// the coordinator waits for the cancelled download to unwind before starting a fresh acquire,
    /// rather than running concurrent acquires for the same key (Finding 3).
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_WhenCancelledAndImmediatelyRetried_AwaitsUnwindAndStartsFreshAcquireAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.retry.mod.test"),
            Name = "Retry Mod",
        };

        var firstTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();
        var secondTcs = new TaskCompletionSource<OperationResult<ContentManifest>>();
        var callCount = 0;

        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Returns<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((_, _, ct) =>
            {
                var current = Interlocked.Increment(ref callCount);
                if (current == 1)
                {
                    ct.Register(() => firstTcs.TrySetCanceled(ct));
                    return firstTcs.Task;
                }

                return secondTcs.Task;
            });

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "retry_test_1",
            Name = "Retry Mod",
            ProviderName = "ModDB",
        };

        // Act 1: Start first download and cancel it immediately
        using var cts1 = new CancellationTokenSource();
        var downloadTask1 = coordinator.DownloadContentAsync(searchResult, cancellationToken: cts1.Token);
        cts1.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask1);

        // Act 2: Immediately retry the same download
        var downloadTask2 = coordinator.DownloadContentAsync(searchResult);

        // Complete the second acquire
        secondTcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));
        var result = await downloadTask2;

        // Assert
        Assert.True(result.Success);
        Assert.Equal(testManifest.Id, result.Data?.Id);
        Assert.Equal(2, callCount);
    }

    /// <summary>
    /// Verifies that HasActiveDownloads returns true while a download is in-flight and false otherwise.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task HasActiveDownloads_TracksInFlightAcquisitionStateAsync()
    {
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var stateServiceMock = new Mock<IContentStateService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var testManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1.github.addon.improvedmenusenglish"),
            Name = "ImprovedMenus (English)",
        };

        var tcs = new TaskCompletionSource<OperationResult<ContentManifest>>();
        orchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var coordinator = new ContentDownloadCoordinator(
            orchestratorMock.Object,
            stateServiceMock.Object,
            notificationServiceMock.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);

        Assert.False(coordinator.HasActiveDownloads);

        var sr = new ContentSearchResult { Id = "test-1", Name = "Test" };
        var downloadTask = coordinator.DownloadContentAsync(sr);

        Assert.True(coordinator.HasActiveDownloads);

        tcs.SetResult(OperationResult<ContentManifest>.CreateSuccess(testManifest));
        await downloadTask;

        Assert.False(coordinator.HasActiveDownloads);
    }
}
