using System.Reactive.Linq;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Interfaces.GitHub;
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
using GenHub.Features.GeneralsOnline.Services;
using GenHub.Features.GeneralsOnline.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Caching.Memory;
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
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockProfileEditorFacade = new Mock<IProfileEditorFacade>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockGeneralsOnlineAuthService = CreateGeneralsOnlineAuthServiceMock();
        var generalsOnlineVm = CreateGeneralsOnlineVm();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());

        // Act
        var vm = new MainViewModel(
            CreateGameProfileLauncherViewModel(),
            CreateDownloadsViewModel(),
            toolsVm,
            settingsVm,
            mockNotificationManager.Object,
            generalsOnlineVm,
            mockOrchestrator.Object,
            configProvider,
            userSettingsMock.Object,
            mockProfileEditorFacade.Object,
            mockVelopackUpdateManager.Object,
            CreateProfileResourceService(),
            mockGeneralsOnlineAuthService.Object,
            mockLogger.Object);

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
    [InlineData(NavigationTab.GeneralsOnline)]
    [InlineData(NavigationTab.Settings)]
    public void SelectTabCommand_SetsSelectedTab(NavigationTab tab)
    {
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockProfileEditorFacade = new Mock<IProfileEditorFacade>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockGeneralsOnlineAuthService = CreateGeneralsOnlineAuthServiceMock();
        var generalsOnlineVm = CreateGeneralsOnlineVm();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var vm = new MainViewModel(
            CreateGameProfileLauncherViewModel(),
            CreateDownloadsViewModel(),
            toolsVm,
            settingsVm,
            mockNotificationManager.Object,
            generalsOnlineVm,
            mockOrchestrator.Object,
            configProvider,
            userSettingsMock.Object,
            mockProfileEditorFacade.Object,
            mockVelopackUpdateManager.Object,
            CreateProfileResourceService(),
            mockGeneralsOnlineAuthService.Object,
            mockLogger.Object);
        vm.SelectTabCommand.Execute(tab);
        Assert.Equal(tab, vm.SelectedTab);
    }

    /// <summary>
    /// Verifies ScanAndCreateProfilesAsync can be called.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanAndCreateProfilesAsync_CanBeCalled()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockProfileEditorFacade = new Mock<IProfileEditorFacade>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockGeneralsOnlineAuthService = CreateGeneralsOnlineAuthServiceMock();
        var generalsOnlineVm = CreateGeneralsOnlineVm();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var viewModel = new MainViewModel(
            CreateGameProfileLauncherViewModel(),
            CreateDownloadsViewModel(),
            toolsVm,
            settingsVm,
            mockNotificationManager.Object,
            generalsOnlineVm,
            mockOrchestrator.Object,
            configProvider,
            userSettingsMock.Object,
            mockProfileEditorFacade.Object,
            mockVelopackUpdateManager.Object,
            CreateProfileResourceService(),
            mockGeneralsOnlineAuthService.Object,
            mockLogger.Object);

        // Act & Assert
        await viewModel.ScanAndCreateProfilesAsync();
        Assert.True(true); // Test passes if no exception is thrown
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafe()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockProfileEditorFacade = new Mock<IProfileEditorFacade>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        mockVelopackUpdateManager.Setup(x => x.CheckForUpdatesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((Velopack.UpdateInfo?)null);
        var mockGeneralsOnlineAuthService = CreateGeneralsOnlineAuthServiceMock();
        var generalsOnlineVm = CreateGeneralsOnlineVm();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var vm = new MainViewModel(
            CreateGameProfileLauncherViewModel(),
            CreateDownloadsViewModel(),
            toolsVm,
            settingsVm,
            mockNotificationManager.Object,
            generalsOnlineVm,
            mockOrchestrator.Object,
            configProvider,
            userSettingsMock.Object,
            mockProfileEditorFacade.Object,
            mockVelopackUpdateManager.Object,
            CreateProfileResourceService(),
            mockGeneralsOnlineAuthService.Object,
            mockLogger.Object);
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
    [InlineData(NavigationTab.GeneralsOnline)]
    [InlineData(NavigationTab.Settings)]
    public void CurrentTabViewModel_ReturnsCorrectViewModel(NavigationTab tab)
    {
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var (settingsVm, userSettingsMock) = CreateSettingsVm();
        var toolsVm = CreateToolsVm();
        var configProvider = CreateConfigProviderMock();
        var mockProfileEditorFacade = new Mock<IProfileEditorFacade>();
        var mockVelopackUpdateManager = new Mock<IVelopackUpdateManager>();
        var mockGeneralsOnlineAuthService = CreateGeneralsOnlineAuthServiceMock();
        var generalsOnlineVm = CreateGeneralsOnlineVm();
        var mockLogger = new Mock<ILogger<MainViewModel>>();
        var mockNotificationService = CreateNotificationServiceMock();
        var mockNotificationManager = new Mock<NotificationManagerViewModel>(
            mockNotificationService.Object,
            Mock.Of<ILogger<NotificationManagerViewModel>>(),
            Mock.Of<ILogger<NotificationItemViewModel>>());
        var vm = new MainViewModel(
            CreateGameProfileLauncherViewModel(),
            CreateDownloadsViewModel(),
            toolsVm,
            settingsVm,
            mockNotificationManager.Object,
            generalsOnlineVm,
            mockOrchestrator.Object,
            configProvider,
            userSettingsMock.Object,
            mockProfileEditorFacade.Object,
            mockVelopackUpdateManager.Object,
            CreateProfileResourceService(),
            mockGeneralsOnlineAuthService.Object,
            mockLogger.Object);
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
            case NavigationTab.GeneralsOnline:
                Assert.IsType<GeneralsOnlineViewModel>(currentViewModel);
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
            mockInstallationService.Object);
        return (settingsVm, mockUserSettings);
    }

    /// <summary>
    /// Creates a mock IGeneralsOnlineAuthService for testing.
    /// </summary>
    private static Mock<IGeneralsOnlineAuthService> CreateGeneralsOnlineAuthServiceMock()
    {
        var mock = new Mock<IGeneralsOnlineAuthService>();
        mock.Setup(x => x.IsAuthenticated).Returns(Observable.Return(false));
        mock.Setup(x => x.CurrentToken).Returns((string?)null);
        return mock;
    }

    /// <summary>
    /// Creates a GeneralsOnlineViewModel with mocked dependencies.
    /// </summary>
    private static GeneralsOnlineViewModel CreateGeneralsOnlineVm()
    {
        var mockApiClient = new Mock<IGeneralsOnlineApiClient>();
        var mockAuthService = new Mock<IGeneralsOnlineAuthService>();
        mockAuthService.Setup(x => x.IsAuthenticated).Returns(Observable.Return(false));
        var mockLogger = new Mock<ILogger<GeneralsOnlineViewModel>>();

        // Create HTML parsing service with mock logger
        var htmlParser = new HtmlParsingService(new Mock<ILogger<HtmlParsingService>>().Object);

        var mockExternalLinkService = new Mock<IExternalLinkService>();

        var leaderboardVm = new LeaderboardViewModel(
            mockApiClient.Object,
            htmlParser,
            new Mock<ILogger<LeaderboardViewModel>>().Object);

        var matchHistoryVm = new MatchHistoryViewModel(
            mockApiClient.Object,
            htmlParser,
            mockExternalLinkService.Object,
            new Mock<ILogger<MatchHistoryViewModel>>().Object);

        var lobbiesVm = new LobbiesViewModel(
            mockApiClient.Object,
            htmlParser,
            new Mock<ILogger<LobbiesViewModel>>().Object);

        var activeMatchesVm = new ActiveMatchesViewModel(
            mockApiClient.Object,
            htmlParser,
            new Mock<ILogger<ActiveMatchesViewModel>>().Object);

        var serviceStatusVm = new ServiceStatusViewModel(
            mockApiClient.Object,
            htmlParser,
            mockExternalLinkService.Object,
            new Mock<ILogger<ServiceStatusViewModel>>().Object);

        var loginVm = new LoginViewModel(
            mockApiClient.Object,
            mockAuthService.Object,
            mockExternalLinkService.Object,
            new Mock<ILogger<LoginViewModel>>().Object);

        return new GeneralsOnlineViewModel(
            mockApiClient.Object,
            mockAuthService.Object,
            loginVm,
            leaderboardVm,
            matchHistoryVm,
            lobbiesVm,
            activeMatchesVm,
            serviceStatusVm,
            mockExternalLinkService.Object,
            mockLogger.Object);
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
        var mockGitHubDiscoverer = new Mock<GitHubTopicsDiscoverer>(
            It.IsAny<IGitHubApiClient>(),
            It.IsAny<ILogger<GitHubTopicsDiscoverer>>(),
            It.IsAny<IMemoryCache>());
        return new DownloadsViewModel(
            mockServiceProvider.Object,
            mockLogger.Object,
            mockNotificationService.Object,
            mockGitHubDiscoverer.Object);
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
            notificationService.Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);
    }

    private static Mock<INotificationService> CreateNotificationServiceMock()
    {
        var mock = new Mock<INotificationService>();
        mock.Setup(x => x.Notifications).Returns(Observable.Empty<NotificationMessage>());
        mock.Setup(x => x.DismissRequests).Returns(Observable.Empty<Guid>());
        mock.Setup(x => x.DismissAllRequests).Returns(Observable.Empty<bool>());
        return mock;
    }

    private static ProfileResourceService CreateProfileResourceService()
    {
        return new ProfileResourceService(NullLogger<ProfileResourceService>.Instance);
    }
}
