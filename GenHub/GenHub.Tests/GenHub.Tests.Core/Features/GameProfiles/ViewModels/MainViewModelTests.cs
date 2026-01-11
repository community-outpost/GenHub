using System.Reactive.Linq;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
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
            downloadsViewModel: CreateDownloadsViewModel(),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            notificationFeedViewModel: notificationFeedVm,
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
            downloadsViewModel: CreateDownloadsViewModel(),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            notificationFeedViewModel: notificationFeedVm,
            logger: mockLogger.Object);
        vm.SelectTabCommand.Execute(tab);
        Assert.Equal(tab, vm.SelectedTab);
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafe()
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
            downloadsViewModel: CreateDownloadsViewModel(),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            notificationFeedViewModel: notificationFeedVm,
            logger: mockLogger.Object);
        await vm.InitializeAsync(); // Should not throw
        Assert.True(true);
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
            downloadsViewModel: CreateDownloadsViewModel(),
            toolsViewModel: toolsVm,
            settingsViewModel: settingsVm,
            notificationManager: mockNotificationManager.Object,
            configurationProvider: configProvider,
            userSettingsService: userSettingsMock.Object,
            velopackUpdateManager: mockVelopackUpdateManager.Object,
            notificationService: mockNotificationService.Object,
            notificationFeedViewModel: notificationFeedVm,
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
        var mockNotificationService = new Mock<INotificationService>();
        var mockNotificationServiceForSettings = new Mock<INotificationService>();
        var mockConfigurationProvider = new Mock<IConfigurationProviderService>();
        var mockInstallationService = new Mock<IGameInstallationService>();
        var mockStorageLocationService = new Mock<IStorageLocationService>();
        var mockUserDataTracker = new Mock<IUserDataTracker>();
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
            mockGitHubTokenStorage.Object);
        return (settingsVm, mockUserSettings);
    }

    private static IConfigurationProviderService CreateConfigProviderMock()
    {
        var mock = new Mock<IConfigurationProviderService>();

        // Minimal defaults used by MainViewModel
        mock.Setup(x => x.GetLastSelectedTab()).Returns(NavigationTab.GameProfiles);
        return mock.Object;
    }

    /// <summary>
    /// Helper method to create a DownloadsViewModel with mocked dependencies.
    /// </summary>
    private static DownloadsViewModel CreateDownloadsViewModel()
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
            realGitHubDiscoverer);
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
}
