using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Contains tests for the <see cref="SharedViewModelModule"/> dependency injection.
/// </summary>
public class SharedViewModelModuleTests
{
    /// <summary>
    /// Verifies that all ViewModels registered in the <see cref="SharedViewModelModule"/>
    /// can be successfully resolved from the service provider.
    /// </summary>
    [Fact]
    public void AllViewModels_Registered()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register all required configuration services first
        var configProvider = CreateMockConfigProvider();
        services.AddSingleton<IConfigurationProviderService>(configProvider);
        services.AddSingleton<IStorageLocationService>(new Mock<IStorageLocationService>().Object);
        services.AddSingleton<IUserSettingsService>(CreateMockUserSettingsService());
        services.AddSingleton<IAppConfiguration>(CreateMockAppConfiguration());

        // Mock IGitHubClient to avoid complex module dependencies
        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        services.AddSingleton<Octokit.IGitHubClient>(gitHubClientMock.Object);

        // Mock IFileHashProvider to avoid dependency issues
        var fileHashProviderMock = new Mock<IFileHashProvider>();
        services.AddSingleton<IFileHashProvider>(fileHashProviderMock.Object);

        // Mock content pipeline dependencies
        var githubDiscovererMock = new Mock<IContentDiscoverer>();
        githubDiscovererMock.SetupGet(d => d.SourceName).Returns("GitHub");
        githubDiscovererMock.SetupGet(d => d.Description).Returns("GitHub discoverer");
        githubDiscovererMock.SetupGet(d => d.IsEnabled).Returns(true);
        githubDiscovererMock.SetupGet(d => d.Capabilities).Returns(default(ContentSourceCapabilities));
        services.AddSingleton<IContentDiscoverer>(githubDiscovererMock.Object);
        var cncDiscovererMock = new Mock<IContentDiscoverer>();
        cncDiscovererMock.SetupGet(d => d.SourceName).Returns("CNC Labs");
        cncDiscovererMock.SetupGet(d => d.Description).Returns("CNC Labs discoverer");
        cncDiscovererMock.SetupGet(d => d.IsEnabled).Returns(true);
        cncDiscovererMock.SetupGet(d => d.Capabilities).Returns(default(ContentSourceCapabilities));
        services.AddSingleton<IContentDiscoverer>(cncDiscovererMock.Object);
        var moddbDiscovererMock = new Mock<IContentDiscoverer>();
        moddbDiscovererMock.SetupGet(d => d.SourceName).Returns("ModDB");
        moddbDiscovererMock.SetupGet(d => d.Description).Returns("ModDB discoverer");
        moddbDiscovererMock.SetupGet(d => d.IsEnabled).Returns(true);
        moddbDiscovererMock.SetupGet(d => d.Capabilities).Returns(default(ContentSourceCapabilities));
        services.AddSingleton<IContentDiscoverer>(moddbDiscovererMock.Object);
        var fileSystemDiscovererMock = new Mock<IContentDiscoverer>();
        fileSystemDiscovererMock.SetupGet(d => d.SourceName).Returns("FileSystem");
        fileSystemDiscovererMock.SetupGet(d => d.Description).Returns("FileSystem discoverer");
        fileSystemDiscovererMock.SetupGet(d => d.IsEnabled).Returns(true);
        fileSystemDiscovererMock.SetupGet(d => d.Capabilities).Returns(default(ContentSourceCapabilities));
        services.AddSingleton<IContentDiscoverer>(fileSystemDiscovererMock.Object);
        var resolverMock = new Mock<IContentResolver>();
        resolverMock.SetupGet(r => r.ResolverId).Returns("GitHub");
        services.AddSingleton<IContentResolver>(resolverMock.Object);
        var cncResolverMock = new Mock<IContentResolver>();
        cncResolverMock.SetupGet(r => r.ResolverId).Returns("CNCLabsMap");
        services.AddSingleton<IContentResolver>(cncResolverMock.Object);
        var moddbResolverMock = new Mock<IContentResolver>();
        moddbResolverMock.SetupGet(r => r.ResolverId).Returns("ModDB");
        services.AddSingleton<IContentResolver>(moddbResolverMock.Object);
        var localResolverMock = new Mock<IContentResolver>();
        localResolverMock.SetupGet(r => r.ResolverId).Returns("Local");
        services.AddSingleton<IContentResolver>(localResolverMock.Object);
        var delivererMock = new Mock<IContentDeliverer>();
        delivererMock.SetupGet(d => d.SourceName).Returns("HTTP");
        delivererMock.SetupGet(d => d.Description).Returns("HTTP deliverer");
        delivererMock.SetupGet(d => d.IsEnabled).Returns(true);
        delivererMock.SetupGet(d => d.Capabilities).Returns(default(ContentSourceCapabilities));
        services.AddSingleton<IContentDeliverer>(delivererMock.Object);
        var fileSystemDelivererMock = new Mock<IContentDeliverer>();
        fileSystemDelivererMock.SetupGet(d => d.SourceName).Returns("FileSystem");
        fileSystemDelivererMock.SetupGet(d => d.Description).Returns("FileSystem deliverer");
        fileSystemDelivererMock.SetupGet(d => d.IsEnabled).Returns(true);
        fileSystemDelivererMock.SetupGet(d => d.Capabilities).Returns(default(ContentSourceCapabilities));
        services.AddSingleton<IContentDeliverer>(fileSystemDelivererMock.Object);
        var validatorMock = new Mock<IContentValidator>();
        services.AddSingleton<IContentValidator>(validatorMock.Object);

        // Mock IShortcutService to avoid dependency issues
        var shortcutServiceMock = new Mock<GenHub.Core.Interfaces.Shortcuts.IShortcutService>();
        services.AddSingleton<GenHub.Core.Interfaces.Shortcuts.IShortcutService>(shortcutServiceMock.Object);

        // Mock IGitHubTokenStorage to avoid dependency issues
        var tokenStorageMock = new Mock<GenHub.Core.Interfaces.GitHub.IGitHubTokenStorage>();
        services.AddSingleton<GenHub.Core.Interfaces.GitHub.IGitHubTokenStorage>(tokenStorageMock.Object);

        // Mock IDialogService to avoid dependency issues
        var dialogServiceMock = new Mock<IDialogService>();
        services.AddSingleton<IDialogService>(dialogServiceMock.Object);

        var playwrightServiceMock = new Mock<IPlaywrightService>();
        services.AddSingleton<IPlaywrightService>(playwrightServiceMock.Object);

        // Register required modules in correct order
        services.AddLoggingModule();
        services.AddValidationServices();
        services.AddGameDetectionService();
        services.AddGameInstallation();
        services.AddCasServices();
        services.AddContentPipelineServices();
        services.AddManifestServices();
        services.AddWorkspaceServices();
        services.AddDownloadServices();
        services.AddNotificationModule();
        services.AddAppUpdateModule();
        services.AddGameProfileServices();
        services.AddUserDataServices();
        services.AddLaunchingServices();
        services.AddToolsServices();
        services.AddSharedViewModelModule();

        // Register IManifestIdService
        services.AddSingleton<IManifestIdService>(new ManifestIdService());

        // Re-register the mock config provider to ensure it's the last one
        services.AddSingleton<IConfigurationProviderService>(configProvider);

        // Build the service provider
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert: Try to resolve each ViewModel that doesn't require complex constructor parameters
        Assert.NotNull(serviceProvider.GetService<MainViewModel>());
        Assert.NotNull(serviceProvider.GetService<GameProfileLauncherViewModel>());
        Assert.NotNull(serviceProvider.GetService<DownloadsViewModel>());
        Assert.NotNull(serviceProvider.GetService<GenHub.Features.Tools.ViewModels.ToolsViewModel>());
        Assert.NotNull(serviceProvider.GetService<SettingsViewModel>());
    }

    private static IConfigurationProviderService CreateMockConfigProvider()
    {
        var mock = new Mock<IConfigurationProviderService>();
        mock.Setup(x => x.GetEnableDetailedLogging()).Returns(false);
        mock.Setup(x => x.GetTheme()).Returns("Dark");
        mock.Setup(x => x.GetWindowWidth()).Returns(1200.0);
        mock.Setup(x => x.GetWindowHeight()).Returns(800.0);
        mock.Setup(x => x.GetIsWindowMaximized()).Returns(false);
        mock.Setup(x => x.GetLastSelectedTab()).Returns(NavigationTab.Home);
        mock.Setup(x => x.GetApplicationDataPath()).Returns(Path.Combine(Path.GetTempPath(), "GenHubTest", "Content"));
        mock.Setup(x => x.GetWorkspacePath()).Returns(Path.Combine(Path.GetTempPath(), "GenHubTest", "Workspace"));
        mock.Setup(x => x.GetContentDirectories()).Returns([Path.GetTempPath()]);
        mock.Setup(x => x.GetGitHubDiscoveryRepositories()).Returns(["test/repo"]);
        mock.Setup(x => x.GetCasConfiguration()).Returns(new GenHub.Core.Models.Storage.CasConfiguration());
        mock.Setup(x => x.GetDownloadUserAgent()).Returns("TestAgent/1.0");
        mock.Setup(x => x.GetDownloadTimeoutSeconds()).Returns(120);
        return mock.Object;
    }

    private static IUserSettingsService CreateMockUserSettingsService()
    {
        var mock = new Mock<IUserSettingsService>();
        mock.Setup(x => x.Get()).Returns(new UserSettings
        {
            Theme = "Dark",
            WindowWidth = 1200.0,
            WindowHeight = 800.0,
            LastSelectedTab = NavigationTab.Home,
        });
        return mock.Object;
    }

    private static IAppConfiguration CreateMockAppConfiguration()
    {
        var mock = new Mock<IAppConfiguration>();
        mock.Setup(x => x.GetDefaultTheme()).Returns("Dark");
        mock.Setup(x => x.GetDefaultWindowWidth()).Returns(1200.0);
        mock.Setup(x => x.GetDefaultWindowHeight()).Returns(800.0);
        return mock.Object;
    }
}
