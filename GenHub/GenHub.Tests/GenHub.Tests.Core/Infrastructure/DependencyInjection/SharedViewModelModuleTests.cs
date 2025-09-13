using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
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
        services.AddSingleton<IUserSettingsService>(CreateMockUserSettingsService());
        services.AddSingleton<IAppConfiguration>(CreateMockAppConfiguration());

        // Mock IGitHubClient to avoid complex module dependencies
        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        services.AddSingleton<Octokit.IGitHubClient>(gitHubClientMock.Object);

        // Mock IFileHashProvider to avoid dependency issues
        var fileHashProviderMock = new Mock<IFileHashProvider>();
        services.AddSingleton<IFileHashProvider>(fileHashProviderMock.Object);

        // Register required modules in correct order
        services.AddLoggingModule(configProvider);
        services.AddValidationServices();
        services.AddGameDetectionService();
        services.AddGameInstallation();
        services.AddContentPipelineServices();
        services.AddManifestServices();
        services.AddWorkspaceServices();
        services.AddCasServices(configProvider);
        services.AddDownloadServices(configProvider);
        services.AddAppUpdateModule();
        services.AddGameProfileServices(configProvider);
        services.AddLaunchingServices(configProvider);
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
        mock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(Path.GetTempPath(), "GenHubTest", "Content"));
        mock.Setup(x => x.GetWorkspacePath()).Returns(Path.Combine(Path.GetTempPath(), "GenHubTest", "Workspace"));
        mock.Setup(x => x.GetContentDirectories()).Returns(new List<string> { Path.GetTempPath() });
        mock.Setup(x => x.GetGitHubDiscoveryRepositories()).Returns(new List<string> { "test/repo" });
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
