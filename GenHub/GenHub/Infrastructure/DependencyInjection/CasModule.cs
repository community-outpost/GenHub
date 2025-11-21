using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for Content-Addressable Storage (CAS) services.
/// </summary>
public static class CasModule
{
    /// <summary>
    /// Registers CAS services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCasServices(this IServiceCollection services)
    {
        // CAS integration services
        services.AddSingleton<ICasService, CasService>();
        services.AddSingleton<ICasStorage, CasStorage>();
        services.AddSingleton<CasReferenceTracker>();

        // Configuration
        services.AddOptions<CasConfiguration>().Configure<IConfigurationProviderService>((config, configProvider) =>
        {
            var userCasConfig = configProvider.GetCasConfiguration();
            config.EnableAutomaticGc = userCasConfig.EnableAutomaticGc;
            config.CasRootPath = userCasConfig.CasRootPath;
            config.HashAlgorithm = userCasConfig.HashAlgorithm;
            config.GcGracePeriod = userCasConfig.GcGracePeriod;
            config.MaxCacheSizeBytes = userCasConfig.MaxCacheSizeBytes;
            config.AutoGcInterval = userCasConfig.AutoGcInterval;
            config.MaxConcurrentOperations = userCasConfig.MaxConcurrentOperations;
            config.VerifyIntegrity = userCasConfig.VerifyIntegrity;
        });

        // Background services
        services.AddHostedService<CasMaintenanceService>();

        return services;
    }
}