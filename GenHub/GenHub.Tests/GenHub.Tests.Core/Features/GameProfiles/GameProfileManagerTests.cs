using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Tests for <see cref="GameProfileManager"/>.
/// </summary>
public class GameProfileManagerTests
{
    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<ILogger<GameProfileManager>> _loggerMock = new();
    private readonly GameProfileManager _profileManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileManagerTests"/> class.
    /// </summary>
    public GameProfileManagerTests()
    {
        _profileManager = new GameProfileManager(
            _profileRepositoryMock.Object,
            _installationServiceMock.Object,
            _manifestPoolMock.Object,
            _gameSettingsServiceMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Should return success when installation and client exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ReturnSuccess_When_InstallationAndClientExistAsync()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var installation = CreateTestInstallation(clientId);
        var request = new CreateProfileRequest { Name = "New Profile", GameInstallationId = installation.Id, GameClientId = clientId };
        var profile = new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            GameInstallationId = installation.Id,
            GameClient = installation.AvailableGameClients.First(),
        };

        _installationServiceMock.Setup(x => x.GetInstallationAsync(installation.Id, default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var result = await _profileManager.CreateProfileAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(profile.Id, result.Data!.Id);
    }

    /// <summary>
    /// Should return failure when installation does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ReturnFailure_When_InstallationNotFoundAsync()
    {
        // Arrange
        var request = new CreateProfileRequest { Name = "New Profile", GameInstallationId = "bad-id", GameClientId = "v1" };
        _installationServiceMock.Setup(x => x.GetInstallationAsync("bad-id", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateFailure("Not found"));

        // Act
        var result = await _profileManager.CreateProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to find game installation", result.FirstError);
    }

    /// <summary>
    /// Should return failure when client not found in installation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ReturnFailure_When_ClientNotFoundInInstallationAsync()
    {
        // Arrange
        var installation = CreateTestInstallation("client-1");
        var request = new CreateProfileRequest
        {
            Name = "New Profile",
            GameInstallationId = installation.Id,
            GameClientId = "non-existent-client",
        };

        _installationServiceMock.Setup(x => x.GetInstallationAsync(installation.Id, default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        // Act
        var result = await _profileManager.CreateProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Game client not found", result.FirstError);
    }

    /// <summary>
    /// Should return failure when repository save fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ReturnFailure_When_RepositorySaveFailsAsync()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var installation = CreateTestInstallation(clientId);
        var request = new CreateProfileRequest
        {
            Name = "New Profile",
            GameInstallationId = installation.Id,
            GameClientId = clientId,
        };

        _installationServiceMock.Setup(x => x.GetInstallationAsync(installation.Id, default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Database error"));

        // Act
        var result = await _profileManager.CreateProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Database error", result.FirstError);
    }

    /// <summary>
    /// Should return success when updating an existing profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ReturnSuccess_When_ProfileExistsAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Old Name",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
        };
        var request = new UpdateProfileRequest { Name = "New Name" };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.Name == "New Name"), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.Name == "New Name"), default), Times.Once);
    }

    /// <summary>
    /// Should return failure when updating non-existent profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ReturnFailure_When_ProfileNotFoundAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var request = new UpdateProfileRequest { Name = "Updated Name" };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Profile not found"));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Profile not found", result.FirstError);
    }

    /// <summary>
    /// Should successfully delete existing profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteProfileAsync_Should_ReturnSuccess_When_ProfileExistsAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.DeleteProfileAsync(profileId);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Should return filtered manifests for available content.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAvailableContentAsync_Should_ReturnFilteredManifestsAsync()
    {
        // Arrange
        var gameClient = new GameClient { GameType = GameType.Generals };
        var manifests = new List<ContentManifest>
            {
                new() { Name = "Map Pack 1", TargetGame = GameType.Generals },
                new() { Name = "Mod 1", TargetGame = GameType.ZeroHour },
                new() { Name = "Map Pack 2", TargetGame = GameType.Generals },
            };
        _manifestPoolMock.Setup(x => x.GetAllManifestsAsync(default))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests));

        // Act
        var result = await _profileManager.GetAvailableContentAsync(gameClient);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, m => Assert.Equal(GameType.Generals, m.TargetGame));
    }

    /// <summary>
    /// Should return failure when manifest pool fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAvailableContentAsync_Should_ReturnFailure_When_ManifestPoolFailsAsync()
    {
        // Arrange
        var gameClient = new GameClient { GameType = GameType.Generals };
        _manifestPoolMock.Setup(x => x.GetAllManifestsAsync(default))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateFailure("Manifest pool error"));

        // Act
        var result = await _profileManager.GetAvailableContentAsync(gameClient);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Manifest pool error", result.FirstError);
    }

    /// <summary>
    /// Should return empty list when no compatible content found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAvailableContentAsync_Should_ReturnEmptyList_When_NoCompatibleContentAsync()
    {
        // Arrange
        var gameClient = new GameClient { GameType = GameType.Generals };
        var manifests = new List<ContentManifest>
            {
                new() { Name = "ZH Mod 1", TargetGame = GameType.ZeroHour },
                new() { Name = "ZH Mod 2", TargetGame = GameType.ZeroHour },
            };
        _manifestPoolMock.Setup(x => x.GetAllManifestsAsync(default))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests));

        // Act
        var result = await _profileManager.GetAvailableContentAsync(gameClient);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    /// <summary>
    /// Should return all profiles from repository.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllProfilesAsync_Should_ReturnAllProfilesAsync()
    {
        // Arrange
        var profiles = new List<GameProfile>
            {
                new() { Id = "1", Name = "Profile 1", GameInstallationId = "install-1", GameClient = new GameClient { Id = "client-1" } },
                new() { Id = "2", Name = "Profile 2", GameInstallationId = "install-2", GameClient = new GameClient { Id = "client-2" } },
                new() { Id = "3", Name = "Profile 3", GameInstallationId = "install-3", GameClient = new GameClient { Id = "client-3" } },
            };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(default))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(profiles));

        // Act
        var result = await _profileManager.GetAllProfilesAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Count);
    }

    /// <summary>
    /// Should handle validation of profile before creation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ValidateProfile_BeforeCreationAsync()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var installation = CreateTestInstallation(clientId);
        var request = new CreateProfileRequest
        {
            Name = string.Empty, // Invalid empty name
            GameInstallationId = installation.Id,
            GameClientId = clientId,
        };

        _installationServiceMock.Setup(x => x.GetInstallationAsync(installation.Id, default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        // Act
        var result = await _profileManager.CreateProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Profile name cannot be empty", result.FirstError);
    }

    /// <summary>
    /// Should reject profile creation when profile name exceeds the maximum length limit.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ReturnFailure_When_NameExceedsMaxLengthAsync()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var installation = CreateTestInstallation(clientId);
        var overlongName = new string('a', ProfileConstants.MaxProfileNameLength + 1);
        var request = new CreateProfileRequest
        {
            Name = overlongName,
            GameInstallationId = installation.Id,
            GameClientId = clientId,
        };

        _installationServiceMock.Setup(x => x.GetInstallationAsync(installation.Id, default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        // Act
        var result = await _profileManager.CreateProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Profile name is too long", result.FirstError);
    }

    /// <summary>
    /// Should update enabled content successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_UpdateEnabledContent_SuccessfullyAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = ["content1"],
        };
        var request = new UpdateProfileRequest
        {
            EnabledContentIds = ["content1", "content2", "content3"],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.EnabledContentIds.Count == 3), default), Times.Once);
    }

    /// <summary>
    /// Should clear ActiveWorkspaceId when enabled content changes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ClearWorkspace_When_ContentChangesAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = ["content1", "content2"],
            ActiveWorkspaceId = "workspace-123",
        };
        var request = new UpdateProfileRequest
        {
            EnabledContentIds = ["content1", "content3"], // Changed: removed content2, added content3
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == string.Empty), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == string.Empty && p.EnabledContentIds.Count == 2), default), Times.Once);
    }

    /// <summary>
    /// Should clear ActiveWorkspaceId when GameClient changes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ClearWorkspace_When_GameClientChangesAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = ["content1", "content2"],
            ActiveWorkspaceId = "workspace-123",
        };
        var newGameClient = new GameClient { Id = "client-2", Version = "2.0" };
        var request = new UpdateProfileRequest
        {
            GameClient = newGameClient,
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == string.Empty && p.GameClient!.Id == "client-2"), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == string.Empty && p.GameClient!.Id == "client-2"), default), Times.Once);
    }

    /// <summary>
    /// Should NOT clear ActiveWorkspaceId when content hasn't changed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_KeepWorkspace_When_ContentUnchangedAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = ["content1", "content2"],
            ActiveWorkspaceId = "workspace-123",
        };
        var request = new UpdateProfileRequest
        {
            Name = "Updated Name Only", // Only name changed, not content
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == "workspace-123"), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == "workspace-123"), default), Times.Once);
    }

    /// <summary>
    /// Should NOT clear ActiveWorkspaceId when content update request is null (content not being updated).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_KeepWorkspace_When_ContentUpdateRequestIsNullAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = ["content1", "content2"],
            ActiveWorkspaceId = "workspace-123",
        };
        var request = new UpdateProfileRequest
        {
            Name = "Updated Name Only", // Only name changed, EnabledContentIds is null
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == "workspace-123"), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.ActiveWorkspaceId == "workspace-123"), default), Times.Once);
    }

    /// <summary>
    /// Should send ProfileUpdatedMessage after successful update.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_SendProfileUpdatedMessage_OnSuccessAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = ["content1"],
        };
        var request = new UpdateProfileRequest
        {
            Name = "Updated Name",
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        var updatedProfile = new GameProfile
        {
            Id = existingProfile.Id,
            Name = "Updated Name",
            GameInstallationId = existingProfile.GameInstallationId,
            GameClient = existingProfile.GameClient,
            EnabledContentIds = existingProfile.EnabledContentIds,
        };

        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(updatedProfile));

        ProfileUpdatedMessage? receivedMessage = null;

        WeakReferenceMessenger.Default.Register<ProfileUpdatedMessage>(this, (_, m) =>
        {
            if (m.Profile.Id == profileId)
            {
                receivedMessage = m;
            }
        });

        try
        {
            // Act
            var result = await _profileManager.UpdateProfileAsync(profileId, request);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(receivedMessage);
            Assert.Equal("Updated Name", receivedMessage.Profile.Name);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<ProfileUpdatedMessage>(this);
        }
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync resolves fallback game client from installation when client is omitted and enabled content matches.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ResolveFallbackGameClient_When_ClientMatchedInInstallationAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var fallbackClientId = "fallback-client-id";
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "old-client-id", Version = "1.0" },
            EnabledContentIds = [fallbackClientId],
        };
        var request = new UpdateProfileRequest
        {
            Name = "Updated Profile",
            GameClient = null,
            EnabledContentIds = [fallbackClientId],
        };

        var matchedClient = new GameClient { Id = fallbackClientId, Version = "2.0", GameType = GameType.Generals };
        var testInstallation = new GameInstallation("C:\\Games\\Generals", GameInstallationType.Retail)
        {
            Id = "install-1",
            AvailableGameClients = [matchedClient],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-1", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(testInstallation));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(request.GameClient);
        Assert.Equal(fallbackClientId, request.GameClient.Id);
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.Is<GameProfile>(p => p.GameClient != null && p.GameClient.Id == fallbackClientId), default), Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync propagates OperationCanceledException when cancellation is requested.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_PropagateCancellation_When_CancelledDuringFallbackResolutionAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = null,
            EnabledContentIds = ["some-client-id"],
        };
        var request = new UpdateProfileRequest
        {
            GameClient = null,
            EnabledContentIds = ["some-client-id"],
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, cts.Token))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-1", cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => _profileManager.UpdateProfileAsync(profileId, request, cts.Token));
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync gracefully handles failure when installation lookup fails during fallback resolution.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_Succeed_When_InstallationLookupFailsAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingClient = new GameClient
        {
            Id = "existing-client-id",
            Name = "Existing Client",
            Version = "1.0",
            GameType = GameType.Generals,
        };
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = existingClient,
            EnabledContentIds = ["some-client-id"],
        };
        var request = new UpdateProfileRequest
        {
            Name = "Updated Name",
            GameClient = null,
            EnabledContentIds = ["some-client-id"],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-1", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateFailure("Installation not found"));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.Null(request.GameClient);
        _profileRepositoryMock.Verify(
            x => x.SaveProfileAsync(
                It.Is<GameProfile>(p => p.Name == "Updated Name" && p.GameClient == existingClient),
                default),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync rejects update when game installation changes and no compatible client exists in the new installation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_RejectUpdate_When_GameInstallationChangesAndNoCompatibleClientExistsAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "old-client-id", Version = "1.0", GameType = GameType.Generals },
            EnabledContentIds = ["old-client-id"],
        };
        var request = new UpdateProfileRequest
        {
            GameInstallationId = "install-2",
            GameClient = null,
            EnabledContentIds = ["old-client-id"],
        };

        var newInstallation = new GameInstallation("C:\\Games\\Generals2", GameInstallationType.Retail)
        {
            Id = "install-2",
            AvailableGameClients = [new GameClient { Id = "new-client-id", Version = "2.0", GameType = GameType.Generals }],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-2", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(newInstallation));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No compatible game client found in installation 'install-2'", result.Errors.First());
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default), Times.Never);
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync resolves game client from new installation when game installation changes and matching client is enabled.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ResolveClientFromNewInstallation_When_GameInstallationChangesAndMatchingClientExistsAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "old-client-id", Version = "1.0", GameType = GameType.Generals },
            EnabledContentIds = ["old-client-id"],
        };
        var request = new UpdateProfileRequest
        {
            GameInstallationId = "install-2",
            GameClient = null,
            EnabledContentIds = ["new-client-id"],
        };

        var newClient = new GameClient { Id = "new-client-id", Version = "2.0", GameType = GameType.Generals };
        var newInstallation = new GameInstallation("C:\\Games\\Generals2", GameInstallationType.Retail)
        {
            Id = "install-2",
            AvailableGameClients = [newClient],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-2", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(newInstallation));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(request.GameClient);
        Assert.Equal("new-client-id", request.GameClient.Id);
        _profileRepositoryMock.Verify(
            x => x.SaveProfileAsync(
                It.Is<GameProfile>(p => p.GameInstallationId == "install-2" && p.GameClient != null && p.GameClient.Id == "new-client-id"),
                default),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync rejects update when game installation changes and explicit client is incompatible.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_RejectUpdate_When_GameInstallationChangesAndExplicitClientIncompatibleAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0", GameType = GameType.Generals },
            EnabledContentIds = ["client-1"],
        };
        var request = new UpdateProfileRequest
        {
            GameInstallationId = "install-2",
            GameClient = new GameClient { Id = "incompatible-client", Version = "1.0", GameType = GameType.Generals },
            EnabledContentIds = ["incompatible-client"],
        };

        var newInstallation = new GameInstallation("C:\\Games\\Generals2", GameInstallationType.Retail)
        {
            Id = "install-2",
            AvailableGameClients = [new GameClient { Id = "client-2", Version = "2.0", GameType = GameType.Generals }],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-2", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(newInstallation));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("is not available in installation 'install-2'", result.Errors.First());
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default), Times.Never);
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync resolves a client from the new installation when numeric versions match across different formats (e.g. 000104 and 1.04).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ResolveClientFromNewInstallation_When_NumericVersionMatchesAcrossFormatsAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "old-client-id", Version = "000104", GameType = GameType.Generals, IsEnabled = true },
            EnabledContentIds = [],
        };
        var request = new UpdateProfileRequest
        {
            GameInstallationId = "install-2",
            GameClient = null,
        };

        var newClient = new GameClient { Id = "new-client-id", Version = "1.04", GameType = GameType.Generals, IsEnabled = true };
        var newInstallation = new GameInstallation("C:\\Games\\Generals2", GameInstallationType.Retail)
        {
            Id = "install-2",
            AvailableGameClients = [newClient],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-2", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(newInstallation));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default))
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(request.GameClient);
        Assert.Equal("new-client-id", request.GameClient.Id);
        Assert.Equal("1.04", request.GameClient.Version);
    }

    /// <summary>
    /// Verifies that UpdateProfileAsync rejects update when an alphanumeric version suffix prevents false-positive numeric equivalence.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_RejectUpdate_When_VersionHasAlphanumericSuffixAndDoesNotMatchAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "old-client-id", Version = "1.04b", GameType = GameType.Generals, IsEnabled = true },
            EnabledContentIds = [],
        };
        var request = new UpdateProfileRequest
        {
            GameInstallationId = "install-2",
            GameClient = null,
        };

        var newClient = new GameClient { Id = "new-client-id", Version = "1.00", GameType = GameType.Generals, IsEnabled = true };
        var newInstallation = new GameInstallation("C:\\Games\\Generals2", GameInstallationType.Retail)
        {
            Id = "install-2",
            AvailableGameClients = [newClient],
        };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, default))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _installationServiceMock.Setup(x => x.GetInstallationAsync("install-2", default))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(newInstallation));

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No compatible game client found in installation 'install-2'", result.Errors.First());
        _profileRepositoryMock.Verify(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), default), Times.Never);
    }

    /// <summary>
    /// Verifies that passing null or empty manifest IDs returns zero counts without accessing repository.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenEmptyIds_ReturnsZeroCountsAsync()
    {
        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.UpdatedProfilesCount);
        Assert.Equal(0, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a profile retaining remaining custom content is updated with scrubbed IDs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenProfileRetainsContent_UpdatesProfileAsync()
    {
        // Arrange
        const string deletedId = "mod.to.delete";
        const string remainingId = "mod.to.keep";
        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Kept Profile",
            GameInstallationId = "inst-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0", GameType = GameType.ZeroHour },
            EnabledContentIds = [deletedId, remainingId],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([deletedId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.UpdatedProfilesCount);
        Assert.Equal(0, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(
            x => x.SaveProfileAsync(
                It.Is<GameProfile>(p => p.EnabledContentIds.Count == 1 && p.EnabledContentIds.Contains(remainingId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when a tool profile contains multiple contents and non-tool content is deleted, the tool profile is updated, not deleted as orphaned.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenToolProfileRetainsToolContent_UpdatesProfileAsync()
    {
        // Arrange
        const string toolId = "tool.worldbuilder";
        const string extraId = "extra.map";
        var toolProfile = new GameProfile
        {
            Id = "tool-profile-1",
            Name = "WorldBuilder Tool",
            ToolContentId = toolId,
            EnabledContentIds = [toolId, extraId],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([toolProfile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("tool-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(toolProfile));
        _profileRepositoryMock.Setup(x => x.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(toolProfile));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([extraId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.UpdatedProfilesCount);
        Assert.Equal(0, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(x => x.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _profileRepositoryMock.Verify(
            x => x.SaveProfileAsync(
                It.Is<GameProfile>(p => p.EnabledContentIds.Count == 1 && p.EnabledContentIds.Contains(toolId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when a tool profile tool content manifest is deleted, the orphaned tool profile is deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenToolContentDeleted_DeletesOrphanedProfileAsync()
    {
        // Arrange
        const string toolId = "tool.worldbuilder";
        var toolProfile = new GameProfile
        {
            Id = "tool-profile-1",
            Name = "WorldBuilder Tool",
            ToolContentId = toolId,
            EnabledContentIds = [toolId],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([toolProfile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("tool-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(toolProfile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync("tool-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(toolProfile));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([toolId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.UpdatedProfilesCount);
        Assert.Equal(1, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(x => x.DeleteProfileAsync("tool-profile-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a profile auto-created with content is deleted when all custom content is removed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenCreatedWithContentLosesCustomContent_DeletesOrphanedProfileAsync()
    {
        // Arrange
        const string modId = "mod.shockwave";
        const string gameInstallId = "1.0.gameinstallation.zerohour";
        var profile = new GameProfile
        {
            Id = "auto-profile-1",
            Name = "ShockWave Profile",
            Description = $"{ProfileConstants.CreatedWithContentDescriptionPrefix}ShockWave",
            GameInstallationId = "inst-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0", GameType = GameType.ZeroHour },
            EnabledContentIds = [modId, gameInstallId],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("auto-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync("auto-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([modId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.UpdatedProfilesCount);
        Assert.Equal(1, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(x => x.DeleteProfileAsync("auto-profile-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when a profile enabled content list becomes empty, it is deleted as orphaned.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenAllContentDeleted_DeletesOrphanedProfileAsync()
    {
        // Arrange
        const string modId = "mod.onlymod";
        var profile = new GameProfile
        {
            Id = "profile-empty-content",
            Name = "Empty Content Profile",
            GameInstallationId = "inst-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0", GameType = GameType.ZeroHour },
            EnabledContentIds = [modId],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("profile-empty-content", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync("profile-empty-content", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([modId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.UpdatedProfilesCount);
        Assert.Equal(1, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(x => x.DeleteProfileAsync("profile-empty-content", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when a profile game client manifest is deleted, the orphaned profile is deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenGameClientDeleted_DeletesOrphanedProfileAsync()
    {
        // Arrange
        const string clientId = "client.genonline.zh";
        var profile = new GameProfile
        {
            Id = "profile-client-deleted",
            Name = "Online Profile",
            GameInstallationId = "inst-1",
            GameClient = new GameClient { Id = clientId, Version = "1.0", GameType = GameType.ZeroHour },
            EnabledContentIds = [clientId, "some.mod"],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("profile-client-deleted", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync("profile-client-deleted", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([clientId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.UpdatedProfilesCount);
        Assert.Equal(1, result.Data.DeletedProfilesCount);
        _profileRepositoryMock.Verify(x => x.DeleteProfileAsync("profile-client-deleted", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that failed profile deletes are recorded in FailedProfileNames.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenDeleteFails_TracksFailedProfileNamesAsync()
    {
        // Arrange
        const string modId = "mod.failing";
        var profile = new GameProfile
        {
            Id = "failing-profile",
            Name = "Failing Profile",
            GameInstallationId = "inst-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0", GameType = GameType.ZeroHour },
            EnabledContentIds = [modId],
        };

        _profileRepositoryMock.Setup(x => x.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _profileRepositoryMock.Setup(x => x.LoadProfileAsync("failing-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync("failing-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Delete locked"));

        // Act
        var result = await _profileManager.ScrubDeletedManifestReferencesAsync([modId]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.DeletedProfilesCount);
        Assert.Contains("Failing Profile", result.Data.FailedProfileNames);
    }

    /// <summary>
    /// Verifies that DeleteProfileAsync sends a ProfileDeletedMessage containing profile ID and name.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_WhenSuccessful_SendsProfileDeletedMessageAsync()
    {
        // Arrange
        const string profileId = "profile-del-msg";
        const string profileName = "Deleted Name";
        var profile = new GameProfile { Id = profileId, Name = profileName };

        _profileRepositoryMock.Setup(x => x.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(x => x.DeleteProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        ProfileDeletedMessage? receivedMessage = null;
        WeakReferenceMessenger.Default.Register<ProfileDeletedMessage>(
            this,
            (_, m) => receivedMessage = m);

        try
        {
            // Act
            var result = await _profileManager.DeleteProfileAsync(profileId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(receivedMessage);
            Assert.Equal(profileId, receivedMessage.ProfileId);
            Assert.Equal(profileName, receivedMessage.ProfileName);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<ProfileDeletedMessage>(this);
        }
    }

    private static GameInstallation CreateTestInstallation(string clientId)
    {
        return new GameInstallation("C:\\Games\\Generals", GameInstallationType.Retail)
        {
            Id = Guid.NewGuid().ToString(),
            AvailableGameClients = [new GameClient { Id = clientId, Version = "1.0", GameType = GameType.Generals }],
        };
    }
}
