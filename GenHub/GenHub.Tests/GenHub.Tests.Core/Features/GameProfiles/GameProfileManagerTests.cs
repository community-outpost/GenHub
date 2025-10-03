using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
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
            _loggerMock.Object);
    }

    /// <summary>
    /// Should return success when installation and client exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileAsync_Should_ReturnSuccess_When_InstallationAndClientExist()
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
            GameClient = installation.AvailableClients.First(),
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
    public async Task CreateProfileAsync_Should_ReturnFailure_When_InstallationNotFound()
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
    public async Task CreateProfileAsync_Should_ReturnFailure_When_ClientNotFoundInInstallation()
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
    public async Task CreateProfileAsync_Should_ReturnFailure_When_RepositorySaveFails()
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
    public async Task UpdateProfileAsync_Should_ReturnSuccess_When_ProfileExists()
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
    public async Task UpdateProfileAsync_Should_ReturnFailure_When_ProfileNotFound()
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
    public async Task DeleteProfileAsync_Should_ReturnSuccess_When_ProfileExists()
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
        Assert.NotNull(result.Data);
    }

    /// <summary>
    /// Should return filtered manifests for available content.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAvailableContentAsync_Should_ReturnFilteredManifests()
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
    public async Task GetAvailableContentAsync_Should_ReturnFailure_When_ManifestPoolFails()
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
    public async Task GetAvailableContentAsync_Should_ReturnEmptyList_When_NoCompatibleContent()
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
    public async Task GetAllProfilesAsync_Should_ReturnAllProfiles()
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
    public async Task CreateProfileAsync_Should_ValidateProfile_BeforeCreation()
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
    /// Should update enabled content successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_UpdateEnabledContent_Successfully()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnabledContentIds = new List<string> { "content1" },
        };
        var request = new UpdateProfileRequest
        {
            EnabledContentIds = new List<string> { "content1", "content2", "content3" },
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
    /// Creates a test installation with a specific client.
    /// </summary>
    private GameInstallation CreateTestInstallation(string clientId)
    {
        return new GameInstallation("C:\\Games\\TestGame", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object)
        {
            Id = Guid.NewGuid().ToString(),
            AvailableClients = new List<GameClient> { new GameClient { Id = clientId, Version = "1.0" }, },
        };
    }
}
