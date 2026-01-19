using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Unit tests for <see cref="SettingsViewModel"/>.
/// </summary>
public class SettingsViewModelTests
{
    private readonly Mock<IUserSettingsService> _mockConfigService;
    private readonly Mock<ILogger<SettingsViewModel>> _mockLogger;
    private readonly Mock<ICasService> _mockCasService;
    private readonly Mock<IGameProfileManager> _mockProfileManager;
    private readonly Mock<IWorkspaceManager> _mockWorkspaceManager;
    private readonly Mock<IContentManifestPool> _mockManifestPool;
    private readonly Mock<IVelopackUpdateManager> _mockUpdateManager;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IConfigurationProviderService> _mockConfigurationProvider;
    private readonly Mock<IGameInstallationService> _mockInstallationService;
    private readonly Mock<IStorageLocationService> _mockStorageLocationService;
    private readonly Mock<IUserDataTracker> _mockUserDataTracker;
    private readonly UserSettings _defaultSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModelTests"/> class.
    /// </summary>
    public SettingsViewModelTests()
    {
        _mockConfigService = new Mock<IUserSettingsService>();
        _mockLogger = new Mock<ILogger<SettingsViewModel>>();
        _mockCasService = new Mock<ICasService>();
        _mockProfileManager = new Mock<IGameProfileManager>();
        _mockWorkspaceManager = new Mock<IWorkspaceManager>();
        _mockManifestPool = new Mock<IContentManifestPool>();
        _mockUpdateManager = new Mock<IVelopackUpdateManager>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockConfigurationProvider = new Mock<IConfigurationProviderService>();
        _mockInstallationService = new Mock<IGameInstallationService>();
        _mockStorageLocationService = new Mock<IStorageLocationService>();
        _mockUserDataTracker = new Mock<IUserDataTracker>();
        _defaultSettings = new UserSettings();

        _mockConfigService.Setup(x => x.Get()).Returns(_defaultSettings);
    }

    /// <summary>
    /// Verifies that the constructor loads settings from the configuration service.
    /// </summary>
    [Fact]
    public void Constructor_LoadsSettingsFromUserSettingsService()
    {
        // Arrange
        var customSettings = new UserSettings
        {
            Theme = "Light",
            MaxConcurrentDownloads = 5,
            EnableDetailedLogging = true,
            WorkspacePath = "/custom/path",
        };

        _mockConfigService.Setup(x => x.Get()).Returns(customSettings);

        // Act
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Assert
        Assert.Equal("Light", viewModel.Theme);
        Assert.Equal(5, viewModel.MaxConcurrentDownloads);
        Assert.True(viewModel.EnableDetailedLogging);
        Assert.Equal("/custom/path", viewModel.WorkspacePath);
    }

    /// <summary>
    /// Verifies that SaveSettingsCommand updates the configuration service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveSettingsCommand_UpdatesUserSettingsService()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object)
        {
            Theme = "Light",
            MaxConcurrentDownloads = 5,
        };

        // Act
        await Task.Run(() => viewModel.SaveSettingsCommand.Execute(null));

        // Assert
        _mockConfigService.Verify(x => x.Update(It.IsAny<Action<UserSettings>>()), Times.Once);
        _mockConfigService.Verify(x => x.SaveAsync(), Times.Once);
    }

    /// <summary>
    /// Verifies that ResetToDefaultsCommand resets all properties.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResetToDefaultsCommand_ResetsAllProperties()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object)
        {
            Theme = "Light",
            MaxConcurrentDownloads = 10,
            EnableDetailedLogging = true,
        };

        // Act
        await Task.Run(() => viewModel.ResetToDefaultsCommand.Execute(null));

        // Assert
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Equal(3, viewModel.MaxConcurrentDownloads);
        Assert.False(viewModel.EnableDetailedLogging);
        Assert.Equal(WorkspaceConstants.DefaultWorkspaceStrategy, viewModel.DefaultWorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that MaxConcurrentDownloads is set within bounds.
    /// </summary>
    [Fact]
    public void MaxConcurrentDownloads_SetsValueWithinBounds()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object)
        {
            // Act & Assert - Test lower bound
            MaxConcurrentDownloads = 0,
        };
        Assert.Equal(1, viewModel.MaxConcurrentDownloads); // ViewModel clamps to 1

        // Act & Assert - Test upper bound
        viewModel.MaxConcurrentDownloads = 15;
        Assert.Equal(10, viewModel.MaxConcurrentDownloads); // ViewModel clamps to 10

        // Act & Assert - Test valid value
        viewModel.MaxConcurrentDownloads = 5;
        Assert.Equal(5, viewModel.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Verifies that AvailableThemes returns expected values.
    /// </summary>
    [Fact]
    public void AvailableThemes_ReturnsExpectedValues()
    {
        // Arrange
        _ = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Act
        var themes = SettingsViewModel.AvailableThemes.ToList();

        // Assert
        Assert.Contains("Dark", themes);
        Assert.Contains("Light", themes);
        Assert.Equal(2, themes.Count);
    }

    /// <summary>
    /// Verifies that AvailableWorkspaceStrategies returns all enum values.
    /// </summary>
    [Fact]
    public void AvailableWorkspaceStrategies_ReturnsAllEnumValues()
    {
        // Arrange
        _ = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Act
        var strategies = SettingsViewModel.AvailableWorkspaceStrategies.ToList();

        // Assert
        Assert.Contains(WorkspaceStrategy.HybridCopySymlink, strategies);

        // Add assertions for other workspace strategies as they're implemented
    }

    /// <summary>
    /// Verifies that SaveSettingsCommand handles configuration service exceptions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveSettingsCommand_HandlesUserSettingsServiceException()
    {
        // Arrange
        _mockConfigService.Setup(x => x.SaveAsync()).ThrowsAsync(new IOException("Disk full"));
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Act
        await Task.Run(() => viewModel.SaveSettingsCommand.Execute(null));

        // Assert
        _mockLogger.Verify(
            x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Failed to save settings")),
            It.IsAny<IOException>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the constructor handles configuration service exceptions and uses defaults.
    /// </summary>
    [Fact]
    public void Constructor_HandlesUserSettingsServiceException()
    {
        // Arrange
        _mockConfigService.Setup(x => x.Get()).Throws(new Exception("Configuration error"));

        // Act
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Assert - Should not throw and use defaults
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Equal(3, viewModel.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Verifies that DeleteCasStorageCommand calls the service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteCasStorageCommand_CallsService()
    {
        // Arrange
        // Setup stats to return valid data so update method works
        _mockCasService.Setup(x => x.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CasStats { ObjectCount = 0, TotalSize = 0 });
        _mockManifestPool.Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
        _mockWorkspaceManager.Setup(x => x.GetAllWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess([]));
        _mockProfileManager.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Act
        await viewModel.DeleteCasStorageCommand.ExecuteAsync(null);

        // Assert
        _mockCasService.Verify(x => x.RunGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that UninstallGenHubCommand calls the service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UninstallGenHubCommand_CallsService()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object);

        // Act
        await viewModel.UninstallGenHubCommand.ExecuteAsync(null);

        // Assert
        _mockUpdateManager.Verify(x => x.Uninstall(), Times.Once);
    }
}
