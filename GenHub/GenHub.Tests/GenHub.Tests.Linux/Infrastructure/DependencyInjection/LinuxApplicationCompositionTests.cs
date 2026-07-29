using System.Runtime.Versioning;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.GameInstallations;
using GenHub.Linux.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Tests.Linux.Infrastructure.DependencyInjection;

/// <summary>
/// Verifies the Linux application dependency injection composition.
/// </summary>
[Collection(ApplicationCompositionCollection.Name)]
public class LinuxApplicationCompositionTests
{
    /// <summary>
    /// Verifies that the real shared and Linux registrations resolve the startup view model graph.
    /// </summary>
    /// <remarks>
    /// This is dependency injection composition coverage. It does not launch Avalonia or a packaged
    /// Linux application.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("linux")]
    public void ConfigureApplicationServices_ResolvesStartupViewModels()
    {
        using var testEnvironment = new TemporaryApplicationEnvironment();
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformServices => platformServices.AddLinuxServices());

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Equal(
            testEnvironment.AppDataPath,
            serviceProvider.GetRequiredService<IConfigurationProviderService>().GetRootAppDataPath());
        Assert.Equal(
            testEnvironment.CasPath,
            serviceProvider.GetRequiredService<IConfigurationProviderService>().GetCasConfiguration().CasRootPath);
        Assert.IsType<LinuxInstallationDetector>(
            serviceProvider.GetRequiredService<IGameInstallationDetector>());
        Assert.NotNull(serviceProvider.GetRequiredService<IShortcutService>());
        Assert.Null(serviceProvider.GetService<IGitHubTokenStorage>());

        var settingsViewModel = serviceProvider.GetRequiredService<SettingsViewModel>();
        Assert.NotNull(serviceProvider.GetRequiredService<GameProfileSettingsViewModel>());

        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        Assert.Same(settingsViewModel, mainViewModel.SettingsViewModel);
        Assert.NotNull(mainViewModel.GameProfilesViewModel);
        Assert.NotNull(mainViewModel.DownloadsViewModel);
        Assert.NotNull(mainViewModel.ToolsViewModel);
    }
}
