using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for workspace initialization notification handling in <see cref="ProfileLauncherFacade"/>.
/// </summary>
public sealed class ProfileLauncherFacadeWorkspaceNotificationTests
{
    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IGameLauncher> _gameLauncherMock = new();
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IDependencyResolver> _dependencyResolverMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<IStorageLocationService> _storageLocationServiceMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IPublisherReconcilerRegistry> _reconcilerRegistryMock = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderMock = new();
    private readonly Mock<IGameProcessManager> _gameProcessManagerMock = new();
    private readonly Mock<ISymlinkCapabilityProvider> _symlinkCapabilityMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileLauncherFacadeWorkspaceNotificationTests"/> class.
    /// </summary>
    public ProfileLauncherFacadeWorkspaceNotificationTests()
    {
        var gameClient = new GameClient
        {
            Id = "client-zh",
            Name = "Zero Hour Client",
            GameType = GameType.ZeroHour,
            ExecutablePath = "generals.exe",
        };

        const string clientManifestId = "1.0.test.gameclient.zh";
        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(clientManifestId),
            Name = "Zero Hour Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        const string installManifestId = "1.0.test.gameinstall.zh";
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installManifestId),
            Name = "Zero Hour Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        var profile = new GameProfile
        {
            Id = "test-profile",
            Name = "Test Profile",
            GameClient = gameClient,
            GameInstallationId = "inst-1",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = [clientManifestId, installManifestId],
        };

        var installation = new GameInstallation(@"C:\Games\ZeroHour", GameInstallationType.Retail)
        {
            Id = "inst-1",
        };

        _profileManagerMock
            .Setup(m => m.GetProfileAsync("test-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _profileManagerMock
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _installationServiceMock
            .Setup(s => s.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(clientManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(installManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));

        _casServiceMock
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CasStats { ObjectCount = 10, TotalSize = 1024, SpaceSaved = 1024 });

        _configurationProviderMock
            .Setup(c => c.GetDefaultWorkspaceStrategy())
            .Returns(WorkspaceStrategy.HardLink);

        _storageLocationServiceMock
            .Setup(s => s.GetCasPoolPath(It.IsAny<GameInstallation>()))
            .Returns(@"C:\Games\ZeroHour\.genhub-cas");
        _storageLocationServiceMock
            .Setup(s => s.GetWorkspacePath(It.IsAny<GameInstallation>()))
            .Returns(@"C:\Games\ZeroHour\.genhub-workspace");

        _symlinkCapabilityMock
            .Setup(s => s.CanCreateSymlinks)
            .Returns(true);

        _gameSettingsServiceMock
            .Setup(s => s.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));

        _launchRegistryMock
            .Setup(r => r.RegisterLaunchAsync(It.IsAny<GameLaunchInfo>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Verifies that when workspace is reused, no workspace initialization notification is shown.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenWorkspaceReused_DoesNotShowWorkspaceNotificationAsync()
    {
        // Arrange
        var shownNotifications = new List<NotificationMessage>();
        _notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(shownNotifications.Add);

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "test-profile",
            WorkspaceId = "test-profile",
            ProcessInfo = new GameProcessInfo { ProcessId = 1234, ProcessName = "generals" },
        };

        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, IProgress<LaunchProgress>?, bool, IReadOnlyDictionary<string, string>?, CancellationToken>((_, progress, _, _, _) =>
            {
                // Normal reuse reports preparing phase without IsInitializingWorkspace
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = 20 });
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });
            })
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.DoesNotContain(shownNotifications, n => n.Title == "Preparing Workspace");
    }

    /// <summary>
    /// Verifies that when workspace is actively initializing, a persistent notification is shown and dismissed when launch completes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenWorkspaceActivelyInitializing_ShowsPersistentNotificationAndDismissesWhenDoneAsync()
    {
        // Arrange
        var shownNotifications = new List<NotificationMessage>();
        _notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(shownNotifications.Add);

        var dismissedNotifications = new List<Guid>();
        _notificationServiceMock
            .Setup(n => n.Dismiss(It.IsAny<Guid>()))
            .Callback<Guid>(dismissedNotifications.Add);

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "test-profile",
            WorkspaceId = "test-profile",
            ProcessInfo = new GameProcessInfo { ProcessId = 1234, ProcessName = "generals" },
        };

        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, IProgress<LaunchProgress>?, bool, IReadOnlyDictionary<string, string>?, CancellationToken>((_, progress, _, _, _) =>
            {
                // Workspace initialization occurs
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.PreparingWorkspace,
                    PercentComplete = 40,
                    IsInitializingWorkspace = true,
                    TotalFiles = 10,
                    FilesProcessed = 4,
                });
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });
            })
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));

        var prepNotification = shownNotifications.FirstOrDefault(n => n.Title == "Preparing Workspace");
        Assert.NotNull(prepNotification);
        Assert.Null(prepNotification.AutoDismissMilliseconds);
        Assert.True(prepNotification.IsPersistent);

        Assert.Contains(prepNotification.Id, dismissedNotifications);
    }

    /// <summary>
    /// Verifies that when launch throws an exception while workspace is initializing, the persistent notification is dismissed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenLaunchThrowsDuringWorkspaceInit_DismissesPersistentNotificationAsync()
    {
        // Arrange
        var shownNotifications = new List<NotificationMessage>();
        _notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(shownNotifications.Add);

        var dismissedNotifications = new List<Guid>();
        _notificationServiceMock
            .Setup(n => n.Dismiss(It.IsAny<Guid>()))
            .Callback<Guid>(dismissedNotifications.Add);

        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, IProgress<LaunchProgress>?, bool, IReadOnlyDictionary<string, string>?, CancellationToken>((_, progress, _, _, _) =>
            {
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.PreparingWorkspace,
                    PercentComplete = 40,
                    IsInitializingWorkspace = true,
                    TotalFiles = 10,
                    FilesProcessed = 4,
                });
                throw new OperationCanceledException();
            });

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.False(result.Success);
        var prepNotification = shownNotifications.FirstOrDefault(n => n.Title == "Preparing Workspace");
        Assert.NotNull(prepNotification);
        Assert.Contains(prepNotification.Id, dismissedNotifications);
    }

    /// <summary>
    /// Verifies that when launch fails while workspace is initializing, the persistent notification is dismissed rather than updated with workspace failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenLaunchFailsDuringWorkspaceInit_DismissesPersistentNotificationAsync()
    {
        // Arrange
        var shownNotifications = new List<NotificationMessage>();
        _notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(shownNotifications.Add);

        var dismissedNotifications = new List<Guid>();
        _notificationServiceMock
            .Setup(n => n.Dismiss(It.IsAny<Guid>()))
            .Callback<Guid>(dismissedNotifications.Add);

        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, IProgress<LaunchProgress>?, bool, IReadOnlyDictionary<string, string>?, CancellationToken>((_, progress, _, _, _) =>
            {
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.PreparingWorkspace,
                    PercentComplete = 40,
                    IsInitializingWorkspace = true,
                    TotalFiles = 10,
                    FilesProcessed = 4,
                });
            })
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateFailure("Game executable not found"));

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.False(result.Success);
        var prepNotification = shownNotifications.FirstOrDefault(n => n.Title == "Preparing Workspace");
        Assert.NotNull(prepNotification);
        Assert.Contains(prepNotification.Id, dismissedNotifications);
        _notificationServiceMock.Verify(n => n.Update(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when a localization service is provided, localized strings are used for workspace notification.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenLocalizationServiceProvided_UsesLocalizedNotificationStringsAsync()
    {
        // Arrange
        var shownNotifications = new List<NotificationMessage>();
        _notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(shownNotifications.Add);

        var localizationServiceMock = new Mock<ILocalizationService>();
        localizationServiceMock
            .Setup(l => l.GetString("GameProfiles.Launch.WorkspacePreparingTitle", It.IsAny<object?[]>()))
            .Returns("Localized Preparing Title");
        localizationServiceMock
            .Setup(l => l.GetString("GameProfiles.Launch.WorkspaceInitializingMessage", It.IsAny<object?[]>()))
            .Returns("Localized Initializing Message");

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "test-profile",
            WorkspaceId = "test-profile",
            ProcessInfo = new GameProcessInfo { ProcessId = 1234, ProcessName = "generals" },
        };

        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, IProgress<LaunchProgress>?, bool, IReadOnlyDictionary<string, string>?, CancellationToken>((_, progress, _, _, _) =>
            {
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.PreparingWorkspace,
                    PercentComplete = 40,
                    IsInitializingWorkspace = true,
                });
            })
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var facade = CreateFacade(localizationServiceMock.Object);

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
        var prepNotification = shownNotifications.FirstOrDefault(n => n.Title == "Localized Preparing Title");
        Assert.NotNull(prepNotification);
        Assert.Equal("Localized Initializing Message", prepNotification.Message);
    }

    /// <summary>
    /// Verifies that when launch fails in GameLauncher, ProfileLauncherFacade does not show duplicate error notification toasts.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenGameLauncherFails_DoesNotShowDuplicateErrorNotificationAsync()
    {
        // Arrange
        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateFailure("Dependency resolution failed"));

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Dependency resolution failed", result.FirstError);
        _notificationServiceMock.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when launch fails due to cross-drive hardlink error, ProfileLauncherFacade translates error but does not show duplicate notification toast.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenCrossDriveHardlinkFails_DoesNotShowDuplicateErrorNotificationAsync()
    {
        // Arrange
        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(It.IsAny<GameProfile>(), It.IsAny<IProgress<LaunchProgress>>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateFailure("Cannot create hard link across different volumes"));

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync("test-profile");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("different drive", result.FirstError);
        _notificationServiceMock.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    private ProfileLauncherFacade CreateFacade(ILocalizationService? localizationService = null) => new(
        _profileManagerMock.Object,
        _gameLauncherMock.Object,
        _workspaceManagerMock.Object,
        _launchRegistryMock.Object,
        _manifestPoolMock.Object,
        _installationServiceMock.Object,
        _dependencyResolverMock.Object,
        _casServiceMock.Object,
        _gameSettingsServiceMock.Object,
        _storageLocationServiceMock.Object,
        _notificationServiceMock.Object,
        _reconcilerRegistryMock.Object,
        _configurationProviderMock.Object,
        _gameProcessManagerMock.Object,
        _symlinkCapabilityMock.Object,
        NullLogger<ProfileLauncherFacade>.Instance,
        new DirectRunner(NullLogger<DirectRunner>.Instance),
        installationCasPoolService: null,
        localizationService: localizationService);
}
