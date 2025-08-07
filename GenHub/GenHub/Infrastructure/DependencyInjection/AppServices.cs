using System;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Main module that orchestrates registration of all application services and passes a single shared
/// IConfigurationProviderService instance into modules that require configuration at registration time.
/// Platform-specific services are registered via a factory to keep Program.cs minimal.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// Registers all shared services and platform-specific services.
    /// </summary>
    /// <param name="services">The service collection to which application services will be registered.</param>
    /// <param name="platformModuleFactory">A factory that registers platform-specific services using the shared config provider.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with registered application services.</returns>
    public static IServiceCollection ConfigureApplicationServices(
        this IServiceCollection services,
        Func<IServiceCollection, IConfigurationProviderService, IServiceCollection>? platformModuleFactory = null)
    {
        services.AddConfigurationModule();

        using var tempProvider = services.BuildServiceProvider();
        var configProvider = tempProvider.GetRequiredService<IConfigurationProviderService>();

        // Register shared services, passing config provider to those that need it
        services.AddGameDetectionService();
        services.AddLoggingModule(configProvider);
        services.AddSharedViewModelModule();
        services.AddAppUpdateModule();
        services.AddDownloadServices(configProvider);
        services.AddValidationServices();
        services.AddManifestServices();
        services.AddWorkspaceServices();

        // Register platform-specific services using the factory if provided
        platformModuleFactory?.Invoke(services, configProvider);

        return services;
    }
}
