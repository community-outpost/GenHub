using System.Reactive.Linq;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Contains unit tests for the <see cref="MainViewModel"/> class.
/// </summary>
public class MainViewModelTests
{
    /// <summary>
    /// Tests that <see cref="MainViewModel"/> can be instantiated successfully.
    /// </summary>
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());

        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        // Act
        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Assert
        Assert.NotNull(vm);
        Assert.IsType<MainViewModel>(vm);
    }

    /// <summary>
    /// Tests that executing <see cref="MainViewModel.SelectTabCommand"/> sets the <see cref="MainViewModel.SelectedTab"/> property.
    /// </summary>
    /// <param name="tab">The tab to select.</param>
    [Theory]
    [InlineData(NavigationTab.GameProfiles)]
    [InlineData(NavigationTab.Downloads)]
    [InlineData(NavigationTab.Tools)]
    [InlineData(NavigationTab.Settings)]
    [InlineData(NavigationTab.Info)]
    public void SelectTabCommand_SetsSelectedTab(NavigationTab tab)
    {
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);
        vm.SelectTabCommand.Execute(tab);
        Assert.Equal(tab, vm.SelectedTab);
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafeAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.Setup(x => x.CheckForUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((Velopack.UpdateInfo?)null);
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);
        await vm.InitializeAsync(); // Should not throw
        Assert.True(true);
    }

    /// <summary>
    /// Tests that receiving <see cref="UpdateSettingsChangedMessage"/> updates periodic update timer settings without throwing.
    /// </summary>
    [Fact]
    public void Receive_UpdateSettingsChangedMessage_UpdatesPeriodicTimer()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Act & Assert (should not throw when enabling/disabling or changing interval)
        vm.Receive(new UpdateSettingsChangedMessage(false, true, 30));
        vm.Receive(new UpdateSettingsChangedMessage(false, false, 30));
        Assert.True(true);
    }

    /// <summary>
    /// Tests that when AutoCheckForUpdatesOnStartup is false, background update check is not triggered on initialize.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenAutoCheckForUpdatesOnStartupFalse_DoesNotCheckUpdatesOnStartupAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        userSettingsMock.Setup(x => x.Get()).Returns(new UserSettings
        {
            AutoCheckForUpdatesOnStartup = false,
            AutoCheckForUpdatesPeriodically = false,
            SubscribedBranch = "main",
        });
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        await vm.InitializeAsync();
        await Task.Delay(100);

        mockVelopackUpdateManager.Verify(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        mockVelopackUpdateManager.Verify(x => x.CheckForUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that background update check queries artifact updates when subscribed to a PR.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenSubscribedToPr_ChecksArtifactUpdatesAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        userSettingsMock.Setup(x => x.Get()).Returns(new UserSettings { SubscribedPrNumber = 265 });
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var updateCheckedTcs = new TaskCompletionSource<bool>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns(() =>
            {
                updateCheckedTcs.TrySetResult(true);
                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        await vm.InitializeAsync();

        // Await deterministic completion of background check
        await updateCheckedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        mockVelopackUpdateManager.Verify(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Tests that background update check queries artifact updates when subscribed to a branch.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenSubscribedToBranch_ChecksArtifactUpdatesAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        userSettingsMock.Setup(x => x.Get()).Returns(new UserSettings { SubscribedBranch = "main" });
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var updateCheckedTcs = new TaskCompletionSource<bool>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns(() =>
            {
                updateCheckedTcs.TrySetResult(true);
                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        await vm.InitializeAsync();

        // Await deterministic completion of background check
        await updateCheckedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        mockVelopackUpdateManager.Verify(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Tests that background update check shows an update notification with update action and triggers progress notification on click.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenArtifactUpdateAvailable_ShowsNotificationWithUpdateActionAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        userSettingsMock.Setup(x => x.Get()).Returns(new UserSettings { SubscribedBranch = "main" });
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var shownNotifications = new List<NotificationMessage>();
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var artifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-main",
            GitHash: "abcdef1",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var installStartedTcs = new TaskCompletionSource<bool>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(artifactInfo);
        mockVelopackUpdateManager.Setup(x => x.InstallArtifactAsync(
                artifactInfo,
                It.IsAny<IProgress<UpdateProgress>>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => installStartedTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                shownNotifications.Add(msg);
                if (msg.Title == AppUpdateConstants.BranchUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Act
        await vm.InitializeAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(updateNotification);
        Assert.Equal(AppUpdateConstants.BranchUpdateAvailableNotificationTitle, updateNotification.Title);
        Assert.Single(updateNotification.Actions);
        Assert.Equal(AppUpdateConstants.UpdateAction, updateNotification.Actions[0].Text);

        // Act - simulate clicking the update action button
        updateNotification.Actions[0].Callback?.Invoke();

        // Await background install execution deterministically
        await installStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert that progress notification was displayed
        mockVelopackUpdateManager.Verify(x => x.InstallArtifactAsync(artifactInfo, It.IsAny<IProgress<UpdateProgress>>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        Assert.Contains(shownNotifications, n => n.Title == AppUpdateConstants.UpdatingAppNotificationTitle);
    }

    /// <summary>
    /// Tests that background update check does not create duplicate notifications when the same update is detected repeatedly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenSameArtifactUpdateCheckedRepeatedly_DeduplicatesNotificationAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        userSettingsMock.Setup(x => x.Get()).Returns(new UserSettings { SubscribedBranch = "main" });
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var shownNotifications = new List<NotificationMessage>();
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var artifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-main",
            GitHash: "abcdef1",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(artifactInfo);

        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                shownNotifications.Add(msg);
                if (msg.Title == AppUpdateConstants.BranchUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        using var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Act
        await vm.InitializeAsync();
        await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Re-trigger update check via second initialize call and message receipt
        vm.Receive(new UpdateSettingsChangedMessage(true, true, 5));
        await vm.InitializeAsync();
        await Task.Delay(200);

        // Assert that branch update notification was shown exactly once
        var branchUpdateNotifications = shownNotifications
            .Where(n => n.Title == AppUpdateConstants.BranchUpdateAvailableNotificationTitle)
            .ToList();
        Assert.Single(branchUpdateNotifications);
    }

    /// <summary>
    /// Tests that background update check detects merged PR and falls back to development branch artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenSubscribedPrIsMergedAndDevArtifactAvailable_ShowsMergedPrFallbackNotificationAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var userSettings = new UserSettings { SubscribedPrNumber = 265 };
        userSettingsMock.Setup(x => x.Get()).Returns(userSettings);
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var shownNotifications = new List<NotificationMessage>();
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-dev",
            GitHash: "abcdef1",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.SetupProperty(x => x.SubscribedPrNumber, 265);
        mockVelopackUpdateManager.SetupProperty(x => x.SubscribedBranch, null);

        // When PR is 265, return null and set IsPrMergedOrClosed = true
        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns<System.Threading.CancellationToken>(_ =>
            {
                if (mockVelopackUpdateManager.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                mockVelopackUpdateManager.SetupGet(m => m.IsPrMergedOrClosed).Returns(true);
                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                shownNotifications.Add(msg);
                if (msg.Title == AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        using var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Act
        await vm.InitializeAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(updateNotification);
        Assert.Equal(AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle, updateNotification.Title);
        Assert.True(updateNotification.IsPersistent);
        Assert.True(updateNotification.ShowInBadge);
        Assert.Contains("265", updateNotification.Message);
        Assert.Contains("merged", updateNotification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(updateNotification.Actions);
    }

    /// <summary>
    /// Tests that clicking update on a merged PR fallback notification clears SubscribedPrNumber in settings.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenSubscribedPrMergedUpdateClicked_ClearsSettingsAndInstallsAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var userSettings = new UserSettings { SubscribedPrNumber = 265 };
        userSettingsMock.Setup(x => x.Get()).Returns(userSettings);
        userSettingsMock.Setup(x => x.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(userSettings));
        userSettingsMock.Setup(x => x.SaveAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();
        var installStartedTcs = new TaskCompletionSource<bool>();

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-dev",
            GitHash: "abcdef1",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.SetupProperty(x => x.SubscribedPrNumber, 265);
        mockVelopackUpdateManager.SetupProperty(x => x.SubscribedBranch, null);

        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns<System.Threading.CancellationToken>(_ =>
            {
                if (mockVelopackUpdateManager.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                mockVelopackUpdateManager.SetupGet(m => m.IsPrMergedOrClosed).Returns(true);
                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        mockVelopackUpdateManager.Setup(x => x.InstallArtifactAsync(
                devArtifactInfo,
                It.IsAny<IProgress<UpdateProgress>>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => installStartedTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        using var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Act
        await vm.InitializeAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Click Update action
        updateNotification.Actions[0].Callback?.Invoke();
        await installStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Null(userSettings.SubscribedPrNumber);
        userSettingsMock.Verify(x => x.SaveAsync(It.IsAny<System.Threading.CancellationToken>()), Times.AtLeastOnce);
        mockVelopackUpdateManager.Verify(x => x.InstallArtifactAsync(devArtifactInfo, It.IsAny<IProgress<UpdateProgress>>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that background update check detects stale feature branch and falls back to development branch artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_WhenSubscribedBranchIsStaleAndDevArtifactAvailable_ShowsBranchStaleFallbackNotificationAsync()
    {
        // Arrange
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var userSettings = new UserSettings { SubscribedBranch = "feat/deleted-branch" };
        userSettingsMock.Setup(x => x.Get()).Returns(userSettings);
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-dev",
            GitHash: "abcdef1",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.SetupProperty(x => x.SubscribedPrNumber, null);
        mockVelopackUpdateManager.SetupProperty(x => x.SubscribedBranch, "feat/deleted-branch");

        mockVelopackUpdateManager.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns<System.Threading.CancellationToken>(_ =>
            {
                if (mockVelopackUpdateManager.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        using var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);

        // Act
        await vm.InitializeAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(updateNotification);
        Assert.Equal(AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle, updateNotification.Title);
        Assert.True(updateNotification.IsPersistent);
        Assert.True(updateNotification.ShowInBadge);
        Assert.Contains("feat/deleted-branch", updateNotification.Message);
        Assert.Single(updateNotification.Actions);
    }

    /// <summary>
    /// Tests that CurrentTabViewModel returns the correct ViewModel based on SelectedTab.
    /// </summary>
    /// <param name="tab">The tab to select.</param>
    [Theory]
    [InlineData(NavigationTab.GameProfiles)]
    [InlineData(NavigationTab.Downloads)]
    [InlineData(NavigationTab.Tools)]
    [InlineData(NavigationTab.Settings)]
    [InlineData(NavigationTab.Info)]
    public void CurrentTabViewModel_ReturnsCorrectViewModel(NavigationTab tab)
    {
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        var vm = new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsViewModel: CreateDownloadsViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);
        vm.SelectTabCommand.Execute(tab);
        var currentViewModel = vm.CurrentTabViewModel;
        Assert.NotNull(currentViewModel);
        switch (tab)
        {
            case NavigationTab.GameProfiles:
                Assert.IsType<GameProfileLauncherViewModel>(currentViewModel);
                break;
            case NavigationTab.Downloads:
                Assert.IsType<DownloadsViewModel>(currentViewModel);
                break;
            case NavigationTab.Tools:
                Assert.IsType<ToolsViewModel>(currentViewModel);
                break;
            case NavigationTab.Settings:
                Assert.IsType<SettingsViewModel>(currentViewModel);
                break;
            case NavigationTab.Info:
                Assert.IsType<InfoViewModel>(currentViewModel);
                break;
            default:
                // No additional specific tab assertions
                break;
        }
    }

    /// <summary>
    /// Creates a default ToolsViewModel with mocked services for reuse.
    /// </summary>
    private static ToolsViewModel CreateToolsVm()
    {
        var mockToolService = new Mock<IToolManager>();
        var mockLogger = new Mock<ILogger<ToolsViewModel>>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        return new ToolsViewModel(mockToolService.Object, mockLogger.Object, mockServiceProvider.Object);
    }

    /// <summary>
    /// Creates a default SettingsViewModel with mocked services for reuse.
    /// </summary>
    private static (SettingsViewModel SettingsVm, Mock<IUserSettingsService> UserSettingsMock) CreateSettingsVm()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());
        var mockLogger = new Mock<ILogger<SettingsViewModel>>();
        var mockCasService = new Mock<ICasService>();
        var mockProfileManager = new Mock<IGameProfileManager>();
        var mockWorkspaceManager = new Mock<IWorkspaceManager>();
        var mockManifestPool = new Mock<IContentManifestPool>();
        var mockUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockNotificationServiceForSettings = new Mock<INotificationService>();
        var mockConfigurationProvider = new Mock<IConfigurationProviderService>();
        var mockInstallationService = new Mock<IGameInstallationService>();
        var mockStorageLocationService = new Mock<IStorageLocationService>();
        var mockUserDataTracker = new Mock<IUserDataTracker>();
        var mockDialogService = new Mock<IDialogService>();
        var mockGitHubTokenStorage = new Mock<IGitHubTokenStorage>();

        var settingsVm = new SettingsViewModel(
            mockUserSettings.Object,
            mockLogger.Object,
            mockCasService.Object,
            mockProfileManager.Object,
            mockWorkspaceManager.Object,
            mockManifestPool.Object,
            mockUpdateManager.Object,
            mockNotificationServiceForSettings.Object,
            mockConfigurationProvider.Object,
            mockInstallationService.Object,
            mockStorageLocationService.Object,
            mockUserDataTracker.Object,
            mockDialogService.Object,
            mockGitHubTokenStorage.Object);
        return (settingsVm, mockUserSettings);
    }

    private static IConfigurationProviderService CreateConfigProviderMock()
    {
        var mock = new Mock<IConfigurationProviderService>();

        // Minimal defaults used by MainViewModel
        mock.Setup(x => x.GetLastSelectedTab()).Returns(NavigationTab.GameProfiles);
        var tempPath = Path.Combine(Path.GetTempPath(), "GenHub", "Manifests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        mock.Setup(x => x.GetManifestsPath()).Returns(tempPath);
        return mock.Object;
    }

    /// <summary>
    /// Helper method to create a DownloadsViewModel with mocked dependencies.
    /// </summary>
    private static DownloadsViewModel CreateDownloadsViewModel(IConfigurationProviderService configProvider)
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<DownloadsViewModel>>();
        var mockNotificationService = new Mock<INotificationService>();

        // Create the three required dependencies for the discoverer
        var mockGitHubClient = new Mock<IGitHubApiClient>();
        var mockDiscovererLogger = new Mock<ILogger<GitHubTopicsDiscoverer>>();

        // Instantiate the real class with the two mocks
        var realGitHubDiscoverer = new GitHubTopicsDiscoverer(
            mockGitHubClient.Object,
            mockDiscovererLogger.Object);

        return new DownloadsViewModel(
            mockServiceProvider.Object,
            mockLogger.Object,
            mockNotificationService.Object,
            realGitHubDiscoverer,
            configProvider);
    }

    /// <summary>
    /// Helper method to create a GameProfileLauncherViewModel with mocked dependencies.
    /// </summary>
    private static GameProfileLauncherViewModel CreateGameProfileLauncherViewModel()
    {
        var installationService = new Mock<IGameInstallationService>();
        var gameProfileManager = new Mock<IGameProfileManager>();
        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new GameProfileSettingsViewModel(
            new Mock<IGameProfileManager>().Object,
            new Mock<IGameSettingsService>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IProfileContentLoader>().Object,
            null, // ProfileResourceService
            null, // INotificationService
            null, // IContentManifestPool
            null, // IContentStorageService
            null, // ILocalContentService
            null, // IGenLauncherNormalizationService
            null, // IDialogService
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);

        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();
        var notificationService = new Mock<INotificationService>();

        return new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<GenHub.Core.Interfaces.GameClients.IGameClientDetector>().Object,
            notificationService.Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);
    }

    private static Mock<INotificationService> CreateNotificationServiceMock()
    {
        var mock = new Mock<INotificationService>();
        mock.Setup(x => x.Notifications).Returns(Observable.Empty<NotificationMessage>());
        mock.Setup(x => x.NotificationHistory).Returns(Observable.Empty<NotificationMessage>());
        mock.Setup(x => x.DismissRequests).Returns(Observable.Empty<Guid>());
        mock.Setup(x => x.DismissAllRequests).Returns(Observable.Empty<bool>());
        mock.Setup(x => x.UpdateRequests).Returns(Observable.Empty<(Guid Id, string? Title, string Message)>());
        return mock;
    }

    private static ProfileResourceService CreateProfileResourceService()
    {
        return new ProfileResourceService(NullLogger<ProfileResourceService>.Instance);
    }

    private static NotificationFeedViewModel CreateNotificationFeedViewModel(INotificationService notificationService)
    {
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<NotificationFeedViewModel>>();
        return new NotificationFeedViewModel(notificationService, mockLoggerFactory.Object, mockLogger.Object);
    }

    private static InfoViewModel CreateInfoViewModel()
    {
        return new InfoViewModel([]);
    }
}
