using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Integration;

/// <summary>
/// Integration tests for content reconciliation concurrency.
/// </summary>
public class ContentReconciliationConcurrencyTests
{
    private readonly Mock<IGameProfileManager> _profileManagerMock;
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<ICasLifecycleManager> _casServiceMock;
    private readonly Mock<ILogger<ContentReconciliationService>> _loggerMock;
    private readonly Mock<ICasReferenceTracker> _casReferenceTrackerMock;
    private readonly ContentReconciliationService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentReconciliationConcurrencyTests"/> class.
    /// </summary>
    public ContentReconciliationConcurrencyTests()
    {
        _profileManagerMock = new Mock<IGameProfileManager>();
        _workspaceManagerMock = new Mock<IWorkspaceManager>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _casServiceMock = new Mock<ICasLifecycleManager>();
        _loggerMock = new Mock<ILogger<ContentReconciliationService>>();
        _casReferenceTrackerMock = new Mock<ICasReferenceTracker>();

        _service = new ContentReconciliationService(
            _profileManagerMock.Object,
            _workspaceManagerMock.Object,
            _manifestPoolMock.Object,
            _casReferenceTrackerMock.Object,
            _casServiceMock.Object,
            _loggerMock.Object);

        // Default mock behaviors
        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        _casReferenceTrackerMock.Setup(x => x.TrackManifestReferencesAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _casReferenceTrackerMock.Setup(x => x.UntrackManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _manifestPoolMock.Setup(x => x.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _manifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }

    /// <summary>
    /// Verifies that concurrent bulk manifest replacement calls are serialized.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ReconcileBulkManifestReplacementAsync_ShouldSerializeConcurrentCalls()
    {
        // Arrange
        var callCounter = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                int current;
                lock (lockObj)
                {
                    callCounter++;
                    current = callCounter;
                    if (current > maxConcurrent) maxConcurrent = current;
                }

                await Task.Delay(100);

                lock (lockObj)
                {
                    callCounter--;
                }

                return ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]);
            });

        var replacements = new Dictionary<string, ContentManifest>
        {
            { "old1", new ContentManifest { Id = ManifestId.Create("1.0.0.mock.test") } },
        };

        // Act
        var task1 = _service.ReconcileBulkManifestReplacementAsync(replacements);
        var task2 = _service.ReconcileBulkManifestReplacementAsync(replacements);

        await Task.WhenAll(task1, task2);

        // Assert
        task1.Result.Success.Should().BeTrue();
        task2.Result.Success.Should().BeTrue();
        maxConcurrent.Should().Be(1);
    }
}
