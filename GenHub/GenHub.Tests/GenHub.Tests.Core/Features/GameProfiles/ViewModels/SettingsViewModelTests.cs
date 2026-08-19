using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.CAS;
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
    private readonly Mock<IGitHubApiClient> _mockGitHubApiClient;
    private readonly Mock<IPublisherSubscriptionStore> _mockSubscriptionStore;
    private readonly Mock<IPublisherCatalogRefreshService> _mockCatalogRefreshService;
    private readonly Mock<IUserDataTracker> _mockUserDataTracker;
    private readonly Mock<IDialogService> _mockDialogService;
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
        _mockGitHubApiClient = new Mock<IGitHubApiClient>();
        _mockSubscriptionStore = new Mock<IPublisherSubscriptionStore>();
        _mockCatalogRefreshService = new Mock<IPublisherCatalogRefreshService>();
        _mockUserDataTracker = new Mock<IUserDataTracker>();
        _mockDialogService = new Mock<IDialogService>();
        _defaultSettings = new UserSettings();

        _mockConfigService.Setup(x => x.Get()).Returns(_defaultSettings);
        _mockUserDataTracker
            .Setup(x => x.DeleteAllUserDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

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
    public async Task SaveSettingsCommand_UpdatesUserSettingsServiceAsync()
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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object)
        {
            Theme = "Light",
            MaxConcurrentDownloads = 5,
        };

        // Act
        await Task.Run(() => viewModel.SaveSettingsCommand.Execute(null));

        // Assert
        _mockConfigService.Verify(x => x.Update(It.IsAny<Action<UserSettings>>()), Times.Once);
        _mockConfigService.Verify(x => x.SaveAsync(default), Times.Once);
    }

    /// <summary>
    /// Verifies that ResetToDefaultsCommand resets all properties.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResetToDefaultsCommand_ResetsAllPropertiesAsync()
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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object)
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
        Assert.True(viewModel.AutoCheckForUpdatesPeriodically);
        Assert.Equal(AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes, viewModel.PeriodicUpdateCheckIntervalMinutes);
    }

    /// <summary>
    /// Verifies that periodic update settings are correctly loaded from UserSettings.
    /// </summary>
    [Fact]
    public void Constructor_LoadsPeriodicUpdateSettingsFromUserSettingsService()
    {
        // Arrange
        var customSettings = new UserSettings
        {
            AutoCheckForUpdatesPeriodically = false,
            PeriodicUpdateCheckIntervalMinutes = 15,
        };

        _mockConfigService.Setup(x => x.Get()).Returns(customSettings);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.False(viewModel.AutoCheckForUpdatesPeriodically);
        Assert.Equal(15, viewModel.PeriodicUpdateCheckIntervalMinutes);
    }

    /// <summary>
    /// Verifies that SaveSettingsCommand persists periodic update settings.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveSettingsCommand_UpdatesPeriodicUpdateSettingsAsync()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.AutoCheckForUpdatesPeriodically = false;
        viewModel.PeriodicUpdateCheckIntervalMinutes = 45;

        UserSettings? capturedSettings = null;
        _mockConfigService.Setup(x => x.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action =>
            {
                capturedSettings = new UserSettings();
                action(capturedSettings);
            });

        // Act
        await Task.Run(() => viewModel.SaveSettingsCommand.Execute(null));

        // Assert
        Assert.NotNull(capturedSettings);
        Assert.False(capturedSettings.AutoCheckForUpdatesPeriodically);
        Assert.Equal(45, capturedSettings.PeriodicUpdateCheckIntervalMinutes);
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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object)
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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

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
    public async Task SaveSettingsCommand_HandlesUserSettingsServiceExceptionAsync()
    {
        // Arrange
        _mockConfigService.Setup(x => x.SaveAsync(default)).ThrowsAsync(new IOException("Disk full"));
        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

        // Assert - Should not throw and use defaults
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Equal(3, viewModel.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Verifies that DeleteCasStorageCommand calls the service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteCasStorageCommand_ReportsGarbageCollectionIsDisabledAsync()
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
        _mockCasService
            .Setup(x => x.RunGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CasGarbageCollectionResult.CreateDisabled());

        var viewModel = new SettingsViewModel(
            _mockConfigService.Object,
            _mockLogger.Object,
            _mockCasService.Object,
            _mockProfileManager.Object,
            _mockWorkspaceManager.Object,
            _mockManifestPool.Object,
            _mockUpdateManager.Object,
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

        // Act
        await viewModel.DeleteCasStorageCommand.ExecuteAsync(null);

        // Assert
        _mockCasService.Verify(x => x.RunGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockNotificationService.Verify(
            service => service.ShowInfo(
                "CAS Cleanup Disabled",
                CasDefaults.GarbageCollectionDisabledMessage,
                (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds,
                It.IsAny<bool>()),
            Times.Once);
        _mockNotificationService.Verify(
            service => service.ShowSuccess(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that UninstallGenHubCommand calls the service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UninstallGenHubCommand_CallsServiceAsync()
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
            _mockSubscriptionStore.Object,
            _mockCatalogRefreshService.Object,
            _mockGitHubApiClient.Object,
            _mockNotificationService.Object,
            _mockConfigurationProvider.Object,
            _mockInstallationService.Object,
            _mockStorageLocationService.Object,
            _mockUserDataTracker.Object,
            _mockDialogService.Object);

        // Act
        await viewModel.UninstallGenHubCommand.ExecuteAsync(null);

        // Assert
        _mockUpdateManager.Verify(x => x.Uninstall(), Times.Once);
    }

    /// <summary>
    /// Verifies that declining the confirmation prompt leaves every piece of application data alone.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAllDataCommand_WhenConfirmationDeclined_DeletesNothingAsync()
    {
        // Arrange
        SetupDeletableData();
        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);

        var viewModel = CreateViewModel();

        // Act
        await viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        // Assert
        _mockUserDataTracker.Verify(x => x.DeleteAllUserDataAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mockCasService.Verify(x => x.RunGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockInstallationService.Verify(x => x.InvalidateCache(), Times.Never);
        _mockProfileManager.Verify(x => x.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockWorkspaceManager.Verify(x => x.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockManifestPool.Verify(x => x.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a confirmation prompt that fails to open — no main window, or an Avalonia
    /// failure — is reported to the user instead of escaping the command unlogged, and that it still
    /// deletes nothing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAllDataCommand_WhenConfirmationThrows_ReportsErrorAndDeletesNothingAsync()
    {
        // Arrange
        SetupDeletableData();
        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("no main window"));

        var viewModel = CreateViewModel();

        // Act
        await viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockUserDataTracker.Verify(x => x.DeleteAllUserDataAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mockProfileManager.Verify(x => x.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockWorkspaceManager.Verify(x => x.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockManifestPool.Verify(x => x.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that accepting the confirmation prompt performs the deletion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAllDataCommand_WhenConfirmationAccepted_DeletesAllDataAsync()
    {
        // Arrange
        SetupDeletableData();
        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);

        var viewModel = CreateViewModel();

        // Act
        await viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        // Assert
        _mockUserDataTracker.Verify(x => x.DeleteAllUserDataAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockCasService.Verify(x => x.RunGarbageCollectionAsync(true, It.IsAny<CancellationToken>()), Times.Once);
        _mockInstallationService.Verify(x => x.InvalidateCache(), Times.Once);
        _mockProfileManager.Verify(x => x.DeleteProfileAsync("profile-to-delete", It.IsAny<CancellationToken>()), Times.Once);
        _mockWorkspaceManager.Verify(x => x.CleanupWorkspaceAsync("workspace-to-delete", It.IsAny<CancellationToken>()), Times.Once);
        _mockManifestPool.Verify(x => x.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a user data deletion that had to keep some data is not followed by a success
    /// message claiming that data was deleted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAllDataCommand_WhenUserDataPartiallyDeleted_DoesNotClaimSuccessAsync()
    {
        // Arrange
        SetupDeletableData();
        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);
        _mockUserDataTracker
            .Setup(x => x.DeleteAllUserDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Your originals were kept at 'backups'."));

        var viewModel = CreateViewModel();

        // Act
        await viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            x => x.ShowError("User Data Partially Deleted", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockNotificationService.Verify(
            x => x.ShowSuccess("Data Deleted", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        _mockNotificationService.Verify(
            x => x.ShowWarning("Data Partially Deleted", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the confirmation prompt states the action is irreversible and that game data
    /// backups are discarded, and that it cannot be suppressed by a "do not ask again" preference.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAllDataCommand_WarnsThatBackupsAreDiscardedAndCannotBeSuppressedAsync()
    {
        // Arrange
        string? capturedMessage = null;
        string? capturedSessionKey = null;
        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Callback<string, string, string, string, string?>((title, message, confirmText, cancelText, sessionKey) =>
            {
                capturedMessage = message;
                capturedSessionKey = sessionKey;
            })
            .ReturnsAsync(false);

        var viewModel = CreateViewModel();

        // Act
        await viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(AppConstants.DeleteAllDataConfirmationMessage, capturedMessage);
        Assert.Contains("irreversible", capturedMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("backups", capturedMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(capturedSessionKey);
    }

    private void SetupDeletableData()
    {
        _mockProfileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([new GameProfile { Id = "profile-to-delete" }]));
        _mockWorkspaceManager
            .Setup(x => x.GetAllWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess([new WorkspaceInfo { Id = "workspace-to-delete" }]));
        _mockManifestPool
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([new ContentManifest { Name = "manifest-to-delete" }]));
    }

    private SettingsViewModel CreateViewModel() => new(
        _mockConfigService.Object,
        _mockLogger.Object,
        _mockCasService.Object,
        _mockProfileManager.Object,
        _mockWorkspaceManager.Object,
        _mockManifestPool.Object,
        _mockUpdateManager.Object,
        _mockSubscriptionStore.Object,
        _mockCatalogRefreshService.Object,
        _mockGitHubApiClient.Object,
        _mockNotificationService.Object,
        _mockConfigurationProvider.Object,
        _mockInstallationService.Object,
        _mockStorageLocationService.Object,
        _mockUserDataTracker.Object,
        _mockDialogService.Object);
}
