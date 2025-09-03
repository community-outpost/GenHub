using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
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

        // Register required modules in correct order
        services.AddLoggingModule(configProvider);
        services.AddGameDetectionService();
        services.AddSharedViewModelModule();

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
