using System;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Main module that orchestrates registration of all application services.
/// Platform-specific services are registered via a factory to keep Program.cs minimal.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// Registers all shared services and platform-specific services.
    /// </summary>
    /// <param name="services">The service collection to which application services will be registered.</param>
    /// <param name="platformModuleFactory">A factory that registers platform-specific services.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with registered application services.</returns>
    public static IServiceCollection ConfigureApplicationServices(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection>? platformModuleFactory = null)
    {
        // Register configuration services first
        services.AddConfigurationModule();

        // Register core services in dependency order
        services.AddLoggingModule();
        services.AddValidationServices();
        services.AddGameDetectionService();
        services.AddGameInstallation();
        services.AddContentPipelineServices();
        services.AddManifestServices();
        services.AddWorkspaceServices();
        services.AddCasServices();
        services.AddDownloadServices();

        // Register GameProfile services (depends on above services)
        services.AddGameProfileServices();
        services.AddLaunchingServices();

        // Register Tools services
        services.AddToolsServices();

        // Register UI services last (depends on all business services)
        services.AddAppUpdateModule();
        services.AddSharedViewModelModule();

        // Register platform-specific services using the factory if provided
        platformModuleFactory?.Invoke(services);

        return services;
    }
}
