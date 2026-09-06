using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

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
                throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown navigation tab");
        }
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel.InitializeAsync"/> initializes tab viewmodels and background update coordinator.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_InitializesTabsAndBackgroundCoordinatorAsync()
    {
        var mockBackgroundCoordinator = new Mock<IBackgroundUpdateCoordinator>();
        var vm = CreateMainViewModel(mockBackgroundCoordinator: mockBackgroundCoordinator);

        await vm.InitializeAsync();

        mockBackgroundCoordinator.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafeAsync()
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

    private static MainViewModel CreateMainViewModel(
        Mock<IBackgroundUpdateCoordinator>? mockBackgroundCoordinator = null,
        Mock<IUserSettingsService>? mockUserSettings = null)
    {
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var coordinator = mockBackgroundCoordinator ?? new Mock<IBackgroundUpdateCoordinator>();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
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
            dialogService: new Mock<IDialogService>().Object,
            notificationFeedViewModel: notificationFeedVm,
            infoViewModel: CreateInfoViewModel(),
            logger: mockLogger.Object);
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
            themeService: null,
            gitHubTokenStorage: mockGitHubTokenStorage.Object);
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
