using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using CoreContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;
using GameInstallationType = GenHub.Core.Models.Enums.GameInstallationType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Unit tests for <see cref="GameProfileSettingsViewModel"/> runtime hotswap mode behavior.
/// </summary>
public class GameProfileSettingsViewModelHotswapTests
{
    private readonly Mock<IGameProfileManager> _gameProfileManagerMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<IConfigurationProviderService> _configProviderMock = new();
    private readonly Mock<IProfileContentLoader> _contentLoaderMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IProfileContentLinker> _profileContentLinkerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly GameProfileSettingsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelHotswapTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelHotswapTests()
    {
        _viewModel = new GameProfileSettingsViewModel(
            _gameProfileManagerMock.Object,
            _gameSettingsServiceMock.Object,
            _configProviderMock.Object,
            _contentLoaderMock.Object,
            null,
            null,
            _manifestPoolMock.Object,
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance,
            _profileContentLinkerMock.Object,
            _launchRegistryMock.Object);
    }

    /// <summary>
    /// Verifies that initializing for a running profile activates Hotswap Mode and locks non-hotswappable content.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_WhenProfileIsRunning_SetsIsHotswapModeTrueAndLocksNonHotswappableContentAsync()
    {
        // Arrange
        const string profileId = "profile-live-1";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string mapId = "1.0.0.map.desert";
        const string modId = "1.0.0.mod.shockwave";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Live Game Profile",
            EnabledContentIds = [installId, mapId, modId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = mapId,
                ManifestId = mapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = modId,
                ManifestId = modId,
                DisplayName = "ShockWave Mod",
                ContentType = ContentType.Mod,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);

        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        // Act
        await _viewModel.InitializeForProfileAsync(profileId);

        // Assert
        Assert.True(_viewModel.IsHotswapMode);
        Assert.False(_viewModel.CanEditImmutableMetadata);

        var mapItem = _viewModel.EnabledContent.FirstOrDefault(c => c.ManifestId.Value == mapId);
        Assert.NotNull(mapItem);
        Assert.False(mapItem.IsLocked);
        Assert.True(mapItem.CanToggle);

        var modItem = _viewModel.EnabledContent.FirstOrDefault(c => c.ManifestId.Value == modId);
        Assert.NotNull(modItem);
        Assert.True(modItem.IsLocked);
        Assert.False(modItem.CanToggle);
    }

    /// <summary>
    /// Verifies that initializing for an idle profile sets Hotswap Mode to false.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_WhenProfileIsNotRunning_SetsIsHotswapModeFalseAsync()
    {
        // Arrange
        const string profileId = "profile-idle-1";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string modId = "1.0.0.mod.shockwave";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Idle Profile",
            EnabledContentIds = [installId, modId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync(new List<GameLaunchInfo>());

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = modId,
                ManifestId = modId,
                DisplayName = "ShockWave Mod",
                ContentType = ContentType.Mod,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);

        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        // Act
        await _viewModel.InitializeForProfileAsync(profileId);

        // Assert
        Assert.False(_viewModel.IsHotswapMode);
        Assert.True(_viewModel.CanEditImmutableMetadata);

        var modItem = _viewModel.EnabledContent.FirstOrDefault(c => c.ManifestId.Value == modId);
        Assert.NotNull(modItem);
        Assert.False(modItem.IsLocked);
        Assert.True(modItem.CanToggle);
    }

    /// <summary>
    /// Verifies that SaveAsync in Hotswap Mode invokes UpdateProfileUserDataAsync on the profile content linker.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenInHotswapMode_CallsUpdateProfileUserDataAsync()
    {
        // Arrange
        const string profileId = "profile-live-2";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string mapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Live Profile",
            EnabledContentIds = [installId, mapId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = mapId,
                ManifestId = mapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var mapManifest = new ContentManifest
        {
            Id = ManifestId.Create(mapId),
            Name = "Tournament Desert",
            ContentType = ContentType.Map,
        };
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installId),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
        };

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(mapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(installId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));

        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            profileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        await _viewModel.InitializeForProfileAsync(profileId);

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        _profileContentLinkerMock.Verify(
            p => p.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 2 && m.Any(x => x.Id.Value == mapId) && m.Any(x => x.Id.Value == installId)),
                GameType.ZeroHour,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _gameProfileManagerMock.Verify(
            m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());
    }

    /// <summary>
    /// Verifies that SaveAsync reports a failure status message when live content sync fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenLiveSyncFails_SetsStatusMessageWarningAsync()
    {
        // Arrange
        const string profileId = "profile-live-3";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string mapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Live Profile",
            EnabledContentIds = [installId, mapId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = mapId,
                ManifestId = mapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var mapManifest = new ContentManifest
        {
            Id = ManifestId.Create(mapId),
            Name = "Tournament Desert",
            ContentType = ContentType.Map,
        };
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installId),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
        };

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(mapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(installId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));

        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            profileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Live file locked by process"));

        await _viewModel.InitializeForProfileAsync(profileId);
        _gameProfileManagerMock.Invocations.Clear();

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("live sync failed", _viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        _gameProfileManagerMock.Verify(
            m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that SaveAsync aborts live sync without calling UpdateProfileUserDataAsync if any enabled manifest cannot be resolved.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenManifestResolutionFails_AbortsLiveSyncAndDoesNotInvokeLinkerAsync()
    {
        // Arrange
        const string profileId = "profile-live-4";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string mapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Live Profile",
            EnabledContentIds = [installId, mapId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = mapId,
                ManifestId = mapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        // Setup install manifest resolution success, but map manifest resolution failure
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installId),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
        };

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(installId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(mapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Manifest not found in pool"));

        await _viewModel.InitializeForProfileAsync(profileId);
        _gameProfileManagerMock.Invocations.Clear();

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("failed to resolve manifests", _viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        _profileContentLinkerMock.Verify(
            p => p.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _gameProfileManagerMock.Verify(
            m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that SaveAsync rolls back live content sync to original manifests if profile persistence fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenProfileUpdateFailsAfterLiveSync_RollsBackLiveSyncToOriginalManifestsAsync()
    {
        // Arrange
        const string profileId = "profile-live-5";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string originalMapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Live Profile",
            EnabledContentIds = [installId, originalMapId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Database lock failure"));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = originalMapId,
                ManifestId = originalMapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var mapManifest = new ContentManifest
        {
            Id = ManifestId.Create(originalMapId),
            Name = "Tournament Desert",
            ContentType = ContentType.Map,
        };
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installId),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
        };

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(originalMapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(installId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));

        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            profileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        await _viewModel.InitializeForProfileAsync(profileId);
        _gameProfileManagerMock.Invocations.Clear();

        // Simulate user disabling the map during active session
        var mapItem = _viewModel.EnabledContent.First(i => i.ManifestId.Value == originalMapId);
        _viewModel.EnabledContent.Remove(mapItem);

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("Failed to update profile", _viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        // First call: forward live update with new enabled content (map removed)
        _profileContentLinkerMock.Verify(
            p => p.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 1 && m.Any(x => x.Id.Value == installId)),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Second call: rollback live update with original enabled content (map restored)
        _profileContentLinkerMock.Verify(
            p => p.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 2 && m.Any(x => x.Id.Value == originalMapId)),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that SaveAsync notifies user with an error when rollback live synchronization fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenLiveSyncRollbackFails_ShowsErrorNotificationAsync()
    {
        // Arrange
        const string profileId = "profile-live-6";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string originalMapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Live Profile",
            EnabledContentIds = [installId, originalMapId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Database lock failure"));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = originalMapId,
                ManifestId = originalMapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var mapManifest = new ContentManifest
        {
            Id = ManifestId.Create(originalMapId),
            Name = "Tournament Desert",
            ContentType = ContentType.Map,
        };
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installId),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
        };

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(originalMapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(installId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));

        int callCount = 0;
        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            profileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? OperationResult<bool>.CreateSuccess(true)
                    : OperationResult<bool>.CreateFailure("Rollback disk IO error");
            });

        await _viewModel.InitializeForProfileAsync(profileId);
        _gameProfileManagerMock.Invocations.Clear();

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("Failed to update profile", _viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Live rollback failed: Rollback disk IO error", _viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that enabling a hotswappable map pack during hotswap mode succeeds without triggering locked installation errors.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnableContent_DuringHotswap_EnablesHotswappableMapPackWithoutAttemptingToModifyLockedInstallationAsync()
    {
        // Arrange
        const string profileId = "profile-live-hotswap";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapPackId = "1.813262.generalsonline.mappack.quickmatchmaps";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "GeneralsOnline 60Hz",
            EnabledContentIds = [installId],
            GameInstallationId = "steam_zh",
            GameClient = new GameClient
            {
                Id = "client-60hz",
                Name = "GeneralsOnline 60Hz",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var installItem = new CoreContentDisplayItem
        {
            Id = installId,
            ManifestId = installId,
            DisplayName = "Zero Hour v1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync([installItem]);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([installItem]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var mapPackManifest = new ContentManifest
        {
            Id = ManifestId.Create(mapPackId),
            Name = "GeneralsOnline QuickMatch Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new()
                {
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(mapPackId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapPackManifest));

        await _viewModel.InitializeForProfileAsync(profileId);

        var mapPackVmItem = new ContentDisplayItem
        {
            ManifestId = ManifestId.Create(mapPackId),
            DisplayName = "GeneralsOnline QuickMatch Maps",
            ContentType = ContentType.MapPack,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = false,
            CanToggle = true,
            IsLocked = false,
        };
        _viewModel.AvailableContent.Add(mapPackVmItem);

        // Act
        await _viewModel.EnableContentCommand.ExecuteAsync(mapPackVmItem);

        // Assert
        Assert.True(_viewModel.IsHotswapMode);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == mapPackId);
        Assert.True(_viewModel.EnabledContent.First(c => c.ManifestId.Value == mapPackId).IsEnabled);
        Assert.Equal(installId, _viewModel.SelectedGameInstallation?.ManifestId.Value);
    }

    /// <summary>
    /// Verifies that when a game session starts during save, live sync fails, and profile rollback succeeds,
    /// SaveAsync sets the appropriate rollback status message.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenGameStartsDuringSaveAndLiveSyncFails_RollsBackPersistedProfileAsync()
    {
        // Arrange
        const string profileId = "profile-postsave-1";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string originalMapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "PostSave Profile",
            EnabledContentIds = [installId, originalMapId],
            VideoResolutionWidth = 800,
            VideoResolutionHeight = 600,
            AudioSoundVolume = 50,
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        bool profileUpdated = false;
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, UpdateProfileRequest req, CancellationToken _) =>
            {
                if (!req.IsRollback)
                {
                    profileUpdated = true;
                }

                return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
            });

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync(() => profileUpdated ? [CreateActiveLaunch(profileId)] : []);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = originalMapId,
                ManifestId = originalMapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _manifestPoolMock.Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = ManifestId.Create(installId) }));

        var syncCallCount = 0;
        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            profileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++syncCallCount == 1
                ? OperationResult<bool>.CreateFailure("Cannot sync locked files")
                : OperationResult<bool>.CreateSuccess(true));

        await _viewModel.InitializeForProfileAsync(profileId);
        _gameProfileManagerMock.Invocations.Clear();

        _viewModel.Name = "New Edited Name";
        _viewModel.Description = "New Edited Description";
        _viewModel.SelectedWorkspaceStrategy = GenHub.Core.Models.Enums.WorkspaceStrategy.SymlinkOnly;
        _viewModel.GameSettingsViewModel.ResolutionWidth = 1920;
        _viewModel.GameSettingsViewModel.ResolutionHeight = 1080;
        _viewModel.GameSettingsViewModel.SoundVolume = 95;

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Live synchronization failed; profile changes were rolled back", _viewModel.StatusMessage);
        Assert.Equal("PostSave Profile", _viewModel.Name);
        _gameProfileManagerMock.Verify(
            m => m.UpdateProfileAsync(
                profileId,
                It.Is<UpdateProfileRequest>(r => r.Name == "New Edited Name"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _gameProfileManagerMock.Verify(
            m => m.UpdateProfileAsync(
                profileId,
                It.Is<UpdateProfileRequest>(r =>
                    r.Name == "PostSave Profile" &&
                    r.EnabledContentIds != null &&
                    r.EnabledContentIds.Contains(originalMapId) &&
                    r.ClearWorkspaceStrategy == true &&
                    r.WorkspaceStrategy == null &&
                    r.VideoResolutionWidth == 800 &&
                    r.VideoResolutionHeight == 600 &&
                    r.AudioSoundVolume == 50),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(800, _viewModel.GameSettingsViewModel.ResolutionWidth);
        Assert.Equal(600, _viewModel.GameSettingsViewModel.ResolutionHeight);
        Assert.Equal(50, _viewModel.GameSettingsViewModel.SoundVolume);
    }

    /// <summary>
    /// Verifies that when a game session starts during save, live sync fails, and profile rollback fails,
    /// SaveAsync sets the failure status message.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WhenGameStartsDuringSaveAndLiveSyncFailsAndRollbackFails_SetsRollbackFailureStatusMessageAsync()
    {
        // Arrange
        const string profileId = "profile-postsave-2";
        const string installId = "1.108.steam.gameinstallation.zh";
        const string originalMapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "PostSave Profile 2",
            EnabledContentIds = [installId, originalMapId],
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        bool profileUpdated = false;
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync(() => profileUpdated ? [CreateActiveLaunch(profileId)] : []);

        var enabledItems = new ObservableCollection<CoreContentDisplayItem>
        {
            new()
            {
                Id = installId,
                ManifestId = installId,
                DisplayName = "Command & Conquer: Zero Hour",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
            },
            new()
            {
                Id = originalMapId,
                ManifestId = originalMapId,
                DisplayName = "Tournament Desert",
                ContentType = ContentType.Map,
                GameType = GameType.ZeroHour,
            },
        };

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabledItems);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _manifestPoolMock.Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = ManifestId.Create(installId) }));

        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            profileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Cannot sync locked files"));

        await _viewModel.InitializeForProfileAsync(profileId);
        _gameProfileManagerMock.Invocations.Clear();

        int updateCount = 0;
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                updateCount++;
                if (updateCount == 1)
                {
                    profileUpdated = true;
                    return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
                }

                return ProfileOperationResult<GameProfile>.CreateFailure("Failed to persist rollback");
            });

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Live synchronization failed and profile rollback could not be persisted", _viewModel.StatusMessage);
        _gameProfileManagerMock.Verify(
            m => m.UpdateProfileAsync(profileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static GameLaunchInfo CreateActiveLaunch(string profileId, string launchId = "launch-1", string workspaceId = "ws-1") => new()
    {
        LaunchId = launchId,
        ProfileId = profileId,
        WorkspaceId = workspaceId,
        ProcessInfo = new GameProcessInfo
        {
            ProcessId = 1234,
            ProcessName = "generals.exe",
            StartTime = DateTime.UtcNow,
        },
    };
}
