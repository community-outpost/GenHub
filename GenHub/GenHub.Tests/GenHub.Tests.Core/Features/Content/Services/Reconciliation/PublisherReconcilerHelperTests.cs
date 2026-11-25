using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Reconciliation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Reconciliation;

/// <summary>
/// Tests for <see cref="PublisherReconcilerHelper"/> create-new-profile update handling.
/// </summary>
public sealed class PublisherReconcilerHelperTests
{
    private const string OldManifestId = "1.0.test.gameclient.old";
    private const string NewManifestId = "1.0.test.gameclient.new";
    private const string TriggeringProfileId = "triggering-profile";

    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    /// <summary>
    /// Verifies that a profile storage failure surfaces as a failure result instead of a silent success.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateNewProfilesForUpdateAsync_WhenGetAllProfilesFails_ReturnsFailureAsync()
    {
        // Arrange
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateFailure("Storage unavailable"));

        // Act
        var result = await PublisherReconcilerHelper.CreateNewProfilesForUpdateAsync(CreateArgs(), CreateContext(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Storage unavailable", result.FirstError);
    }

    /// <summary>
    /// Verifies that zero successful clones despite relevant profiles surfaces as a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateNewProfilesForUpdateAsync_WhenAllClonesFail_ReturnsFailureAsync()
    {
        // Arrange
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([CreateRelevantProfile()]));
        _profileManagerMock
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Clone failed"));

        // Act
        var result = await PublisherReconcilerHelper.CreateNewProfilesForUpdateAsync(CreateArgs(), CreateContext(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that a successful clone reports its count and the cloned triggering profile id.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateNewProfilesForUpdateAsync_WhenCloneSucceeds_ReturnsCountAndTargetAsync()
    {
        // Arrange
        const string clonedProfileId = "cloned-profile";
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([CreateRelevantProfile()]));
        _profileManagerMock
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = clonedProfileId, Name = "Cloned" }));

        // Act
        var result = await PublisherReconcilerHelper.CreateNewProfilesForUpdateAsync(CreateArgs(), CreateContext(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.CreatedCount);
        Assert.Equal(clonedProfileId, result.Data.TargetProfileId);
    }

    /// <summary>
    /// Verifies that a create-new-profile storage failure flags AnyFailure and warns instead of reporting success.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplyUpdateStrategyAsync_WhenCreateNewProfilesFails_FlagsAnyFailureAsync()
    {
        // Arrange
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateFailure("Storage unavailable"));

        // Act
        var result = await PublisherReconcilerHelper.ApplyUpdateStrategyAsync(CreateArgs(), CreateContext(), CancellationToken.None);

        // Assert
        Assert.True(result.AnyFailure);
        Assert.Equal(0, result.ProfilesUpdated);
        Assert.Null(result.TargetProfileId);
        _notificationServiceMock.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    private static GameProfile CreateRelevantProfile()
    {
        return new GameProfile
        {
            Id = TriggeringProfileId,
            Name = "Triggering Profile",
            GameClient = new GameClient
            {
                Id = OldManifestId,
                Name = "Old Client",
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                ExecutablePath = "generals.exe",
            },
            GameInstallationId = "inst-1",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = [OldManifestId],
        };
    }

    private static UpdateStrategyExecutionArgs CreateArgs()
    {
        var oldManifest = new ContentManifest
        {
            Id = ManifestId.Create(OldManifestId),
            Name = "Old Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create(NewManifestId),
            Name = "New Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "2.0",
        };
        return new UpdateStrategyExecutionArgs(
            UpdateStrategy.CreateNewProfile,
            [oldManifest],
            [newManifest],
            new Dictionary<string, string> { [OldManifestId] = NewManifestId },
            "2.0",
            false,
            TriggeringProfileId);
    }

    private PublisherReconciliationContext CreateContext()
    {
        return new PublisherReconciliationContext(
            _profileManagerMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            NullLogger.Instance,
            "Test Publisher",
            "[Test]");
    }
}
