using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
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
}
