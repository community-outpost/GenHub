using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Common.Services;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Messages;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using GenHub.Tests.Core.Features.Tools.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Contains unit tests for the <see cref="MainViewModel"/> class.
/// </summary>
public class MainViewModelTests
{
    private const string DeferredGettingStartedLog = "Deferring the Getting Started dialog";

    private const string ClosedGettingStartedLog = "Closed the Getting Started dialog";

    private static readonly TimeSpan DispatcherSignalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Tests that <see cref="MainViewModel"/> can be instantiated successfully.
    /// </summary>
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        var vm = CreateMainViewModel();

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
        var vm = CreateMainViewModel();
        vm.SelectTabCommand.Execute(tab);
        Assert.Equal(tab, vm.SelectedTab);
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
        var vm = CreateMainViewModel();
        vm.SelectTabCommand.Execute(tab);
        var currentViewModel = vm.CurrentTabViewModel;
        Assert.NotNull(currentViewModel);
        switch (tab)
        {
            case NavigationTab.GameProfiles:
                Assert.IsType<GameProfileLauncherViewModel>(currentViewModel);
                break;
            case NavigationTab.Downloads:
                Assert.IsType<DownloadsBrowserViewModel>(currentViewModel);
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
                Assert.Fail($"Unexpected tab type: {tab}");
                break;
        }
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel.InitializeAsync"/> completes without exceptions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfullyAsync()
    {
        var mockBackgroundCoordinator = new Mock<IBackgroundUpdateCoordinator>();
        var vm = CreateMainViewModel(mockBackgroundCoordinator: mockBackgroundCoordinator);

        var exception = await Record.ExceptionAsync(() => vm.InitializeAsync());

        Assert.Null(exception);
        mockBackgroundCoordinator.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel.InitializeAsync"/> can be called multiple times safely.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_CanBeCalledMultipleTimesAsync()
    {
        var mockBackgroundCoordinator = new Mock<IBackgroundUpdateCoordinator>();
        var vm = CreateMainViewModel(mockBackgroundCoordinator: mockBackgroundCoordinator);
        await vm.InitializeAsync();
        await vm.InitializeAsync();
        mockBackgroundCoordinator.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel.Dispose"/> can be called multiple times without throwing.
    /// </summary>
    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var vm = CreateMainViewModel();

        var exception = Record.Exception(() =>
        {
            vm.Dispose();
            vm.Dispose();
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel.SelectTabCommand"/> selects the requested tab.
    /// </summary>
    [Fact]
    public void SelectTabCommand_SelectsRequestedTab()
    {
        var vm = CreateMainViewModel();
        vm.SelectTabCommand.Execute(NavigationTab.Settings);
        Assert.Equal(NavigationTab.Settings, vm.SelectedTab);
    }

    /// <summary>
    /// Tests that selecting the Tools tab activates the tools tab, opens the pane, and restores the remembered tool.
    /// </summary>
    [Fact]
    public void SelectTab_ToolsTab_ActivatesToolsTabAndRestoresRememberedTool()
    {
        var vm = CreateMainViewModel();
        var tool = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Author");
        vm.ToolsViewModel.InstalledTools.Add(tool);
        vm.ToolsViewModel.SelectedTool = tool;
        Assert.Equal(tool, vm.ToolsViewModel.SelectedTool);

        // Simulate tab switch away from Tools and closing pane
        vm.ToolsViewModel.SelectedTool = null;
        vm.ToolsViewModel.IsPaneOpen = false;
        vm.SelectTabCommand.Execute(NavigationTab.GameProfiles);

        // Select Tools tab
        vm.SelectTabCommand.Execute(NavigationTab.Tools);

        // Assert
        Assert.Equal(NavigationTab.Tools, vm.SelectedTab);
        Assert.True(vm.ToolsViewModel.IsPaneOpen);
        Assert.Equal(tool, vm.ToolsViewModel.SelectedTool);
    }

    /// <summary>
    /// Tests that InitializeAsync announces post-update when a new app version is detected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_WhenAppVersionUpgraded_UpdatesLastSeenAppVersion()
    {
        // Arrange
        var userSettings = new UserSettings { LastSeenAppVersion = "0.0.0" };
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(s => s.Get()).Returns(userSettings);
        mockUserSettings.Setup(s => s.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(userSettings));

        var vm = CreateMainViewModel(mockUserSettings: mockUserSettings);

        // Act
        await vm.InitializeAsync();

        // Assert
        Assert.NotNull(userSettings.LastSeenAppVersion);
        Assert.NotEqual("0.0.0", userSettings.LastSeenAppVersion);
    }

    /// <summary>
    /// Tests that InitializeAsync shows the post-update notification with the View Changelog action when a new app version is detected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_WhenAppVersionUpgraded_ShowsChangelogNotificationAsync()
    {
        // Arrange
        var userSettings = new UserSettings { LastSeenAppVersion = "0.0.0" };
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(s => s.Get()).Returns(userSettings);
        mockUserSettings.Setup(s => s.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(userSettings));
        var mockNotificationService = CreateNotificationServiceMock();

        var vm = CreateMainViewModel(
            mockUserSettings: mockUserSettings,
            mockNotificationServiceParam: mockNotificationService);

        // Act
        await vm.InitializeAsync();

        // Assert
        // The announcement is suppressed on CI-built binaries (GitShortHash metadata
        // present), so the expectation follows the build under test to stay
        // deterministic for both local and CI-built test runs.
        var expectedShows = AppConstants.IsCiBuild ? Times.Never() : Times.Once();
        mockNotificationService.Verify(
            x => x.Show(It.Is<NotificationMessage>(m =>
                m.Actions.Any(a => a.Text == AppUpdateConstants.ViewChangelogAction))),
            expectedShows);
    }

    /// <summary>
    /// Verifies that a session opened to handle a link defers the Getting Started dialog so the two modals never stack.
    /// </summary>
    /// <param name="linkReceived">Whether the session received a link before launching finished.</param>
    /// <param name="expectedDialogs">How many Getting Started dialogs should open.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task InitializeAsync_GettingStarted_DeferredWhenSessionOpenedForLinkAsync(bool linkReceived, int expectedDialogs)
    {
        var dialogShown = NewSignal();
        var dialogService = CreateGettingStartedDialogService(dialogShown);
        var logger = new SignalingLogger(DeferredGettingStartedLog);
        using var tracker = new LinkActivationTracker();
        tracker.MarkLaunchFinished();
        if (linkReceived)
        {
            tracker.RecordLink();
        }

        using var vm = CreateMainViewModel(dialogService: dialogService, linkActivationTracker: tracker, logger: logger);

        await vm.InitializeAsync();
        await PumpDispatcherUntilAsync(Task.WhenAny(dialogShown.Task, logger.Logged));

        VerifyGettingStartedShown(dialogService, Times.Exactly(expectedDialogs));
    }

    /// <summary>
    /// Verifies that Getting Started waits for the launch to finish, because macOS delivers a launch link just before that.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task InitializeAsync_GettingStarted_DeferredWhenLinkArrivesBeforeLaunchFinishesAsync()
    {
        var dialogService = CreateGettingStartedDialogService();
        var logger = new SignalingLogger(DeferredGettingStartedLog);
        using var tracker = new LinkActivationTracker(launchFinished: false);
        using var vm = CreateMainViewModel(dialogService: dialogService, linkActivationTracker: tracker, logger: logger);

        await vm.InitializeAsync();
        Dispatcher.UIThread.RunJobs();
        tracker.RecordLink();
        tracker.MarkLaunchFinished();
        await PumpDispatcherUntilAsync(logger.Logged);

        VerifyGettingStartedShown(dialogService, Times.Never());
    }

    /// <summary>
    /// Verifies that Getting Started opens as soon as a launch without a link finishes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task InitializeAsync_GettingStarted_ShownWhenLaunchFinishesWithoutLinkAsync()
    {
        var dialogShown = NewSignal();
        var dialogService = CreateGettingStartedDialogService(dialogShown);
        using var tracker = new LinkActivationTracker(launchFinished: false);
        using var vm = CreateMainViewModel(dialogService: dialogService, linkActivationTracker: tracker);

        await vm.InitializeAsync();
        Dispatcher.UIThread.RunJobs();
        VerifyGettingStartedShown(dialogService, Times.Never());

        tracker.MarkLaunchFinished();
        await PumpDispatcherUntilAsync(dialogShown.Task);

        VerifyGettingStartedShown(dialogService, Times.Once());
    }

    /// <summary>
    /// Verifies that a link arriving while Getting Started is open closes it without marking it as seen.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task InitializeAsync_GettingStarted_ClosedWithoutMarkingSeenWhenLinkArrivesAsync()
    {
        var dialogToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<(DialogAction? Action, bool DoNotAskAgain)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, IEnumerable<DialogAction> _, bool _, CancellationToken token) =>
            {
                token.Register(() => dialogResult.TrySetCanceled(token));
                dialogToken.TrySetResult(token);
                return dialogResult.Task;
            });
        var settings = new UserSettings();
        var userSettings = new Mock<IUserSettingsService>();
        userSettings.Setup(x => x.Get()).Returns(settings);
        userSettings.Setup(x => x.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(settings));
        var logger = new SignalingLogger(ClosedGettingStartedLog);
        using var tracker = new LinkActivationTracker();
        tracker.MarkLaunchFinished();
        using var vm = CreateMainViewModel(mockUserSettings: userSettings, dialogService: dialogService, linkActivationTracker: tracker, logger: logger);

        await vm.InitializeAsync();
        await PumpDispatcherUntilAsync(dialogToken.Task);
        var token = await dialogToken.Task;
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);

        tracker.RecordLink();
        await PumpDispatcherUntilAsync(logger.Logged);

        Assert.True(token.IsCancellationRequested);
        Assert.False(settings.HasSeenQuickStart);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Mock<IDialogService> CreateGettingStartedDialogService(TaskCompletionSource? shown = null)
    {
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => shown?.TrySetResult())
            .ReturnsAsync((null, false));
        return dialogService;
    }

    private static void VerifyGettingStartedShown(Mock<IDialogService> dialogService, Times times) =>
        dialogService.Verify(
            x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), true, It.IsAny<CancellationToken>()),
            times);

    /// <summary>
    /// Runs dispatcher jobs until <paramref name="signal"/> completes, because the Getting Started callback
    /// resumes through a thread pool hop whose timing a fixed number of pumps cannot bound.
    /// </summary>
    /// <param name="signal">The task that completes once the code under test reached the awaited point.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous wait.</returns>
    private static async Task PumpDispatcherUntilAsync(Task signal)
    {
        var deadline = DateTime.UtcNow + DispatcherSignalTimeout;
        while (!signal.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(signal.IsCompleted, $"The dispatcher did not reach the expected state within {DispatcherSignalTimeout.TotalSeconds} seconds.");
        Dispatcher.UIThread.RunJobs();
    }

    private static MainViewModel CreateMainViewModel(
        Mock<IBackgroundUpdateCoordinator>? mockBackgroundCoordinator = null,
        Mock<IUserSettingsService>? mockUserSettings = null,
        Mock<INotificationService>? mockNotificationServiceParam = null,
        Mock<IDialogService>? dialogService = null,
        ILinkActivationTracker? linkActivationTracker = null,
        ILogger<MainViewModel>? logger = null)
    {
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var coordinator = mockBackgroundCoordinator ?? new Mock<IBackgroundUpdateCoordinator>();
        var mockNotificationService = mockNotificationServiceParam ?? CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var notificationFeedVm = CreateNotificationFeedViewModel(mockNotificationService.Object);

        return new MainViewModel(
            gameProfilesViewModel: CreateGameProfileLauncherViewModel(),
            downloadsBrowserViewModel: CreateDownloadsBrowserViewModel(configProvider),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: mockUserSettings?.Object ?? userSettingsMock.Object,
            backgroundUpdateCoordinator: coordinator.Object,
            notificationService: mockNotificationService.Object,
            dialogService: (dialogService ?? new Mock<IDialogService>()).Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: logger ?? Mock.Of<ILogger<MainViewModel>>(),
            linkActivationTracker: linkActivationTracker);
    }

    private static ToolsViewModel CreateToolsVm()
    {
        var mockToolService = new Mock<IToolManager>();
        var mockLogger = new Mock<ILogger<ToolsViewModel>>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        return new ToolsViewModel(mockToolService.Object, mockLogger.Object, mockServiceProvider.Object);
    }

    private static (SettingsViewModel SettingsVm, Mock<IUserSettingsService> UserSettingsMock) CreateSettingsVm()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());
        var mockLogger = new Mock<ILogger<SettingsViewModel>>();
        var mockCasService = new Mock<ICasService>();
        var mockCasLifecycleManager = new Mock<ICasLifecycleManager>();
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
        var mockStorageMigrationService = new Mock<IStorageMigrationService>();
        var mockGitHubAuthService = new Mock<IGitHubAuthService>();

        var settingsVm = new SettingsViewModel(
            mockUserSettings.Object,
            mockLogger.Object,
            mockCasService.Object,
            mockCasLifecycleManager.Object,
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
            mockStorageMigrationService.Object,
            themeService: null,
            gitHubAuthService: mockGitHubAuthService.Object);
        return (settingsVm, mockUserSettings);
    }

    private static IConfigurationProviderService CreateConfigProviderMock()
    {
        var mock = new Mock<IConfigurationProviderService>();
        mock.Setup(x => x.GetLastSelectedTab()).Returns(NavigationTab.GameProfiles);
        var tempPath = Path.Combine(Path.GetTempPath(), "GenHub", "Manifests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        mock.Setup(x => x.GetManifestsPath()).Returns(tempPath);
        return mock.Object;
    }

    /// <summary>
    /// Helper method to create a DownloadsBrowserViewModel with mocked dependencies.
    /// </summary>
    private static DownloadsBrowserViewModel CreateDownloadsBrowserViewModel(IConfigurationProviderService configProvider)
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<DownloadsBrowserViewModel>>();
        var mockDiscoverers = new List<IContentDiscoverer>();
        var mockContentStateService = new Mock<IContentStateService>();
        var mockContentOrchestrator = new Mock<IContentOrchestrator>();
        var mockProfileContentService = new Mock<IProfileContentService>();
        var mockProfileManager = new Mock<IGameProfileManager>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockSubscriptionStore = new Mock<IPublisherSubscriptionStore>();
        mockSubscriptionStore
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        return new DownloadsBrowserViewModel(
            mockServiceProvider.Object,
            mockLogger.Object,
            mockDiscoverers,
            mockContentStateService.Object,
            mockContentOrchestrator.Object,
            mockProfileContentService.Object,
            mockProfileManager.Object,
            mockNotificationService.Object,
            new Mock<ILoggerFactory>().Object,
            mockSubscriptionStore.Object);
    }

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
            null,
            null,
            null,
            null,
            null,
            null,
            null,
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
            NullLogger<GameProfileLauncherViewModel>.Instance,
            new Mock<ILocalizationService>().Object);
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
        var localizationMock = new Mock<ILocalizationService>();
        localizationMock.Setup(m => m.CurrentCulture).Returns(System.Globalization.CultureInfo.InvariantCulture);
        localizationMock.Setup(m => m.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, args) => args != null && args.Length > 0 ? $"{args[0]}" : key);
        return new ProfileResourceService(NullLogger<ProfileResourceService>.Instance, localizationMock.Object);
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

    /// <summary>
    /// Completes <see cref="Logged"/> when <see cref="MainViewModel"/> logs a message that marks the end of a decision.
    /// </summary>
    /// <param name="messageFragment">The fragment that identifies the awaited message.</param>
    private sealed class SignalingLogger(string messageFragment) : ILogger<MainViewModel>
    {
        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets a task that completes once the awaited message is logged.
        /// </summary>
        public Task Logged => _logged.Task;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains(messageFragment, StringComparison.Ordinal))
            {
                _logged.TrySetResult();
            }
        }
    }
}
