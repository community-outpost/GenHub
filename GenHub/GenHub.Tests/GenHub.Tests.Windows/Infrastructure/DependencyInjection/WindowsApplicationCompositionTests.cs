using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Launching;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;
using GenHub.Windows.Features.GitHub.Services;
using GenHub.Windows.GameInstallations;
using GenHub.Windows.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace GenHub.Tests.Windows.Infrastructure.DependencyInjection;

/// <summary>
/// Verifies the Windows application dependency injection composition.
/// </summary>
[Collection(ApplicationCompositionCollection.Name)]
public class WindowsApplicationCompositionTests
{
    /// <summary>
    /// Verifies that the real shared and Windows registrations resolve the startup view model graph.
    /// </summary>
    /// <remarks>
    /// This is dependency injection composition coverage. It does not launch Avalonia or a packaged
    /// Windows application.
    /// </remarks>
    [Fact]
    public void ConfigureApplicationServices_ResolvesStartupViewModels()
    {
        using var testEnvironment = new TemporaryApplicationEnvironment();
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformServices => platformServices.AddWindowsServices());

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IGitHubTokenStorage)
                && descriptor.ImplementationType == typeof(WindowsGitHubTokenStorage)
                && descriptor.Lifetime == ServiceLifetime.Singleton);

        var authServiceMock = new Mock<IGitHubAuthService>();
        authServiceMock.Setup(service => service.IsAuthenticated).Returns(true);
        services.AddSingleton(authServiceMock.Object);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Equal(
            testEnvironment.AppDataPath,
            serviceProvider.GetRequiredService<IConfigurationProviderService>().GetRootAppDataPath());
        Assert.Equal(
            testEnvironment.CasPath,
            serviceProvider.GetRequiredService<IConfigurationProviderService>().GetCasConfiguration().CasRootPath);
        Assert.IsType<WindowsInstallationDetector>(
            serviceProvider.GetRequiredService<IGameInstallationDetector>());
        Assert.IsType<WindowsInstallationSearchPathProvider>(
            serviceProvider.GetRequiredService<IInstallationSearchPathProvider>());
        Assert.IsType<DirectRunner>(
            serviceProvider.GetRequiredService<IGameLaunchRunner>());
        Assert.NotNull(serviceProvider.GetRequiredService<IShortcutService>());
        Assert.IsType<WindowsGitHubTokenStorage>(serviceProvider.GetRequiredService<IGitHubTokenStorage>());

        var settingsViewModel = serviceProvider.GetRequiredService<SettingsViewModel>();
        Assert.NotNull(settingsViewModel);
        Assert.Same(authServiceMock.Object, serviceProvider.GetRequiredService<IGitHubAuthService>());
        authServiceMock.VerifyGet(service => service.IsAuthenticated, Times.AtLeastOnce);
        Assert.NotNull(serviceProvider.GetRequiredService<GameProfileSettingsViewModel>());

        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        Assert.Same(settingsViewModel, mainViewModel.SettingsViewModel);
        Assert.NotNull(mainViewModel.GameProfilesViewModel);
        Assert.NotNull(mainViewModel.DownloadsBrowserViewModel);
        Assert.NotNull(mainViewModel.ToolsViewModel);
    }
}
