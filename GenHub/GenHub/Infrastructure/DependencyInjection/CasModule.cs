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
        // Pool management services (must be registered first for CasService to use)
        services.AddSingleton<ICasPoolResolver, CasPoolResolver>();
        services.AddSingleton<ICasPoolManager, CasPoolManager>();

        // CAS integration services
        services.AddSingleton<ICasService, CasService>();
        services.AddSingleton<ICasStorage, CasStorage>();
        services.AddSingleton<CasReferenceTracker>();
        services.AddSingleton<ICasReferenceTracker>(sp => sp.GetRequiredService<CasReferenceTracker>());

        // Configuration
        services.AddOptions<CasConfiguration>().Configure<IConfigurationProviderService>((config, configProvider) =>
        {
            var userCasConfig = configProvider.GetCasConfiguration();
            config.EnableAutomaticGc = userCasConfig.EnableAutomaticGc;
            config.CasRootPath = userCasConfig.CasRootPath;
            config.InstallationPoolRootPath = userCasConfig.InstallationPoolRootPath;
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
