using GenHub.Common.ViewModels;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

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

        // Register the SharedViewModelModule
        services.AddSharedViewModelModule();

        // Register other modules that MainViewModel (and other ViewModels) depend on.
        // This simulates the full application service registration.
        services.AddGameDetectionService(); // Registers IGameInstallationDetectionOrchestrator
        services.AddLoggingModule();       // Registers ILogger<T>

        // Build the service provider
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert: Try to resolve each ViewModel
        Assert.NotNull(serviceProvider.GetService<MainViewModel>());
        Assert.NotNull(serviceProvider.GetService<GameProfileLauncherViewModel>());
        Assert.NotNull(serviceProvider.GetService<DownloadsViewModel>());
        Assert.NotNull(serviceProvider.GetService<SettingsViewModel>());
    }
}
