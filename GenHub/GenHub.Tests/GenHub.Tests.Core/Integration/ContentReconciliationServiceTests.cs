using System;
using System.Collections.Generic;
using System.IO;
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
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Integration;

/// <summary>
/// Tests for the <see cref="ContentReconciliationService"/>.
/// </summary>
public class ContentReconciliationServiceTests
{
    private readonly Mock<IGameProfileManager> _profileManagerMock;
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<ICasLifecycleManager> _casServiceMock;
    private readonly Mock<ILogger<ContentReconciliationService>> _loggerMock;
    private readonly Mock<ICasReferenceTracker> _casReferenceTrackerMock;
    private readonly ContentReconciliationService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentReconciliationServiceTests"/> class.
    /// </summary>
    public ContentReconciliationServiceTests()
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

        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));
    }

    /// <summary>
    /// Verifies that profile update orchestration correctly adds new manifest to pool and updates affected profiles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task OrchestrateLocalUpdateAsync_WhenIdChanges_ShouldAddManifestToPool_AndUpdateProfiles()
    {
        // Arrange
        var oldId = "1.0.local.gameclient.old";
        var newId = "1.0.local.gameclient.new";

        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create(newId),
            Name = "New Content",
            Version = "1.0",
            TargetGame = GenHub.Core.Models.Enums.GameType.ZeroHour,
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
        };

        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Test Profile",
            GameClient = new GameClient { Id = oldId, Name = "Old Content" },
            EnabledContentIds = [],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));

        _profileManagerMock.Setup(x => x.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Mock GetManifestAsync to return the new manifest (simulating successful addition)
        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == newId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(newManifest));

        // Act
        var result = await _service.OrchestrateLocalUpdateAsync(oldId, newManifest);

        // Assert
        result.Success.Should().BeTrue(); // The orchestration itself succeeds (best effort)

        // 1. Verify AddManifest called
        _manifestPoolMock.Verify(x => x.AddManifestAsync(newManifest, It.IsAny<CancellationToken>()), Times.Once);

        // 2. Verify UpdateProfileAsync IS called
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                "profile-1",
                It.Is<UpdateProfileRequest>(r => r.GameClient != null && r.GameClient.Id == newId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Should update profile with new manifest ID");
    }

    /// <summary>
    /// Verifies that profile update orchestration fails if manifest tracking fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task OrchestrateLocalUpdateAsync_WhenTrackingFails_ShouldReturnFailure()
    {
        // Arrange
        var oldId = "1.0.local.gameclient.old";
        var newId = "1.0.local.gameclient.new";

        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create(newId),
            Name = "New Content",
            Version = "1.0",
            TargetGame = GenHub.Core.Models.Enums.GameType.ZeroHour,
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
        };

        _casReferenceTrackerMock.Setup(x => x.TrackManifestReferencesAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateFailure("Tracking failed"));

        // Act
        var result = await _service.OrchestrateLocalUpdateAsync(oldId, newManifest);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Failed to track CAS references");

        // Verify UpdateProfileAsync is NEVER called
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                It.IsAny<string>(),
                It.IsAny<UpdateProfileRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that bulk update orchestration skips specific manifests if manifest pool returns null SUCCESS.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task OrchestrateBulkUpdateAsync_WhenManifestIsNull_ShouldSkipSpecificManifests()
    {
        // Arrange
        var oldId = "1.0.test.mod.old";
        var newId = "1.0.test.mod.new";
        var replacements = new Dictionary<string, string> { { oldId, newId } };

        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Test Profile",
            EnabledContentIds = [oldId],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));

        // Mock GetManifestAsync to return SUCCESS with NULL
        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == newId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

        // Act
        var result = await _service.OrchestrateBulkUpdateAsync(replacements);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.ProfilesUpdated.Should().Be(0);

        // Verify UpdateProfileAsync is NEVER called
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                It.IsAny<string>(),
                It.IsAny<UpdateProfileRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that bulk update orchestration skips specific manifests if they cannot be resolved from pool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task OrchestrateBulkUpdateAsync_WhenManifestResolutionFails_ShouldSkipSpecificManifests()
    {
        // Arrange
        var oldId = "1.0.test.mod.old";
        var newId = "1.0.test.mod.new";
        var replacements = new Dictionary<string, string> { { oldId, newId } };

        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Test Profile",
            EnabledContentIds = [oldId],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));

        // Mock GetManifestAsync to FAIL
        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == newId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        // Act
        var result = await _service.OrchestrateBulkUpdateAsync(replacements);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.ProfilesUpdated.Should().Be(0);

        // Verify UpdateProfileAsync is NEVER called
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                It.IsAny<string>(),
                It.IsAny<UpdateProfileRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}